using System;
using System.Collections.Generic;
using System.IO;
using Behaviour.Item;
using Behaviour.UI;
using Behaviour.UI.Spacestation;
using EchoEnhanced.AutoFavorite;
using HarmonyLib;
using LightJson;
using Source.Galaxy.POI;
using Source.Item;
using Source.Player;

namespace EchoEnhanced.AutoSell
{
    public sealed class SellScanResult
    {
        public int InventoriesScanned { get; set; }
        public int ItemsScanned { get; set; }
        public int SkippedFavourited { get; set; }
        public int Matched { get; set; }
        public int Sold { get; set; }
        public long CreditsEarned { get; set; }
        public bool DryRun { get; set; }
    }

    public sealed class AutoSellEvent
    {
        public string Timestamp { get; set; }
        public string DisplayName { get; set; }
        public string Rarity { get; set; }
        public int ItemLevel { get; set; }
        public string Category { get; set; }
        public string RuleName { get; set; }
        public int Count { get; set; }
        public long Credits { get; set; }
        public bool DryRun { get; set; }
    }

    public sealed class AutoSellEngine
    {
        private const int MaxRecentEvents = 50;

        private struct PendingCandidate
        {
            public Inventory Inventory;
            public Inventory.InventoryItem Entry;
        }

        private readonly EchoEnhancedConfig _config;
        private readonly AutoFavoriteEngine _autoFavorite;
        private readonly string _rulesPath;
        private readonly object _lock = new object();
        private readonly List<AutoSellEvent> _recentEvents = new List<AutoSellEvent>();
        private readonly Queue<PendingCandidate> _pending = new Queue<PendingCandidate>();
        private List<AutoFavoriteRule> _rules = new List<AutoFavoriteRule>();
        private DateTime _lastWriteUtc = DateTime.MinValue;
        private int _sessionSoldCount;
        private long _sessionCreditsEarned;

        public string RulesPath => _rulesPath;
        public int RuleCount
        {
            get { lock (_lock) { return _rules.Count; } }
        }

        public AutoSellEngine(EchoEnhancedConfig config, AutoFavoriteEngine autoFavorite, string bepInExConfigPath)
        {
            _config = config;
            _autoFavorite = autoFavorite;
            string dir = Path.Combine(bepInExConfigPath, "EchoEnhanced");
            Directory.CreateDirectory(dir);
            _rulesPath = Path.Combine(dir, "auto_sell_rules.json");

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
                var parsed = AutoFavoriteRule.ParseRulesJson(json);

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

                Plugin.Log?.LogInfo($"[AutoSell] Loaded {parsed.Count} rules ({enabledCount} enabled) from {_rulesPath}");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[AutoSell] Failed to load rules from {_rulesPath}: {ex.Message}");
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

                var parsed = AutoFavoriteRule.ParseRulesJson(rawJson);
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
            int soldCount;
            long creditsEarned;

            lock (_lock)
            {
                ruleCount = _rules.Count;
                for (int i = 0; i < _rules.Count; i++)
                {
                    if (_rules[i].Enabled) enabledCount++;
                }

                soldCount = _sessionSoldCount;
                creditsEarned = _sessionCreditsEarned;

                for (int i = _recentEvents.Count - 1; i >= 0; i--)
                {
                    var ev = _recentEvents[i];
                    eventsArr.Add(new JsonObject
                    {
                        { "timestamp", ev.Timestamp },
                        { "displayName", ev.DisplayName },
                        { "rarity", ev.Rarity },
                        { "itemLevel", ev.ItemLevel },
                        { "category", ev.Category },
                        { "ruleName", ev.RuleName },
                        { "count", ev.Count },
                        { "credits", (double)ev.Credits },
                        { "dryRun", ev.DryRun }
                    });
                }
            }

            return new JsonObject
            {
                { "enabled", _config.AutoSellEnabled.Value },
                { "armoryOnly", _config.AutoSellArmoryOnly.Value },
                { "dryRun", _config.AutoSellDryRun.Value },
                { "scanOnLoad", _config.AutoSellScanOnLoad.Value },
                { "discordAlert", _config.AutoSellDiscordAlert.Value },
                { "verboseLog", _config.AutoSellVerboseLog.Value },
                { "ruleCount", ruleCount },
                { "enabledRuleCount", enabledCount },
                { "sessionSoldCount", soldCount },
                { "sessionCreditsEarned", (double)creditsEarned },
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
                if (obj["enabled"].IsBoolean) _config.AutoSellEnabled.Value = obj["enabled"].AsBoolean;
                if (obj["armoryOnly"].IsBoolean) _config.AutoSellArmoryOnly.Value = obj["armoryOnly"].AsBoolean;
                if (obj["dryRun"].IsBoolean) _config.AutoSellDryRun.Value = obj["dryRun"].AsBoolean;
                if (obj["scanOnLoad"].IsBoolean) _config.AutoSellScanOnLoad.Value = obj["scanOnLoad"].AsBoolean;
                if (obj["discordAlert"].IsBoolean) _config.AutoSellDiscordAlert.Value = obj["discordAlert"].AsBoolean;
                if (obj["verboseLog"].IsBoolean) _config.AutoSellVerboseLog.Value = obj["verboseLog"].AsBoolean;

                return new JsonObject { { "ok", true } }.ToString();
            }
            catch (Exception ex)
            {
                return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
            }
        }

