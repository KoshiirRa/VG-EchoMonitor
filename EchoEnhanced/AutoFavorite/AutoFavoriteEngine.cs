using System;
using System.Collections.Generic;
using System.IO;
using Behaviour.Equipment.Aspect;
using LightJson;
using Source.Item;
using Source.Util;

namespace EchoEnhanced.AutoFavorite
{
    public sealed class ScanResult
    {
        public int InventoriesScanned { get; set; }
        public int ItemsScanned { get; set; }
        public int AlreadyFavourited { get; set; }
        public int Matched { get; set; }
        public bool DryRun { get; set; }
    }

    public sealed class AutoFavoriteEvent
    {
        public string Timestamp { get; set; }
        public string DisplayName { get; set; }
        public string Rarity { get; set; }
        public int ItemLevel { get; set; }
        public string Category { get; set; }
        public string RuleName { get; set; }
        public bool DryRun { get; set; }
    }

    public sealed class AutoFavoriteEngine
    {
        private const int MaxRecentEvents = 50;

        private readonly EchoEnhancedConfig _config;
        private readonly string _rulesPath;
        private readonly object _lock = new object();
        private readonly List<AutoFavoriteEvent> _recentEvents = new List<AutoFavoriteEvent>();
        private List<AutoFavoriteRule> _rules = new List<AutoFavoriteRule>();
        private DateTime _lastWriteUtc = DateTime.MinValue;

        public string RulesPath => _rulesPath;
        public int RuleCount
        {
            get { lock (_lock) { return _rules.Count; } }
        }

        public AutoFavoriteEngine(EchoEnhancedConfig config, string bepInExConfigPath)
        {
            _config = config;
            string dir = Path.Combine(bepInExConfigPath, "EchoEnhanced");
            Directory.CreateDirectory(dir);
            _rulesPath = Path.Combine(dir, "auto_favorite_rules.json");

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

                Plugin.Log?.LogInfo($"[AutoFavorite] Loaded {parsed.Count} rules ({enabledCount} enabled) from {_rulesPath}");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[AutoFavorite] Failed to load rules from {_rulesPath}: {ex.Message}");
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

        public bool MatchesAnyEnabledRule(ModuleSnapshot snap)
        {
            if (snap == null) return false;
            ReloadRulesIfChanged(force: false);

            List<AutoFavoriteRule> rules;
            lock (_lock)
            {
                rules = _rules;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Matches(snap)) return true;
            }
            return false;
        }

