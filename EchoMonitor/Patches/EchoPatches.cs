using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Behaviour.Item;
using HarmonyLib;
using Source.Galaxy.POI;
using Source.Galaxy.Trade;
using Source.MissionSystem;
using Source.Player;
using Source.SpaceShip;
using Source.Util;

namespace EchoMonitor.Patches
{
    public static class EchoPatches
    {
        public static string LastIdleStatus { get; private set; } = string.Empty;
        public static string LastIdleRawKey { get; private set; } = string.Empty;
        public static Action<string, string> OnPlayerEmergencyWarp { get; set; }
        public static Action<MissionLogEntry> OnMissionEvent { get; set; }
        public static Action<string, int> OnEmpireRefined { get; set; }
        public static Action<string, int> OnEmpireProduced { get; set; }
        public static Action<string, int> OnEmpireCrafted { get; set; }
        public static Action<long, long, long> OnEmpireTradeRun { get; set; }
        public static Action<long> OnEmpireSale { get; set; }
        public static Action<KillRecord> OnEnemyKilled { get; set; }
        public static Action<WaveRecord> OnWaveSpawned { get; set; }

        private static readonly ConcurrentDictionary<Mission, string> PendingFailReasons = new ConcurrentDictionary<Mission, string>();

        public static void Apply(Harmony harmony)
        {
            TryPatch(
                harmony,
                AccessTools.Method("Behaviour.UI.Side_Menu.SidePanel:SetIdleStatus", new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(SetIdleStatusPostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Behaviour.Gameplay.IdleManager:UpdateActivity", new[] { typeof(string), typeof(object[]) }),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(UpdateActivityPostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Behaviour.Unit.SpaceShip:TryEmergencyJump"),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(TryEmergencyJumpPostfix)),
                prefix: new HarmonyMethod(typeof(EchoPatches), nameof(TryEmergencyJumpPrefix)));

            TryPatch(
                harmony,
                AccessTools.Method("Source.Player.GamePlayer:AcceptMission", new[] { typeof(Mission) }),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(AcceptMissionPostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Source.Player.GamePlayer:RemoveMission", new[] { typeof(Mission), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(RemoveMissionPostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Source.MissionSystem.Mission:MissionFailed", new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(MissionFailedPostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Source.Galaxy.EmpireLogStations:OnRefined", new[] { typeof(PlayerStation), typeof(InventoryItemType), typeof(int) }),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(EmpireOnRefinedPostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Source.Galaxy.EmpireLogStations:OnWorkBayDelivered"),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(EmpireOnWorkBayDeliveredPostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Source.Galaxy.EmpireLogStations:OnTradeRunFinished", new[] { typeof(PlayerStation), typeof(SpaceShipData), typeof(TradeRun) }),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(EmpireOnTradeRunFinishedPostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Source.Galaxy.EmpireLogStations:OnSale", new[] { typeof(PlayerStation), typeof(long) }),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(EmpireOnSalePostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Source.Galaxy.EmpireLogStations:OnCrafted", new[] { typeof(PlayerStation), typeof(InventoryItemType), typeof(int) }),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(EmpireOnCraftedPostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Behaviour.Unit.AbstractUnit:HandleHullDestroyed"),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(EnemyShipRemovedPostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Source.Galaxy.MapTriggeredPayload:TriggerPayload"),
                postfix: null,
                prefix: new HarmonyMethod(typeof(EchoPatches), nameof(TriggerPayloadPrefix)));
        }

        private static void TryPatch(Harmony harmony, MethodBase target, HarmonyMethod postfix, HarmonyMethod prefix = null)
        {
            if (target == null)
            {
                Plugin.Log?.LogWarning("EchoPatches: target method not found for optional patch.");
                return;
            }
            try
            {
                harmony.Patch(target, prefix: prefix, postfix: postfix);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"EchoPatches: failed to patch {target.DeclaringType?.Name}.{target.Name}: {ex.Message}");
            }
        }

        private static void SetIdleStatusPostfix(string msg)
        {
            if (!string.IsNullOrWhiteSpace(msg))
            {
                LastIdleStatus = msg.Trim();
            }
        }

        private static void UpdateActivityPostfix(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                LastIdleRawKey = text.Trim();
            }
        }

        private static void TryEmergencyJumpPrefix(out bool __state)
        {
            __state = Source.Player.GamePlayer.current?.emergencyJump ?? false;
        }

