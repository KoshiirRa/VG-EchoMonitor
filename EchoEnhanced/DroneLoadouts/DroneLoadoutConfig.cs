using System;
using System.Collections.Generic;
using LightJson;

namespace EchoEnhanced.DroneLoadouts
{
    public sealed class ShipDroneSlots
    {
        public string ShipGuid { get; set; } = string.Empty;
        public string ShipName { get; set; } = string.Empty;
        public List<string> Slots { get; set; } = new List<string>();

        public JsonObject ToJson()
        {
            var arr = new JsonArray();
            foreach (var slot in Slots)
            {
                arr.Add(slot ?? string.Empty);
            }

            return new JsonObject
            {
                { "shipGuid", ShipGuid ?? string.Empty },
                { "shipName", ShipName ?? string.Empty },
                { "slots", arr }
            };
        }

        public static ShipDroneSlots FromJson(string fallbackGuid, JsonObject obj)
        {
            var result = new ShipDroneSlots
            {
                ShipGuid = obj["shipGuid"].IsString ? obj["shipGuid"].AsString : fallbackGuid,
                ShipName = obj["shipName"].IsString ? obj["shipName"].AsString : string.Empty
            };

            if (obj["slots"].IsJsonArray)
            {
                foreach (var item in obj["slots"].AsJsonArray)
                {
                    result.Slots.Add(item.IsString ? item.AsString : string.Empty);
                }
            }

            return result;
        }
    }

    public sealed class DronePreset
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, ShipDroneSlots> Ships { get; set; } = new Dictionary<string, ShipDroneSlots>(StringComparer.OrdinalIgnoreCase);

        public JsonObject ToJson()
        {
            var shipsObj = new JsonObject();
            foreach (var kv in Ships)
            {
                shipsObj[kv.Key] = kv.Value.ToJson();
            }

            return new JsonObject
            {
                { "name", Name ?? string.Empty },
                { "ships", shipsObj }
            };
        }

        public static DronePreset FromJson(string fallbackName, JsonObject obj)
        {
            var preset = new DronePreset
            {
                Name = obj["name"].IsString && !string.IsNullOrWhiteSpace(obj["name"].AsString)
                    ? obj["name"].AsString
                    : fallbackName
            };

            if (obj["ships"].IsJsonObject)
            {
                foreach (var kv in obj["ships"].AsJsonObject)
                {
                    if (kv.Value.IsJsonObject)
                    {
                        preset.Ships[kv.Key] = ShipDroneSlots.FromJson(kv.Key, kv.Value.AsJsonObject);
                    }
                }
            }

            return preset;
        }
    }

    public sealed class EchoDroneBindings
    {
        public bool AutoSwapEnabled { get; set; } = true;
        public bool SwapToSalvageForWrecks { get; set; } = true;
        public bool HoldForPostMissionSalvage { get; set; } = true;
        public string CombatPreset { get; set; } = "Combat";
        public string SalvagePreset { get; set; } = "Salvage";
        public string MiningPreset { get; set; } = "Mining";
        public float CombatClearHysteresisSeconds { get; set; } = 4f;

        public JsonObject ToJson()
        {
            return new JsonObject
            {
                { "autoSwapEnabled", AutoSwapEnabled },
                { "swapToSalvageForWrecks", SwapToSalvageForWrecks },
                { "holdForPostMissionSalvage", HoldForPostMissionSalvage },
                { "combatPreset", CombatPreset ?? string.Empty },
                { "salvagePreset", SalvagePreset ?? string.Empty },
                { "miningPreset", MiningPreset ?? string.Empty },
                { "combatClearHysteresisSeconds", CombatClearHysteresisSeconds }
            };
        }

        public static EchoDroneBindings FromJson(JsonObject obj)
        {
            var b = new EchoDroneBindings();
            if (obj == null) return b;
            if (obj["autoSwapEnabled"].IsBoolean) b.AutoSwapEnabled = obj["autoSwapEnabled"].AsBoolean;
            if (obj["swapToSalvageForWrecks"].IsBoolean) b.SwapToSalvageForWrecks = obj["swapToSalvageForWrecks"].AsBoolean;
            if (obj["holdForPostMissionSalvage"].IsBoolean) b.HoldForPostMissionSalvage = obj["holdForPostMissionSalvage"].AsBoolean;
            if (obj["combatPreset"].IsString) b.CombatPreset = obj["combatPreset"].AsString;
            if (obj["salvagePreset"].IsString) b.SalvagePreset = obj["salvagePreset"].AsString;
            if (obj["miningPreset"].IsString) b.MiningPreset = obj["miningPreset"].AsString;
            if (obj["combatClearHysteresisSeconds"].IsNumber)
            {
                b.CombatClearHysteresisSeconds = Math.Max(0f, (float)obj["combatClearHysteresisSeconds"].AsNumber);
            }
            return b;
        }
    }

    public sealed class DroneLoadoutStore
    {
        public Dictionary<string, DronePreset> Presets { get; set; } = new Dictionary<string, DronePreset>(StringComparer.OrdinalIgnoreCase);
        public EchoDroneBindings EchoBindings { get; set; } = new EchoDroneBindings();

        public JsonObject ToJson()
        {
            var presetsObj = new JsonObject();
            foreach (var kv in Presets)
            {
                presetsObj[kv.Key] = kv.Value.ToJson();
            }

            return new JsonObject
            {
                { "version", 1 },
                { "presets", presetsObj },
                { "echoBindings", EchoBindings.ToJson() }
            };
        }

        public static DroneLoadoutStore FromJson(JsonObject obj)
        {
            var store = new DroneLoadoutStore();
            if (obj == null) return store;

            if (obj["presets"].IsJsonObject)
            {
                foreach (var kv in obj["presets"].AsJsonObject)
                {
                    if (kv.Value.IsJsonObject)
                    {
                        var preset = DronePreset.FromJson(kv.Key, kv.Value.AsJsonObject);
                        store.Presets[preset.Name] = preset;
                    }
                }
            }

            if (obj["echoBindings"].IsJsonObject)
            {
                store.EchoBindings = EchoDroneBindings.FromJson(obj["echoBindings"].AsJsonObject);
            }

            return store;
        }
    }
}
