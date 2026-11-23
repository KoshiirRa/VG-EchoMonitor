using System;
using System.Collections.Generic;
using System.Reflection;
using Behaviour.Item;
using Behaviour.Item.Usable;
using Behaviour.Managers;
using Behaviour.UI.Spacestation;
using Behaviour.Util;
using EchoMonitor.Patches;
using HarmonyLib;
using LightJson;
using Source.Galaxy;
using Source.Item;
using Source.MissionSystem;
using Source.Player;
using Source.Util;

namespace EchoMonitor
{
    public sealed class TelemetryCollector
    {
        private static readonly HashSet<string> DogTagIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "CommendationDogTag",
            "CommendationDogTag2",
            "CommendationDogTag3"
        };

        private static readonly FieldInfo EquipmentStateField =
            AccessTools.Field(typeof(InventoryItemType), "equipmentState");
        private static readonly FieldInfo EquipmentViewField =
            AccessTools.Field(typeof(InventoryItemType), "view");

        /// <summary>
        /// Must ONLY be called on the Unity main thread.
        /// </summary>
        public LiveTelemetry Collect(StuckDetector stuckDetector = null)
        {
            var gp = GamePlayer.current;
            bool hasActiveWorld = GameplayManager.initialized || SpaceStationInterior.instance != null;
            if (gp == null || !hasActiveWorld || gp.elapsedTime <= 0.0)
            {
                return new LiveTelemetry { InGame = false };
            }

            var echo = CollectEchoStatus(gp);
            var ship = GameplayManager.initialized && GameplayManager.Instance != null ? GameplayManager.Instance.spaceShip : null;
            var crew = CollectCrewStatus(gp);
            var droneBay = CollectDroneBayStatus(ship);
            var hangarBay = CollectHangarBayStatus(ship);
            var fleet = CollectFleetStatus(gp, ship);
            var alerts = stuckDetector != null ? stuckDetector.Evaluate(gp, ship) : new AlertsStatus();
            var snapshot = CollectPlayerSnapshot(gp);

            return new LiveTelemetry
            {
                InGame = true,
                Echo = echo,
                Crew = crew,
                DroneBay = droneBay,
                HangarBay = hangarBay,
                Fleet = fleet,
                Alerts = alerts,
                Snapshot = snapshot
            };
        }

        private static string TranslateIfNeeded(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (!value.StartsWith("@", StringComparison.Ordinal)) return value;
            try
            {
                string translated = Translation.Translate(value);
                return !string.IsNullOrEmpty(translated) ? translated : value;
            }
            catch
            {
                return value;
            }
        }

        private static EchoStatus CollectEchoStatus(GamePlayer gp)
        {
            var status = new EchoStatus
            {
                Enabled = gp.autoPlay,
                Docked = SpaceStationInterior.instance != null,
                SystemName = TranslateIfNeeded(gp.currentSystem?.name),
                PoiName = TranslateIfNeeded(gp.currentPointOfInterest?.name),
                PoiType = gp.currentPointOfInterest?.GetType().Name ?? string.Empty,
                ActiveMission = TranslateIfNeeded(ResolveActiveMissionName(gp))
            };

            // Determine ECHO state string
            bool isWarping = false;
            if (Singleton<TravelManager>.HasInstance)
            {
                var tm = Singleton<TravelManager>.Instance;
                if (tm != null)
                {
                    isWarping = tm.isWarping || tm.gateJumpActive || tm.fastLaneTravelActive || tm.emergencyJumpActive;
                    if (isWarping && tm.targetPoi != null)
                    {
                        if (string.IsNullOrEmpty(status.PoiName))
                            status.PoiName = TranslateIfNeeded(tm.targetPoi.name);
                        if (string.IsNullOrEmpty(status.SystemName))
                            status.SystemName = TranslateIfNeeded(tm.targetPoi.system?.name);
                    }
                }
            }

            if (!gp.autoPlay)
            {
                status.State = status.Docked ? "Docked (Manual)" : (isWarping ? "Warping (Manual)" : "Manual");
            }
            else if (!string.IsNullOrEmpty(EchoPatches.LastIdleStatus))
            {
                status.State = EchoPatches.LastIdleStatus;
            }
            else if (!string.IsNullOrEmpty(EchoPatches.LastIdleRawKey))
            {
                status.State = EchoPatches.LastIdleRawKey.TrimStart('@');
            }
            else if (isWarping)
            {
                status.State = "Warping";
            }
            else if (status.Docked)
            {
                status.State = "Docked";
            }
            else
            {
                status.State = "Active";
            }

            // Player ship hull, armor & shield
            if (GameplayManager.initialized && GameplayManager.Instance != null)
            {
                var ship = GameplayManager.Instance.spaceShip;
                if (ship != null)
                {
                    status.Hull = ship.currentHullHP;
                    status.MaxHull = ship.maxHullHP;
                    status.Armor = ship.currentArmorHP;
                    status.MaxArmor = ship.maxArmorHP;
                    status.Shield = ship.currentShieldHP;
                    status.MaxShield = ship.maxShieldHP;
                }
            }

            return status;
        }

