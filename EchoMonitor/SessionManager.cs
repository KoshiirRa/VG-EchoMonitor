using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace EchoMonitor
{
    public sealed class SessionManager
    {
        public const char KeySep = '\u001f';

        private static readonly Dictionary<string, int> RequiredPiecesBySize = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Size1"] = 4,
            ["Size2"] = 6,
            ["Size3"] = 8,
            ["Size4"] = 10,
            ["Size5"] = 12,
            ["Size6"] = 14
        };
        private const int DefaultRequiredPieces = 10;

        private static readonly (string Id, string Label, int VmEach)[] DogTagTypes =
        {
            ("CommendationDogTag", "Dog Tags", 3),
            ("CommendationDogTag2", "Elite Dog Tags", 8),
            ("CommendationDogTag3", "Executive Dog Tags", 15)
        };

        private readonly string _rootDir;
        private readonly object _lock = new object();

        private readonly struct BacklogSample
        {
            public readonly DateTime TimestampUtc;
            public readonly int RawTotal;
            public readonly int RefTotal;

            public BacklogSample(DateTime timestampUtc, int rawTotal, int refTotal)
            {
                TimestampUtc = timestampUtc;
                RawTotal = rawTotal;
                RefTotal = refTotal;
            }
        }

        private const double RollingBacklogWindowMinutes = 60.0;

        private string _activeSaveId;
        private PlayerSnapshot _baseline;
        private readonly List<HistoryEntry> _history = new List<HistoryEntry>();
        private readonly List<MissionLogEntry> _missionLog = new List<MissionLogEntry>();
        private readonly List<BacklogSample> _backlogSamples = new List<BacklogSample>();
        private EmpireActivityDelta _empireActivity = new EmpireActivityDelta();
        private DateTime _lastHistoryWriteUtc = DateTime.MinValue;

        public SessionManager(string configRootPath)
        {
            _rootDir = Path.Combine(configRootPath, "EchoMonitor");
            Directory.CreateDirectory(_rootDir);
        }

        public static int SumDogTags(Dictionary<string, int> byType)
        {
            if (byType == null) return 0;
            int total = 0;
            foreach (var dt in DogTagTypes)
            {
                if (byType.TryGetValue(dt.Id, out int c)) total += c;
            }
            return total;
        }

        public static int SumDogTagsVm(Dictionary<string, int> byType)
        {
            if (byType == null) return 0;
            int total = 0;
            foreach (var dt in DogTagTypes)
            {
                if (byType.TryGetValue(dt.Id, out int c)) total += c * dt.VmEach;
            }
            return total;
        }

        public string BuildStateJson(LiveTelemetry telemetry, int snapshotIntervalMinutes)
        {
            if (telemetry == null || !telemetry.InGame || telemetry.Snapshot == null)
            {
                return JsonUtil.SerializeStateResponse(false, string.Empty, string.Empty, 0.0, null, null, null, null, null, null, null, null, null, null, null);
            }

            TelemetryCollector.MergePendingInventories(telemetry.Snapshot);

            lock (_lock)
            {
                EnsureSessionLoaded(telemetry.Snapshot);

                var deltas = ComputeDeltas(_baseline, telemetry.Snapshot, out double durationHours);
                PopulateEmpireMetricsLocked(telemetry.Empire, _baseline, telemetry.Snapshot, durationHours);

                // Populate officer level deltas against session baseline
                if (telemetry.Crew?.Officers != null)
                {
                    foreach (var off in telemetry.Crew.Officers)
                    {
                        if (off == null || string.IsNullOrEmpty(off.Name)) continue;
                        if (_baseline.OfficerLevels != null && _baseline.OfficerLevels.TryGetValue(off.Name, out int baseLvl))
                        {
                            off.LevelDelta = Math.Max(0, off.Level - baseLvl);
                        }
                        else
                        {
                            off.LevelDelta = 0;
                        }
                    }
                }

                int intervalMin = Math.Max(1, snapshotIntervalMinutes);
                if ((DateTime.UtcNow - _lastHistoryWriteUtc).TotalMinutes >= intervalMin)
                {
                    AppendHistoryEntryLocked(telemetry.Snapshot, deltas);
                }

                var recentMissions = GetRecentMissionLogLocked(100);

                return JsonUtil.SerializeStateResponse(
                    true,
                    _activeSaveId,
                    _baseline.Timestamp,
                    durationHours,
                    telemetry.Echo,
                    telemetry.Crew,
                    telemetry.DroneBay,
                    telemetry.HangarBay,
                    telemetry.Fleet,
                    telemetry.Alerts,
                    telemetry.Empire,
                    recentMissions,
                    telemetry.Snapshot,
                    _baseline,
                    deltas);
            }
        }

        public void RecordEmpireRefined(string itemName, int amount)
        {
            if (string.IsNullOrEmpty(itemName) || amount <= 0) return;
            lock (_lock)
            {
                _empireActivity.RefinedTotal += amount;
                _empireActivity.RefinedByItem.TryGetValue(itemName, out int prev);
                _empireActivity.RefinedByItem[itemName] = prev + amount;
            }
        }

        public void RecordEmpireProduced(string itemName, int amount)
        {
            if (string.IsNullOrEmpty(itemName) || amount <= 0) return;
            lock (_lock)
            {
                _empireActivity.OutpostProducedTotal += amount;
                _empireActivity.OutpostProducedByItem.TryGetValue(itemName, out int prev);
                _empireActivity.OutpostProducedByItem[itemName] = prev + amount;
            }
        }

        public void RecordEmpireCrafted(string itemName, int amount)
        {
            if (string.IsNullOrEmpty(itemName) || amount <= 0) return;
            lock (_lock)
            {
                _empireActivity.CraftedTotal += amount;
                _empireActivity.CraftedByItem.TryGetValue(itemName, out int prev);
                _empireActivity.CraftedByItem[itemName] = prev + amount;
            }
        }

        public void RecordEmpireTradeRun(long earned, long spent, long profit)
        {
            lock (_lock)
            {
                _empireActivity.TradeRuns++;
                _empireActivity.TradeEarned += earned;
                _empireActivity.TradeSpent += spent;
                _empireActivity.TradeProfit += profit;
            }
        }

        public void RecordEmpireSale(long credits)
        {
            if (credits <= 0) return;
            lock (_lock)
            {
                _empireActivity.StationSalesCredits += credits;
            }
        }

        private void PopulateEmpireMetricsLocked(EmpireStatus empire, PlayerSnapshot baseline, PlayerSnapshot current, double durationHours)
        {
            if (empire == null || baseline == null || current == null) return;

            double safeHours = durationHours > 0.001 ? durationHours : 0.0;

            // Raw materials & salvage backlog across active depots
            var rawKeys = new HashSet<string>(StringComparer.Ordinal);
            if (baseline.DepotRawMaterials != null) foreach (var k in baseline.DepotRawMaterials.Keys) rawKeys.Add(k);
            if (current.DepotRawMaterials != null) foreach (var k in current.DepotRawMaterials.Keys) rawKeys.Add(k);

            int rawTotal = 0;
            int rawBaseTotal = 0;
            var rawItems = new List<DepotMaterialEntry>();

            foreach (var key in rawKeys)
            {
                int baseVal = baseline.DepotRawMaterials != null && baseline.DepotRawMaterials.TryGetValue(key, out int bv) ? bv : 0;
                int curVal = current.DepotRawMaterials != null && current.DepotRawMaterials.TryGetValue(key, out int cv) ? cv : 0;
                int delta = curVal - baseVal;
                rawTotal += curVal;
                rawBaseTotal += baseVal;

                var parts = key.Split(KeySep);
                string itemId = parts.Length > 0 ? parts[0] : key;
                string name = parts.Length > 1 ? parts[1] : itemId;
                string cat = parts.Length > 2 ? parts[2] : "Ore";

                rawItems.Add(new DepotMaterialEntry
                {
                    ItemId = itemId,
                    Name = name,
                    Category = cat,
                    Baseline = baseVal,
                    Current = curVal,
                    Delta = delta,
                    RatePerHour = safeHours > 0 ? Math.Round(delta / safeHours, 1) : 0.0
                });
            }

            rawItems.Sort((a, b) =>
            {
                int cmp = b.Current.CompareTo(a.Current);
                if (cmp != 0) return cmp;
                return Math.Abs(b.Delta).CompareTo(Math.Abs(a.Delta));
            });

            empire.RawBacklogTotal = rawTotal;
            empire.RawBacklogDelta = rawTotal - rawBaseTotal;
            empire.RawBacklogRatePerHour = safeHours > 0 ? Math.Round(empire.RawBacklogDelta / safeHours, 1) : 0.0;
            empire.RawBacklogItems = rawItems;

            // Refined products stored at active depots
            var refKeys = new HashSet<string>(StringComparer.Ordinal);
            if (baseline.DepotRefinedProducts != null) foreach (var k in baseline.DepotRefinedProducts.Keys) refKeys.Add(k);
            if (current.DepotRefinedProducts != null) foreach (var k in current.DepotRefinedProducts.Keys) refKeys.Add(k);

            int refTotal = 0;
            int refBaseTotal = 0;
            var refItems = new List<DepotMaterialEntry>();

            foreach (var key in refKeys)
            {
                int baseVal = baseline.DepotRefinedProducts != null && baseline.DepotRefinedProducts.TryGetValue(key, out int bv) ? bv : 0;
                int curVal = current.DepotRefinedProducts != null && current.DepotRefinedProducts.TryGetValue(key, out int cv) ? cv : 0;
                int delta = curVal - baseVal;
                refTotal += curVal;
                refBaseTotal += baseVal;

                var parts = key.Split(KeySep);
                string itemId = parts.Length > 0 ? parts[0] : key;
                string name = parts.Length > 1 ? parts[1] : itemId;

                refItems.Add(new DepotMaterialEntry
                {
                    ItemId = itemId,
                    Name = name,
                    Category = "RefinedProduct",
                    Baseline = baseVal,
                    Current = curVal,
                    Delta = delta,
                    RatePerHour = safeHours > 0 ? Math.Round(delta / safeHours, 1) : 0.0
                });
            }

            refItems.Sort((a, b) => b.Current.CompareTo(a.Current));

            empire.RefinedStockTotal = refTotal;
            empire.RefinedStockDelta = refTotal - refBaseTotal;
            empire.RefinedStockRatePerHour = safeHours > 0 ? Math.Round(empire.RefinedStockDelta / safeHours, 1) : 0.0;
            empire.RefinedStockItems = refItems;

            // Rolling 60-minute backlog & refined stock trend
            DateTime nowUtc = DateTime.UtcNow;
            if (_backlogSamples.Count == 0 || (nowUtc - _backlogSamples[_backlogSamples.Count - 1].TimestampUtc).TotalSeconds >= 60.0)
            {
                _backlogSamples.Add(new BacklogSample(nowUtc, rawTotal, refTotal));
            }

            // Keep the newest sample at or older than the 60m boundary, pruning anything earlier
            int dropCount = 0;
            while (_backlogSamples.Count - dropCount > 1 &&
                   (nowUtc - _backlogSamples[dropCount + 1].TimestampUtc).TotalMinutes >= RollingBacklogWindowMinutes)
            {
                dropCount++;
            }
            if (dropCount > 0)
            {
                _backlogSamples.RemoveRange(0, dropCount);
            }

            if (_backlogSamples.Count > 0)
            {
                var refSample = _backlogSamples[0];
                double elapsedMin = (nowUtc - refSample.TimestampUtc).TotalMinutes;
                if (elapsedMin >= 1.0)
                {
                    double elapsedHours = elapsedMin / 60.0;
                    empire.BacklogWindowMinutes = Math.Max(1, (int)Math.Round(Math.Min(elapsedMin, RollingBacklogWindowMinutes)));
                    empire.RawBacklogWindowDelta = rawTotal - refSample.RawTotal;
                    empire.RawBacklogWindowRatePerHour = Math.Round(empire.RawBacklogWindowDelta / elapsedHours, 1);
                    empire.RefinedStockWindowDelta = refTotal - refSample.RefTotal;
                    empire.RefinedStockWindowRatePerHour = Math.Round(empire.RefinedStockWindowDelta / elapsedHours, 1);
                }
                else
                {
                    empire.BacklogWindowMinutes = 0;
                    empire.RawBacklogWindowDelta = 0;
                    empire.RawBacklogWindowRatePerHour = 0.0;
                    empire.RefinedStockWindowDelta = 0;
                    empire.RefinedStockWindowRatePerHour = 0.0;
                }
            }

            // Snapshot of session empire activity
            empire.Activity = new EmpireActivityDelta
            {
                RefinedTotal = _empireActivity.RefinedTotal,
                RefinedByItem = new Dictionary<string, int>(_empireActivity.RefinedByItem),
                TradeRuns = _empireActivity.TradeRuns,
                TradeEarned = _empireActivity.TradeEarned,
                TradeSpent = _empireActivity.TradeSpent,
                TradeProfit = _empireActivity.TradeProfit,
                StationSalesCredits = _empireActivity.StationSalesCredits,
                OutpostProducedTotal = _empireActivity.OutpostProducedTotal,
                OutpostProducedByItem = new Dictionary<string, int>(_empireActivity.OutpostProducedByItem),
                CraftedTotal = _empireActivity.CraftedTotal,
                CraftedByItem = new Dictionary<string, int>(_empireActivity.CraftedByItem)
            };
        }

        public string GetHistoryJson()
        {
            lock (_lock)
            {
                return JsonUtil.SerializeHistoryList(_history);
            }
        }

        public string GetMissionLogJson()
        {
            lock (_lock)
            {
                return JsonUtil.SerializeMissionLogList(GetRecentMissionLogLocked(200));
            }
        }

        public void RecordMissionEvent(MissionLogEntry entry)
        {
            if (entry == null) return;
            lock (_lock)
            {
                _missionLog.Add(entry);
                if (_missionLog.Count > 500)
                {
                    _missionLog.RemoveRange(0, _missionLog.Count - 500);
                }

                if (!string.IsNullOrEmpty(_activeSaveId))
                {
                    try
                    {
                        string path = Path.Combine(GetSaveDir(_activeSaveId), "mission_log.jsonl");
                        File.AppendAllText(path, JsonUtil.SerializeMissionLogEntry(entry) + "\n", Encoding.UTF8);
                    }
                    catch { }
                }
            }
        }

        private List<MissionLogEntry> GetRecentMissionLogLocked(int maxCount)
        {
            int count = Math.Min(maxCount, _missionLog.Count);
            var res = new List<MissionLogEntry>(count);
            for (int i = _missionLog.Count - 1; i >= _missionLog.Count - count; i--)
            {
                res.Add(_missionLog[i]);
            }
            return res;
        }

        public (string SaveId, string SessionStartedAt) ResetSession(PlayerSnapshot current)
        {
            TelemetryCollector.MergePendingInventories(current);
            lock (_lock)
            {
                if (current == null)
                {
                    return (_activeSaveId ?? string.Empty, string.Empty);
                }

                _activeSaveId = SanitizeSaveId(current.SaveId);
                _baseline = current;
                _history.Clear();
                _backlogSamples.Clear();
                _missionLog.Clear();
                _empireActivity = new EmpireActivityDelta();

                string saveDir = GetSaveDir(_activeSaveId);
                Directory.CreateDirectory(saveDir);

                File.WriteAllText(Path.Combine(saveDir, "baseline.json"), JsonUtil.SerializePlayerSnapshot(_baseline), Encoding.UTF8);
                string historyPath = Path.Combine(saveDir, "history.jsonl");
                if (File.Exists(historyPath))
                {
                    File.Delete(historyPath);
                }
                string missionLogPath = Path.Combine(saveDir, "mission_log.jsonl");
                if (File.Exists(missionLogPath))
                {
                    File.Delete(missionLogPath);
                }

                var deltas = ComputeDeltas(_baseline, current, out _);
                AppendHistoryEntryLocked(current, deltas);

                return (_activeSaveId, _baseline.Timestamp);
            }
        }

        private void EnsureSessionLoaded(PlayerSnapshot current)
        {
            string saveId = SanitizeSaveId(current.SaveId);
            if (_activeSaveId == saveId && _baseline != null)
            {
                return;
            }

            _activeSaveId = saveId;
            _history.Clear();
            _missionLog.Clear();
            _baseline = null;
            _lastHistoryWriteUtc = DateTime.MinValue;

            string saveDir = GetSaveDir(saveId);
            Directory.CreateDirectory(saveDir);

            string baselinePath = Path.Combine(saveDir, "baseline.json");
            string historyPath = Path.Combine(saveDir, "history.jsonl");
            string missionLogPath = Path.Combine(saveDir, "mission_log.jsonl");

            if (File.Exists(missionLogPath))
            {
                try
                {
                    foreach (string line in File.ReadLines(missionLogPath, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var entry = JsonUtil.DeserializeMissionLogEntry(line);
                        if (entry != null) _missionLog.Add(entry);
                    }
                    if (_missionLog.Count > 500)
                    {
                        _missionLog.RemoveRange(0, _missionLog.Count - 500);
                    }
                }
                catch { }
            }

            if (File.Exists(baselinePath))
            {
                try
                {
                    var loaded = JsonUtil.DeserializePlayerSnapshot(File.ReadAllText(baselinePath, Encoding.UTF8));
                    if (loaded != null && HasLegacyModuleKeys(loaded))
                    {
                        Plugin.Log?.LogWarning($"Detected legacy module key format in baseline.json for {saveId}; resetting baseline.");
                        _baseline = null;
                    }
                    else
                    {
                        _baseline = loaded;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"Failed to load baseline.json for {saveId}, starting fresh: {ex.Message}");
                }
            }

            if (_baseline == null)
            {
                _baseline = current;
                File.WriteAllText(baselinePath, JsonUtil.SerializePlayerSnapshot(_baseline), Encoding.UTF8);
                var deltas = ComputeDeltas(_baseline, current, out _);
                AppendHistoryEntryLocked(current, deltas);
                return;
            }

            if (File.Exists(historyPath))
            {
                try
                {
                    foreach (string line in File.ReadLines(historyPath, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var entry = JsonUtil.DeserializeHistoryEntry(line);
                        if (entry != null) _history.Add(entry);
                    }
                    if (_history.Count > 0 && DateTime.TryParse(_history[_history.Count - 1].Timestamp, out var dt))
                    {
                        _lastHistoryWriteUtc = dt.ToUniversalTime();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"Failed to load history.jsonl for {saveId}: {ex.Message}");
                }
            }
        }

        private void AppendHistoryEntryLocked(PlayerSnapshot current, ComputedDeltas deltas)
        {
            var entry = new HistoryEntry
            {
                Timestamp = current.Timestamp,
                ElapsedHours = current.ElapsedHours,
                Credits = current.Credits,
                CreditsDelta = deltas.Credits,
                VanguardMarks = current.VanguardMarks,
                MarksDelta = deltas.VanguardMarks,
                DogTags = SumDogTags(current.DogTagsByType),
                DogTagsDelta = deltas.DogTags,
                DogTagsVmDelta = deltas.DogTagsVm,
                CommanderLevel = current.CommanderLevel,
                CommanderLevelDelta = deltas.CommanderLevel,
                CommanderXp = current.CommanderXp,
                CommanderXpDelta = deltas.CommanderXp,
                CommanderProgressPct = Math.Round(current.CommanderProgressPct, 1),
                EmpireLevel = current.EmpireLevel,
                EmpireLevelDelta = deltas.EmpireLevel,
                EmpireXp = current.EmpireXp,
                EmpireXpDelta = deltas.EmpireXp,
                EmpireProgressPct = Math.Round(current.EmpireProgressPct, 1),
                NewModulesCount = deltas.NewModules.Sum(m => m.Gained),
                NewShipBpsCount = deltas.NewShipBlueprints.Sum(s => s.Gained),
                NewModBpsCount = deltas.NewModuleBlueprintItems.Sum(b => b.Gained) + deltas.NewUploadedBlueprints.Count,
                CapturedShipsCount = deltas.NewCapturedShips.Count
            };

            _history.Add(entry);
            _lastHistoryWriteUtc = DateTime.UtcNow;

            try
            {
                string historyPath = Path.Combine(GetSaveDir(_activeSaveId), "history.jsonl");
                File.AppendAllText(historyPath, JsonUtil.SerializeHistoryEntry(entry) + "\n", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"Failed to append history.jsonl: {ex.Message}");
            }
        }

        public static ComputedDeltas ComputeDeltas(PlayerSnapshot b, PlayerSnapshot c, out double durationHours)
        {
            double elapsedDelta = Math.Max(0.0, c.ElapsedHours - b.ElapsedHours);
            durationHours = elapsedDelta;
            if (durationHours <= 0.0001 && DateTime.TryParse(b.Timestamp, out var startDt) && DateTime.TryParse(c.Timestamp, out var endDt))
            {
                durationHours = Math.Max(0.0, (endDt.ToUniversalTime() - startDt.ToUniversalTime()).TotalHours);
            }

            long creditsDelta = c.Credits - b.Credits;
            long marksDelta = c.VanguardMarks - b.VanguardMarks;

            double cmdrXpSum = c.CommanderXp - b.CommanderXp;
            for (int lvl = b.CommanderLevel; lvl < c.CommanderLevel; lvl++)
            {
                cmdrXpSum += Source.Util.GameMath.GetMaxExperienceForLevel(lvl);
            }
            long cmdrXpDelta = (long)Math.Round(cmdrXpSum);

            double empXpSum = c.EmpireXp - b.EmpireXp;
            for (int lvl = b.EmpireLevel; lvl < c.EmpireLevel; lvl++)
            {
                empXpSum += Source.Player.EmpireLevel.GetExperienceToNext(lvl);
            }
            long empXpDelta = (long)Math.Round(empXpSum);

            double rateHours = durationHours >= 0.002 ? durationHours : 0.0;
            long creditsPerHour = rateHours > 0 ? (long)Math.Round(creditsDelta / rateHours) : 0;
            long cmdrXpPerHour = rateHours > 0 ? (long)Math.Round(cmdrXpDelta / rateHours) : 0;
            long empXpPerHour = rateHours > 0 ? (long)Math.Round(empXpDelta / rateHours) : 0;

            var deltas = new ComputedDeltas
            {
                ElapsedHours = Math.Round(elapsedDelta, 3),
                Credits = creditsDelta,
                CreditsPerHour = creditsPerHour,
                VanguardMarks = marksDelta,
                CommanderLevel = c.CommanderLevel - b.CommanderLevel,
                CommanderXp = cmdrXpDelta,
                CommanderXpPerHour = cmdrXpPerHour,
                EmpireLevel = c.EmpireLevel - b.EmpireLevel,
                EmpireXp = empXpDelta,
                EmpireXpPerHour = empXpPerHour
            };

            // Ranks
            foreach (var rk in new[] { "Patrol", "Bounty", "Industry" })
            {
                int bv = b.Ranks != null && b.Ranks.TryGetValue(rk, out int x) ? x : 0;
                int cv = c.Ranks != null && c.Ranks.TryGetValue(rk, out int y) ? y : 0;
                deltas.Ranks[rk] = cv - bv;
            }

            // Dog Tags
            int dtDeltaTotal = 0;
            int dtVmBase = 0;
            int dtVmCurr = 0;
            foreach (var dt in DogTagTypes)
            {
                int bc = b.DogTagsByType != null && b.DogTagsByType.TryGetValue(dt.Id, out int x) ? x : 0;
                int cc = c.DogTagsByType != null && c.DogTagsByType.TryGetValue(dt.Id, out int y) ? y : 0;
                int dc = cc - bc;
                dtDeltaTotal += dc;
                dtVmBase += bc * dt.VmEach;
                dtVmCurr += cc * dt.VmEach;
                if (cc > 0 || dc != 0)
                {
                    deltas.DogTagsBreakdown.Add(new DogTagBreakdownEntry
                    {
                        Id = dt.Id,
                        Label = dt.Label,
                        VmEach = dt.VmEach,
                        Baseline = bc,
                        Current = cc,
                        Delta = dc,
                        VmCurrent = cc * dt.VmEach,
                        VmDelta = dc * dt.VmEach
                    });
                }
            }
            deltas.DogTags = dtDeltaTotal;
            deltas.DogTagsVm = dtVmCurr - dtVmBase;

            // Reputation
            var allFactions = new SortedSet<string>(StringComparer.Ordinal);
            if (b.Reputation != null) foreach (var k in b.Reputation.Keys) allFactions.Add(k);
            if (c.Reputation != null) foreach (var k in c.Reputation.Keys) allFactions.Add(k);
            foreach (var fac in allFactions)
            {
                int bv = b.Reputation != null && b.Reputation.TryGetValue(fac, out int x) ? x : 0;
                int cv = c.Reputation != null && c.Reputation.TryGetValue(fac, out int y) ? y : 0;
                int dv = cv - bv;
                if (dv != 0)
                {
                    deltas.Reputation.Add(new ReputationDeltaEntry
                    {
                        Faction = fac,
                        Current = cv,
                        Delta = dv
                    });
                }
            }
            deltas.Reputation.Sort((a, z) => z.Delta.CompareTo(a.Delta));

            // New modules
            if (c.Modules != null)
            {
                foreach (var kv in c.Modules)
                {
                    int bv = b.Modules != null && b.Modules.TryGetValue(kv.Key, out int x) ? x : 0;
                    int diff = kv.Value - bv;
                    if (diff > 0)
                    {
                        var parts = kv.Key.Split(KeySep);
                        string name = parts.Length > 0 ? parts[0] : kv.Key;
                        string rarity = parts.Length > 1 ? parts[1] : "Common";
                        int ilvl = parts.Length > 2 && int.TryParse(parts[2], out int l) ? l : 0;
                        string eqType = parts.Length > 3 ? parts[3] : string.Empty;
                        var entry = new ModuleDeltaEntry
                        {
                            Name = name,
                            Rarity = rarity,
                            ItemLevel = ilvl,
                            EquipmentType = eqType,
                            Gained = diff
                        };
                        if (c.ModuleSubstats != null && c.ModuleSubstats.TryGetValue(kv.Key, out var subs) && subs != null)
                        {
                            entry.Substats = subs;
                        }
                        if (c.ModuleAspects != null && c.ModuleAspects.TryGetValue(kv.Key, out var asps) && asps != null)
                        {
                            entry.Aspects = asps;
                        }
                        deltas.NewModules.Add(entry);
                    }
                }
                deltas.NewModules.Sort((a, z) =>
                {
                    int cmp = z.ItemLevel.CompareTo(a.ItemLevel);
                    if (cmp != 0) return cmp;
                    cmp = string.Compare(a.Rarity, z.Rarity, StringComparison.Ordinal);
                    return cmp != 0 ? cmp : string.Compare(a.Name, z.Name, StringComparison.Ordinal);
                });
            }

            // Ship blueprints
            var baseUnlocked = new HashSet<string>(b.UnlockedShipBlueprints ?? new List<string>(), StringComparer.Ordinal);
            var currUnlocked = new HashSet<string>(c.UnlockedShipBlueprints ?? new List<string>(), StringComparer.Ordinal);
            foreach (var s in currUnlocked)
            {
                if (!baseUnlocked.Contains(s)) deltas.NewlyUnlockedShips.Add(s);
            }
            deltas.NewlyUnlockedShips.Sort(StringComparer.Ordinal);

            if (c.ShipBlueprintPieces != null)
            {
                foreach (var kv in c.ShipBlueprintPieces)
                {
                    int bv = b.ShipBlueprintPieces != null && b.ShipBlueprintPieces.TryGetValue(kv.Key, out int x) ? x : 0;
                    int diff = kv.Value - bv;
                    if (diff > 0)
                    {
                        string shipId = kv.Key;
                        string disp = c.ShipDisplayNames != null && c.ShipDisplayNames.TryGetValue(shipId, out var dn) && !string.IsNullOrEmpty(dn) ? dn : shipId;
                        string size = c.ShipSizes != null && c.ShipSizes.TryGetValue(shipId, out var sz) ? sz : string.Empty;
                        int req = !string.IsNullOrEmpty(size) && RequiredPiecesBySize.TryGetValue(size, out int r) ? r : 0;
                        bool alreadyUnlocked = currUnlocked.Contains(shipId);
                        bool canComplete = req > 0 && kv.Value >= req && !alreadyUnlocked;
                        deltas.NewShipBlueprints.Add(new ShipBlueprintDeltaEntry
                        {
                            ShipId = shipId,
                            DisplayName = disp,
                            Size = size,
                            Gained = diff,
                            Total = kv.Value,
                            Required = req,
                            CanComplete = canComplete,
                            AlreadyUnlocked = alreadyUnlocked
                        });
                    }
                }
                deltas.NewShipBlueprints.Sort((a, z) =>
                {
                    int cmp = z.CanComplete.CompareTo(a.CanComplete);
                    if (cmp != 0) return cmp;
                    cmp = z.Total.CompareTo(a.Total);
                    return cmp != 0 ? cmp : string.Compare(a.DisplayName, z.DisplayName, StringComparison.Ordinal);
                });
            }

            // Module blueprint items
            if (c.ModuleBlueprintItems != null)
            {
                foreach (var kv in c.ModuleBlueprintItems)
                {
                    int bv = b.ModuleBlueprintItems != null && b.ModuleBlueprintItems.TryGetValue(kv.Key, out int x) ? x : 0;
                    int diff = kv.Value - bv;
                    if (diff > 0)
                    {
                        var parts = kv.Key.Split(KeySep);
                        string disp = parts.Length > 0 ? parts[0] : kv.Key;
                        string bpName = parts.Length > 1 ? parts[1] : string.Empty;
                        int lvl = parts.Length > 2 && int.TryParse(parts[2], out int l) ? l : 0;
                        string rarity = parts.Length > 3 ? parts[3] : string.Empty;
                        deltas.NewModuleBlueprintItems.Add(new ModuleBlueprintItemDeltaEntry
                        {
                            DisplayName = disp,
                            BlueprintName = bpName,
                            Level = lvl,
                            Rarity = rarity,
                            Gained = diff
                        });
                    }
                }
                deltas.NewModuleBlueprintItems.Sort((a, z) =>
                {
                    int cmp = z.Level.CompareTo(a.Level);
                    return cmp != 0 ? cmp : string.Compare(a.DisplayName, z.DisplayName, StringComparison.Ordinal);
                });
            }

            // Uploaded blueprints
            if (c.UploadedBlueprints != null)
            {
                foreach (var kv in c.UploadedBlueprints)
                {
                    int oldLvl = 0;
                    bool hasOld = b.UploadedBlueprints != null && b.UploadedBlueprints.TryGetValue(kv.Key, out oldLvl);
                    if (!hasOld || kv.Value > oldLvl)
                    {
                        deltas.NewUploadedBlueprints.Add(new UploadedBlueprintDeltaEntry
                        {
                            Recipe = kv.Key,
                            OldLevel = hasOld ? (int?)oldLvl : null,
                            NewLevel = kv.Value
                        });
                    }
                }
                deltas.NewUploadedBlueprints.Sort((a, z) =>
                {
                    int cmp = z.NewLevel.CompareTo(a.NewLevel);
                    return cmp != 0 ? cmp : string.Compare(a.Recipe, z.Recipe, StringComparison.Ordinal);
                });
            }

            // Captured ships
            if (c.CapturedShips != null)
            {
                foreach (var kv in c.CapturedShips)
                {
                    if (b.CapturedShips == null || !b.CapturedShips.ContainsKey(kv.Key))
                    {
                        var info = kv.Value;
                        deltas.NewCapturedShips.Add(new CapturedShipDeltaEntry
                        {
                            Guid = kv.Key,
                            ShipClass = info.ShipClass,
                            DisplayName = !string.IsNullOrEmpty(info.DisplayName) ? info.DisplayName : info.ShipClass,
                            Size = info.Size,
                            Rarity = info.ShipRarity,
                            CustomName = info.CustomName
                        });
                    }
                }
                deltas.NewCapturedShips.Sort((a, z) => string.Compare(a.DisplayName, z.DisplayName, StringComparison.Ordinal));
            }

            // Locked crates
            if (c.LockedCrates != null)
            {
                int totalGained = 0;
                foreach (var kv in c.LockedCrates)
                {
                    int bv = b.LockedCrates != null && b.LockedCrates.TryGetValue(kv.Key, out int x) ? x : 0;
                    int diff = kv.Value - bv;
                    if (diff > 0)
                    {
                        totalGained += diff;
                        var parts = kv.Key.Split(KeySep);
                        string name = parts.Length > 0 ? parts[0] : kv.Key;
                        string rarity = parts.Length > 1 ? parts[1] : "Standard";
                        int ilvl = parts.Length > 2 && int.TryParse(parts[2], out int l) ? l : 0;
                        deltas.NewLockedCrates.Add(new LockedCrateDeltaEntry
                        {
                            Name = name,
                            Rarity = rarity,
                            ItemLevel = ilvl,
                            Gained = diff
                        });
                    }
                }
                deltas.LockedCratesTotal = totalGained;
                deltas.NewLockedCrates.Sort((a, z) =>
                {
                    int cmp = z.ItemLevel.CompareTo(a.ItemLevel);
                    if (cmp != 0) return cmp;
                    cmp = string.Compare(a.Rarity, z.Rarity, StringComparison.Ordinal);
                    return cmp != 0 ? cmp : string.Compare(a.Name, z.Name, StringComparison.Ordinal);
                });
            }

            return deltas;
        }

        private static bool HasLegacyModuleKeys(PlayerSnapshot snap)
        {
            if (snap?.Modules == null || snap.Modules.Count == 0) return false;
            foreach (var key in snap.Modules.Keys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                var parts = key.Split(KeySep);
                if (parts.Length < 5) return true;
            }
            return false;
        }

        private string GetSaveDir(string saveId) => Path.Combine(_rootDir, SanitizeSaveId(saveId));

        private static string SanitizeSaveId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "default";
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            return sb.Length > 0 ? sb.ToString() : "default";
        }
    }
}
