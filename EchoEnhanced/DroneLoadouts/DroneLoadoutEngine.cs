using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Behaviour.Crew;
using Behaviour.Equipment.Module;
using Behaviour.Gameplay;
using Behaviour.Managers;
using Behaviour.Salvage;
using Behaviour.UI.Spacestation;
using Behaviour.UI.Spacestation.Hangar;
using Behaviour.UI.Spacestation.Location;
using Behaviour.Unit;
using Behaviour.Weapons;
using LightJson;
using LightJson.Serialization;
using Source.Item;
using Source.Player;
using Source.SpaceShip;
using Source.Util;
using UnityEngine;

namespace EchoEnhanced.DroneLoadouts
{
    public sealed class DroneSwapResult
    {
        public bool Ok { get; set; }
        public string PresetName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int ShipsUpdated { get; set; }
        public int SlotsChanged { get; set; }
        public int LockedDronesSkipped { get; set; }
    }

    public sealed class DroneLoadoutEngine
    {
        private readonly EchoEnhancedConfig _config;
        private readonly object _sync = new object();

        public string StorePath { get; }
        public DroneLoadoutStore Store { get; private set; } = new DroneLoadoutStore();

        public string LastAppliedPreset { get; private set; } = string.Empty;
        public string LastSwapReason { get; private set; } = string.Empty;
        public float LastCombatSeenTime { get; private set; } = -999f;
        public bool InOpportunisticSalvage { get; private set; }

        public DroneLoadoutEngine(EchoEnhancedConfig config, string bepInExConfigDir)
        {
            _config = config;
            var dir = Path.Combine(bepInExConfigDir, "EchoEnhanced");
            Directory.CreateDirectory(dir);
            StorePath = Path.Combine(dir, "drone_loadouts.json");
            Load();
        }

        public void Load()
        {
            lock (_sync)
            {
                try
                {
                    if (!File.Exists(StorePath))
                    {
                        Store = new DroneLoadoutStore();
                        SaveInternal();
                        return;
                    }

                    var raw = File.ReadAllText(StorePath);
                    var parsed = JsonReader.Parse(raw);
                    if (parsed.IsJsonObject)
                    {
                        Store = DroneLoadoutStore.FromJson(parsed.AsJsonObject);
                    }
                    else
                    {
                        Store = new DroneLoadoutStore();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[DroneLoadouts] Failed to load {StorePath}: {ex.Message}");
                    Store = new DroneLoadoutStore();
                }
            }
        }

        public void Save()
        {
            lock (_sync)
            {
                SaveInternal();
            }
        }

        private void SaveInternal()
        {
            try
            {
                var json = Store.ToJson().ToString(pretty: true);
                File.WriteAllText(StorePath, json);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DroneLoadouts] Failed to save {StorePath}: {ex.Message}");
            }
        }

        public static bool CanSwapRightNow(out string reason)
        {
            if (GamePlayer.current == null)
            {
                reason = "No active player.";
                return false;
            }

            if (SpaceStationInterior.instance != null || PersonalHangar.arenaMode)
            {
                reason = "Docked";
                return true;
            }

            if (SkilltreeNode.dronesChangeDronesInField != null && SkilltreeNode.dronesChangeDronesInField.isActive)
            {
                reason = "In-field swap skill active";
                return true;
            }

            reason = "Requires the in-field drone swap skill (or dock at a station).";
            return false;
        }

        public static Dictionary<string, Drone> GetUnlockedDroneMap()
        {
            var map = new Dictionary<string, Drone>(StringComparer.OrdinalIgnoreCase);
            var gp = GamePlayer.current;
            if (gp == null) return map;

            var unlocked = gp.GetUnlockedDrones();
            if (unlocked == null) return map;

            foreach (var d in unlocked)
            {
                if (d == null) continue;
                var key = !string.IsNullOrEmpty(d.identifier) ? d.identifier : d.name;
                if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                {
                    map[key] = d;
                }
            }
            return map;
        }

