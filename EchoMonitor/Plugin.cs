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
        public static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private EchoConfig _config;
        private MainThreadDispatcher _dispatcher;
        private TelemetryCollector _collector;
        private StuckDetector _stuckDetector;
        private SessionManager _sessionManager;
        private HttpServer _server;

        private string _latestStateJson = "{\"inGame\":false,\"timestamp\":\"\",\"saveId\":\"\",\"sessionStartedAt\":\"\",\"sessionDurationHours\":0,\"echo\":null,\"crew\":null,\"droneBay\":null,\"hangarBay\":null,\"fleet\":null,\"alerts\":null,\"missionLog\":[],\"current\":null,\"baseline\":null,\"deltas\":null}";
        private readonly object _stateLock = new object();
        private float _nextSampleTime;

        private void Awake()
        {
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

            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            EchoPatches.Apply(_harmony);

            _server = new HttpServer(
                _config,
                GetCachedOrLiveStateJson,
                () => _sessionManager.GetHistoryJson(),
                () => _sessionManager.GetMissionLogJson(),
                HandleResetSession,
                HandleWarpSafeStation,
                HandleResetEcho,
                HandleToggleEcho,
                HandleAbandonMission,
                HandleWarpDefend,
                HandleDismissAlerts);

            try
            {
                _server.Start();
                if (_config.OpenBrowserOnStart.Value)
                {
                    string url = $"http://127.0.0.1:{_config.Port.Value}/";
                    TryOpenBrowser(url);
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

        private void OnDestroy()
        {
            _server?.Dispose();
            _server = null;
            _harmony?.UnpatchSelf();
            _harmony = null;
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

        private void TryOpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                return;
            }
            catch (Exception ex)
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
                        return;
                    }
                    catch { /* fall through to warning */ }
                }
                Log.LogWarning($"Could not open browser: {ex.Message}");
            }
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
                        bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
                        string primary = Path.Combine(pluginDir, isWindows ? "EchoMonitorBot.exe" : "EchoMonitorBot");
                        string fallback = Path.Combine(pluginDir, isWindows ? "EchoMonitorBot" : "EchoMonitorBot.exe");
                        exePath = File.Exists(primary) ? primary : (File.Exists(fallback) ? fallback : primary);
                    }
                }

                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    Log.LogWarning($"AutoLaunchBot is enabled, but EchoMonitorBot was not found at: {exePath}");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                Log.LogInfo($"Auto-launched EchoMonitorBot from {exePath}");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to auto-launch EchoMonitorBot: {ex.Message}");
            }
        }
    }
}
