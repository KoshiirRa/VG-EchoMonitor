using System;
using System.Collections.Generic;
using System.Reflection;
using Behaviour.Item;
using HarmonyLib;
using LightJson;
using Source.Item;
using Source.Player;
using Source.SpaceShip;
using Source.Util;

namespace EchoEnhanced.AutoFavorite
{
    public sealed class ModuleAspectSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class ModuleSnapshot
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
        public ItemCategory Category { get; set; }
        public Rarity Rarity { get; set; }
        public int ItemLevel { get; set; }
        public List<string> Substats { get; set; } = new List<string>();
        public List<ModuleAspectSnapshot> Aspects { get; set; } = new List<ModuleAspectSnapshot>();
    }

    public static class ModuleInspector
    {
        private static readonly FieldInfo EquipmentStateField =
            AccessTools.Field(typeof(InventoryItemType), "equipmentState");
        private static readonly FieldInfo EquipmentViewField =
            AccessTools.Field(typeof(InventoryItemType), "view");

        public static bool IsEquipmentCategory(ItemCategory category)
        {
            return category == ItemCategory.Turret
                || category == ItemCategory.Module
                || category == ItemCategory.Booster
                || category == ItemCategory.Drone
                || category == ItemCategory.DefensiveTurret;
        }

        public static bool IsArmoryInventory(Inventory inv)
        {
            if (inv == null) return false;
            var gp = GamePlayer.current;
            return gp != null && inv == gp.globalInventory;
        }

        public static bool IsDataInventory(Inventory inv)
        {
            if (inv == null) return false;
            var gp = GamePlayer.current;
            return gp != null && inv == gp.dataInventory;
        }

        public static bool IsPlayerInventory(Inventory inv)
        {
            if (inv == null) return false;
            var gp = GamePlayer.current;
            if (gp == null) return false;

            if (inv == gp.globalInventory) return true;
            if (inv == gp.dataInventory) return true;
            if (gp.currentSpaceShip != null && gp.currentSpaceShip.cargo == inv) return true;

            if (gp.spaceShips != null)
            {
                foreach (SpaceShipData ship in gp.spaceShips)
                {
                    if (ship != null && ship.cargo == inv) return true;
                }
            }

            if (gp.activeFleet != null)
            {
                foreach (SpaceShipData ship in gp.activeFleet)
                {
                    if (ship != null && ship.cargo == inv) return true;
                }
            }

            return false;
        }

        public static IEnumerable<Inventory> EnumeratePlayerInventories(bool armoryOnly = false, bool includeDataInventory = false)
        {
            var gp = GamePlayer.current;
            if (gp == null) yield break;

            var seen = new HashSet<Inventory>();

            if (gp.globalInventory != null && seen.Add(gp.globalInventory))
                yield return gp.globalInventory;

            if (includeDataInventory && gp.dataInventory != null && seen.Add(gp.dataInventory))
                yield return gp.dataInventory;

            if (armoryOnly) yield break;

            if (gp.currentSpaceShip?.cargo != null && seen.Add(gp.currentSpaceShip.cargo))
                yield return gp.currentSpaceShip.cargo;

            if (gp.spaceShips != null)
            {
                foreach (SpaceShipData ship in gp.spaceShips)
                {
                    if (ship?.cargo != null && seen.Add(ship.cargo))
                        yield return ship.cargo;
                }
            }

            if (gp.activeFleet != null)
            {
                foreach (SpaceShipData ship in gp.activeFleet)
                {
                    if (ship?.cargo != null && seen.Add(ship.cargo))
                        yield return ship.cargo;
                }
            }
        }

        public static ModuleSnapshot Inspect(InventoryItemType item)
        {
            var snap = new ModuleSnapshot
            {
                DisplayName = TranslateIfNeeded(!string.IsNullOrEmpty(item.displayName) ? item.displayName : item.identifier),
                Identifier = item.identifier ?? string.Empty,
                Category = item.itemCategory,
                Rarity = item.rarity,
                ItemLevel = item.itemLevel
            };

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
                            snap.Substats.Add(readable.Trim());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[ModuleInspector] Failed to read substats for {snap.DisplayName}: {ex.Message}");
            }

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
                        snap.Aspects.Add(new ModuleAspectSnapshot
                        {
                            Name = TranslateIfNeeded(asp.displayName ?? asp.name ?? string.Empty),
                            Description = TranslateIfNeeded(asp.description ?? string.Empty)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[ModuleInspector] Failed to read aspects for {snap.DisplayName}: {ex.Message}");
            }

            return snap;
        }

        public static string TranslateIfNeeded(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            try
            {
                if (text.StartsWith("@", StringComparison.Ordinal))
                {
                    return Translation.Translate(text) ?? text;
                }
            }
            catch { }
            return text;
        }
    }
}