        /// <summary>
        /// Queues a newly added item to be evaluated on the next Update() frame,
        /// avoiding mutating the inventory inside Inventory.Add's postfix.
        /// </summary>
        public void EnqueueCandidate(Inventory inventory, Inventory.InventoryItem entry)
        {
            if (!_config.Enabled.Value || !_config.AutoSellEnabled.Value) return;
            if (inventory == null || entry == null || entry.item == null) return;
            if (entry.favourite) return;
            if (!entry.item.canSell || entry.item.sellValue <= 0L) return;

            if (_config.AutoSellArmoryOnly.Value)
            {
                if (!ModuleInspector.IsArmoryInventory(inventory) && !ModuleInspector.IsDataInventory(inventory)) return;
            }
            else if (!ModuleInspector.IsPlayerInventory(inventory))
            {
                return;
            }

            lock (_lock)
            {
                _pending.Enqueue(new PendingCandidate
                {
                    Inventory = inventory,
                    Entry = entry
                });
            }
        }

        public void ProcessPendingQueue()
        {
            if (!_config.Enabled.Value || !_config.AutoSellEnabled.Value)
            {
                lock (_lock) { _pending.Clear(); }
                return;
            }

            List<AutoSellEvent> liveSoldEvents = null;
            int liveSoldCount = 0;
            long liveSoldCredits = 0L;

            while (true)
            {
                PendingCandidate next;
                lock (_lock)
                {
                    if (_pending.Count == 0) break;
                    next = _pending.Dequeue();
                }

                if (next.Inventory == null || next.Entry == null || next.Entry.item == null) continue;
                if (next.Entry.count <= 0) continue;
                if (!InventoryContains(next.Inventory, next.Entry)) continue;

                if (EvaluateAndSell(next.Inventory, next.Entry, allowWhenDisabled: false, out long credits, out var soldEv)
                    && soldEv != null
                    && !soldEv.DryRun)
                {
                    liveSoldEvents ??= new List<AutoSellEvent>();
                    liveSoldEvents.Add(soldEv);
                    liveSoldCount += soldEv.Count;
                    liveSoldCredits += credits;
                }
            }

            if (liveSoldEvents != null && liveSoldEvents.Count > 0 && _config.AutoSellDiscordAlert.Value)
            {
                string stationName = GetDockedShopStation()?.name ?? string.Empty;
                BroadcastAutoSellAlert(stationName, liveSoldCount, liveSoldCredits, liveSoldEvents);
            }
        }

