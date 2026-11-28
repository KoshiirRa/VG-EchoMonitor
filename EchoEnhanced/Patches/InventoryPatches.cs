using System;
using System.Reflection;
using Behaviour.Item;
using EchoEnhanced.AutoFavorite;
using EchoEnhanced.AutoSell;
using HarmonyLib;
using Source.Item;

namespace EchoEnhanced.Patches
{
    public static class InventoryPatches
    {
        public static AutoFavoriteEngine AutoFavorite { get; set; }
        public static AutoSellEngine AutoSell { get; set; }

        public static void Apply(Harmony harmony)
        {
            MethodInfo addMethod = AccessTools.Method(
                typeof(Inventory),
                nameof(Inventory.Add),
                new[] { typeof(InventoryItemType), typeof(int), typeof(bool), typeof(bool) });

            if (addMethod == null)
            {
                Plugin.Log?.LogError("[InventoryPatches] Could not locate Inventory.Add(InventoryItemType, int, bool, bool).");
                return;
            }

            harmony.Patch(
                addMethod,
                postfix: new HarmonyMethod(typeof(InventoryPatches), nameof(Inventory_Add_Postfix)));

            Plugin.Log?.LogInfo("[InventoryPatches] Patched Inventory.Add for Auto-Favorite and Auto-Sell.");
        }

        private static void Inventory_Add_Postfix(Inventory __instance, Inventory.InventoryItem __result)
        {
            if (__result == null) return;

            Inventory targetInv = __result.inventory ?? __instance;

            if (AutoFavorite != null)
            {
                try
                {
                    AutoFavorite.EvaluateAndApply(targetInv, __result);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"[InventoryPatches] Auto-Favorite error in Inventory.Add postfix: {ex}");
                }
            }

            if (AutoSell != null && !__result.favourite)
            {
                try
                {
                    AutoSell.EnqueueCandidate(targetInv, __result);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"[InventoryPatches] Auto-Sell enqueue error in Inventory.Add postfix: {ex}");
                }
            }
        }
    }
}
