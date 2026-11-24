using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Behaviour.Item;
using HarmonyLib;
using Source.Galaxy.POI;
using Source.Galaxy.Trade;
using Source.MissionSystem;
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

                OnMissionEvent.Invoke(new MissionLogEntry
                {
                    Timestamp = DateTime.UtcNow.ToString("o"),
                    Event = evt,
                    MissionName = name,
                    GameplayType = mission.gameplayType.ToString(),
                    IsStory = !string.IsNullOrWhiteSpace(mission.storyId),
                    Reason = translatedReason
                });
            }
            catch { }
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
    }
}