        private static bool InventoryContains(Inventory inv, Inventory.InventoryItem entry)
        {
            if (inv == null || inv.items == null || entry == null) return false;
            foreach (var it in inv.items)
            {
                if (ReferenceEquals(it, entry)) return true;
            }
            return false;
        }

        public static SpaceStation GetDockedShopStation()
        {
            var interior = SpaceStationInterior.instance;
            if (interior == null) return null;
            var station = interior.spacestation;
            if (station == null || station.shopInventory == null) return null;
            return station;
        }

        public bool EvaluateAndSell(
            Inventory inventory,
            Inventory.InventoryItem entry,
            bool allowWhenDisabled,
            out long creditsEarned,
            out AutoSellEvent recordedEvent)
        {
            creditsEarned = 0L;
            recordedEvent = null;

            if (!_config.Enabled.Value) return false;
            if (!allowWhenDisabled && !_config.AutoSellEnabled.Value) return false;
            if (inventory == null || entry == null || entry.item == null) return false;
            if (entry.count <= 0) return false;

            // Safety guard 1: Never sell favourited items
            if (entry.favourite) return false;

            // Safety guard 2: Item must be sellable with positive value
            if (!entry.item.canSell || entry.item.sellValue <= 0L) return false;

            // Safety guard 3: Inventory scope check
            if (_config.AutoSellArmoryOnly.Value)
            {
                if (!ModuleInspector.IsArmoryInventory(inventory) && !ModuleInspector.IsDataInventory(inventory)) return false;
            }
            else if (!ModuleInspector.IsPlayerInventory(inventory))
            {
                return false;
            }

            // Safety guard 4: Protect mission-required items
            var gp = GamePlayer.current;
            if (gp == null) return false;
            int missionReq = 0;
            try
            {
                missionReq = gp.RequiredItemCountForMissions(entry.item);
            }
            catch { }

            int sellableCount = entry.count - missionReq;
            if (sellableCount <= 0) return false;

            bool dryRun = _config.AutoSellDryRun.Value || !_config.AutoSellEnabled.Value;

            // Safety guard 5: Must be docked at a SpaceStation with an actual store to execute a live sale
            SpaceStation station = GetDockedShopStation();
            if (!dryRun && station == null)
            {
                return false;
            }

            ReloadRulesIfChanged(force: false);

            List<AutoFavoriteRule> rules;
            lock (_lock)
            {
                rules = _rules;
            }
            if (rules.Count == 0) return false;

            var snap = ModuleInspector.Inspect(entry.item);

            // Safety guard 6: Never sell an item that matches any enabled Auto-Favorite rule
            if (_autoFavorite != null && _autoFavorite.MatchesAnyEnabledRule(snap))
            {
                if (_config.AutoSellVerboseLog.Value)
                {
                    Plugin.Log?.LogInfo(
                        $"[AutoSell][Protected] Skipped {snap.Rarity} {snap.DisplayName} (iLvl {snap.ItemLevel}) — protected by an enabled Auto-Favorite rule.");
                }
                return false;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.Matches(snap)) continue;

                long value = entry.item.sellValue * sellableCount;

                if (!dryRun)
                {
                    if (!ExecuteStoreSell(station, gp, inventory, entry, sellableCount, value))
                    {
                        return false;
                    }

                    lock (_lock)
                    {
                        _sessionSoldCount += sellableCount;
                        _sessionCreditsEarned += value;
                    }
                }

                creditsEarned = value;
                recordedEvent = RecordEvent(snap, rule.Name, sellableCount, value, dryRun);

                string prefix = dryRun ? "[AutoSell][DryRun] Would sell" : $"[AutoSell] Sold at {station.name}";
                Plugin.Log?.LogInfo(
                    $"{prefix} {sellableCount}x {snap.Rarity} {snap.DisplayName} (iLvl {snap.ItemLevel}, {snap.Category}) for ${value:N0} — matched rule '{rule.Name}'");
                return true;
            }

