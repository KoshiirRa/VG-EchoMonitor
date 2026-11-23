using System;
using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using Source.MissionSystem;
using Source.Util;

namespace EchoMonitor.Patches
{
    public static class EchoPatches
    {
        public static string LastIdleStatus { get; private set; } = string.Empty;
        public static string LastIdleRawKey { get; private set; } = string.Empty;
        public static string LoadedSaveName { get; private set; } = string.Empty;
        public static Action<string, string> OnPlayerEmergencyWarp { get; set; }
        public static Action<MissionLogEntry> OnMissionEvent { get; set; }

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
                AccessTools.Method("Source.Util.SaveGameFile:LoadSaveGame"),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(LoadSaveGamePostfix)));

            TryPatch(
                harmony,
                AccessTools.Method("Behaviour.Unit.SpaceShip:TryEmergencyJump"),
                postfix: new HarmonyMethod(typeof(EchoPatches), nameof(TryEmergencyJumpPostfix)));

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
        }

        private static void TryPatch(Harmony harmony, MethodBase target, HarmonyMethod postfix)
        {
            if (target == null)
            {
                Plugin.Log?.LogWarning("EchoPatches: target method not found for optional patch.");
                return;
            }
            try
            {
                harmony.Patch(target, postfix: postfix);
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

        private static void LoadSaveGamePostfix(object __instance)
        {
            if (__instance == null) return;
            try
            {
                var prop = AccessTools.Property(__instance.GetType(), "Name");
                if (prop != null)
                {
                    var name = prop.GetValue(__instance) as string;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        LoadedSaveName = name.Trim();
                    }
                }
            }
            catch { }
        }

        private static void TryEmergencyJumpPostfix(bool __result, Behaviour.Unit.SpaceShip __instance)
        {
            if (!__result || __instance == null) return;
            try
            {
                if (!__instance.IsPlayer(true)) return;
                var gp = Source.Player.GamePlayer.current;
                string poiName = gp?.currentPointOfInterest?.name ?? string.Empty;
                string missionName = gp?.infiniteMission?.name
                    ?? gp?.currentBounty?.name
                    ?? gp?.currentPatrol?.name
                    ?? gp?.currentIndustry?.name
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
    }
}