        private static CrewStatus CollectCrewStatus(GamePlayer gp)
        {
            try
            {
                var shipData = gp.currentSpaceShip;
                if (shipData == null) return null;

                var crew = new CrewStatus
                {
                    MaxCrew = shipData.GetMaxGrunts()
                };

                if (shipData.crewData != null)
                {
                    crew.TotalCrew = shipData.crewData.totalCrew;
                    if (shipData.crewData.crew != null)
                    {
                        foreach (var kv in shipData.crewData.crew)
                        {
                            if (!string.IsNullOrEmpty(kv.Key) && kv.Value > 0)
                            {
                                crew.Roles[kv.Key] = kv.Value;
                            }
                        }
                    }
                }

                if (shipData.officers != null)
                {
                    foreach (var off in shipData.officers)
                    {
                        if (off == null) continue;
                        crew.Officers.Add(new OfficerInfo
                        {
                            Name = off.GetFullName() ?? string.Empty,
                            Profession = off.profession.ToString(),
                            Level = off.level,
                            Rarity = off.rarity.ToString()
                        });
                    }
                }

                return crew;
            }
            catch
            {
                return null;
            }
        }

        private static DroneBayStatus CollectDroneBayStatus(Behaviour.Unit.SpaceShip ship)
        {
            if (ship == null) return null;
            try
            {
                var db = ship.droneBayModule;
                if (db == null) return null;

                int deployedCount = 0;
                if (db.drones != null)
                {
                    foreach (var d in db.drones)
                    {
                        if (d != null) deployedCount++;
                    }
                }

                string stateStr;
                if (db.rebuilding) stateStr = "Rebuilding";
                else if (db.deploying) stateStr = "Deploying";
                else if (db.returning || db.AreDronesReturning()) stateStr = "Returning";
                else if (db.AreDronesDeployed() || deployedCount > 0) stateStr = "Deployed";
                else if (db.AllDronesDocked()) stateStr = "Docked";
                else stateStr = "Standby";

                return new DroneBayStatus
                {
                    Equipped = true,
                    MaxDrones = db.droneAmount,
                    BuiltDrones = db.CountBuiltDrones(),
                    DeployedDrones = deployedCount,
                    BehaviorMode = db.behaviorMode.ToString(),
                    Status = stateStr,
                    Rebuilding = db.rebuilding,
                    RebuildProgress = db.GetRebuildProgress()
                };
            }
            catch
            {
                return null;
            }
        }