            if (_config.AutoSellVerboseLog.Value && ModuleInspector.IsEquipmentCategory(snap.Category))
            {
                Plugin.Log?.LogInfo(
                    $"[AutoSell][Keep] {snap.Rarity} {snap.DisplayName} (iLvl {snap.ItemLevel}, {snap.Category}, value=${entry.item.sellValue:N0})");
            }

            return false;
        }

        private static readonly System.Reflection.FieldInfo InventoryShopField =
            AccessTools.Field(typeof(InventoryInteractionManager), "inventoryShop");

        private static bool ExecuteStoreSell(
            SpaceStation station,
            GamePlayer gp,
            Inventory inventory,
            Inventory.InventoryItem entry,
            int amount,
            long totalCredits)
        {
            if (station == null || station.shopInventory == null) return false;

            // Use the game's canonical InventoryInteractionManager.SellAmount when the shop UI is active
            var iim = InventoryInteractionManager.Instance;
            if (iim != null && InventoryShopField?.GetValue(iim) != null)
            {
                bool ok = iim.SellAmount(entry, amount);
                if (ok)
                {
                    iim.ReloadUI();
                }
                return ok;
            }

            // Otherwise, sell directly into the docked station's actual shopInventory (same logic as SellAmount without yanking the player's current station tab)
            InventoryItemType itemType = entry.item;
            gp.credits += totalCredits;
            if (itemType.itemCategory != ItemCategory.Data)
            {
                station.shopInventory.Add(itemType, amount, itemType.buyBack);
            }
            inventory.Remove(entry, amount);
            iim?.ReloadUI();
            return true;
        }

        public SellScanResult ScanAllPlayerInventories(bool manual = false)
        {
            bool dryRun = _config.AutoSellDryRun.Value;
            var res = new SellScanResult { DryRun = dryRun };

            if (!_config.Enabled.Value) return res;
            bool allowDryRunTest = manual && dryRun;
            if (!_config.AutoSellEnabled.Value && !allowDryRunTest) return res;

            ReloadRulesIfChanged(force: true);

            var liveSoldEvents = new List<AutoSellEvent>();
            int liveSoldUnits = 0;

            foreach (var inv in ModuleInspector.EnumeratePlayerInventories(_config.AutoSellArmoryOnly.Value, includeDataInventory: true))
            {
                res.InventoriesScanned++;
                // Snapshot the list because selling removes items from inv.items
                var snapshot = new List<Inventory.InventoryItem>(inv.items);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var entry = snapshot[i];
                    if (entry == null || entry.item == null || entry.count <= 0) continue;
                    res.ItemsScanned++;
                    if (entry.favourite)
                    {
                        res.SkippedFavourited++;
                        continue;
                    }

                    if (EvaluateAndSell(inv, entry, allowWhenDisabled: allowDryRunTest, out long credits, out var soldEv))
                    {
                        res.Matched++;
                        if (!dryRun && _config.AutoSellEnabled.Value)
                        {
                            res.Sold++;
                            if (soldEv != null && !soldEv.DryRun)
                            {
                                liveSoldEvents.Add(soldEv);
                                liveSoldUnits += soldEv.Count;
                            }
                        }
                        res.CreditsEarned += credits;
                    }
                }
            }

            if (liveSoldEvents.Count > 0 && _config.AutoSellDiscordAlert.Value)
            {
                string stationName = GetDockedShopStation()?.name ?? string.Empty;
                BroadcastAutoSellAlert(stationName, liveSoldUnits, res.CreditsEarned, liveSoldEvents);
            }

            Plugin.Log?.LogInfo(
                $"[AutoSell] Inventory scan complete: {res.Matched} matched, ${res.CreditsEarned:N0} value ({res.ItemsScanned} items across {res.InventoriesScanned} inventories, {res.SkippedFavourited} favourited skipped){(res.DryRun ? " [DryRun]" : "")}.");
            return res;
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

