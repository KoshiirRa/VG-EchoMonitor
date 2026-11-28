using System;
using System.Collections.Generic;
using System.IO;
using Behaviour.Crafting;
using Behaviour.Item;
using Behaviour.Item.Usable;
using Behaviour.UI;
using EchoEnhanced.AutoFavorite;
using EchoEnhanced.AutoSell;
using LightJson;
using Source.Galaxy;
using Source.Galaxy.POI;
using Source.Item;
using Source.Player;

namespace EchoEnhanced.AutoBuy
{
    public sealed class BuyScanResult
    {
        public bool DockedWithStore { get; set; }
        public string StationName { get; set; } = string.Empty;
        public int ItemsScanned { get; set; }
        public int Matched { get; set; }
        public int Bought { get; set; }
        public long CreditsSpent { get; set; }
        public bool DryRun { get; set; }
    }

    public sealed class AutoBuyEvent
    {
        public string Timestamp { get; set; }
        public string StationName { get; set; }
        public string DisplayName { get; set; }
        public string Rarity { get; set; }
        public int ItemLevel { get; set; }
        public string Category { get; set; }
        public string RuleName { get; set; }
        public long Cost { get; set; }
        public bool DryRun { get; set; }
    }

    public sealed class AutoBuyEngine
    {
        private const int MaxRecentEvents = 50;

        private readonly EchoEnhancedConfig _config;
        private readonly string _rulesPath;
        private readonly object _lock = new object();
        private readonly List<AutoBuyEvent> _recentEvents = new List<AutoBuyEvent>();
        private List<AutoBuyRule> _rules = new List<AutoBuyRule>();
        private DateTime _lastWriteUtc = DateTime.MinValue;
        private int _sessionBoughtCount;
        private long _sessionCreditsSpent;

        public string RulesPath => _rulesPath;
        public int RuleCount
        {
            get { lock (_lock) { return _rules.Count; } }
        }

        public AutoBuyEngine(EchoEnhancedConfig config, string bepInExConfigPath)
        {
            _config = config;
            string dir = Path.Combine(bepInExConfigPath, "EchoEnhanced");
            Directory.CreateDirectory(dir);
            _rulesPath = Path.Combine(dir, "auto_buy_rules.json");

            EnsureDefaultRulesFile();
            ReloadRulesIfChanged(force: true);
        }

        public void ReloadRulesIfChanged(bool force = false)
        {
            try
            {
                if (!File.Exists(_rulesPath))
                {
                    EnsureDefaultRulesFile();
                }

                DateTime writeUtc = File.GetLastWriteTimeUtc(_rulesPath);
                if (!force && writeUtc == _lastWriteUtc) return;

                string json = File.ReadAllText(_rulesPath);
                var parsed = AutoBuyRule.ParseRulesJson(json);

                int enabledCount = 0;
                for (int i = 0; i < parsed.Count; i++)
                {
                    if (parsed[i].Enabled) enabledCount++;
                }

                lock (_lock)
                {
                    _rules = parsed;
                    _lastWriteUtc = writeUtc;
                }

                Plugin.Log?.LogInfo($"[AutoBuy] Loaded {parsed.Count} rules ({enabledCount} enabled) from {_rulesPath}");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[AutoBuy] Failed to load rules from {_rulesPath}: {ex.Message}");
            }
        }

        public string GetRulesJson()
        {
            EnsureDefaultRulesFile();
            return File.ReadAllText(_rulesPath);
        }

        public string SaveRulesJson(string rawJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawJson))
                {
                    return new JsonObject { { "ok", false }, { "error", "Empty JSON payload." } }.ToString();
                }

                var parsed = AutoBuyRule.ParseRulesJson(rawJson);
                File.WriteAllText(_rulesPath, rawJson);
                ReloadRulesIfChanged(force: true);