        private static HangarBayStatus CollectHangarBayStatus(Behaviour.Unit.SpaceShip ship)
        {
            if (ship == null) return null;
            try
            {
                var hb = ship.hangarBayModule;
                if (hb == null) return null;

                var status = new HangarBayStatus
                {
                    Equipped = true,
                    MaxSlots = hb.hangarSlotCount,
                    Deployed = hb.CountDeployed(),
                    InBay = hb.CountInBay(),
                    Repairing = hb.CountRepairing(),
                    Rebuilding = hb.CountRebuilding()
                };

                // Map deployed live fighters by hangarSlotIndex
                var deployedBySlot = new Dictionary<int, Behaviour.Unit.SpaceShip>();
                var deployedList = hb.GetDeployedFighters();
                if (deployedList != null)
                {
                    foreach (var f in deployedList)
                    {
                        if (f != null) deployedBySlot[f.hangarSlotIndex] = f;
                    }
                }

                var rebuildingNames = new HashSet<string>(StringComparer.Ordinal);
                var rebList = hb.GetRebuildingFighters();
                if (rebList != null)
                {
                    foreach (var r in rebList)
                    {
                        if (!string.IsNullOrEmpty(r.Item1)) rebuildingNames.Add(r.Item1);
                    }
                }

                for (int i = 0; i < hb.hangarSlotCount; i++)
                {
                    var fData = hb.GetFighterData(i);
                    if (fData == null) continue;

                    string name = fData.GetShipName() ?? string.Empty;
                    string shipClass = TranslateIfNeeded(fData.shipClass?.displayName ?? fData.shipClass?.name ?? string.Empty);
                    string rarity = fData.shipRarity.ToString();

                    var entry = new HangarFighterStatus
                    {
                        Slot = i,
                        Name = name,
                        ShipClass = shipClass,
                        Rarity = rarity
                    };

                    if (deployedBySlot.TryGetValue(i, out var liveFighter) && liveFighter != null)
                    {
                        entry.Status = "Deployed";
                        entry.Hull = liveFighter.currentHullHP;
                        entry.MaxHull = liveFighter.maxHullHP;
                        entry.Armor = liveFighter.currentArmorHP;
                        entry.MaxArmor = liveFighter.maxArmorHP;
                        entry.Shield = liveFighter.currentShieldHP;
                        entry.MaxShield = liveFighter.maxShieldHP;
                    }
                    else if (rebuildingNames.Contains(name))
                    {
                        entry.Status = "Rebuilding";
                        entry.RepairFraction = hb.GetRebuildProgress();
                    }
                    else if (hb.IsAutoRepairing(i))
                    {
                        entry.Status = "Repairing";
                        if (hb.TryGetRepairFraction(i, out float frac))
                        {
                            entry.RepairFraction = frac;
                        }
                    }
                    else if (hb.IsResupplying(i))
                    {
                        entry.Status = "Resupplying";
                    }
                    else
                    {
                        entry.Status = "Docked";
                    }

                    status.Fighters.Add(entry);
                }

                return status;
            }
            catch
            {
                return null;
            }
        }

        private static List<FleetShipInfo> CollectFleetStatus(GamePlayer gp, Behaviour.Unit.SpaceShip flagshipLive)
        {
            var list = new List<FleetShipInfo>();
            var seenGuids = new HashSet<string>(StringComparer.Ordinal);

            void AddShip(Source.SpaceShip.SpaceShipData data, bool isFlagship, Behaviour.Unit.SpaceShip liveOverride)
            {
                if (data == null) return;
                string guid = data.guid ?? string.Empty;
                if (!string.IsNullOrEmpty(guid) && !seenGuids.Add(guid)) return;

                try
                {
                    var live = liveOverride ?? (data.unit as Behaviour.Unit.SpaceShip);
                    var info = new FleetShipInfo
                    {
                        Guid = guid,
                        Name = data.GetShipName() ?? string.Empty,
                        ShipClass = TranslateIfNeeded(data.shipClass?.displayName ?? data.shipClass?.name ?? string.Empty),
                        Rarity = data.shipRarity.ToString(),
                        IsFlagship = isFlagship,
                        AwayForRepairs = data.awayForRepairs,
                        RepairTimer = data.repairTimer
                    };

                    try
                    {
                        info.Stance = Behaviour.UI.HUD.Fleet.FleetShipStatus.StanceName(data.fleetStance) ?? string.Empty;
                        info.Activity = isFlagship
                            ? (EchoPatches.LastIdleStatus ?? string.Empty)
                            : (Behaviour.UI.HUD.Fleet.FleetShipStatus.ActivityText(data) ?? string.Empty);
                    }
                    catch { }

                    if (live != null)
                    {
                        info.Hull = live.currentHullHP;
                        info.MaxHull = live.maxHullHP;
                        info.Armor = live.currentArmorHP;
                        info.MaxArmor = live.maxArmorHP;
                        info.Shield = live.currentShieldHP;
                        info.MaxShield = live.maxShieldHP;
                        info.DroneBay = CollectDroneBayStatus(live);
                        info.HangarBay = CollectHangarBayStatus(live);
                    }
                    else
                    {
                        info.Hull = data.currentHullHP;
                        info.Armor = data.currentArmorHP;
                        info.Shield = data.currentShieldHP;
                    }

                    list.Add(info);
                }
                catch { }
            }

            AddShip(gp.currentSpaceShip, true, flagshipLive);
            if (gp.activeFleet != null)
            {
                foreach (var wingman in gp.activeFleet)
                {
                    if (wingman == null || wingman == gp.currentSpaceShip) continue;
                    AddShip(wingman, false, null);
                }
            }

            return list;
        }

