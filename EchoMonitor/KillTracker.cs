using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace EchoMonitor
{
    public sealed class KillRecord
    {
        public int Id;
        public string Timestamp;
        public string VictimName;
        public string VictimClass;
        public string VictimSize;
        public string VictimRank;
        public int VictimLevel;
        public string VictimFaction;
        public bool IsDrone;
        public string KillerName;
        public string KillerClass;
        public string WeaponName;
        public string SystemName;
        public string PoiName;
        public string MissionName;
        public int WaveNumber;
        public int PointValue;
    }

    public sealed class WaveUnitSummary
    {
        public string ShipClass;
        public string Rank;
        public int Level;
        public int Count;
    }

    public sealed class WaveRecord
    {
        public int Id;
        public int WaveNumber;
        public string Timestamp;
        public string SystemName;
        public string PoiName;
        public string MissionName;
        public int UnitCount;
        public List<WaveUnitSummary> Units = new List<WaveUnitSummary>();
    }

    public sealed class KillTracker
    {
        private const int MaxKills = 500;
        private const int MaxWaves = 200;

        private readonly object _lock = new object();
        private readonly List<KillRecord> _kills = new List<KillRecord>();
        private readonly List<WaveRecord> _waves = new List<WaveRecord>();
        private readonly Dictionary<string, int> _byClass = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _classIsDrone = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _byRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _byFaction = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _byKiller = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private int _nextKillId = 1;
        private int _nextWaveId = 1;
        private int _totalKills;
        private int _totalWaves;
        private string _currentContextKey = string.Empty;
        private string _currentPoi = string.Empty;
        private string _currentMission = string.Empty;
        private int _currentContextWaves;
        private int _currentContextKills;

        public void RecordKill(KillRecord record)
        {
            if (record == null) return;
            lock (_lock)
            {
                string ctxKey = $"{record.SystemName}|{record.PoiName}|{record.MissionName}";
                if (!string.Equals(_currentContextKey, ctxKey, StringComparison.Ordinal))
                {
                    _currentContextKey = ctxKey;
                    _currentPoi = record.PoiName ?? string.Empty;
                    _currentMission = record.MissionName ?? string.Empty;
                    _currentContextWaves = 0;
                    _currentContextKills = 0;
                }

                record.Id = _nextKillId++;
                record.WaveNumber = _currentContextWaves;
                _totalKills++;
                _currentContextKills++;

                _kills.Add(record);
                if (_kills.Count > MaxKills)
                {
                    _kills.RemoveAt(0);
                }

                string cls = string.IsNullOrWhiteSpace(record.VictimClass) ? "Unknown" : record.VictimClass;
                _byClass[cls] = _byClass.TryGetValue(cls, out int c) ? c + 1 : 1;
                _classIsDrone[cls] = record.IsDrone;

                string rank = string.IsNullOrWhiteSpace(record.VictimRank) ? "Standard" : record.VictimRank;
                _byRank[rank] = _byRank.TryGetValue(rank, out int r) ? r + 1 : 1;

                string fac = string.IsNullOrWhiteSpace(record.VictimFaction) ? "Unknown" : record.VictimFaction;
                _byFaction[fac] = _byFaction.TryGetValue(fac, out int f) ? f + 1 : 1;

                string killer = string.IsNullOrWhiteSpace(record.KillerName) ? "Fleet" : record.KillerName;
                _byKiller[killer] = _byKiller.TryGetValue(killer, out int k) ? k + 1 : 1;
            }
        }

        public void RecordWave(WaveRecord wave)
        {
            if (wave == null) return;
            lock (_lock)
            {
                string ctxKey = $"{wave.SystemName}|{wave.PoiName}|{wave.MissionName}";
                if (!string.Equals(_currentContextKey, ctxKey, StringComparison.Ordinal))
                {
                    _currentContextKey = ctxKey;
                    _currentPoi = wave.PoiName ?? string.Empty;
                    _currentMission = wave.MissionName ?? string.Empty;
                    _currentContextWaves = 0;
                    _currentContextKills = 0;
                }

                _totalWaves++;
                _currentContextWaves++;
                wave.Id = _nextWaveId++;
                wave.WaveNumber = _currentContextWaves;

                _waves.Add(wave);
                if (_waves.Count > MaxWaves)
                {
                    _waves.RemoveAt(0);
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _kills.Clear();
                _waves.Clear();
                _byClass.Clear();
                _classIsDrone.Clear();
                _byRank.Clear();
                _byFaction.Clear();
                _byKiller.Clear();
                _nextKillId = 1;
                _nextWaveId = 1;
                _totalKills = 0;
                _totalWaves = 0;
                _currentContextWaves = 0;
                _currentContextKills = 0;
            }
        }

        public string GetKillsJson()
        {
            lock (_lock)
            {
                var sb = new StringBuilder(8192);
                sb.Append('{');
                sb.Append("\"totalKills\":").Append(_totalKills);
                sb.Append(",\"totalWaves\":").Append(_totalWaves);
                sb.Append(",\"currentPoi\":"); AppendString(sb, _currentPoi);
                sb.Append(",\"currentMission\":"); AppendString(sb, _currentMission);
                sb.Append(",\"currentMissionWaves\":").Append(_currentContextWaves);
                sb.Append(",\"currentMissionKills\":").Append(_currentContextKills);

                sb.Append(",\"byClass\":[");
                int idx = 0;
                foreach (var kv in _byClass.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                {
                    if (idx++ > 0) sb.Append(',');
                    bool isDrone = _classIsDrone.TryGetValue(kv.Key, out bool d) && d;
                    sb.Append("{\"shipClass\":"); AppendString(sb, kv.Key);
                    sb.Append(",\"count\":").Append(kv.Value);
                    sb.Append(",\"isDrone\":").Append(isDrone ? "true" : "false").Append('}');
                }
                sb.Append(']');

                sb.Append(",\"byRank\":[");
                idx = 0;
                foreach (var kv in _byRank.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                {
                    if (idx++ > 0) sb.Append(',');
                    sb.Append("{\"rank\":"); AppendString(sb, kv.Key);
                    sb.Append(",\"count\":").Append(kv.Value).Append('}');
                }
                sb.Append(']');

                sb.Append(",\"byFaction\":[");
                idx = 0;
                foreach (var kv in _byFaction.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                {
                    if (idx++ > 0) sb.Append(',');
                    sb.Append("{\"faction\":"); AppendString(sb, kv.Key);
                    sb.Append(",\"count\":").Append(kv.Value).Append('}');
                }
                sb.Append(']');

                sb.Append(",\"byKiller\":[");
                idx = 0;
                foreach (var kv in _byKiller.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                {
                    if (idx++ > 0) sb.Append(',');
                    sb.Append("{\"killer\":"); AppendString(sb, kv.Key);
                    sb.Append(",\"count\":").Append(kv.Value).Append('}');
                }
                sb.Append(']');

                sb.Append(",\"waves\":[");
                for (int i = _waves.Count - 1; i >= 0; i--)
                {
                    if (i < _waves.Count - 1) sb.Append(',');
                    var w = _waves[i];
                    sb.Append("{\"id\":").Append(w.Id);
                    sb.Append(",\"waveNumber\":").Append(w.WaveNumber);
                    sb.Append(",\"timestamp\":"); AppendString(sb, w.Timestamp);
                    sb.Append(",\"systemName\":"); AppendString(sb, w.SystemName);
                    sb.Append(",\"poiName\":"); AppendString(sb, w.PoiName);
                    sb.Append(",\"missionName\":"); AppendString(sb, w.MissionName);
                    sb.Append(",\"unitCount\":").Append(w.UnitCount);
                    sb.Append(",\"units\":[");
                    for (int u = 0; u < w.Units.Count; u++)
                    {
                        if (u > 0) sb.Append(',');
                        var wu = w.Units[u];
                        sb.Append("{\"shipClass\":"); AppendString(sb, wu.ShipClass);
                        sb.Append(",\"rank\":"); AppendString(sb, wu.Rank);
                        sb.Append(",\"level\":").Append(wu.Level);
                        sb.Append(",\"count\":").Append(wu.Count).Append('}');
                    }
                    sb.Append("]}");
                }
                sb.Append(']');

                sb.Append(",\"kills\":[");
                for (int i = _kills.Count - 1; i >= 0; i--)
                {
                    if (i < _kills.Count - 1) sb.Append(',');
                    var k = _kills[i];
                    sb.Append("{\"id\":").Append(k.Id);
                    sb.Append(",\"timestamp\":"); AppendString(sb, k.Timestamp);
                    sb.Append(",\"victimName\":"); AppendString(sb, k.VictimName);
                    sb.Append(",\"victimClass\":"); AppendString(sb, k.VictimClass);
                    sb.Append(",\"victimSize\":"); AppendString(sb, k.VictimSize);
                    sb.Append(",\"victimRank\":"); AppendString(sb, k.VictimRank);
                    sb.Append(",\"victimLevel\":").Append(k.VictimLevel);
                    sb.Append(",\"victimFaction\":"); AppendString(sb, k.VictimFaction);
                    sb.Append(",\"isDrone\":").Append(k.IsDrone ? "true" : "false");
                    sb.Append(",\"killerName\":"); AppendString(sb, k.KillerName);
                    sb.Append(",\"killerClass\":"); AppendString(sb, k.KillerClass);
                    sb.Append(",\"weaponName\":"); AppendString(sb, k.WeaponName);
                    sb.Append(",\"systemName\":"); AppendString(sb, k.SystemName);
                    sb.Append(",\"poiName\":"); AppendString(sb, k.PoiName);
                    sb.Append(",\"missionName\":"); AppendString(sb, k.MissionName);
                    sb.Append(",\"waveNumber\":").Append(k.WaveNumber);
                    sb.Append(",\"pointValue\":").Append(k.PointValue).Append('}');
                }
                sb.Append("]}");
                return sb.ToString();
            }
        }

        private static void AppendString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("\"\"");
                return;
            }
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