                var rule = AutoFavoriteRule.ParseSingleRule(ruleObj);
                rule.Enabled = true;

                int invCount = 0;
                int itemCount = 0;
                long totalSellValue = 0L;
                var matches = new JsonArray();

                foreach (var inv in ModuleInspector.EnumeratePlayerInventories(_config.AutoSellArmoryOnly.Value, includeDataInventory: true))
                {
                    invCount++;
                    foreach (var entry in inv.items)
                    {
                        if (entry == null || entry.item == null || entry.count <= 0) continue;
                        itemCount++;
                        if (entry.favourite) continue;
                        if (!entry.item.canSell || entry.item.sellValue <= 0L) continue;

                        var snap = ModuleInspector.Inspect(entry.item);
                        if (_autoFavorite != null && _autoFavorite.MatchesAnyEnabledRule(snap)) continue;
                        if (!rule.Matches(snap)) continue;

                        long itemTotal = entry.item.sellValue * entry.count;
                        totalSellValue += itemTotal;

                        var subs = new JsonArray();
                        for (int i = 0; i < snap.Substats.Count; i++) subs.Add(snap.Substats[i]);

                        var asps = new JsonArray();
                        for (int i = 0; i < snap.Aspects.Count; i++) asps.Add(snap.Aspects[i].Name);

                        matches.Add(new JsonObject
                        {
                            { "displayName", snap.DisplayName },
                            { "rarity", snap.Rarity.ToString() },
                            { "itemLevel", snap.ItemLevel },
                            { "category", snap.Category.ToString() },
                            { "count", entry.count },
                            { "sellValue", (double)itemTotal },
                            { "substats", subs },
                            { "aspects", asps }
                        });
                    }
                }

                return new JsonObject
                {
                    { "ok", true },
                    { "inventoriesScanned", invCount },
                    { "itemsScanned", itemCount },
                    { "totalSellValue", (double)totalSellValue },
                    { "matches", matches }
                }.ToString();
            }
            catch (Exception ex)
            {
                return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
            }
        }

        private AutoSellEvent RecordEvent(ModuleSnapshot snap, string ruleName, int count, long credits, bool dryRun)
        {
            var ev = new AutoSellEvent
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                DisplayName = snap.DisplayName,
                Rarity = snap.Rarity.ToString(),
                ItemLevel = snap.ItemLevel,
                Category = snap.Category.ToString(),
                RuleName = ruleName,
                Count = count,
                Credits = credits,
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

        private static void BroadcastAutoSellAlert(string stationName, int totalCount, long totalCredits, List<AutoSellEvent> events)
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
                        { "count", ev.Count },
                        { "credits", (double)ev.Credits }
                    });
                }

                var payload = new JsonObject
                {
                    { "kind", "autosell" },
                    { "stationName", stationName ?? string.Empty },
                    { "count", totalCount },
                    { "totalCredits", (double)totalCredits },
                    { "items", itemsArr }
                };

                EchoMonitor.Plugin.BroadcastSseEvent("enhanced_alert", payload.ToString());
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[AutoSell] Failed to broadcast Discord alert: {ex.Message}");
            }
        }

        private void EnsureDefaultRulesFile()
        {
            if (File.Exists(_rulesPath)) return;

            const string defaultJson = @"{
  ""rules"": [
    {
      ""name"": ""Sell Standard & Enhanced Equipment"",
      ""enabled"": false,
      ""maxRarity"": ""Enhanced""
    },
    {
      ""name"": ""Sell Low-Level HighGrade (< iLvl 60)"",
      ""enabled"": false,
      ""maxRarity"": ""HighGrade"",
      ""maxItemLevel"": 59
    },
    {
      ""name"": ""Sell Non-Aspect Equipment (Up to HighGrade)"",
      ""enabled"": false,
      ""maxRarity"": ""HighGrade"",
      ""hasAspect"": false
    }
  ]
}
";
            File.WriteAllText(_rulesPath, defaultJson);
        }
    }
}
