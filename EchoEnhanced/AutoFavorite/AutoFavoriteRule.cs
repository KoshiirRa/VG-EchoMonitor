using System;
using System.Collections.Generic;
using LightJson;
using Source.Item;

namespace EchoEnhanced.AutoFavorite
{
    public sealed class SubstatRule
    {
        public int MinMatches { get; set; } = 1;
        public List<string> AnyOf { get; set; } = new List<string>();
        public List<string> RequireAll { get; set; } = new List<string>();
    }

    public sealed class AutoFavoriteRule
    {
        public string Name { get; set; } = "Unnamed Rule";
        public bool Enabled { get; set; } = true;
        public List<ItemCategory> Categories { get; set; } = new List<ItemCategory>();
        public List<string> NameContains { get; set; } = new List<string>();
        public List<string> NameExcludes { get; set; } = new List<string>();
        public Rarity? MinRarity { get; set; }
        public Rarity? MaxRarity { get; set; }
        public int? MinItemLevel { get; set; }
        public int? MaxItemLevel { get; set; }
        public bool? HasAspect { get; set; }
        public List<string> AspectContains { get; set; } = new List<string>();
        public SubstatRule Substats { get; set; }

        public bool Matches(ModuleSnapshot mod)
        {
            if (!Enabled || mod == null) return false;

            // Category check: default to equippable categories when omitted
            if (Categories.Count > 0)
            {
                if (!Categories.Contains(mod.Category)) return false;
            }
            else if (!ModuleInspector.IsEquipmentCategory(mod.Category))
            {
                return false;
            }

            // Rarity bounds
            if (MinRarity.HasValue && mod.Rarity < MinRarity.Value) return false;
            if (MaxRarity.HasValue && mod.Rarity > MaxRarity.Value) return false;

            // Item level bounds
            if (MinItemLevel.HasValue && mod.ItemLevel < MinItemLevel.Value) return false;
            if (MaxItemLevel.HasValue && mod.ItemLevel > MaxItemLevel.Value) return false;

            // Name contains (OR across patterns; matches displayName or identifier)
            if (NameContains.Count > 0)
            {
                bool anyName = false;
                for (int i = 0; i < NameContains.Count; i++)
                {
                    string pat = NameContains[i];
                    if (ContainsIgnoreCase(mod.DisplayName, pat) || ContainsIgnoreCase(mod.Identifier, pat))
                    {
                        anyName = true;
                        break;
                    }
                }
                if (!anyName) return false;
            }

            // Name excludes (if any matches, reject)
            if (NameExcludes.Count > 0)
            {
                for (int i = 0; i < NameExcludes.Count; i++)
                {
                    string pat = NameExcludes[i];
                    if (ContainsIgnoreCase(mod.DisplayName, pat) || ContainsIgnoreCase(mod.Identifier, pat))
                    {
                        return false;
                    }
                }
            }

            // Aspect presence
            if (HasAspect.HasValue)
            {
                bool has = mod.Aspects.Count > 0;
                if (has != HasAspect.Value) return false;
            }

            // Aspect substring match (OR across patterns against aspect Name or Description)
            if (AspectContains.Count > 0)
            {
                bool anyAspect = false;
                for (int i = 0; i < AspectContains.Count && !anyAspect; i++)
                {
                    string pat = AspectContains[i];
                    for (int j = 0; j < mod.Aspects.Count; j++)
                    {
                        var asp = mod.Aspects[j];
                        if (ContainsIgnoreCase(asp.Name, pat) || ContainsIgnoreCase(asp.Description, pat))
                        {
                            anyAspect = true;
                            break;
                        }
                    }
                }
                if (!anyAspect) return false;
            }

            // Workshop reroll-aware substats ("N of M" + optional RequireAll)
            if (Substats != null)
            {
                if (Substats.RequireAll.Count > 0)
                {
                    for (int i = 0; i < Substats.RequireAll.Count; i++)
                    {
                        if (!HasMatchingSubstat(mod.Substats, Substats.RequireAll[i]))
                        {
                            return false;
                        }
                    }
                }

                if (Substats.AnyOf.Count > 0)
                {
                    int needed = Math.Max(1, Substats.MinMatches);
                    int matched = 0;
                    for (int i = 0; i < Substats.AnyOf.Count; i++)
                    {
                        if (HasMatchingSubstat(mod.Substats, Substats.AnyOf[i]))
                        {
                            matched++;
                            if (matched >= needed) break;
                        }
                    }
                    if (matched < needed) return false;
                }
            }

            return true;
        }

