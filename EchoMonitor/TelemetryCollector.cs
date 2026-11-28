using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Behaviour.Item;
using Behaviour.Item.Usable;
using Behaviour.Managers;
using Behaviour.UI.Spacestation;
using Behaviour.Unit;
using Behaviour.Util;
using EchoMonitor.Patches;
using HarmonyLib;
using LightJson;
using Source.Galaxy;
using Source.Galaxy.POI;
using Source.Galaxy.Trade;
using Source.Item;
using Source.Mining;
using Source.MissionSystem;
using Source.Spacestation;
using Source.Player;
using Source.SpaceShip;
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

        private enum CachedItemKind
        {
            Other,
            DogTag,
            Module,
            LockedCrate,
            ShipBlueprint,
            ModuleBlueprint
        }

        private sealed class CachedItemMeta
        {
            public CachedItemKind Kind;
            public int ItemLevel;
            public string Key;
            public List<string> Substats;
            public List<ModuleAspectInfo> Aspects;
            public string ShipId;
            public string ShipSize;
            public string ShipDisplayName;
        }

        private sealed class CachedInventoryScan
        {
            public Inventory Inventory;
            public int SlotCount;
            public double SpaceUsed;
            public float LastScanTime;
            public CachedInventorySnapshot Snapshot;
        }

        private const float MinInventoryRescanIntervalSec = 5.0f;
        private const float SlowInventoryRescanIntervalSec = 60.0f;
        private const float EmpireScanIntervalSec = 5.0f;

        private static readonly ConditionalWeakTable<InventoryItemType, CachedItemMeta> ItemMetaCache =
            new ConditionalWeakTable<InventoryItemType, CachedItemMeta>();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> TranslationCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        private readonly CachedInventoryScan _globalInvCache = new CachedInventoryScan();
        private readonly CachedInventoryScan _dataInvCache = new CachedInventoryScan();

        private float _lastEmpireScanTime = -999f;
        private List<OutpostStorageInfo> _cachedOutposts;
        private int _cachedDepotCount;
        private double _cachedDepotSpaceUsed;
        private double _cachedDepotCapacity;
        private double _cachedDepotFillPct;
        private Dictionary<string, int> _cachedDepotRawMaterials;
        private Dictionary<string, int> _cachedDepotRefinedProducts;

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
            var snapshot = CollectPlayerSnapshot(gp);
            var empire = CollectEmpireStatus(gp, snapshot);
            var alerts = stuckDetector != null ? stuckDetector.Evaluate(gp, ship, empire?.Outposts) : new AlertsStatus();

            return new LiveTelemetry
            {
                InGame = true,
                Echo = echo,
                Crew = crew,
                DroneBay = droneBay,
                HangarBay = hangarBay,
                Fleet = fleet,
                Alerts = alerts,
                Empire = empire,
                Snapshot = snapshot
            };
        }

        private static List<OutpostJobInfo> CollectOutpostJobs(PlayerStation st)
        {
            var list = new List<OutpostJobInfo>();
            if (st == null) return list;

            try
            {
                if (st.isUpgrading)
                {
                    double dur = st.upgradeDuration;
                    double pct = dur > 0.0 ? Math.Round(Math.Max(0.0, Math.Min(100.0, (st.upgradeTimer / dur) * 100.0)), 1) : 0.0;
                    bool awaiting = !string.IsNullOrEmpty(st.guid) && SupplyNetwork.IsAwaitingSupplies(st.guid);
                    double rem = awaiting || dur <= 0.0 ? -1.0 : Math.Round(Math.Max(0.0, dur - st.upgradeTimer), 1);
                    list.Add(new OutpostJobInfo
                    {
                        JobType = "Upgrade",
                        Name = $"Upgrade to Tier {st.tier + 1}",
                        ProgressPct = pct,
                        InitialAmount = 1,
                        RemainingAmount = 1,
                        AwaitingSupplies = awaiting,
                        TimeRemainingSec = rem
                    });
                }

                foreach (var job in st.GetJobs())
                {
                    if (job == null) continue;
                    double pct = Math.Round(Math.Max(0.0, Math.Min(100.0, job.jobProgress * 100.0)), 1);

                    if (job is TradeGoodProductionJob prod)
                    {
                        list.Add(new OutpostJobInfo
                        {
                            JobType = "Production",
                            Name = TranslateIfNeeded(prod.jobName),
                            ProgressPct = pct,
                            InitialAmount = prod.initialAmount,
                            RemainingAmount = prod.remainingAmount,
                            Repeat = true,
                            Stalled = prod.stalled,
                            TimeRemainingSec = prod.stalled ? -1.0 : Math.Round(Math.Max(0.0, prod.timeUntilNextCycle), 1)
                        });
                    }
                    else if (job is ForgeJob fj)
                    {
                        bool waiting = fj.isWaiting || fj.isAwaitingSupplies;
                        double rem = waiting || fj.craftingTime <= 0f
                            ? -1.0
                            : Math.Round(Math.Max(0.0, (1f - fj.jobProgress) * fj.craftingTime), 1);
                        list.Add(new OutpostJobInfo
                        {
                            JobType = "Forge",
                            Name = TranslateIfNeeded(fj.jobName),
                            ProgressPct = pct,
                            InitialAmount = fj.initialAmount,
                            RemainingAmount = fj.remainingAmount,
                            Repeat = fj.repeat,
                            Stalled = fj.stalled,
                            Paused = fj.paused,
                            AwaitingSupplies = fj.isAwaitingSupplies,
                            TimeRemainingSec = rem
                        });
                    }
                    else if (job is RefineryJob rj)
                    {
                        bool waiting = rj.stalled || rj.isAwaitingSupplies;
                        double rem = waiting || rj.refineTime <= 0f
                            ? -1.0
                            : Math.Round(Math.Max(0.0, (1f - rj.jobProgress) * rj.refineTime), 1);
                        list.Add(new OutpostJobInfo
                        {
                            JobType = "Refinery",
                            Name = TranslateIfNeeded(rj.jobName),
                            ProgressPct = pct,
                            InitialAmount = rj.initialAmount,
                            RemainingAmount = rj.remainingAmount,
                            Stalled = rj.stalled,
                            AwaitingSupplies = rj.isAwaitingSupplies,
                            TimeRemainingSec = rem
                        });
                    }
                    else if (job is ConstructionYardJob cj)
                    {
                        bool awaiting = !string.IsNullOrEmpty(cj.supplyKey) && SupplyNetwork.IsAwaitingSupplies(cj.supplyKey);
                        double cPct = cj.totalDuration > 0f
                            ? Math.Round(Math.Max(0.0, Math.Min(100.0, (cj.elapsed / cj.totalDuration) * 100.0)), 1)
                            : pct;
                        double rem = cj.isQueued || awaiting || cj.totalDuration <= 0f
                            ? -1.0
                            : Math.Round(Math.Max(0.0, cj.totalDuration - cj.elapsed), 1);
                        list.Add(new OutpostJobInfo
                        {
                            JobType = "Construction",
                            Name = TranslateIfNeeded(cj.shipName),
                            ProgressPct = cPct,
                            InitialAmount = 1,
                            RemainingAmount = 1,
                            AwaitingSupplies = awaiting,
                            TimeRemainingSec = rem
                        });
                    }
                    else
                    {
                        string jType = job.GetType().Name.Replace("Job", "");
                        list.Add(new OutpostJobInfo
                        {
                            JobType = string.IsNullOrEmpty(jType) ? "Job" : jType,
                            Name = TranslateIfNeeded(job.jobName),
                            ProgressPct = pct,
                            InitialAmount = job.initialAmount,
                            RemainingAmount = job.remainingAmount,
                            TimeRemainingSec = -1.0
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"CollectOutpostJobs error for {st.name}: {ex.Message}");
            }

            return list;
        }

        private EmpireStatus CollectEmpireStatus(GamePlayer gp, PlayerSnapshot snapshot)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (_cachedOutposts != null && (now - _lastEmpireScanTime) < EmpireScanIntervalSec)
            {
                if (_cachedDepotRawMaterials != null) snapshot.DepotRawMaterials = _cachedDepotRawMaterials;
                if (_cachedDepotRefinedProducts != null) snapshot.DepotRefinedProducts = _cachedDepotRefinedProducts;
                return new EmpireStatus
                {
                    Outposts = _cachedOutposts,
                    DepotCount = _cachedDepotCount,
                    DepotSpaceUsed = _cachedDepotSpaceUsed,
                    DepotCapacity = _cachedDepotCapacity,
                    DepotFillPct = _cachedDepotFillPct
                };
            }

            _lastEmpireScanTime = now;
            var status = new EmpireStatus();
            try
            {
                var depots = new HashSet<SpaceStation>();
                foreach (var st in PlayerStation.GetAllOwned())
                {
                    if (st == null || st.materialStorage == null) continue;

                    double used = st.materialStorage.spaceUsed;
                    double cap = st.materialStorage.capacity;
                    double fillPct = cap > 0 ? Math.Round((used / cap) * 100.0, 1) : 0.0;

                    SpaceStation activeDepot = SupplyNetwork.GetActiveDepot(st.system?.sector)
                        ?? SupplyNetwork.GetDepot(st.system?.sector);
                    if (activeDepot != null && activeDepot.materialStorage != null)
                    {
                        depots.Add(activeDepot);
                    }

                    string largestId = string.Empty;
                    string largestName = string.Empty;
                    int largestCount = 0;
                    double largestM3 = 0.0;

                    foreach (var entry in st.materialStorage.items)
                    {
                        if (entry == null || entry.item == null || entry.count <= 0) continue;
                        double m3 = entry.spaceRequired;
                        if (m3 > largestM3 || (Math.Abs(m3 - largestM3) < 0.001 && entry.count > largestCount))
                        {
                            largestM3 = Math.Round(m3, 1);
                            largestCount = entry.count;
                            largestId = entry.item.identifier ?? string.Empty;
                            largestName = TranslateIfNeeded(!string.IsNullOrEmpty(entry.item.displayName)
                                ? entry.item.displayName
                                : entry.item.identifier);
                        }
                    }

                    var stationState = PlayerStationStatus.Get(st);
                    var jobs = CollectOutpostJobs(st);
                    bool hasStalled = PlayerStationStatus.HasStalledRepeatingJob(st)
                        || stationState == PlayerStationState.MissingMaterials
                        || stationState == PlayerStationState.StorageFull
                        || stationState == PlayerStationState.Raided;
                    int activeJobs = 0;
                    for (int ji = 0; ji < jobs.Count; ji++)
                    {
                        if (jobs[ji].Stalled) hasStalled = true;
                        if (!jobs[ji].Stalled && !jobs[ji].Paused) activeJobs++;
                    }

                    status.Outposts.Add(new OutpostStorageInfo
                    {
                        StationGuid = st.guid ?? string.Empty,
                        Name = TranslateIfNeeded(st.name),
                        SectorName = TranslateIfNeeded(st.system?.sector?.name),
                        SystemName = TranslateIfNeeded(st.system?.name),
                        StationType = st.stationType.ToString(),
                        Tier = st.tier,
                        SpaceUsed = Math.Round(used, 1),
                        Capacity = Math.Round(cap, 1),
                        FillPct = fillPct,
                        HasActiveDepot = activeDepot != null || gp.homeStation != null,
                        LargestStackItemId = largestId,
                        LargestStackItemName = largestName,
                        LargestStackCount = largestCount,
                        LargestStackM3 = largestM3,
                        State = stationState.ToString(),
                        HasStalledJob = hasStalled,
                        ActiveJobCount = activeJobs,
                        Jobs = jobs
                    });
                }

                status.Outposts.Sort((a, b) => b.FillPct.CompareTo(a.FillPct));

                // Also discover any configured supply depots across all sectors
                if (GalaxyMapData.current != null)
                {
                    foreach (var sector in GalaxyMapData.current.allSectors)
                    {
                        if (sector == null) continue;
                        var dep = SupplyNetwork.GetActiveDepot(sector) ?? SupplyNetwork.GetDepot(sector);
                        if (dep != null && dep.materialStorage != null)
                        {
                            depots.Add(dep);
                        }
                    }
                }

                // Fallback to homeStation if no sector supply depots are configured yet
                if (depots.Count == 0 && gp.homeStation != null && gp.homeStation.materialStorage != null)
                {
                    depots.Add(gp.homeStation);
                }

                status.DepotCount = depots.Count;
                double totalDepotUsed = 0.0;
                double totalDepotCap = 0.0;

                foreach (var depot in depots)
                {
                    if (depot?.materialStorage == null) continue;
                    totalDepotUsed += depot.materialStorage.spaceUsed;
                    totalDepotCap += depot.materialStorage.capacity;

                    foreach (var entry in depot.materialStorage.items)
                    {
                        if (entry == null || entry.item == null || entry.count <= 0) continue;
                        var cat = entry.item.itemCategory;
                        string itemId = entry.item.identifier ?? string.Empty;
                        string itemName = TranslateIfNeeded(!string.IsNullOrEmpty(entry.item.displayName)
                            ? entry.item.displayName
                            : itemId);

                        if (cat == ItemCategory.Ore || cat == ItemCategory.Salvage || cat == ItemCategory.Crystal)
                        {
                            string key = $"{itemId}\u001f{itemName}\u001f{cat}";
                            snapshot.DepotRawMaterials.TryGetValue(key, out int prev);
                            snapshot.DepotRawMaterials[key] = prev + entry.count;
                        }
                        else if (cat == ItemCategory.RefinedProduct)
                        {
                            string key = $"{itemId}\u001f{itemName}";
                            snapshot.DepotRefinedProducts.TryGetValue(key, out int prev);
                            snapshot.DepotRefinedProducts[key] = prev + entry.count;
                        }
                    }
                }

                status.DepotSpaceUsed = Math.Round(totalDepotUsed, 1);
                status.DepotCapacity = Math.Round(totalDepotCap, 1);
                status.DepotFillPct = totalDepotCap > 0 ? Math.Round((totalDepotUsed / totalDepotCap) * 100.0, 1) : 0.0;

                _cachedOutposts = status.Outposts;
                _cachedDepotCount = status.DepotCount;
                _cachedDepotSpaceUsed = status.DepotSpaceUsed;
                _cachedDepotCapacity = status.DepotCapacity;
                _cachedDepotFillPct = status.DepotFillPct;
                _cachedDepotRawMaterials = snapshot.DepotRawMaterials;
                _cachedDepotRefinedProducts = snapshot.DepotRefinedProducts;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"CollectEmpireStatus error: {ex.Message}");
            }

            return status;
        }

        public static DepotInventoryResponse CollectStationInventories()
        {
            var resp = new DepotInventoryResponse();
            var gp = GamePlayer.current;
            if (gp == null)
            {
                resp.InGame = false;
                return resp;
            }

            resp.InGame = true;

            try
            {
                var activeDepots = new HashSet<SpaceStation>();
                if (GalaxyMapData.current != null)
                {
                    foreach (var sector in GalaxyMapData.current.allSectors)
                    {
                        if (sector == null) continue;
                        var dep = SupplyNetwork.GetActiveDepot(sector) ?? SupplyNetwork.GetDepot(sector);
                        if (dep != null && dep.materialStorage != null)
                        {
                            activeDepots.Add(dep);
                        }
                    }
                }

                var seen = new HashSet<SpaceStation>();
                var candidates = new List<SpaceStation>();

                void AddCandidate(SpaceStation ss)
                {
                    if (ss != null && ss.materialStorage != null && seen.Add(ss))
                    {
                        candidates.Add(ss);
                    }
                }

                if (gp.homeStation != null) AddCandidate(gp.homeStation);
                foreach (var dep in activeDepots) AddCandidate(dep);
                foreach (var ps in PlayerStation.GetAllOwned()) AddCandidate(ps);

                if (GalaxyMapData.current != null)
                {
                    foreach (var sys in GalaxyMapData.current.allSystems)
                    {
                        if (sys?.pointsOfInterest == null) continue;
                        foreach (var poi in sys.pointsOfInterest)
                        {
                            if (poi is SpaceStation ss)
                            {
                                AddCandidate(ss);
                            }
                        }
                    }
                }

                foreach (var ss in candidates)
                {
                    bool isActiveDepot = activeDepots.Contains(ss);
                    bool isHome = gp.homeStation == ss;
                    bool isOutpost = ss is PlayerStation;

                    var byItemId = new Dictionary<string, StationInventoryItem>(StringComparer.Ordinal);
                    int totalCount = 0;
                    int rawCount = 0;
                    int refinedCount = 0;
                    int otherCount = 0;

                    if (ss.materialStorage.items != null)
                    {
                        foreach (var entry in ss.materialStorage.items)
                        {
                            if (entry == null || entry.item == null || entry.count <= 0) continue;
                            var cat = entry.item.itemCategory;
                            string group = (cat == ItemCategory.Ore || cat == ItemCategory.Salvage || cat == ItemCategory.Crystal)
                                ? "raw"
                                : (cat == ItemCategory.RefinedProduct ? "refined" : "other");

                            string itemId = entry.item.identifier ?? string.Empty;
                            string itemName = TranslateIfNeeded(!string.IsNullOrEmpty(entry.item.displayName)
                                ? entry.item.displayName
                                : itemId);
                            string key = !string.IsNullOrEmpty(itemId) ? itemId : itemName;

                            if (!byItemId.TryGetValue(key, out var itemInfo))
                            {
                                itemInfo = new StationInventoryItem
                                {
                                    ItemId = itemId,
                                    Name = itemName,
                                    Group = group,
                                    Category = cat.ToString(),
                                    Count = 0,
                                    SpaceM3 = 0.0
                                };
                                byItemId[key] = itemInfo;
                            }

                            itemInfo.Count += entry.count;
                            itemInfo.SpaceM3 = Math.Round(itemInfo.SpaceM3 + entry.spaceRequired, 2);

                            totalCount += entry.count;
                            if (group == "raw") rawCount += entry.count;
                            else if (group == "refined") refinedCount += entry.count;
                            else otherCount += entry.count;
                        }
                    }

                    // Skip empty NPC stations that aren't depots, home station, or player outposts
                    if (byItemId.Count == 0 && !isActiveDepot && !isHome && !isOutpost)
                    {
                        continue;
                    }

                    var items = new List<StationInventoryItem>(byItemId.Values);
                    items.Sort((a, b) =>
                    {
                        int cmp = b.Count.CompareTo(a.Count);
                        return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    });

                    double used = ss.materialStorage.spaceUsed;
                    double cap = ss.materialStorage.capacity;
                    double fillPct = cap > 0 ? Math.Round((used / cap) * 100.0, 1) : 0.0;

                    resp.Stations.Add(new StationInventoryInfo
                    {
                        StationGuid = ss.guid ?? string.Empty,
                        Name = TranslateIfNeeded(ss.name),
                        SystemName = TranslateIfNeeded(ss.system?.name),
                        SectorName = TranslateIfNeeded(ss.system?.sector?.name),
                        IsActiveDepot = isActiveDepot,
                        IsHomeStation = isHome,
                        IsPlayerOutpost = isOutpost,
                        SpaceUsed = Math.Round(used, 1),
                        Capacity = Math.Round(cap, 1),
                        FillPct = fillPct,
                        TotalItemCount = totalCount,
                        RawCount = rawCount,
                        RefinedCount = refinedCount,
                        OtherCount = otherCount,
                        Items = items
                    });
                }

                resp.Stations.Sort((a, b) =>
                {
                    int cmp = b.IsHomeStation.CompareTo(a.IsHomeStation);
                    if (cmp != 0) return cmp;
                    cmp = b.IsActiveDepot.CompareTo(a.IsActiveDepot);
                    if (cmp != 0) return cmp;
                    cmp = b.IsPlayerOutpost.CompareTo(a.IsPlayerOutpost);
                    if (cmp != 0) return cmp;
                    cmp = b.SpaceUsed.CompareTo(a.SpaceUsed);
                    if (cmp != 0) return cmp;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });

                resp.StationCount = resp.Stations.Count;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"CollectStationInventories error: {ex.Message}");
            }

            return resp;
        }

        private static string TranslateIfNeeded(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (!value.StartsWith("@", StringComparison.Ordinal)) return value;
            if (TranslationCache.TryGetValue(value, out string cached)) return cached;
            try
            {
                string translated = Translation.Translate(value);
                string result = !string.IsNullOrEmpty(translated) ? translated : value;
                TranslationCache[value] = result;
                return result;
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

        private PlayerSnapshot CollectPlayerSnapshot(GamePlayer gp)
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

            // Scan globalInventory and dataInventory via change-detected immutable snapshots
            // (merged off the main thread in SessionManager), while scanning active ship/fleet
            // cargo directly every tick. Per-item metadata & reflection results are cached via ConditionalWeakTable.
            if (gp.globalInventory != null)
            {
                ScanCachedInventory(gp.globalInventory, _globalInvCache, snap);
            }
            if (gp.dataInventory != null && !ReferenceEquals(gp.dataInventory, gp.globalInventory))
            {
                ScanCachedInventory(gp.dataInventory, _dataInvCache, snap);
            }

            var cargoInventories = new HashSet<Inventory>();
            if (gp.currentSpaceShip?.cargo != null &&
                !ReferenceEquals(gp.currentSpaceShip.cargo, gp.globalInventory) &&
                !ReferenceEquals(gp.currentSpaceShip.cargo, gp.dataInventory))
            {
                cargoInventories.Add(gp.currentSpaceShip.cargo);
            }
            if (gp.activeFleet != null)
            {
                foreach (var fleetShip in gp.activeFleet)
                {
                    var cargo = fleetShip?.cargo;
                    if (cargo != null &&
                        !ReferenceEquals(cargo, gp.globalInventory) &&
                        !ReferenceEquals(cargo, gp.dataInventory))
                    {
                        cargoInventories.Add(cargo);
                    }
                }
            }

            foreach (var inv in cargoInventories)
            {
                ScanInventoryIntoSnapshot(inv, snap);
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

        private static void ScanCachedInventory(Inventory inv, CachedInventoryScan cache, PlayerSnapshot snap)
        {
            if (inv?.items == null) return;

            int count = (inv.items as System.Collections.ICollection)?.Count ?? -1;
            double spaceUsed = inv.spaceUsed;
            float now = UnityEngine.Time.unscaledTime;
            float elapsed = now - cache.LastScanTime;

            bool sameInventory = ReferenceEquals(cache.Inventory, inv);
            bool changed = !sameInventory
                || cache.SlotCount != count
                || Math.Abs(cache.SpaceUsed - spaceUsed) > 0.0001;

            bool needsRescan = cache.Snapshot == null
                || !sameInventory
                || (changed && elapsed >= MinInventoryRescanIntervalSec)
                || elapsed >= SlowInventoryRescanIntervalSec;

            if (needsRescan)
            {
                var newSnap = new CachedInventorySnapshot();
                cache.Inventory = inv;
                cache.SlotCount = count;
                cache.SpaceUsed = spaceUsed;
                cache.LastScanTime = now;

                foreach (var slot in inv.items)
                {
                    if (slot == null || slot.item == null || slot.count <= 0) continue;
                    try
                    {
                        var meta = GetOrBuildItemMeta(slot.item);
                        ApplyItemMeta(
                            meta,
                            slot.count,
                            newSnap.DogTagsByType,
                            newSnap.Modules,
                            newSnap.ModuleSubstats,
                            newSnap.ModuleAspects,
                            newSnap.LockedCrates,
                            newSnap.ShipBlueprintPieces,
                            newSnap.ShipSizes,
                            newSnap.ShipDisplayNames,
                            newSnap.ModuleBlueprintItems);
                    }
                    catch { }
                }

                cache.Snapshot = newSnap;
            }

            if (cache.Snapshot != null)
            {
                if (snap.PendingInventorySnapshots == null)
                {
                    snap.PendingInventorySnapshots = new List<CachedInventorySnapshot>(2);
                }
                snap.PendingInventorySnapshots.Add(cache.Snapshot);
            }
        }

        private static void ScanInventoryIntoSnapshot(Inventory inv, PlayerSnapshot snap)
        {
            if (inv?.items == null) return;
            foreach (var slot in inv.items)
            {
                if (slot == null || slot.item == null || slot.count <= 0) continue;
                try
                {
                    var meta = GetOrBuildItemMeta(slot.item);
                    ApplyItemMeta(
                        meta,
                        slot.count,
                        snap.DogTagsByType,
                        snap.Modules,
                        snap.ModuleSubstats,
                        snap.ModuleAspects,
                        snap.LockedCrates,
                        snap.ShipBlueprintPieces,
                        snap.ShipSizes,
                        snap.ShipDisplayNames,
                        snap.ModuleBlueprintItems);
                }
                catch { }
            }
        }

        public static void MergePendingInventories(PlayerSnapshot snap)
        {
            if (snap?.PendingInventorySnapshots == null || snap.PendingInventorySnapshots.Count == 0)
            {
                return;
            }

            var pending = snap.PendingInventorySnapshots;
            snap.PendingInventorySnapshots = null;

            for (int i = 0; i < pending.Count; i++)
            {
                MergeCachedInventory(pending[i], snap);
            }
        }

        private static void MergeCachedInventory(CachedInventorySnapshot cache, PlayerSnapshot snap)
        {
            foreach (var kv in cache.DogTagsByType)
            {
                snap.DogTagsByType[kv.Key] = snap.DogTagsByType.TryGetValue(kv.Key, out int prev) ? prev + kv.Value : kv.Value;
            }
            foreach (var kv in cache.Modules)
            {
                snap.Modules[kv.Key] = snap.Modules.TryGetValue(kv.Key, out int prev) ? prev + kv.Value : kv.Value;
            }
            foreach (var kv in cache.ModuleSubstats)
            {
                if (!snap.ModuleSubstats.ContainsKey(kv.Key))
                    snap.ModuleSubstats[kv.Key] = kv.Value;
            }
            foreach (var kv in cache.ModuleAspects)
            {
                if (!snap.ModuleAspects.ContainsKey(kv.Key))
                    snap.ModuleAspects[kv.Key] = kv.Value;
            }
            foreach (var kv in cache.LockedCrates)
            {
                snap.LockedCrates[kv.Key] = snap.LockedCrates.TryGetValue(kv.Key, out int prev) ? prev + kv.Value : kv.Value;
            }
            foreach (var kv in cache.ShipBlueprintPieces)
            {
                snap.ShipBlueprintPieces[kv.Key] = snap.ShipBlueprintPieces.TryGetValue(kv.Key, out int prev) ? prev + kv.Value : kv.Value;
            }
            foreach (var kv in cache.ShipSizes)
            {
                if (!snap.ShipSizes.ContainsKey(kv.Key))
                    snap.ShipSizes[kv.Key] = kv.Value;
            }
            foreach (var kv in cache.ShipDisplayNames)
            {
                if (!snap.ShipDisplayNames.ContainsKey(kv.Key))
                    snap.ShipDisplayNames[kv.Key] = kv.Value;
            }
            foreach (var kv in cache.ModuleBlueprintItems)
            {
                snap.ModuleBlueprintItems[kv.Key] = snap.ModuleBlueprintItems.TryGetValue(kv.Key, out int prev) ? prev + kv.Value : kv.Value;
            }
        }

        private static void ApplyItemMeta(
            CachedItemMeta meta,
            int count,
            Dictionary<string, int> dogTags,
            Dictionary<string, int> modules,
            Dictionary<string, List<string>> moduleSubstats,
            Dictionary<string, List<ModuleAspectInfo>> moduleAspects,
            Dictionary<string, int> lockedCrates,
            Dictionary<string, int> shipBpPieces,
            Dictionary<string, string> shipSizes,
            Dictionary<string, string> shipDisplayNames,
            Dictionary<string, int> moduleBpItems)
        {
            switch (meta.Kind)
            {
                case CachedItemKind.DogTag:
                    dogTags[meta.Key] = dogTags.TryGetValue(meta.Key, out int dtPrev) ? dtPrev + count : count;
                    break;
                case CachedItemKind.Module:
                    modules[meta.Key] = modules.TryGetValue(meta.Key, out int modPrev) ? modPrev + count : count;
                    if (!moduleSubstats.ContainsKey(meta.Key))
                    {
                        moduleSubstats[meta.Key] = meta.Substats;
                        moduleAspects[meta.Key] = meta.Aspects;
                    }
                    break;
                case CachedItemKind.LockedCrate:
                    lockedCrates[meta.Key] = lockedCrates.TryGetValue(meta.Key, out int cratePrev) ? cratePrev + count : count;
                    break;
                case CachedItemKind.ShipBlueprint:
                    shipBpPieces[meta.ShipId] = shipBpPieces.TryGetValue(meta.ShipId, out int sbpPrev) ? sbpPrev + count : count;
                    if (!string.IsNullOrEmpty(meta.ShipSize) && !shipSizes.ContainsKey(meta.ShipId))
                        shipSizes[meta.ShipId] = meta.ShipSize;
                    if (!string.IsNullOrEmpty(meta.ShipDisplayName) && !shipDisplayNames.ContainsKey(meta.ShipId))
                        shipDisplayNames[meta.ShipId] = meta.ShipDisplayName;
                    break;
                case CachedItemKind.ModuleBlueprint:
                    moduleBpItems[meta.Key] = moduleBpItems.TryGetValue(meta.Key, out int mbpPrev) ? mbpPrev + count : count;
                    break;
            }
        }

        private static CachedItemMeta GetOrBuildItemMeta(InventoryItemType item)
        {
            if (ItemMetaCache.TryGetValue(item, out var cached))
            {
                if (cached.Kind != CachedItemKind.Module || cached.ItemLevel == item.itemLevel)
                {
                    return cached;
                }
                ItemMetaCache.Remove(item);
            }

            var meta = BuildItemMeta(item);
            ItemMetaCache.Add(item, meta);
            return meta;
        }

        private static CachedItemMeta BuildItemMeta(InventoryItemType item)
        {
            string id = item.identifier ?? string.Empty;

            if (DogTagIds.Contains(id))
            {
                return new CachedItemMeta { Kind = CachedItemKind.DogTag, Key = id };
            }

            if (item.equipmentDefinition != null)
            {
                string name = TranslateIfNeeded(item.displayName ?? item.identifier ?? "Unknown");
                string rarity = item.rarity.ToString();
                int ilvl = item.itemLevel;
                string eqType = TranslateIfNeeded(item.equipmentDefinition.typeDisplayName);
                if (string.IsNullOrEmpty(eqType))
                    eqType = TranslateIfNeeded("@EquipSlot" + item.equipmentDefinition.slot);
                if (string.IsNullOrEmpty(eqType))
                    eqType = item.equipmentDefinition.name ?? string.Empty;
                string size = TranslateIfNeeded(item.equipmentDefinition.size.GetDisplayName());
                if (string.IsNullOrEmpty(size))
                    size = item.equipmentDefinition.size.ToString();

                ExtractModuleDetails(item, out var substats, out var aspects);
                string sig = ComputeModuleSignature(substats, aspects);
                string key = $"{name}{SessionManager.KeySep}{rarity}{SessionManager.KeySep}{ilvl}{SessionManager.KeySep}{eqType}{SessionManager.KeySep}{size}{SessionManager.KeySep}{sig}";

                return new CachedItemMeta
                {
                    Kind = CachedItemKind.Module,
                    ItemLevel = ilvl,
                    Key = key,
                    Substats = substats,
                    Aspects = aspects
                };
            }

            if (item.TryGetPart<LootBoxItem>(out _) || item.TryGetState<LootBoxState>(out _))
            {
                string name = TranslateIfNeeded(item.displayName ?? item.identifier ?? "Locked Crate");
                string rarity = item.rarity.ToString();
                int ilvl = item.itemLevel;
                string key = $"{name}{SessionManager.KeySep}{rarity}{SessionManager.KeySep}{ilvl}";
                return new CachedItemMeta { Kind = CachedItemKind.LockedCrate, Key = key };
            }

            if (item.TryGetState<ShipBlueprintState>(out var sbs) && sbs != null && !string.IsNullOrEmpty(sbs.shipId))
            {
                string shipSize = string.Empty;
                string shipDisp = string.Empty;
                var spaceShip = SpaceShip.Get(sbs.shipId);
                if (spaceShip != null)
                {
                    shipSize = spaceShip.shipRoleType.GetShipType().ToString();
                    if (!string.IsNullOrEmpty(spaceShip.originalName))
                    {
                        shipDisp = TranslateIfNeeded(spaceShip.originalName);
                    }
                }
                if (string.IsNullOrEmpty(shipDisp) && !string.IsNullOrEmpty(item.displayName))
                {
                    shipDisp = TranslateIfNeeded(item.displayName);
                }
                return new CachedItemMeta
                {
                    Kind = CachedItemKind.ShipBlueprint,
                    ShipId = sbs.shipId,
                    ShipSize = shipSize,
                    ShipDisplayName = shipDisp
                };
            }

            if (item.TryGetState<BlueprintState>(out var bs) && bs != null && !string.IsNullOrEmpty(bs.blueprintName))
            {
                string disp = TranslateIfNeeded(item.displayName ?? bs.blueprintName);
                string rarity = item.rarity.ToString();
                string key = $"{disp}{SessionManager.KeySep}{bs.blueprintName}{SessionManager.KeySep}{bs.blueprintLevel}{SessionManager.KeySep}{rarity}";
                return new CachedItemMeta { Kind = CachedItemKind.ModuleBlueprint, Key = key };
            }

            return new CachedItemMeta { Kind = CachedItemKind.Other };
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
            string cmdr = gp.commander?.lastName ?? "Commander";
            string starter = gp.starterSpaceshipName ?? "Ship";
            return $"{cmdr}_{starter}";
        }
    }
}