        public JsonObject GetStatusObject()
        {
            ReloadRulesIfChanged(force: false);

            var eventsArr = new JsonArray();
            int ruleCount;
            int enabledCount = 0;

            lock (_lock)
            {
                ruleCount = _rules.Count;
                for (int i = 0; i < _rules.Count; i++)
                {
                    if (_rules[i].Enabled) enabledCount++;
                }

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
                        { "dryRun", ev.DryRun }
                    });
                }
            }

            return new JsonObject
            {
                { "enabled", _config.AutoFavoriteEnabled.Value },
                { "armoryOnly", _config.AutoFavoriteArmoryOnly.Value },
                { "dryRun", _config.AutoFavoriteDryRun.Value },
                { "scanOnLoad", _config.AutoFavoriteScanOnLoad.Value },
                { "verboseLog", _config.AutoFavoriteVerboseLog.Value },
                { "ruleCount", ruleCount },
                { "enabledRuleCount", enabledCount },
                { "rulesPath", _rulesPath },
                { "recentEvents", eventsArr }
            };
        }

        public string GetStatusJson(string pluginVersion)
        {
            return new JsonObject
            {
                { "installed", true },
                { "version", pluginVersion },
                { "autoFavorite", GetStatusObject() }
            }.ToString();
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
                if (obj["enabled"].IsBoolean) _config.AutoFavoriteEnabled.Value = obj["enabled"].AsBoolean;
                if (obj["armoryOnly"].IsBoolean) _config.AutoFavoriteArmoryOnly.Value = obj["armoryOnly"].AsBoolean;
                if (obj["dryRun"].IsBoolean) _config.AutoFavoriteDryRun.Value = obj["dryRun"].AsBoolean;
                if (obj["scanOnLoad"].IsBoolean) _config.AutoFavoriteScanOnLoad.Value = obj["scanOnLoad"].AsBoolean;
                if (obj["verboseLog"].IsBoolean) _config.AutoFavoriteVerboseLog.Value = obj["verboseLog"].AsBoolean;

                return new JsonObject { { "ok", true } }.ToString();
            }
            catch (Exception ex)
            {
                return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
            }
        }

        public bool EvaluateAndApply(Inventory inventory, Inventory.InventoryItem entry, bool allowWhenDisabled = false)
        {
            if (!_config.Enabled.Value) return false;
            if (!allowWhenDisabled && !_config.AutoFavoriteEnabled.Value) return false;
            if (inventory == null || entry == null || entry.item == null) return false;
            if (entry.favourite) return false;
            if (!entry.item.CanFavourite()) return false;
            if (_config.AutoFavoriteArmoryOnly.Value)
            {
                if (!ModuleInspector.IsArmoryInventory(inventory)) return false;
            }
            else if (!ModuleInspector.IsPlayerInventory(inventory))
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

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.Matches(snap)) continue;

                bool dryRun = _config.AutoFavoriteDryRun.Value || !_config.AutoFavoriteEnabled.Value;
                if (!dryRun)
                {
                    entry.favourite = true;
                    inventory.MarkChanged();
                }

                RecordEvent(snap, rule.Name, dryRun);

                string prefix = dryRun ? "[AutoFavorite][DryRun] Would favourite" : "[AutoFavorite] Favourited";
                Plugin.Log?.LogInfo(
                    $"{prefix} {snap.Rarity} {snap.DisplayName} (iLvl {snap.ItemLevel}, {snap.Category}) — matched rule '{rule.Name}'");
                return true;
            }

            if (_config.AutoFavoriteVerboseLog.Value && ModuleInspector.IsEquipmentCategory(snap.Category))
            {
                Plugin.Log?.LogInfo(
                    $"[AutoFavorite][Skip] {snap.Rarity} {snap.DisplayName} (iLvl {snap.ItemLevel}, {snap.Category}, substats={snap.Substats.Count}, aspects={snap.Aspects.Count})");
            }

            return false;
        }

        public ScanResult ScanAllPlayerInventories(bool manual = false)
        {
            bool dryRun = _config.AutoFavoriteDryRun.Value;
            var res = new ScanResult { DryRun = dryRun };

            if (!_config.Enabled.Value) return res;
            bool allowDryRunTest = manual && dryRun;
            if (!_config.AutoFavoriteEnabled.Value && !allowDryRunTest) return res;

            ReloadRulesIfChanged(force: true);

            foreach (var inv in ModuleInspector.EnumeratePlayerInventories(_config.AutoFavoriteArmoryOnly.Value))
            {
                res.InventoriesScanned++;
                foreach (var entry in inv.items)
                {
                    if (entry == null || entry.item == null) continue;
                    res.ItemsScanned++;
                    if (entry.favourite)
                    {
                        res.AlreadyFavourited++;
                        continue;
                    }
                    if (EvaluateAndApply(inv, entry, allowWhenDisabled: allowDryRunTest))
                    {
                        res.Matched++;
                    }
                }
            }

            Plugin.Log?.LogInfo(
                $"[AutoFavorite] Inventory scan complete: {res.Matched} matched ({res.ItemsScanned} items across {res.InventoriesScanned} inventories, {res.AlreadyFavourited} already favourited){(res.DryRun ? " [DryRun]" : "")}.");
            return res;
        }

        public string GetCatalogJson()
        {
            var cats = new JsonArray { "Turret", "Module", "Booster", "Drone", "DefensiveTurret" };
            var rarities = new JsonArray { "Standard", "Enhanced", "HighGrade", "Exotic", "Legendary" };

            var statSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (EquipStat st in Enum.GetValues(typeof(EquipStat)))
                {
                    string name = st.GetDisplayName();
                    if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith("@", StringComparison.Ordinal))
                    {
                        statSet.Add(name.Trim());
                    }
                }
            }
            catch { }

            var statsArr = new JsonArray();
            foreach (var s in statSet) statsArr.Add(s);

            var aspectSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (EquipAspect.all != null)
                {
                    foreach (var asp in EquipAspect.all)
                    {
                        if (asp == null) continue;
                        string name = ModuleInspector.TranslateIfNeeded(asp.displayName ?? asp.name ?? string.Empty);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            aspectSet.Add(name.Trim());
                        }
                    }
                }
            }
            catch { }

            var aspectsArr = new JsonArray();
            foreach (var a in aspectSet) aspectsArr.Add(a);

            return new JsonObject
            {
                { "categories", cats },
                { "rarities", rarities },
                { "substats", statsArr },
                { "aspects", aspectsArr }
            }.ToString();
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
                bool includeFav = !obj["includeFavourited"].IsBoolean || obj["includeFavourited"].AsBoolean;
                JsonObject ruleObj = obj["rule"].IsJsonObject ? obj["rule"].AsJsonObject : obj;

                var rule = AutoFavoriteRule.ParseSingleRule(ruleObj);
                rule.Enabled = true; // always evaluate during preview

                int invCount = 0;
                int itemCount = 0;
                var matches = new JsonArray();

                foreach (var inv in ModuleInspector.EnumeratePlayerInventories(_config.AutoFavoriteArmoryOnly.Value))
                {
                    invCount++;
                    foreach (var entry in inv.items)
                    {
                        if (entry == null || entry.item == null) continue;
                        itemCount++;
                        if (!includeFav && entry.favourite) continue;
                        if (!entry.item.CanFavourite()) continue;

                        var snap = ModuleInspector.Inspect(entry.item);
                        if (!rule.Matches(snap)) continue;

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
                            { "alreadyFavourited", entry.favourite },
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
                    { "matches", matches }
                }.ToString();
            }
            catch (Exception ex)
            {
                return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
            }
        }

        private void RecordEvent(ModuleSnapshot snap, string ruleName, bool dryRun)
        {
            lock (_lock)
            {
                _recentEvents.Add(new AutoFavoriteEvent
                {
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    DisplayName = snap.DisplayName,
                    Rarity = snap.Rarity.ToString(),
                    ItemLevel = snap.ItemLevel,
                    Category = snap.Category.ToString(),
                    RuleName = ruleName,
                    DryRun = dryRun
                });

                if (_recentEvents.Count > MaxRecentEvents)
                {
                    _recentEvents.RemoveAt(0);
                }
            }
        }

        private void EnsureDefaultRulesFile()
        {
            if (File.Exists(_rulesPath)) return;

            const string defaultJson = @"{
  ""rules"": [
    {
      ""name"": ""All Legendary Equipment"",
      ""enabled"": true,
      ""minRarity"": ""Legendary""
    },
    {
      ""name"": ""Wishlist: Exotic+ Missile/Swarm iLvl 90+"",
      ""enabled"": false,
      ""categories"": [""Turret""],
      ""nameContains"": [""Microcell Swarm"", ""Hailfire"", ""Missile Launcher""],
      ""minRarity"": ""Exotic"",
      ""minItemLevel"": 90
    },
    {
      ""name"": ""Workshop Reroll Candidate (2-of-4 Energy/Crit Substats)"",
      ""enabled"": false,
      ""categories"": [""Turret"", ""Module"", ""Booster""],
      ""minRarity"": ""Exotic"",
      ""minItemLevel"": 85,
      ""substats"": {
        ""minMatches"": 2,
        ""anyOf"": [
          ""Turret Rotation Speed"",
          ""Critical Damage"",
          ""Energy Damage"",
          ""Reload Speed""
        ]
      }
    }
  ]
}
";
            File.WriteAllText(_rulesPath, defaultJson);
        }
    }
}