        private static bool HasMatchingSubstat(List<string> substats, string pattern)
        {
            for (int i = 0; i < substats.Count; i++)
            {
                if (ContainsIgnoreCase(substats[i], pattern)) return true;
            }
            return false;
        }

        private static bool ContainsIgnoreCase(string source, string value)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value)) return false;
            return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static List<AutoFavoriteRule> ParseRulesJson(string jsonText)
        {
            var result = new List<AutoFavoriteRule>();
            if (string.IsNullOrWhiteSpace(jsonText)) return result;

            JsonValue root = JsonValue.Parse(jsonText);
            JsonArray arr = null;
            if (root.IsJsonArray)
            {
                arr = root.AsJsonArray;
            }
            else if (root.IsJsonObject && root.AsJsonObject["rules"].IsJsonArray)
            {
                arr = root.AsJsonObject["rules"].AsJsonArray;
            }

            if (arr == null) return result;

            for (int i = 0; i < arr.Count; i++)
            {
                if (!arr[i].IsJsonObject) continue;
                result.Add(ParseSingleRule(arr[i].AsJsonObject, i));
            }

            return result;
        }

        public static AutoFavoriteRule ParseSingleRule(JsonObject obj, int index = 0)
        {
            var rule = new AutoFavoriteRule
            {
                Name = obj["name"].IsString ? obj["name"].AsString : $"Rule #{index + 1}",
                Enabled = !obj["enabled"].IsBoolean || obj["enabled"].AsBoolean
            };

            if (obj["categories"].IsJsonArray)
            {
                foreach (var v in obj["categories"].AsJsonArray)
                {
                    if (v.IsString && Enum.TryParse(v.AsString, true, out ItemCategory cat))
                        rule.Categories.Add(cat);
                }
            }

            ReadStringArray(obj["nameContains"], rule.NameContains);
            ReadStringArray(obj["nameExcludes"], rule.NameExcludes);
            ReadStringArray(obj["aspectContains"], rule.AspectContains);

            if (obj["minRarity"].IsString && Enum.TryParse(obj["minRarity"].AsString, true, out Rarity minR))
                rule.MinRarity = minR;
            if (obj["maxRarity"].IsString && Enum.TryParse(obj["maxRarity"].AsString, true, out Rarity maxR))
                rule.MaxRarity = maxR;

            if (obj["minItemLevel"].IsNumber) rule.MinItemLevel = obj["minItemLevel"].AsInteger;
            if (obj["maxItemLevel"].IsNumber) rule.MaxItemLevel = obj["maxItemLevel"].AsInteger;
            if (obj["hasAspect"].IsBoolean) rule.HasAspect = obj["hasAspect"].AsBoolean;

            if (obj["substats"].IsJsonObject)
            {
                var subObj = obj["substats"].AsJsonObject;
                var sr = new SubstatRule();
                if (subObj["minMatches"].IsNumber) sr.MinMatches = subObj["minMatches"].AsInteger;
                ReadStringArray(subObj["anyOf"], sr.AnyOf);
                ReadStringArray(subObj["requireAll"], sr.RequireAll);
                rule.Substats = sr;
            }

            return rule;
        }

        private static void ReadStringArray(JsonValue val, List<string> target)
        {
            if (!val.IsJsonArray) return;
            foreach (var item in val.AsJsonArray)
            {
                if (item.IsString && !string.IsNullOrWhiteSpace(item.AsString))
                {
                    target.Add(item.AsString.Trim());
                }
            }
        }
    }
}
