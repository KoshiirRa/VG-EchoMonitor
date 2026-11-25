using System;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using EchoMonitor.Patches;
using HarmonyLib;

namespace EchoMonitor
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        public static void RegisterExtensionRoute(string method, string path, Func<string, string> handler)
        {
            Instance?._server?.RegisterRoute(method, path, handler);
        }

        public static void UnregisterExtensionRoute(string method, string path)
        {
            Instance?._server?.UnregisterRoute(method, path);
        }

        public static T RunOnMainThread<T>(Func<T> func, int timeoutMs = 5000)
        {
            var disp = Instance?._dispatcher;
            if (disp == null) throw new InvalidOperationException("EchoMonitor dispatcher is not initialized.");
            return disp.Invoke(func, timeoutMs);
        }

        private Harmony _harmony;
        private EchoConfig _config;
        private MainThreadDispatcher _dispatcher;
        private TelemetryCollector _collector;
        private StuckDetector _stuckDetector;
        private SessionManager _sessionManager;
        private HttpServer _server;
        private Process _botProcess;
        private bool _shutdownDone;

        private string _latestStateJson = "{\"inGame\":false,\"timestamp\":\"\",\"saveId\":\"\",\"sessionStartedAt\":\"\",\"sessionDurationHours\":0,\"echo\":null,\"crew\":null,\"droneBay\":null,\"hangarBay\":null,\"fleet\":null,\"alerts\":null,\"empire\":null,\"missionLog\":[],\"current\":null,\"baseline\":null,\"deltas\":null}";
        private readonly object _stateLock = new object();
        private float _nextSampleTime;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _config = new EchoConfig(Config);

            if (!_config.Enabled.Value)
            {
                Log.LogInfo("EchoMonitor is disabled in config.");
                return;
            }

            _dispatcher = new MainThreadDispatcher();
            _collector = new TelemetryCollector();
            _sessionManager = new SessionManager(Paths.ConfigPath);
            _stuckDetector = new StuckDetector();

            EchoPatches.OnPlayerEmergencyWarp = (poi, mission) => _stuckDetector.RecordEmergencyWarp(poi, mission);
            EchoPatches.OnMissionEvent = entry => _sessionManager.RecordMissionEvent(entry);
            EchoPatches.OnEmpireRefined = (item, amt) => _sessionManager.RecordEmpireRefined(item, amt);
            EchoPatches.OnEmpireProduced = (item, amt) => _sessionManager.RecordEmpireProduced(item, amt);
            EchoPatches.OnEmpireCrafted = (item, amt) => _sessionManager.RecordEmpireCrafted(item, amt);
            EchoPatches.OnEmpireTradeRun = (earned, spent, profit) => _sessionManager.RecordEmpireTradeRun(earned, spent, profit);
            EchoPatches.OnEmpireSale = credits => _sessionManager.RecordEmpireSale(credits);

            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            EchoPatches.Apply(_harmony);

            UnityEngine.Application.quitting += Shutdown;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            _server = new HttpServer(
                _config,
                GetCachedOrLiveStateJson,
                () => _sessionManager.GetHistoryJson(),
                () => _sessionManager.GetMissionLogJson(),
                HandleGetDepots,
                HandleResetSession,
                HandleWarpSafeStation,
                HandleResetEcho,
                HandleToggleEcho,
                HandleAbandonMission,
                HandleWarpDefend,
                HandleDismissAlerts,
                HandleRelieveOutpost);

            try
            {
                _server.Start();
                if (_config.OpenBrowserOnStart.Value)
                {
                    string url = $"http://127.0.0.1:{_config.Port.Value}/";
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                    catch (Exception ex) { Log.LogWarning($"Could not open browser: {ex.Message}"); }
                }

                if (_config.AutoLaunchBot.Value)
                {
                    TryLaunchCompanionBot();
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to start EchoMonitor HTTP server: {ex}");
            }

            Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} is loaded!");
        }

        private void Update()
        {
            if (_dispatcher == null) return;

            _dispatcher.Drain();

            // Sample telemetry on the Unity main thread every ~1 second
            if (UnityEngine.Time.unscaledTime >= _nextSampleTime)
            {
                _nextSampleTime = UnityEngine.Time.unscaledTime + 1.0f;
                RefreshStateOnMainThread();
            }
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnProcessExit(object sender, EventArgs e)
        {
            Shutdown();
        }

        private void Shutdown()
        {
            if (_shutdownDone) return;
            _shutdownDone = true;

            try { UnityEngine.Application.quitting -= Shutdown; } catch { }
            try { AppDomain.CurrentDomain.ProcessExit -= OnProcessExit; } catch { }

            _server?.Dispose();
            _server = null;

            StopCompanionBot();

            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        private void StopCompanionBot()
        {
            var proc = _botProcess;
            _botProcess = null;
            if (proc == null) return;

            try
            {
                if (!proc.HasExited)
                {
                    // Give the bot a brief window to receive the SSE shutdown event,
                    // post its Discord clean-exit embed, and exit on its own.
                    if (!proc.WaitForExit(3000))
                    {
                        Log.LogWarning("EchoMonitorBot did not exit within 3s of shutdown; terminating.");
                        proc.Kill();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Error stopping EchoMonitorBot: {ex.Message}");
            }
            finally
            {
                try { proc.Dispose(); } catch { }
            }
        }

        private void RefreshStateOnMainThread()
        {
            try
            {
                var telemetry = _collector.Collect(_stuckDetector);
                string json = _sessionManager.BuildStateJson(telemetry, _config.SnapshotIntervalMinutes.Value);
                lock (_stateLock)
                {
                    _latestStateJson = json;
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Telemetry refresh error: {ex.Message}");
            }
        }

        private string GetCachedOrLiveStateJson()
        {
            lock (_stateLock)
            {
                return _latestStateJson;
            }
        }

        private string HandleResetSession()
        {
            var telemetry = _dispatcher.Invoke(() => _collector.Collect(_stuckDetector), timeoutMs: 5000);
            if (telemetry == null || !telemetry.InGame || telemetry.Snapshot == null)
            {
                return JsonUtil.SerializeResetResponse(false, string.Empty, string.Empty);
            }

            var (saveId, startedAt) = _sessionManager.ResetSession(telemetry.Snapshot);
            string updated = _sessionManager.BuildStateJson(telemetry, _config.SnapshotIntervalMinutes.Value);
            lock (_stateLock)
            {
                _latestStateJson = updated;
            }
            return JsonUtil.SerializeResetResponse(true, saveId, startedAt);
        }

        private string HandleWarpSafeStation()
        {
            var res = _dispatcher.Invoke(() => RecoveryController.WarpToSafeStation(_stuckDetector), timeoutMs: 5000);
            return JsonUtil.SerializeRecoveryResponse(res);
        }

        private string HandleResetEcho()
        {
            var res = _dispatcher.Invoke(() => RecoveryController.ResetEcho(_stuckDetector), timeoutMs: 5000);
            return JsonUtil.SerializeRecoveryResponse(res);
        }

        private string HandleToggleEcho()
        {
            var res = _dispatcher.Invoke(() => RecoveryController.ToggleEcho(_stuckDetector), timeoutMs: 5000);
            return JsonUtil.SerializeRecoveryResponse(res);
        }

        private string HandleAbandonMission()
        {
            var res = _dispatcher.Invoke(() => RecoveryController.AbandonActiveMission(_stuckDetector), timeoutMs: 5000);
            return JsonUtil.SerializeRecoveryResponse(res);
        }

        private string HandleWarpDefend()
        {
            var res = _dispatcher.Invoke(() => RecoveryController.WarpToDefend(_stuckDetector), timeoutMs: 5000);
            return JsonUtil.SerializeRecoveryResponse(res);
        }

        private string HandleDismissAlerts()
        {
            _stuckDetector?.DismissAll();
            return "{\"ok\":true}";
        }

        private string HandleRelieveOutpost(string stationGuid)
        {
            var res = _dispatcher.Invoke(() => RecoveryController.RelieveOutpostStorage(stationGuid, _stuckDetector), timeoutMs: 5000);
            return JsonUtil.SerializeRecoveryResponse(res);
        }

        private string HandleGetDepots()
        {
            var res = _dispatcher.Invoke(() => TelemetryCollector.CollectStationInventories(), timeoutMs: 5000);
            return JsonUtil.SerializeDepotInventory(res);
        }

        private void TryLaunchCompanionBot()
        {
            try
            {
                var existing = Process.GetProcessesByName("EchoMonitorBot");
                if (existing != null && existing.Length > 0)
                {
                    Log.LogInfo("EchoMonitorBot is already running; skipping auto-launch.");
                    return;
                }

                string exePath = _config.BotExePath.Value?.Trim();
                if (string.IsNullOrEmpty(exePath))
                {
                    string pluginDir = Path.GetDirectoryName(Info.Location);
                    if (!string.IsNullOrEmpty(pluginDir))
                    {
                        exePath = Path.Combine(pluginDir, "EchoMonitorBot.exe");
                    }
                }

                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    Log.LogWarning($"AutoLaunchBot is enabled, but EchoMonitorBot.exe was not found at: {exePath}");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                _botProcess = Process.Start(psi);
                Log.LogInfo($"Auto-launched EchoMonitorBot from {exePath}");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to auto-launch EchoMonitorBot: {ex.Message}");
            }
        }
    }
}