        private static void TryEmergencyJumpPostfix(bool __result, Behaviour.Unit.SpaceShip __instance, bool __state)
        {
            if (!__result || __instance == null || __state) return;
            try
            {
                if (!__instance.IsPlayer(true)) return;
                var gp = Source.Player.GamePlayer.current;
                if (gp == null || !gp.emergencyJump) return;
                string poiName = gp.currentPointOfInterest?.name ?? string.Empty;
                string missionName = gp.infiniteMission?.name
                    ?? gp.currentBounty?.name
                    ?? gp.currentPatrol?.name
                    ?? gp.currentIndustry?.name
                    ?? string.Empty;
                OnPlayerEmergencyWarp?.Invoke(poiName, missionName);
            }
            catch { }
        }

        private static void AcceptMissionPostfix(Mission mission)
        {
            EmitMissionEvent(mission, "Accepted", string.Empty);
        }

        private static void MissionFailedPostfix(Mission __instance, string reason)
        {
            if (__instance == null) return;
            PendingFailReasons[__instance] = reason ?? string.Empty;
        }

        private static void RemoveMissionPostfix(Mission mission, bool completed)
        {
            if (mission == null) return;
            PendingFailReasons.TryRemove(mission, out string failReason);
            string evt = completed ? "Completed" : (mission.failed ? "Failed" : "Abandoned");
            EmitMissionEvent(mission, evt, failReason ?? string.Empty);
        }

        private static void EmitMissionEvent(Mission mission, string evt, string reason)
        {
            if (mission == null || OnMissionEvent == null) return;
            try
            {
                string rawName = mission.name ?? string.Empty;
                string name = rawName.StartsWith("@", StringComparison.Ordinal)
                    ? (Translation.Translate(rawName) ?? rawName)
                    : rawName;
                string translatedReason = !string.IsNullOrEmpty(reason) && reason.StartsWith("@", StringComparison.Ordinal)
                    ? (Translation.Translate(reason) ?? reason)
                    : (reason ?? string.Empty);

                string stationName = string.Empty;
                string missionPoiName = string.Empty;
                string systemName = string.Empty;
                string sectorName = string.Empty;
                var boardMissions = new List<MissionBoardEntry>();
                var targetPoi = mission.GetActivePoi() ?? mission.mostRecentStep?.GetMissionPoi();
                if (targetPoi != null)
                {
                    missionPoiName = TranslateIfNeeded(targetPoi.name);
                }
                var gp = GamePlayer.current;
                if (gp != null)
                {
                    var sys = targetPoi?.system ?? gp.currentSystem;
                    if (gp.currentPointOfInterest is SpaceStation st)
                    {
                        stationName = TranslateIfNeeded(st.name);
                        if (sys == null) sys = st.system;

                        if (string.Equals(evt, "Accepted", StringComparison.Ordinal) && st.missionBoard != null)
                        {
                            var avail = st.missionBoard.availableMissions;
                            if (avail != null)
                            {
                                for (int i = 0; i < avail.Count; i++)
                                {
                                    var bm = BuildBoardEntry(avail[i], isUmbral: false);
                                    if (bm != null) boardMissions.Add(bm);
                                }
                            }
                            if (st.missionBoard.umbralMission != null)
                            {
                                var ubm = BuildBoardEntry(st.missionBoard.umbralMission, isUmbral: true);
                                if (ubm != null) boardMissions.Add(ubm);
                            }
                        }
                    }
                    if (sys != null)
                    {
                        systemName = TranslateIfNeeded(sys.name);
                        sectorName = TranslateIfNeeded(sys.sector?.name);
                    }
                }

                OnMissionEvent.Invoke(new MissionLogEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Event = evt,
                    MissionName = name,
                    GameplayType = mission.gameplayType.ToString(),
                    IsStory = !string.IsNullOrWhiteSpace(mission.storyId),
                    Reason = translatedReason,
                    StationName = stationName,
                    MissionPoiName = missionPoiName,
                    SystemName = systemName,
                    SectorName = sectorName,
                    BoardMissions = boardMissions
                });
            }
            catch { }
        }

