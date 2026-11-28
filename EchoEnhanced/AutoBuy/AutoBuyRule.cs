using System.Collections.Generic;
using EchoEnhanced.AutoFavorite;
using LightJson;
using Source.Item;

namespace EchoEnhanced.AutoBuy
{
    public sealed class AutoBuyRule
    {
        public AutoFavoriteRule BaseRule { get; set; } = new AutoFavoriteRule();
        public bool OnlyUnknownBlueprints { get; set; } = true;
        public bool BuyFullStack { get; set; } = false;
        public long? MaxPrice { get; set; }

        public string Name => BaseRule.Name;
        public bool Enabled
        {
            get => BaseRule.Enabled;
            set => BaseRule.Enabled = value;
        }

        public bool Matches(ModuleSnapshot snap, long unitCost, bool isKnownBlueprint)
        {
            if (BaseRule == null || !BaseRule.Enabled || snap == null) return false;

            if (MaxPrice.HasValue && unitCost > MaxPrice.Value)
            {
                return false;
            }

            if (snap.Category == ItemCategory.Blueprint && OnlyUnknownBlueprints && isKnownBlueprint)
            {
                return false;
            }

            return BaseRule.Matches(snap);
        }

        public static List<AutoBuyRule> ParseRulesJson(string jsonText)
        {
            var result = new List<AutoBuyRule>();
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

        public static AutoBuyRule ParseSingleRule(JsonObject obj, int index = 0)
        {
            var rule = new AutoBuyRule
            {
                BaseRule = AutoFavoriteRule.ParseSingleRule(obj, index),
                OnlyUnknownBlueprints = !obj["onlyUnknownBlueprints"].IsBoolean || obj["onlyUnknownBlueprints"].AsBoolean,
                BuyFullStack = obj["buyFullStack"].IsBoolean && obj["buyFullStack"].AsBoolean
            };

            if (obj["maxPrice"].IsNumber)
            {
                rule.MaxPrice = (long)obj["maxPrice"].AsNumber;
            }

            return rule;
        }
    }
}