                return new JsonObject
                {
                    { "ok", true },
                    { "ruleCount", parsed.Count }
                }.ToString();
            }
            catch (Exception ex)
            {
                return new JsonObject
                {
                    { "ok", false },
                    { "error", ex.Message }
                }.ToString();
            }
        }

        public JsonObject GetStatusObject()
        {
            ReloadRulesIfChanged(force: false);

            var eventsArr = new JsonArray();
            int ruleCount;
            int enabledCount = 0;
            int boughtCount;
            long creditsSpent;

            lock (_lock)
            {
                ruleCount = _rules.Count;
                for (int i = 0; i < _rules.Count; i++)
                {
                    if (_rules[i].Enabled) enabledCount++;
                }

                boughtCount = _sessionBoughtCount;
                creditsSpent = _sessionCreditsSpent;

                for (int i = _recentEvents.Count - 1; i >= 0; i--)
                {
                    var ev = _recentEvents[i];
                    eventsArr.Add(new JsonObject
                    {
                        { "timestamp", ev.Timestamp },
                        { "stationName", ev.StationName },
                        { "displayName", ev.DisplayName },
                        { "rarity", ev.Rarity },
                        { "itemLevel", ev.ItemLevel },
                        { "category", ev.Category },
                        { "ruleName", ev.RuleName },
                        { "cost", (double)ev.Cost },
                        { "dryRun", ev.DryRun }
                    });
                }
            }

            return new JsonObject
            {
                { "enabled", _config.AutoBuyEnabled.Value },
                { "dryRun", _config.AutoBuyDryRun.Value },
                { "minCreditsReserve", (double)_config.AutoBuyMinCreditsReserve.Value },
                { "discordAlert", _config.AutoBuyDiscordAlert.Value },
                { "verboseLog", _config.AutoBuyVerboseLog.Value },
                { "ruleCount", ruleCount },
                { "enabledRuleCount", enabledCount },
                { "sessionBoughtCount", boughtCount },
                { "sessionCreditsSpent", (double)creditsSpent },
                { "rulesPath", _rulesPath },
                { "recentEvents", eventsArr }
            };
        }

        public string UpdateConfigJson(string rawJson)
        {
            try
            {
                var val = JsonValue.Parse(rawJson);
                if (!val.IsJsonObject)
                {
                    return new JsonObject { { "ok", false }, { "error", "Expected JSON object." } }.ToString();
                }

                var obj = val.AsJsonObject;
                if (obj["enabled"].IsBoolean) _config.AutoBuyEnabled.Value = obj["enabled"].AsBoolean;
                if (obj["dryRun"].IsBoolean) _config.AutoBuyDryRun.Value = obj["dryRun"].AsBoolean;
                if (obj["minCreditsReserve"].IsNumber) _config.AutoBuyMinCreditsReserve.Value = Math.Max(0L, (long)obj["minCreditsReserve"].AsNumber);
                if (obj["discordAlert"].IsBoolean) _config.AutoBuyDiscordAlert.Value = obj["discordAlert"].AsBoolean;
                if (obj["verboseLog"].IsBoolean) _config.AutoBuyVerboseLog.Value = obj["verboseLog"].AsBoolean;

                return new JsonObject { { "ok", true } }.ToString();
            }
            catch (Exception ex)
            {
                return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
            }
        }

        public static bool IsBlueprintAlreadyKnown(GamePlayer gp, InventoryItemType item)
        {
            if (gp == null || item == null || item.itemCategory != ItemCategory.Blueprint) return false;

            try
            {
                if (item.TryGetState<ShipBlueprintState>(out var shipState) && !string.IsNullOrEmpty(shipState.shipId))
                {
                    return gp.HasShipBlueprint(shipState.shipId);
                }

                if (item.TryGetState<BlueprintState>(out var bpState) && !string.IsNullOrEmpty(bpState.blueprintName))
                {
                    var recipe = CraftingRecipe.Get(bpState.blueprintName);
                    if (recipe != null
                        && (recipe.category == ItemCategory.Module || recipe.category == ItemCategory.Turret)
                        && bpState.blueprintLevel > 0)
                    {
                        return gp.GetBlueprintMaxLevel(bpState.blueprintName) >= bpState.blueprintLevel;
                    }
                    return gp.HasBlueprint(bpState.blueprintName);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[AutoBuy] Blueprint check error for {item.identifier}: {ex.Message}");
            }

            return false;
        }

        private static bool CanPurchaseStoreEntry(GamePlayer gp, SpaceStation station, Inventory.InventoryItem entry, long minReserve)
        {
            if (gp == null || station == null || entry == null || entry.item == null || entry.count <= 0) return false;

            // Only credit purchases (never spend special currency items like Vanguard Marks automatically)
            if (entry.costItem != null) return false;

            long cost = entry.cost;
            if (cost <= 0L) return false;
            if (!gp.CanAfford(cost)) return false;
            if (gp.credits - cost < minReserve) return false;

            // Faction rep & player level requirements
            Faction faction = station.faction ?? gp.currentPointOfInterest?.faction;
            if (faction != null && !entry.item.EnoughRepWithFaction(faction)) return false;
            if (entry.item.shopItemData != null && gp.level < entry.item.shopItemData.levelRequirement) return false;

            return true;
        }

        public BuyScanResult ScanDockedStore(bool manual = false)
        {
            bool dryRun = _config.AutoBuyDryRun.Value;
            var res = new BuyScanResult { DryRun = dryRun };

            if (!_config.Enabled.Value) return res;
            bool allowDryRunTest = manual && dryRun;
            if (!_config.AutoBuyEnabled.Value && !allowDryRunTest) return res;

            var gp = GamePlayer.current;
            var station = AutoSellEngine.GetDockedShopStation();
            if (gp == null || station == null || station.shopInventory == null)
            {
                return res;
            }

            res.DockedWithStore = true;
            res.StationName = station.name ?? string.Empty;

            ReloadRulesIfChanged(force: true);

            List<AutoBuyRule> rules;
            lock (_lock)
            {
                rules = _rules;
            }
            if (rules.Count == 0) return res;

            long minReserve = Math.Max(0L, _config.AutoBuyMinCreditsReserve.Value);
            var snapshot = new List<Inventory.InventoryItem>(station.shopInventory.items);
            var liveBoughtEvents = new List<AutoBuyEvent>();

            for (int i = 0; i < snapshot.Count; i++)
            {
                var entry = snapshot[i];
                if (entry == null || entry.item == null || entry.count <= 0) continue;
                res.ItemsScanned++;

                if (!CanPurchaseStoreEntry(gp, station, entry, minReserve)) continue;

                var snap = ModuleInspector.Inspect(entry.item);
                bool knownBp = IsBlueprintAlreadyKnown(gp, entry.item);
                long unitCost = entry.cost;

                for (int r = 0; r < rules.Count; r++)
                {
                    var rule = rules[r];
                    if (!rule.Matches(snap, unitCost, knownBp)) continue;

                    bool effectiveDryRun = dryRun || !_config.AutoBuyEnabled.Value;
                    bool buyStack = rule.BuyFullStack && snap.Category != ItemCategory.Blueprint;
                    int maxUnits = buyStack ? entry.count : 1;
                    int boughtUnits = 0;
                    long totalCost = 0L;

                    if (!effectiveDryRun)
                    {
                        for (int u = 0; u < maxUnits; u++)
                        {
                            if (!CanPurchaseStoreEntry(gp, station, entry, minReserve)) break;
                            if (!ExecuteStoreBuy(gp, station, entry, unitCost)) break;
                            boughtUnits++;
                            totalCost += unitCost;
                        }

                        if (boughtUnits == 0) break;

                        res.Matched += boughtUnits;
                        res.Bought += boughtUnits;
                        res.CreditsSpent += totalCost;
                        lock (_lock)
                        {
                            _sessionBoughtCount += boughtUnits;
                            _sessionCreditsSpent += totalCost;
                        }
                    }
                    else
                    {
                        long available = Math.Max(0L, gp.credits - minReserve);
                        int affordable = unitCost > 0L ? (int)Math.Min(maxUnits, available / unitCost) : 0;
                        boughtUnits = Math.Max(1, affordable);
                        totalCost = unitCost * boughtUnits;
                        res.Matched += boughtUnits;
                        res.CreditsSpent += totalCost;
                    }

                    string displayLabel = boughtUnits > 1 ? $"{snap.DisplayName} x{boughtUnits}" : snap.DisplayName;
                    var ev = RecordEvent(res.StationName, snap, rule.Name, totalCost, effectiveDryRun, boughtUnits);
                    if (!effectiveDryRun)
                    {
                        liveBoughtEvents.Add(ev);
                    }

                    string prefix = effectiveDryRun
                        ? $"[AutoBuy][DryRun] Would buy at {res.StationName}"
                        : $"[AutoBuy] Bought at {res.StationName}";
                    Plugin.Log?.LogInfo(
                        $"{prefix} {snap.Rarity} {displayLabel} (iLvl {snap.ItemLevel}, {snap.Category}) for ${totalCost:N0} — matched rule '{rule.Name}'");
                    break;
                }
            }

            if (liveBoughtEvents.Count > 0 && _config.AutoBuyDiscordAlert.Value)
            {
                BroadcastAutoBuyAlert(res.StationName, res.Bought, res.CreditsSpent, liveBoughtEvents);
            }

            if (res.Matched > 0 || manual)
            {
                Plugin.Log?.LogInfo(
                    $"[AutoBuy] Store scan at {res.StationName}: {res.Matched} matched, ${res.CreditsSpent:N0} cost ({res.ItemsScanned} store items){(res.DryRun ? " [DryRun]" : "")}.");
            }

            return res;
        }

        private static bool ExecuteStoreBuy(GamePlayer gp, SpaceStation station, Inventory.InventoryItem entry, long unitCost)
        {
            if (gp == null || station?.shopInventory == null || entry?.item == null || entry.count <= 0) return false;

            Inventory targetInv = gp.globalInventory;
            if (targetInv == null || targetInv.IsFull(entry.item.m3))
            {
                targetInv = gp.currentSpaceShip?.cargo;
            }
            if (targetInv == null || targetInv.IsFull(entry.item.m3))
            {
                Plugin.Log?.LogWarning($"[AutoBuy] Skipped buying {entry.item.displayName}: Armory and ship cargo are full.");
                return false;
            }

            if (!gp.CanAfford(unitCost)) return false;

            InventoryItemType itemType = entry.item;
            gp.RemoveCredits(unitCost, itemType.GetCreditSpendCategory());
            targetInv.Add(itemType, 1);
            station.shopInventory.Remove(entry, 1);
            InventoryInteractionManager.Instance?.ReloadUI();
            return true;
        }

        public string PreviewRuleJson(string rawJson)
        {
            try
            {
                var root = JsonValue.Parse(rawJson);
                if (!root.IsJsonObject)
                {
                    return new JsonObject { { "ok", false }, { "error", "Expected JSON object." } }.ToString();
                }

                var obj = root.AsJsonObject;
                JsonObject ruleObj = obj["rule"].IsJsonObject ? obj["rule"].AsJsonObject : obj;

                var rule = AutoBuyRule.ParseSingleRule(ruleObj);
                rule.Enabled = true;

                var gp = GamePlayer.current;
                var station = AutoSellEngine.GetDockedShopStation();
                if (gp == null || station == null || station.shopInventory == null)
                {
                    return new JsonObject
                    {
                        { "ok", true },
                        { "dockedWithStore", false },
                        { "stationName", string.Empty },
                        { "itemsScanned", 0 },
                        { "totalCost", 0.0 },
                        { "matches", new JsonArray() }
                    }.ToString();
                }

                int itemCount = 0;
                long totalCost = 0L;
                var matches = new JsonArray();

                foreach (var entry in station.shopInventory.items)
                {
                    if (entry == null || entry.item == null || entry.count <= 0) continue;
                    itemCount++;
                    if (entry.costItem != null || entry.cost <= 0L) continue;

                    var snap = ModuleInspector.Inspect(entry.item);
                    bool knownBp = IsBlueprintAlreadyKnown(gp, entry.item);
                    if (!rule.Matches(snap, entry.cost, knownBp)) continue;

                    int qty = (rule.BuyFullStack && snap.Category != ItemCategory.Blueprint) ? entry.count : 1;
                    long entryTotal = entry.cost * qty;
                    totalCost += entryTotal;

                    var subs = new JsonArray();
                    for (int i = 0; i < snap.Substats.Count; i++) subs.Add(snap.Substats[i]);

                    var asps = new JsonArray();
                    for (int i = 0; i < snap.Aspects.Count; i++) asps.Add(snap.Aspects[i].Name);

                    matches.Add(new JsonObject
                    {
                        { "displayName", qty > 1 ? $"{snap.DisplayName} x{qty}" : snap.DisplayName },
                        { "rarity", snap.Rarity.ToString() },
                        { "itemLevel", snap.ItemLevel },
                        { "category", snap.Category.ToString() },
                        { "cost", (double)entryTotal },
                        { "knownBlueprint", knownBp },
                        { "substats", subs },
                        { "aspects", asps }
                    });
                }

                return new JsonObject
                {
                    { "ok", true },
                    { "dockedWithStore", true },
                    { "stationName", station.name ?? string.Empty },
                    { "itemsScanned", itemCount },
                    { "totalCost", (double)totalCost },
                    { "matches", matches }
                }.ToString();
            }
            catch (Exception ex)
            {
                return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
            }
        }

        private AutoBuyEvent RecordEvent(string stationName, ModuleSnapshot snap, string ruleName, long cost, bool dryRun, int quantity = 1)
        {
            var ev = new AutoBuyEvent
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                StationName = stationName,
                DisplayName = quantity > 1 ? $"{snap.DisplayName} x{quantity}" : snap.DisplayName,
                Rarity = snap.Rarity.ToString(),
                ItemLevel = snap.ItemLevel,
                Category = snap.Category.ToString(),
                RuleName = ruleName,
                Cost = cost,
                DryRun = dryRun
            };

            lock (_lock)
            {
                _recentEvents.Add(ev);

                if (_recentEvents.Count > MaxRecentEvents)
                {
                    _recentEvents.RemoveAt(0);
                }
            }

            return ev;
        }

        private static void BroadcastAutoBuyAlert(string stationName, int totalCount, long totalCost, List<AutoBuyEvent> events)
        {
            try
            {
                var itemsArr = new JsonArray();
                for (int i = 0; i < events.Count; i++)
                {
                    var ev = events[i];
                    itemsArr.Add(new JsonObject
                    {
                        { "displayName", ev.DisplayName },
                        { "rarity", ev.Rarity },
                        { "itemLevel", ev.ItemLevel },
                        { "category", ev.Category },
                        { "ruleName", ev.RuleName },
                        { "cost", (double)ev.Cost }
                    });
                }

                var payload = new JsonObject
                {
                    { "kind", "autobuy" },
                    { "stationName", stationName ?? string.Empty },
                    { "count", totalCount },
                    { "totalCost", (double)totalCost },
                    { "items", itemsArr }
                };

                EchoMonitor.Plugin.BroadcastSseEvent("enhanced_alert", payload.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[AutoBuy] Failed to broadcast Discord alert: {ex.Message}");
            }
        }

        private void EnsureDefaultRulesFile()
        {
            if (File.Exists(_rulesPath)) return;

            const string defaultJson = @"{
  ""rules"": [
    {
      ""name"": ""Buy All Unknown Blueprints"",
      ""enabled"": false,
      ""categories"": [""Blueprint""],
      ""onlyUnknownBlueprints"": true
    },
    {
      ""name"": ""Buy Exotic+ Turrets/Modules (iLvl 90+)"",
      ""enabled"": false,
      ""categories"": [""Turret"", ""Module"", ""Booster""],
      ""minRarity"": ""Exotic"",
      ""minItemLevel"": 90
    }
  ]
}
";
            File.WriteAllText(_rulesPath, defaultJson);
        }
    }
}