        private static string ResolveActiveMissionName(GamePlayer gp)
        {
            Mission m = gp.infiniteMission;
            if (m == null) m = gp.currentBounty;
            if (m == null) m = gp.currentPatrol;
            if (m == null) m = gp.currentIndustry;
            if (m == null && gp.missions != null)
            {
                foreach (var item in gp.missions)
                {
                    if (item != null && !item.isComplete)
                    {
                        m = item;
                        break;
                    }
                }
            }
            return m?.name ?? string.Empty;
        }

        private static PlayerSnapshot CollectPlayerSnapshot(GamePlayer gp)
        {
            var snap = new PlayerSnapshot
            {
                Timestamp = DateTime.UtcNow.ToString("o"),
                SaveId = ResolveSaveId(gp),
                ElapsedHours = Math.Round(gp.elapsedTime / 3600.0, 4),
                Credits = gp.credits,
                VanguardMarks = gp.vanguardMarks,
                CommanderLevel = gp.commander != null ? gp.commander.level : 0,
                CommanderXp = gp.commander != null ? (long)Math.Round(gp.commander.experience) : 0L,
                CommanderProgressPct = gp.commander != null
                    ? (gp.commander.IsMaxLevel() ? 100.0 : (gp.commander.maxExperience > 0f ? Math.Clamp((gp.commander.experience / gp.commander.maxExperience) * 100.0, 0.0, 100.0) : 0.0))
                    : 0.0,
                EmpireLevel = gp.empireLevel != null ? gp.empireLevel.level : 0,
                EmpireXp = gp.empireLevel != null ? (long)Math.Round(gp.empireLevel.experience) : 0L,
                EmpireProgressPct = gp.empireLevel != null
                    ? (gp.empireLevel.isMaxLevel ? 100.0 : (gp.empireLevel.experienceToNext > 0.0 ? Math.Clamp((gp.empireLevel.experience / gp.empireLevel.experienceToNext) * 100.0, 0.0, 100.0) : 0.0))
                    : 0.0,
                Ranks = new Dictionary<string, int>
                {
                    ["Patrol"] = gp.patrolRank,
                    ["Bounty"] = gp.bountyRank,
                    ["Industry"] = gp.industryRank
                }
            };

            // Initialize dog tag buckets
            foreach (var dtId in DogTagIds)
            {
                snap.DogTagsByType[dtId] = 0;
            }

            // Faction reputation
            if (gp.factionData != null && Faction.all != null)
            {
                foreach (var f in Faction.all)
                {
                    if (f == null || f == Faction.player) continue;
                    string key = !string.IsNullOrEmpty(f.identifier) ? f.identifier : (f.name ?? "Unknown");
                    snap.Reputation[key] = gp.factionData.GetReputation(Faction.player, f);
                }
            }

            // Scan globalInventory, dataInventory, currentSpaceShip.cargo, and activeFleet cargo
            // so loot & blueprints are tracked continuously both in-flight and after docking.
            var inventories = new HashSet<Inventory>();
            if (gp.globalInventory != null) inventories.Add(gp.globalInventory);
            if (gp.dataInventory != null) inventories.Add(gp.dataInventory);
            if (gp.currentSpaceShip?.cargo != null) inventories.Add(gp.currentSpaceShip.cargo);
            if (gp.activeFleet != null)
            {
                foreach (var fleetShip in gp.activeFleet)
                {
                    if (fleetShip?.cargo != null) inventories.Add(fleetShip.cargo);
                }
            }

            foreach (var inv in inventories)
            {
                if (inv?.items == null) continue;
                foreach (var slot in inv.items)
                {
                    if (slot == null || slot.item == null || slot.count <= 0) continue;
                    try
                    {
                        var item = slot.item;
                        string id = item.identifier ?? string.Empty;

                        if (DogTagIds.Contains(id))
                        {
                            snap.DogTagsByType[id] += slot.count;
                            continue;
                        }

                        if (item.equipmentDefinition != null)
                        {
                            string name = TranslateIfNeeded(item.displayName ?? item.identifier ?? "Unknown");
                            string rarity = item.rarity.ToString();
                            int ilvl = item.itemLevel;
                            string eqType = item.equipmentDefinition.name ?? string.Empty;

                            ExtractModuleDetails(item, out var substats, out var aspects);
                            string sig = ComputeModuleSignature(substats, aspects);
                            string key = $"{name}{SessionManager.KeySep}{rarity}{SessionManager.KeySep}{ilvl}{SessionManager.KeySep}{eqType}{SessionManager.KeySep}{sig}";

                            snap.Modules[key] = snap.Modules.TryGetValue(key, out int prev) ? prev + slot.count : slot.count;
                            if (!snap.ModuleSubstats.ContainsKey(key))
                            {
                                snap.ModuleSubstats[key] = substats;
                                snap.ModuleAspects[key] = aspects;
                            }
                            continue;
                        }

                        if (item.TryGetPart<LootBoxItem>(out _) || item.TryGetState<LootBoxState>(out _))
                        {
                            string name = TranslateIfNeeded(item.displayName ?? item.identifier ?? "Locked Crate");
                            string rarity = item.rarity.ToString();
                            int ilvl = item.itemLevel;
                            string key = $"{name}{SessionManager.KeySep}{rarity}{SessionManager.KeySep}{ilvl}";
                            snap.LockedCrates[key] = snap.LockedCrates.TryGetValue(key, out int prev) ? prev + slot.count : slot.count;
                            continue;
                        }

                        if (item.TryGetState<ShipBlueprintState>(out var sbs) && sbs != null && !string.IsNullOrEmpty(sbs.shipId))
                        {
                            snap.ShipBlueprintPieces[sbs.shipId] =
                                snap.ShipBlueprintPieces.TryGetValue(sbs.shipId, out int prev) ? prev + slot.count : slot.count;
                            if (!snap.ShipDisplayNames.ContainsKey(sbs.shipId) && !string.IsNullOrEmpty(item.displayName))
                            {
                                snap.ShipDisplayNames[sbs.shipId] = TranslateIfNeeded(item.displayName);
                            }
                            continue;
                        }

                        if (item.TryGetState<BlueprintState>(out var bs) && bs != null && !string.IsNullOrEmpty(bs.blueprintName))
                        {
                            string disp = TranslateIfNeeded(item.displayName ?? bs.blueprintName);
                            string rarity = item.rarity.ToString();
                            string key = $"{disp}{SessionManager.KeySep}{bs.blueprintName}{SessionManager.KeySep}{bs.blueprintLevel}{SessionManager.KeySep}{rarity}";
                            snap.ModuleBlueprintItems[key] =
                                snap.ModuleBlueprintItems.TryGetValue(key, out int prev) ? prev + slot.count : slot.count;
                        }
                    }
                    catch { }
                }
            }

            // Unlocked ship blueprints
            if (gp.unlockedShipBlueprints != null)
            {
                foreach (var shipId in gp.unlockedShipBlueprints)
                {
                    if (!string.IsNullOrEmpty(shipId))
                    {
                        snap.UnlockedShipBlueprints.Add(shipId);
                    }
                }
                snap.UnlockedShipBlueprints.Sort(StringComparer.Ordinal);
            }

            // Uploaded blueprints
            if (gp.blueprintLevels != null)
            {
                foreach (var kv in gp.blueprintLevels)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                    {
                        snap.UploadedBlueprints[kv.Key] = kv.Value;
                    }
                }
            }