        private static MissionBoardEntry BuildBoardEntry(Mission m, bool isUmbral)
        {
            if (m == null) return null;
            var tags = new List<string>();
            var gt = m.gameplayType;
            if (gt == 0) gt = m.primaryGameplayType;
            if (gt != 0) tags.Add(gt.ToString());
            tags.Add(m.difficulty.ToString());
            if (m.targetLayer.HasValue) tags.Add(m.targetLayer.Value.ToString());
            string cat = TranslateIfNeeded(m.category);
            if (!string.IsNullOrWhiteSpace(cat) && !tags.Contains(cat)) tags.Add(cat);
            if (m.canBeIdled) tags.Add("ECHO");
            if (!string.IsNullOrWhiteSpace(m.storyId)) tags.Add("Story");
            if (isUmbral) tags.Add("Umbral");
            return new MissionBoardEntry
            {
                Name = TranslateIfNeeded(m.name),
                Tags = tags
            };
        }

        private static string TranslateIfNeeded(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            return raw.StartsWith("@", StringComparison.Ordinal) ? (Translation.Translate(raw) ?? raw) : raw;
        }

        private static string ResolveItemName(InventoryItemType item)
        {
            if (item == null) return "Unknown";
            string raw = !string.IsNullOrEmpty(item.displayName) ? item.displayName : item.identifier;
            if (string.IsNullOrEmpty(raw)) return "Unknown";
            return raw.StartsWith("@", StringComparison.Ordinal) ? (Translation.Translate(raw) ?? raw) : raw;
        }

        private static void EmpireOnRefinedPostfix(PlayerStation station, InventoryItemType ore, int amount)
        {
            if (station == null || ore == null || amount <= 0 || OnEmpireRefined == null) return;
            try
            {
                OnEmpireRefined.Invoke(ResolveItemName(ore), amount);
            }
            catch { }
        }

        private static void EmpireOnWorkBayDeliveredPostfix(PlayerStation station, IEnumerable<(InventoryItemType item, int amount)> gathered)
        {
            if (station == null || gathered == null || OnEmpireProduced == null) return;
            try
            {
                foreach (var (item, amount) in gathered)
                {
                    if (item != null && amount > 0)
                    {
                        OnEmpireProduced.Invoke(ResolveItemName(item), amount);
                    }
                }
            }
            catch { }
        }

        private static void EmpireOnTradeRunFinishedPostfix(PlayerStation station, SpaceShipData ship, TradeRun run)
        {
            if (station == null || run == null || OnEmpireTradeRun == null) return;
            try
            {
                if (run.earned > 0 || run.spent > 0)
                {
                    OnEmpireTradeRun.Invoke(run.earned, run.spent, run.profit);
                }
            }
            catch { }
        }

        private static void EmpireOnSalePostfix(PlayerStation station, long credits)
        {
            if (station == null || credits <= 0 || OnEmpireSale == null) return;
            try
            {
                OnEmpireSale.Invoke(credits);
            }
            catch { }
        }

        private static void EmpireOnCraftedPostfix(PlayerStation station, InventoryItemType item, int amount)
        {
            if (station == null || item == null || amount <= 0 || OnEmpireCrafted == null) return;
            try
            {
                OnEmpireCrafted.Invoke(ResolveItemName(item), amount);
            }
            catch { }
        }

        private static string GetActiveMissionName(GamePlayer gp)
        {
            if (gp == null) return string.Empty;
            string raw = gp.infiniteMission?.name
                ?? gp.currentBounty?.name
                ?? gp.currentPatrol?.name
                ?? gp.currentIndustry?.name
                ?? string.Empty;
            return TranslateIfNeeded(raw);
        }

        private static string ResolveUnitClassName(Behaviour.Unit.AbstractUnit unit, Source.Data.AbstractUnitData data)
        {
            if (data is SpaceShipData ssd && ssd.shipClass != null)
            {
                string dn = TranslateIfNeeded(ssd.shipClass.displayName);
                if (!string.IsNullOrWhiteSpace(dn)) return dn;
                if (!string.IsNullOrWhiteSpace(ssd.shipClass.name)) return ssd.shipClass.name;
            }
            if (data?.unitDefinition != null)
            {
                string raw = TranslateIfNeeded(data.unitDefinition.name);
                if (!string.IsNullOrWhiteSpace(raw)) return raw;
            }
            if (unit != null)
            {
                string uName = unit.name ?? string.Empty;
                if (uName.EndsWith("(Clone)", StringComparison.Ordinal))
                {
                    uName = uName.Substring(0, uName.Length - 7).Trim();
                }
                if (!string.IsNullOrWhiteSpace(uName)) return uName;
            }
            return "Unknown";
        }