        public static List<SpaceShipData> GetActiveFleetDroneShips()
        {
            var result = new List<SpaceShipData>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var gp = GamePlayer.current;
            if (gp == null) return result;

            void TryAdd(SpaceShipData shipData)
            {
                if (shipData == null || string.IsNullOrEmpty(shipData.guid)) return;
                if (!seen.Add(shipData.guid)) return;

                bool hasDroneBay = shipData.GetEquippedItem(EquipmentSlot.DroneBay) != null
                    || (shipData.unit is SpaceShip ss && ss.droneBayModule != null);
                if (hasDroneBay)
                {
                    result.Add(shipData);
                }
            }

            TryAdd(gp.currentSpaceShip);
            if (gp.activeFleet != null)
            {
                foreach (var ship in gp.activeFleet)
                {
                    TryAdd(ship);
                }
            }

            return result;
        }

        public DroneSwapResult CaptureCurrentFleetAsPreset(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName))
            {
                return new DroneSwapResult { Ok = false, Message = "Preset name cannot be empty." };
            }

            presetName = presetName.Trim();
            var ships = GetActiveFleetDroneShips();
            if (ships.Count == 0)
            {
                return new DroneSwapResult { Ok = false, PresetName = presetName, Message = "No active fleet ships with a Drone Bay found." };
            }

            int totalShipsInPreset;
            lock (_sync)
            {
                if (!Store.Presets.TryGetValue(presetName, out var preset))
                {
                    preset = new DronePreset { Name = presetName };
                    Store.Presets[presetName] = preset;
                }

                foreach (var shipData in ships)
                {
                    var slots = new List<string>();
                    if (shipData.droneSlots != null)
                    {
                        foreach (var d in shipData.droneSlots)
                        {
                            slots.Add(d != null ? (!string.IsNullOrEmpty(d.identifier) ? d.identifier : d.name) : string.Empty);
                        }
                    }

                    preset.Ships[shipData.guid] = new ShipDroneSlots
                    {
                        ShipGuid = shipData.guid,
                        ShipName = shipData.name ?? shipData.guid,
                        Slots = slots
                    };
                }

                totalShipsInPreset = preset.Ships.Count;
                SaveInternal();
            }

