using System;
using Behaviour.Gameplay;
using EchoEnhanced.DroneLoadouts;
using HarmonyLib;

namespace EchoEnhanced.Patches
{
    public static class DroneLoadoutPatches
    {
        public static DroneLoadoutEngine Engine { get; set; }

        public static void Apply(Harmony harmony)
        {
            var idleActivity = AccessTools.Method(typeof(IdleManager), "IdleActivityInPoi");
            if (idleActivity != null)
            {
                harmony.Patch(
                    idleActivity,
                    prefix: new HarmonyMethod(typeof(DroneLoadoutPatches), nameof(IdleActivityInPoi_Prefix)));
                Plugin.Log.LogInfo("[DroneLoadouts] Hooked IdleManager.IdleActivityInPoi.");
            }
            else
            {
                Plugin.Log.LogWarning("[DroneLoadouts] Could not find IdleManager.IdleActivityInPoi to patch.");
            }

            var findActivity = AccessTools.Method(typeof(IdleManager), "FindActivityForEquipment");
            if (findActivity != null)
            {
                harmony.Patch(
                    findActivity,
                    prefix: new HarmonyMethod(typeof(DroneLoadoutPatches), nameof(FindActivityForEquipment_Prefix)));
                Plugin.Log.LogInfo("[DroneLoadouts] Hooked IdleManager.FindActivityForEquipment.");
            }
            else
            {
                Plugin.Log.LogWarning("[DroneLoadouts] Could not find IdleManager.FindActivityForEquipment to patch.");
            }

            var travelToMissionStation = AccessTools.Method(typeof(IdleManager), "TravelToMissionStation");
            if (travelToMissionStation != null)
            {
                harmony.Patch(
                    travelToMissionStation,
                    prefix: new HarmonyMethod(typeof(DroneLoadoutPatches), nameof(TravelToMissionStation_Prefix)));
                Plugin.Log.LogInfo("[DroneLoadouts] Hooked IdleManager.TravelToMissionStation.");
            }
            else
            {
                Plugin.Log.LogWarning("[DroneLoadouts] Could not find IdleManager.TravelToMissionStation to patch.");
            }
        }

        private static bool TravelToMissionStation_Prefix(ref bool __result)
        {
            try
            {
                if (Engine != null && Engine.ShouldHoldForPostMissionSalvage())
                {
                    Engine.EvaluateEchoAutoSwap();
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DroneLoadouts] TravelToMissionStation_Prefix error: {ex.Message}");
            }
            return true;
        }

        private static void IdleActivityInPoi_Prefix()
        {
            try
            {
                Engine?.EvaluateEchoAutoSwap();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DroneLoadouts] IdleActivityInPoi_Prefix error: {ex.Message}");
            }
        }

        private static void FindActivityForEquipment_Prefix()
        {
            try
            {
                Engine?.RestoreBeforeFindActivity();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[DroneLoadouts] FindActivityForEquipment_Prefix error: {ex.Message}");
            }
        }
    }
}
