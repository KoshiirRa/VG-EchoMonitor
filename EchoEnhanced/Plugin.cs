using System;
using BepInEx;
using BepInEx.Logging;
using Behaviour.Managers;
using Behaviour.UI.Spacestation;
using EchoEnhanced.AiSearch;
using EchoEnhanced.AutoBuy;
using EchoEnhanced.AutoFavorite;
using EchoEnhanced.AutoSell;
using EchoEnhanced.DroneLoadouts;
using EchoEnhanced.NaturalLanguage;
using EchoEnhanced.Patches;
using HarmonyLib;
using LightJson;
using Source.Player;

namespace EchoEnhanced
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("EchoMonitor", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; }

        private Harmony _harmony;
        private EchoEnhancedConfig _config;
        private AutoFavoriteEngine _autoFavorite;
        private AutoSellEngine _autoSell;
        private AutoBuyEngine _autoBuy;
        private DroneLoadoutEngine _droneLoadouts;
        private DroneLoadoutOverlay _droneOverlay;
        private DirectLlmClient _directLlm;
        private CloudflareAiSearchClient _aiSearch;
        private NaturalLanguageRuleEngine _nlRules;
        private GamePlayer _lastScannedPlayer;
        private Source.Galaxy.POI.SpaceStation _lastDockedShopStation;

        private void Awake()
        {
            Log = Logger;
            _config = new EchoEnhancedConfig(Config);

            if (!_config.Enabled.Value)
            {
                Log.LogInfo("EchoEnhanced is disabled in config.");
                return;
            }

            _autoFavorite = new AutoFavoriteEngine(_config, Paths.ConfigPath);
            _autoSell = new AutoSellEngine(_config, _autoFavorite, Paths.ConfigPath);
            _autoBuy = new AutoBuyEngine(_config, Paths.ConfigPath);
            _droneLoadouts = new DroneLoadoutEngine(_config, Paths.ConfigPath);
            _droneOverlay = new DroneLoadoutOverlay(_droneLoadouts, _config);
            _directLlm = new DirectLlmClient(Paths.ConfigPath);
            _aiSearch = new CloudflareAiSearchClient(Paths.ConfigPath);
            _nlRules = new NaturalLanguageRuleEngine(_directLlm, _aiSearch, _autoFavorite, _autoSell, _autoBuy);

            InventoryPatches.AutoFavorite = _autoFavorite;
            InventoryPatches.AutoSell = _autoSell;
            DroneLoadoutPatches.Engine = _droneLoadouts;

            _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            InventoryPatches.Apply(_harmony);
            DroneLoadoutPatches.Apply(_harmony);

            RegisterEchoMonitorRoutes();

            Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} is loaded! AutoFavorite: {_autoFavorite.RulesPath} | AutoSell: {_autoSell.RulesPath} | AutoBuy: {_autoBuy.RulesPath} | DroneLoadouts: {_droneLoadouts.StorePath}");
        }

        private void RegisterEchoMonitorRoutes()
        {
            EchoMonitor.Plugin.RegisterExtensionRoute("GET", "/api/enhanced/status", _ =>
            {
                return new JsonObject
                {
                    { "installed", true },
                    { "version", MyPluginInfo.PLUGIN_VERSION },
                    { "autoFavorite", _autoFavorite.GetStatusObject() },
                    { "autoSell", _autoSell.GetStatusObject() },
                    { "autoBuy", _autoBuy.GetStatusObject() },
                    { "droneLoadouts", _droneLoadouts.GetStatusObject() },
                    { "aiSearch", _nlRules.GetStatusObject() }
                }.ToString();
            });

            EchoMonitor.Plugin.RegisterExtensionRoute("GET", "/api/enhanced/autofav/rules",
                _ => _autoFavorite.GetRulesJson());

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autofav/rules",
                body => _autoFavorite.SaveRulesJson(body));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autofav/config",
                body => _autoFavorite.UpdateConfigJson(body));

            EchoMonitor.Plugin.RegisterExtensionRoute("GET", "/api/enhanced/autofav/catalog",
                _ => EchoMonitor.Plugin.RunOnMainThread(() => _autoFavorite.GetCatalogJson(), 5000));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autofav/preview",
                body => EchoMonitor.Plugin.RunOnMainThread(() => _autoFavorite.PreviewRuleJson(body), 10000));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autofav/scan", _ =>
            {
                try
                {
                    var res = EchoMonitor.Plugin.RunOnMainThread(() => _autoFavorite.ScanAllPlayerInventories(manual: true), 10000);
                    return new JsonObject
                    {
                        { "ok", true },
                        { "matched", res.Matched },
                        { "itemsScanned", res.ItemsScanned },
                        { "inventoriesScanned", res.InventoriesScanned },
                        { "alreadyFavourited", res.AlreadyFavourited },
                        { "dryRun", res.DryRun }
                    }.ToString();
                }
                catch (Exception ex)
                {
                    return new JsonObject
                    {
                        { "ok", false },
                        { "error", ex.Message }
                    }.ToString();
                }
            });

            EchoMonitor.Plugin.RegisterExtensionRoute("GET", "/api/enhanced/autosell/rules",
                _ => _autoSell.GetRulesJson());

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autosell/rules",
                body => _autoSell.SaveRulesJson(body));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autosell/config",
                body => _autoSell.UpdateConfigJson(body));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autosell/preview",
                body => EchoMonitor.Plugin.RunOnMainThread(() => _autoSell.PreviewRuleJson(body), 10000));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autosell/scan", _ =>
            {
                try
                {
                    var res = EchoMonitor.Plugin.RunOnMainThread(() => _autoSell.ScanAllPlayerInventories(manual: true), 10000);
                    return new JsonObject
                    {
                        { "ok", true },
                        { "matched", res.Matched },
                        { "sold", res.Sold },
                        { "creditsEarned", (double)res.CreditsEarned },
                        { "itemsScanned", res.ItemsScanned },
                        { "inventoriesScanned", res.InventoriesScanned },
                        { "skippedFavourited", res.SkippedFavourited },
                        { "dryRun", res.DryRun }
                    }.ToString();
                }
                catch (Exception ex)
                {
                    return new JsonObject
                    {
                        { "ok", false },
                        { "error", ex.Message }
                    }.ToString();
                }
            });

            EchoMonitor.Plugin.RegisterExtensionRoute("GET", "/api/enhanced/autobuy/rules",
                _ => _autoBuy.GetRulesJson());

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autobuy/rules",
                body => _autoBuy.SaveRulesJson(body));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autobuy/config",
                body => _autoBuy.UpdateConfigJson(body));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autobuy/preview",
                body => EchoMonitor.Plugin.RunOnMainThread(() => _autoBuy.PreviewRuleJson(body), 10000));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/autobuy/scan", _ =>
            {
                try
                {
                    var res = EchoMonitor.Plugin.RunOnMainThread(() => _autoBuy.ScanDockedStore(manual: true), 10000);
                    return new JsonObject
                    {
                        { "ok", true },
                        { "dockedWithStore", res.DockedWithStore },
                        { "stationName", res.StationName },
                        { "itemsScanned", res.ItemsScanned },
                        { "matched", res.Matched },
                        { "bought", res.Bought },
                        { "creditsSpent", (double)res.CreditsSpent },
                        { "dryRun", res.DryRun }
                    }.ToString();
                }
                catch (Exception ex)
                {
                    return new JsonObject
                    {
                        { "ok", false },
                        { "error", ex.Message }
                    }.ToString();
                }
            });

            EchoMonitor.Plugin.RegisterExtensionRoute("GET", "/api/enhanced/drones/store",
                _ => _droneLoadouts.GetStoreJson());

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/drones/config",
                body => _droneLoadouts.UpdateConfigJson(body));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/drones/apply", body =>
            {
                try
                {
                    var parsed = LightJson.Serialization.JsonReader.Parse(body ?? "{}");
                    string preset = parsed.IsJsonObject && parsed.AsJsonObject["preset"].IsString ? parsed.AsJsonObject["preset"].AsString : string.Empty;
                    var res = EchoMonitor.Plugin.RunOnMainThread(() => _droneLoadouts.ApplyPreset(preset, "Web UI"), 5000);
                    return new JsonObject
                    {
                        { "ok", res.Ok },
                        { "preset", res.PresetName },
                        { "message", res.Message },
                        { "shipsUpdated", res.ShipsUpdated },
                        { "slotsChanged", res.SlotsChanged },
                        { "lockedDronesSkipped", res.LockedDronesSkipped }
                    }.ToString();
                }
                catch (Exception ex)
                {
                    return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
                }
            });

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/drones/capture", body =>
            {
                try
                {
                    var parsed = LightJson.Serialization.JsonReader.Parse(body ?? "{}");
                    string preset = parsed.IsJsonObject && parsed.AsJsonObject["preset"].IsString ? parsed.AsJsonObject["preset"].AsString : string.Empty;
                    var res = EchoMonitor.Plugin.RunOnMainThread(() => _droneLoadouts.CaptureCurrentFleetAsPreset(preset), 5000);
                    return new JsonObject
                    {
                        { "ok", res.Ok },
                        { "preset", res.PresetName },
                        { "message", res.Message },
                        { "shipsUpdated", res.ShipsUpdated }
                    }.ToString();
                }
                catch (Exception ex)
                {
                    return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
                }
            });

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/drones/delete", body =>
            {
                try
                {
                    var parsed = LightJson.Serialization.JsonReader.Parse(body ?? "{}");
                    string preset = parsed.IsJsonObject && parsed.AsJsonObject["preset"].IsString ? parsed.AsJsonObject["preset"].AsString : string.Empty;
                    bool ok = _droneLoadouts.DeletePreset(preset);
                    return new JsonObject { { "ok", ok }, { "preset", preset } }.ToString();
                }
                catch (Exception ex)
                {
                    return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
                }
            });

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/aisearch/query",
                body => _nlRules.QueryAiSearchJson(body));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/nl/rule/parse",
                body => _nlRules.ParseRuleJson(body));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/nl/rule/confirm",
                body => _nlRules.ConfirmDraftJson(body));

            EchoMonitor.Plugin.RegisterExtensionRoute("POST", "/api/enhanced/nl/rule/cancel",
                body => _nlRules.CancelDraftJson(body));

            Log.LogInfo("[EchoEnhanced] Registered /api/enhanced/* routes with EchoMonitor HTTP server.");
        }

        private void Update()
        {
            if (_autoFavorite == null || !_config.Enabled.Value) return;

            try
            {
                if (_config.RescanHotkey.Value.IsDown())
                {
                    Log.LogInfo("[AutoFavorite] Manual rescan triggered via hotkey.");
                    _autoFavorite.ScanAllPlayerInventories(manual: true);
                }

                if (_autoSell != null && _config.AutoSellScanHotkey.Value.IsDown())
                {
                    Log.LogInfo("[AutoSell] Manual scan triggered via hotkey.");
                    _autoSell.ScanAllPlayerInventories(manual: true);
                }

                if (_autoBuy != null && _config.AutoBuyScanHotkey.Value.IsDown())
                {
                    Log.LogInfo("[AutoBuy] Manual store scan triggered via hotkey.");
                    _autoBuy.ScanDockedStore(manual: true);
                }

                if (_droneLoadouts != null && _config.DroneLoadoutsEnabled.Value)
                {
                    if (_config.DroneLoadoutUiHotkey.Value.IsDown() && _droneOverlay != null)
                    {
                        _droneOverlay.Visible = !_droneOverlay.Visible;
                    }
                    else if (_config.DroneCombatHotkey.Value.IsDown())
                    {
                        _droneLoadouts.ApplyPreset(_droneLoadouts.Store.EchoBindings.CombatPreset, "Hotkey: Combat");
                    }
                    else if (_config.DroneSalvageHotkey.Value.IsDown())
                    {
                        _droneLoadouts.ApplyPreset(_droneLoadouts.Store.EchoBindings.SalvagePreset, "Hotkey: Salvage");
                    }
                    else if (_config.DroneMiningHotkey.Value.IsDown())
                    {
                        _droneLoadouts.ApplyPreset(_droneLoadouts.Store.EchoBindings.MiningPreset, "Hotkey: Mining");
                    }
                }

                _autoSell?.ProcessPendingQueue();

                // Automatically run Auto-Sell and Auto-Buy when docking at a SpaceStation with a store
                var dockedShopStation = AutoSellEngine.GetDockedShopStation();
                if (dockedShopStation != null && !ReferenceEquals(dockedShopStation, _lastDockedShopStation))
                {
                    _lastDockedShopStation = dockedShopStation;
                    if (_config.AutoSellEnabled.Value)
                    {
                        Log.LogInfo($"[AutoSell] Docked at {dockedShopStation.name} (store available) — scanning for Auto-Sell matches.");
                        _autoSell?.ScanAllPlayerInventories();
                    }
                    if (_config.AutoBuyEnabled.Value)
                    {
                        Log.LogInfo($"[AutoBuy] Docked at {dockedShopStation.name} (store available) — scanning store for Auto-Buy matches.");
                        _autoBuy?.ScanDockedStore();
                    }
                }
                else if (dockedShopStation == null)
                {
                    _lastDockedShopStation = null;
                }

                _droneLoadouts?.TickFleetAutoDeploy();

                if (_config.AutoFavoriteScanOnLoad.Value || _config.AutoSellScanOnLoad.Value)
                {
                    var gp = GamePlayer.current;
                    bool inGame = gp != null
                        && (GameplayManager.initialized || SpaceStationInterior.instance != null)
                        && gp.elapsedTime > 0.0;

                    if (inGame && !ReferenceEquals(gp, _lastScannedPlayer))
                    {
                        _lastScannedPlayer = gp;
                        if (_config.AutoFavoriteScanOnLoad.Value)
                        {
                            _autoFavorite.ScanAllPlayerInventories();
                        }
                        if (_config.AutoSellScanOnLoad.Value)
                        {
                            _autoSell?.ScanAllPlayerInventories();
                        }
                    }
                    else if (!inGame)
                    {
                        _lastScannedPlayer = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[EchoEnhanced] Update error: {ex}");
            }
        }

        private void OnGUI()
        {
            if (_droneOverlay != null && _config != null && _config.Enabled.Value && _config.DroneLoadoutsEnabled.Value)
            {
                _droneOverlay.Draw();
            }
        }

        private void OnDestroy()
        {
            try
            {
                EchoMonitor.Plugin.UnregisterExtensionRoute("GET", "/api/enhanced/status");
                EchoMonitor.Plugin.UnregisterExtensionRoute("GET", "/api/enhanced/autofav/rules");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autofav/rules");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autofav/config");
                EchoMonitor.Plugin.UnregisterExtensionRoute("GET", "/api/enhanced/autofav/catalog");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autofav/preview");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autofav/scan");
                EchoMonitor.Plugin.UnregisterExtensionRoute("GET", "/api/enhanced/autosell/rules");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autosell/rules");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autosell/config");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autosell/preview");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autosell/scan");
                EchoMonitor.Plugin.UnregisterExtensionRoute("GET", "/api/enhanced/autobuy/rules");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autobuy/rules");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autobuy/config");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autobuy/preview");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/autobuy/scan");
                EchoMonitor.Plugin.UnregisterExtensionRoute("GET", "/api/enhanced/drones/store");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/drones/config");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/drones/apply");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/drones/capture");
                EchoMonitor.Plugin.UnregisterExtensionRoute("POST", "/api/enhanced/drones/delete");
                _harmony?.UnpatchSelf();
            }
            catch { }
        }
    }
}