            // Captured ships
            if (gp.capturedShips != null)
            {
                foreach (var ship in gp.capturedShips)
                {
                    if (ship == null || string.IsNullOrEmpty(ship.guid)) continue;
                    string shipClass = ship.shipClass?.name ?? "Unknown";
                    string displayName = ship.shipClass?.displayName ?? shipClass;
                    snap.CapturedShips[ship.guid] = new CapturedShipInfo
                    {
                        ShipClass = shipClass,
                        DisplayName = displayName,
                        ShipRarity = ship.shipRarity.ToString(),
                        CustomName = ship.GetShipName() ?? string.Empty
                    };
                }
            }

            // Officer levels (for session levelDelta tracking)
            if (gp.currentSpaceShip?.officers != null)
            {
                foreach (var off in gp.currentSpaceShip.officers)
                {
                    if (off == null) continue;
                    string offName = off.GetFullName() ?? string.Empty;
                    if (!string.IsNullOrEmpty(offName))
                    {
                        snap.OfficerLevels[offName] = off.level;
                    }
                }
            }

            return snap;
        }

        private static void ExtractModuleDetails(
            InventoryItemType item,
            out List<string> substats,
            out List<ModuleAspectInfo> aspects)
        {
            substats = new List<string>();
            aspects = new List<ModuleAspectInfo>();

            // Rolled substats (avoiding GetEquipmentView() so we don't instantiate GameObjects)
            try
            {
                List<EquipStatLine> statLines = null;
                var view = EquipmentViewField?.GetValue(item) as Behaviour.Equipment.AbstractEquipment;
                if (item.isEquipmentInstance && view == null && EquipmentStateField != null)
                {
                    if (EquipmentStateField.GetValue(item) is JsonObject eqState)
                    {
                        var terms = EquipmentSearchTerms.FromJson(eqState);
                        statLines = terms.Item1;
                    }
                }
                else
                {
                    var eq = view ?? item.equipmentDefinition;
                    statLines = eq?.stats;
                }

                if (statLines != null)
                {
                    foreach (var sl in statLines)
                    {
                        string readable = sl.ToReadableString();
                        if (!string.IsNullOrWhiteSpace(readable))
                        {
                            substats.Add(readable.Trim());
                        }
                    }
                }
            }
            catch { }

            // Aspects
            try
            {
                var aspectSlots = item.GetAspectSlots();
                if (aspectSlots != null)
                {
                    foreach (var aSlot in aspectSlots)
                    {
                        var asp = aSlot?.equipAspect;
                        if (asp == null) continue;
                        string aspName = TranslateIfNeeded(asp.displayName ?? asp.name ?? string.Empty);
                        string aspDesc = asp.description ?? string.Empty;
                        aspects.Add(new ModuleAspectInfo
                        {
                            Name = aspName,
                            Description = aspDesc
                        });
                    }
                }
            }
            catch { }
        }

        private static string ComputeModuleSignature(List<string> substats, List<ModuleAspectInfo> aspects)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                void Feed(string s)
                {
                    if (string.IsNullOrEmpty(s)) return;
                    for (int i = 0; i < s.Length; i++)
                    {
                        hash ^= s[i];
                        hash *= 1099511628211UL;
                    }
                    hash ^= 0x1F;
                    hash *= 1099511628211UL;
                }

                if (substats != null)
                {
                    for (int i = 0; i < substats.Count; i++) Feed(substats[i]);
                }
                Feed("|");
                if (aspects != null)
                {
                    for (int i = 0; i < aspects.Count; i++)
                    {
                        Feed(aspects[i]?.Name);
                        Feed(aspects[i]?.Description);
                    }
                }
                return hash.ToString("x16");
            }
        }

        private static string ResolveSaveId(GamePlayer gp)
        {
            if (!string.IsNullOrWhiteSpace(EchoPatches.LoadedSaveName))
            {
                return EchoPatches.LoadedSaveName;
            }

            try
            {
                var latest = SaveGame.GetLatestSave();
                if (latest != null && !string.IsNullOrWhiteSpace(latest.Name))
                {
                    return latest.Name;
                }
            }
            catch { }

            string cmdr = gp.commander?.lastName ?? "Commander";
            string starter = gp.starterSpaceshipName ?? "Ship";
            return $"{cmdr}_{starter}";
        }
    }
}