        private static void EnemyShipRemovedPostfix(Behaviour.Unit.AbstractUnit __instance, Behaviour.Weapons.DamageData damageData)
        {
            if (__instance == null || OnEnemyKilled == null) return;
            try
            {
                if (__instance is Behaviour.Unit.Torpedo) return;
                var data = __instance.unitData;
                if (data == null || !data.IsPlayerEnemy()) return;

                bool isDrone = __instance is Behaviour.Unit.Drone;
                string victimClass = ResolveUnitClassName(__instance, data);
                string victimName = victimClass;
                string victimSize = isDrone ? "Drone" : "Ship";

                string victimRank = data.unitRank.ToString();
                int victimLevel = data.level;
                string victimFaction = TranslateIfNeeded(data.faction?.name);

                string killerName = "Fleet";
                string killerClass = string.Empty;
                if (damageData?.sourceUnit != null)
                {
                    var ku = damageData.sourceUnit;
                    var kd = ku.unitData;
                    killerClass = ResolveUnitClassName(ku, kd);
                    if (kd is SpaceShipData kssd && !string.IsNullOrWhiteSpace(kssd.name))
                    {
                        killerName = TranslateIfNeeded(kssd.name);
                    }
                    else
                    {
                        killerName = killerClass;
                    }
                }

                string weaponName = string.Empty;
                if (damageData?.sourceTurret != null)
                {
                    weaponName = TranslateIfNeeded(damageData.sourceTurret.name);
                    if (weaponName.EndsWith("(Clone)", StringComparison.Ordinal))
                    {
                        weaponName = weaponName.Substring(0, weaponName.Length - 7).Trim();
                    }
                }

                var gp = GamePlayer.current;
                string sysName = TranslateIfNeeded(gp?.currentSystem?.name);
                string poiName = TranslateIfNeeded(gp?.currentPointOfInterest?.name);
                string missionName = GetActiveMissionName(gp);

                OnEnemyKilled.Invoke(new KillRecord
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    VictimName = victimName,
                    VictimClass = victimClass,
                    VictimSize = victimSize,
                    VictimRank = victimRank,
                    VictimLevel = victimLevel,
                    VictimFaction = victimFaction,
                    IsDrone = isDrone,
                    KillerName = killerName,
                    KillerClass = killerClass,
                    WeaponName = weaponName,
                    SystemName = sysName,
                    PoiName = poiName,
                    MissionName = missionName,
                    PointValue = __instance.pointValue
                });
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"EchoPatches.EnemyShipRemovedPostfix error: {ex.Message}");
            }
        }

        private static void TriggerPayloadPrefix(Source.Galaxy.MapTriggeredPayload __instance)
        {
            if (__instance == null || OnWaveSpawned == null) return;
            try
            {
                if (!__instance.HasEnemyReinforcements()) return;
                var units = __instance.units;
                if (units == null || units.Count == 0) return;

                var grouped = new Dictionary<string, WaveUnitSummary>(StringComparer.OrdinalIgnoreCase);
                int enemyCount = 0;
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (u == null || !u.IsPlayerEnemy()) continue;
                    enemyCount++;
                    string cls = ResolveUnitClassName(null, u);
                    string rank = u.unitRank.ToString();
                    int lvl = u.level;
                    string key = $"{cls}|{rank}|{lvl}";
                    if (grouped.TryGetValue(key, out var existing))
                    {
                        existing.Count++;
                    }
                    else
                    {
                        grouped[key] = new WaveUnitSummary
                        {
                            ShipClass = cls,
                            Rank = rank,
                            Level = lvl,
                            Count = 1
                        };
                    }
                }

                if (enemyCount == 0) return;

                var gp = GamePlayer.current;
                string sysName = TranslateIfNeeded(gp?.currentSystem?.name);
                string poiName = TranslateIfNeeded(gp?.currentPointOfInterest?.name);
                string missionName = GetActiveMissionName(gp);

                var summaryList = new List<WaveUnitSummary>(grouped.Values);
                summaryList.Sort((a, b) => b.Count.CompareTo(a.Count));

                OnWaveSpawned.Invoke(new WaveRecord
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    SystemName = sysName,
                    PoiName = poiName,
                    MissionName = missionName,
                    UnitCount = enemyCount,
                    Units = summaryList
                });
            }
            catch { }
        }
    }
}