            Plugin.Log.LogInfo($"[DroneLoadouts] Captured preset '{presetName}': merged {ships.Count} active ship(s) ({totalShipsInPreset} total saved).");
            return new DroneSwapResult
            {
                Ok = true,
                PresetName = presetName,
                ShipsUpdated = ships.Count,
                Message = $"Saved '{presetName}' ({ships.Count} active, {totalShipsInPreset} total)."
            };
        }

        public bool DeletePreset(string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName)) return false;
            lock (_sync)
            {
                if (!Store.Presets.Remove(presetName.Trim())) return false;
                SaveInternal();
                return true;
            }
        }

        public DroneSwapResult ApplyPreset(string presetName, string reason = "Manual")
        {
            if (string.IsNullOrWhiteSpace(presetName))
            {
                return new DroneSwapResult { Ok = false, Message = "Preset name is empty." };
            }

            if (!CanSwapRightNow(out var blockReason))
            {
                return new DroneSwapResult { Ok = false, PresetName = presetName, Message = blockReason };
            }

            DronePreset preset;
            lock (_sync)
            {
                if (!Store.Presets.TryGetValue(presetName.Trim(), out preset))
                {
                    return new DroneSwapResult { Ok = false, PresetName = presetName, Message = $"Preset '{presetName}' not found." };
                }
            }

            var unlocked = GetUnlockedDroneMap();
            var fleetShips = GetActiveFleetDroneShips();
            var flagship = GameplayManager.Instance?.spaceShip;

            int shipsUpdated = 0;
            int slotsChanged = 0;
            int lockedSkipped = 0;

            foreach (var shipData in fleetShips)
            {
                if (!preset.Ships.TryGetValue(shipData.guid, out var desired))
                {
                    continue;
                }

                var liveShip = shipData.unit as SpaceShip;
                var droneBay = liveShip?.droneBayModule;
                int maxSlots = droneBay != null ? droneBay.droneAmount : desired.Slots.Count;
                if (maxSlots <= 0) maxSlots = desired.Slots.Count;

                if (shipData.droneSlots == null)
                {
                    continue;
                }

                bool shipChanged = false;
                int targetCount = Math.Min(maxSlots, desired.Slots.Count);

                for (int i = 0; i < targetCount; i++)
                {
                    var wantedId = desired.Slots[i];
                    Drone wantedDrone = null;

                    if (!string.IsNullOrWhiteSpace(wantedId))
                    {
                        if (!unlocked.TryGetValue(wantedId, out wantedDrone))
                        {
                            lockedSkipped++;
                            continue;
                        }
                    }

                    while (shipData.droneSlots.Count <= i)
                    {
                        shipData.droneSlots.Add(null);
                    }

                    if (shipData.droneSlots[i] != wantedDrone)
                    {
                        shipData.droneSlots[i] = wantedDrone;
                        shipChanged = true;
                        slotsChanged++;
                    }
                }

                if (shipChanged)
                {
                    shipsUpdated++;
                    if (liveShip != null)
                    {
                        bool shouldStayDeployed = droneBay != null
                            && !droneBay.playerRecalled
                            && !droneBay.returning
                            && (droneBay.shouldDeploy || droneBay.deploying || droneBay.drones.Any(d => d != null && d.isDeployed));

                        droneBay?.RebuildDroneLoadout();
                        liveShip.InvalidatePreferredGameplayTypeCache();

                        if (shouldStayDeployed && droneBay != null)
                        {
                            droneBay.playerRecalled = false;
                            droneBay.returning = false;
                            droneBay.shouldDeploy = true;
                            if (!droneBay.deploying && !liveShip.travelling && !liveShip.inShipYard && droneBay.active && droneBay.isActiveAndEnabled)
                            {
                                droneBay.DeployDrones();
                            }
                        }

                        if (liveShip == flagship)
                        {
                            IdleManager.ForceImmediateActivityCheck();
                        }
                    }
                }
            }

            LastAppliedPreset = preset.Name;
            LastSwapReason = reason;

            if (shipsUpdated > 0)
            {
                if (DroneLoadoutUI.active)
                {
                    DroneLoadoutUI.instance?.RefreshSlots();
                }
                Plugin.Log.LogInfo($"[DroneLoadouts] Applied '{preset.Name}' ({reason}): {shipsUpdated} ship(s), {slotsChanged} slot(s) changed, {lockedSkipped} locked skipped.");
            }

            return new DroneSwapResult
            {
                Ok = true,
                PresetName = preset.Name,
                ShipsUpdated = shipsUpdated,
                SlotsChanged = slotsChanged,
                LockedDronesSkipped = lockedSkipped,
                Message = shipsUpdated > 0
                    ? $"Applied '{preset.Name}' ({shipsUpdated} ship(s), {slotsChanged} slot(s))."
                    : $"Already on '{preset.Name}'."
            };
        }

        public static bool HasHostilesAtCurrentPoi()
        {
            var poiMgr = BasePoiManager.current;
            if (poiMgr == null) return false;

            var units = poiMgr.GetComponentsInChildren<AbstractUnit>();
            if (units == null) return false;

            foreach (var u in units)
            {
                if (u == null || !u.enabled) continue;
                if (u is Drone) continue;
                if (u.isBoardable) continue;
                if (u is CombatStationPart part && part.dungeonLocationData != null) continue;
                if (u.IsPlayerEnemy())
                {
                    return true;
                }
            }
            return false;
        }

        public static bool HasSalvageableWrecksAtCurrentPoi()
        {
            var poiMgr = BasePoiManager.current;
            if (poiMgr == null) return false;

            var containers = poiMgr.GetComponentsInChildren<SalvageContainer>();
            if (containers == null) return false;

            foreach (var sc in containers)
            {
                if (sc == null || sc.data == null) continue;
                if (sc.data.HasItems() || sc.data.HasScrap() || sc.data.HasStructuralContent())
                {
                    return true;
                }
            }
            return false;
        }

        public static bool HasReinforcementsPendingAtCurrentPoi()
        {
            var poi = Source.Galaxy.MapPointOfInterest.current;
            return poi != null && poi.hasPayload;
        }

        public bool ShouldHoldForPostMissionSalvage()
        {
            if (!_config.DroneLoadoutsEnabled.Value) return false;
            var bindings = Store.EchoBindings;
            if (bindings == null || !bindings.AutoSwapEnabled || !bindings.SwapToSalvageForWrecks || !bindings.HoldForPostMissionSalvage) return false;
            if (!HasPreset(bindings.SalvagePreset)) return false;
            if (!CanSwapRightNow(out _)) return false;

            var poi = Source.Galaxy.MapPointOfInterest.current;
            if (poi == null || poi is Source.Galaxy.POI.SpaceStation) return false;

            if (HasHostilesAtCurrentPoi() || HasReinforcementsPendingAtCurrentPoi()) return false;
            return HasSalvageableWrecksAtCurrentPoi();
        }

        public void EvaluateEchoAutoSwap()
        {
            if (!_config.DroneLoadoutsEnabled.Value) return;
            var bindings = Store.EchoBindings;
            if (bindings == null || !bindings.AutoSwapEnabled) return;
            if (!CanSwapRightNow(out _)) return;

            var gp = GamePlayer.current;
            if (gp == null) return;

            float now = Time.unscaledTime;
            bool hostilesPresent = HasHostilesAtCurrentPoi();
            bool reinforcementsPending = HasReinforcementsPendingAtCurrentPoi();

            // Priority 1: Active combat threat or pending reinforcement waves always win
            if (hostilesPresent || reinforcementsPending)
            {
                LastCombatSeenTime = now;
                InOpportunisticSalvage = false;
                TryEchoSwapTo(bindings.CombatPreset, hostilesPresent ? "ECHO: Hostiles detected" : "ECHO: Reinforcements pending");
                return;
            }

            // Hysteresis window after combat before swapping away from Combat
            if (now - LastCombatSeenTime < bindings.CombatClearHysteresisSeconds)
            {
                return;
            }

            // Priority 2: Opportunistic salvage when wrecks are present
            if (bindings.SwapToSalvageForWrecks && HasSalvageableWrecksAtCurrentPoi())
            {
                if (HasPreset(bindings.SalvagePreset))
                {
                    InOpportunisticSalvage = true;
                    TryEchoSwapTo(bindings.SalvagePreset, "ECHO: Salvageable wrecks present");
                    return;
                }
            }

            // Priority 3: Match ECHO's configured GameplayType mode
            InOpportunisticSalvage = false;
            var mode = gp.autopilotSettings != null ? gp.autopilotSettings.preferredGameplayType : GameplayType.Generic;
            string desiredPreset = mode switch
            {
                GameplayType.Mining => bindings.MiningPreset,
                GameplayType.Salvage => bindings.SalvagePreset,
                GameplayType.Combat => bindings.CombatPreset,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(desiredPreset))
            {
                TryEchoSwapTo(desiredPreset, $"ECHO: Mode {mode}");
            }
        }

        public void RestoreBeforeFindActivity()
        {
            if (!_config.DroneLoadoutsEnabled.Value) return;
            var bindings = Store.EchoBindings;
            if (bindings == null || !bindings.AutoSwapEnabled) return;
            if (!CanSwapRightNow(out _)) return;

            var gp = GamePlayer.current;
            if (gp == null) return;

            var mode = gp.autopilotSettings != null ? gp.autopilotSettings.preferredGameplayType : GameplayType.Generic;

            // If we swapped opportunistically to Salvage for wrecks and ECHO is in Autodetect (Generic),
            // restore Combat preset before FindActivityForEquipment() inspects preferredGameplayType.
            if (InOpportunisticSalvage && mode == GameplayType.Generic)
            {
                InOpportunisticSalvage = false;
                TryEchoSwapTo(bindings.CombatPreset, "ECHO: Restore before FindActivity");
            }
            else if (mode == GameplayType.Mining)
            {
                TryEchoSwapTo(bindings.MiningPreset, "ECHO: Restore Mining before FindActivity");
            }
            else if (mode == GameplayType.Salvage)
            {
                TryEchoSwapTo(bindings.SalvagePreset, "ECHO: Restore Salvage before FindActivity");
            }
            else if (mode == GameplayType.Combat)
            {
                TryEchoSwapTo(bindings.CombatPreset, "ECHO: Restore Combat before FindActivity");
            }
        }

        private bool HasPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            lock (_sync)
            {
                return Store.Presets.ContainsKey(name.Trim());
            }
        }

        private void TryEchoSwapTo(string presetName, string reason)
        {
            if (string.IsNullOrWhiteSpace(presetName)) return;
            if (string.Equals(LastAppliedPreset, presetName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!HasPreset(presetName)) return;

            ApplyPreset(presetName, reason);
        }

        public List<string> GetPresetNames()
        {
            lock (_sync)
            {
                return Store.Presets.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public JsonObject GetStatusObject()
        {
            bool canSwap = CanSwapRightNow(out var swapStatus);
            var presetsArr = new JsonArray();
            foreach (var name in GetPresetNames())
            {
                presetsArr.Add(name);
            }

            return new JsonObject
            {
                { "enabled", _config.DroneLoadoutsEnabled.Value },
                { "canSwapNow", canSwap },
                { "swapStatus", swapStatus },
                { "lastAppliedPreset", LastAppliedPreset ?? string.Empty },
                { "lastSwapReason", LastSwapReason ?? string.Empty },
                { "presets", presetsArr },
                { "echoBindings", Store.EchoBindings.ToJson() }
            };
        }

        public string GetStoreJson()
        {
            lock (_sync)
            {
                return Store.ToJson().ToString(pretty: true);
            }
        }

        public string UpdateConfigJson(string body)
        {
            try
            {
                var parsed = JsonReader.Parse(body ?? "{}");
                if (!parsed.IsJsonObject)
                {
                    return new JsonObject { { "ok", false }, { "error", "Expected JSON object." } }.ToString();
                }

                var obj = parsed.AsJsonObject;
                if (obj["enabled"].IsBoolean)
                {
                    _config.DroneLoadoutsEnabled.Value = obj["enabled"].AsBoolean;
                }

                lock (_sync)
                {
                    var b = Store.EchoBindings;
                    if (obj["autoSwapEnabled"].IsBoolean) b.AutoSwapEnabled = obj["autoSwapEnabled"].AsBoolean;
                    if (obj["swapToSalvageForWrecks"].IsBoolean) b.SwapToSalvageForWrecks = obj["swapToSalvageForWrecks"].AsBoolean;
                    if (obj["holdForPostMissionSalvage"].IsBoolean) b.HoldForPostMissionSalvage = obj["holdForPostMissionSalvage"].AsBoolean;
                    if (obj["combatPreset"].IsString) b.CombatPreset = obj["combatPreset"].AsString;
                    if (obj["salvagePreset"].IsString) b.SalvagePreset = obj["salvagePreset"].AsString;
                    if (obj["miningPreset"].IsString) b.MiningPreset = obj["miningPreset"].AsString;
                    SaveInternal();
                }

                return new JsonObject
                {
                    { "ok", true },
                    { "droneLoadouts", GetStatusObject() }
                }.ToString();
            }
            catch (Exception ex)
            {
                return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
            }
        }
    }
}
