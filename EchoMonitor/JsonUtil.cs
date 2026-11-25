using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EchoMonitor
{
    public static class JsonUtil
    {
        public static void AppendString(StringBuilder sb, string value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append('"');
            foreach (char c in value)
            {
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
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        public static void AppendDouble(StringBuilder sb, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                sb.Append('0');
                return;
            }
            sb.Append(Math.Round(value, 3).ToString(CultureInfo.InvariantCulture));
        }

        public static void AppendBool(StringBuilder sb, bool value)
        {
            sb.Append(value ? "true" : "false");
        }

        public static void AppendStringIntDict(StringBuilder sb, Dictionary<string, int> dict)
        {
            sb.Append('{');
            if (dict != null)
            {
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    AppendString(sb, kv.Key);
                    sb.Append(':');
                    sb.Append(kv.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
            sb.Append('}');
        }

        public static void AppendStringStringDict(StringBuilder sb, Dictionary<string, string> dict)
        {
            sb.Append('{');
            if (dict != null)
            {
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    AppendString(sb, kv.Key);
                    sb.Append(':');
                    AppendString(sb, kv.Value);
                }
            }
            sb.Append('}');
        }

        public static void AppendStringList(StringBuilder sb, List<string> list)
        {
            sb.Append('[');
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    AppendString(sb, list[i]);
                }
            }
            sb.Append(']');
        }

        public static string SerializePlayerSnapshot(PlayerSnapshot s)
        {
            var sb = new StringBuilder(2048);
            sb.Append('{');
            sb.Append("\"timestamp\":"); AppendString(sb, s.Timestamp);
            sb.Append(",\"saveId\":"); AppendString(sb, s.SaveId);
            sb.Append(",\"elapsedHours\":"); AppendDouble(sb, s.ElapsedHours);
            sb.Append(",\"credits\":"); sb.Append(s.Credits.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"vanguardMarks\":"); sb.Append(s.VanguardMarks.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"dogTagsByType\":"); AppendStringIntDict(sb, s.DogTagsByType);
            sb.Append(",\"reputation\":"); AppendStringIntDict(sb, s.Reputation);
            sb.Append(",\"commanderLevel\":"); sb.Append(s.CommanderLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderXp\":"); sb.Append(s.CommanderXp.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderProgressPct\":"); AppendDouble(sb, Math.Round(s.CommanderProgressPct, 1));
            sb.Append(",\"empireLevel\":"); sb.Append(s.EmpireLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"empireXp\":"); sb.Append(s.EmpireXp.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"empireProgressPct\":"); AppendDouble(sb, Math.Round(s.EmpireProgressPct, 1));
            sb.Append(",\"ranks\":"); AppendStringIntDict(sb, s.Ranks);
            sb.Append(",\"modules\":"); AppendStringIntDict(sb, s.Modules);
            sb.Append(",\"shipBlueprintPieces\":"); AppendStringIntDict(sb, s.ShipBlueprintPieces);
            sb.Append(",\"shipDisplayNames\":"); AppendStringStringDict(sb, s.ShipDisplayNames);
            sb.Append(",\"shipSizes\":"); AppendStringStringDict(sb, s.ShipSizes);
            sb.Append(",\"unlockedShipBlueprints\":"); AppendStringList(sb, s.UnlockedShipBlueprints);
            sb.Append(",\"moduleBlueprintItems\":"); AppendStringIntDict(sb, s.ModuleBlueprintItems);
            sb.Append(",\"uploadedBlueprints\":"); AppendStringIntDict(sb, s.UploadedBlueprints);
            sb.Append(",\"officerLevels\":"); AppendStringIntDict(sb, s.OfficerLevels);
            sb.Append(",\"lockedCrates\":"); AppendStringIntDict(sb, s.LockedCrates);
            sb.Append(",\"depotRawMaterials\":"); AppendStringIntDict(sb, s.DepotRawMaterials);
            sb.Append(",\"depotRefinedProducts\":"); AppendStringIntDict(sb, s.DepotRefinedProducts);
            sb.Append(",\"capturedShips\":{");
            if (s.CapturedShips != null)
            {
                bool first = true;
                foreach (var kv in s.CapturedShips)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    AppendString(sb, kv.Key);
                    sb.Append(":{\"shipClass\":"); AppendString(sb, kv.Value.ShipClass);
                    sb.Append(",\"shipRarity\":"); AppendString(sb, kv.Value.ShipRarity);
                    sb.Append(",\"customName\":"); AppendString(sb, kv.Value.CustomName);
                    sb.Append(",\"displayName\":"); AppendString(sb, kv.Value.DisplayName);
                    sb.Append(",\"size\":"); AppendString(sb, kv.Value.Size);
                    sb.Append('}');
                }
            }
            sb.Append("}}");
            return sb.ToString();
        }

        public static PlayerSnapshot DeserializePlayerSnapshot(string json)
        {
            var root = Parse(json) as Dictionary<string, object>;
            if (root == null) return null;

            var s = new PlayerSnapshot
            {
                Timestamp = GetString(root, "timestamp", DateTime.UtcNow.ToString("o")),
                SaveId = GetString(root, "saveId", "default"),
                ElapsedHours = GetDouble(root, "elapsedHours"),
                Credits = GetLong(root, "credits"),
                VanguardMarks = GetLong(root, "vanguardMarks"),
                DogTagsByType = GetStringIntDict(root, "dogTagsByType"),
                Reputation = GetStringIntDict(root, "reputation"),
                CommanderLevel = GetInt(root, "commanderLevel"),
                CommanderXp = GetLong(root, "commanderXp"),
                EmpireLevel = GetInt(root, "empireLevel"),
                EmpireXp = GetLong(root, "empireXp"),
                Ranks = GetStringIntDict(root, "ranks"),
                Modules = GetStringIntDict(root, "modules"),
                ShipBlueprintPieces = GetStringIntDict(root, "shipBlueprintPieces"),
                ShipDisplayNames = GetStringStringDict(root, "shipDisplayNames"),
                ShipSizes = GetStringStringDict(root, "shipSizes"),
                UnlockedShipBlueprints = GetStringList(root, "unlockedShipBlueprints"),
                ModuleBlueprintItems = GetStringIntDict(root, "moduleBlueprintItems"),
                UploadedBlueprints = GetStringIntDict(root, "uploadedBlueprints"),
                OfficerLevels = GetStringIntDict(root, "officerLevels"),
                LockedCrates = GetStringIntDict(root, "lockedCrates"),
                DepotRawMaterials = GetStringIntDict(root, "depotRawMaterials"),
                DepotRefinedProducts = GetStringIntDict(root, "depotRefinedProducts"),
                CapturedShips = new Dictionary<string, CapturedShipInfo>()
            };

            if (root.TryGetValue("capturedShips", out var csObj) && csObj is Dictionary<string, object> csDict)
            {
                foreach (var kv in csDict)
                {
                    if (kv.Value is Dictionary<string, object> info)
                    {
                        s.CapturedShips[kv.Key] = new CapturedShipInfo
                        {
                            ShipClass = GetString(info, "shipClass", "Unknown"),
                            ShipRarity = GetString(info, "shipRarity", "None"),
                            CustomName = GetString(info, "customName", string.Empty),
                            DisplayName = GetString(info, "displayName", string.Empty),
                            Size = GetString(info, "size", string.Empty)
                        };
                    }
                }
            }

            return s;
        }

        public static string SerializeHistoryEntry(HistoryEntry h)
        {
            var sb = new StringBuilder(512);
            AppendHistoryEntry(sb, h);
            return sb.ToString();
        }

        public static string SerializeHistoryList(List<HistoryEntry> list)
        {
            var sb = new StringBuilder(Math.Max(256, list.Count * 256));
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendHistoryEntry(sb, list[i]);
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static void AppendHistoryEntry(StringBuilder sb, HistoryEntry h)
        {
            sb.Append('{');
            sb.Append("\"timestamp\":"); AppendString(sb, h.Timestamp);
            sb.Append(",\"elapsedHours\":"); AppendDouble(sb, h.ElapsedHours);
            sb.Append(",\"credits\":"); sb.Append(h.Credits.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"creditsDelta\":"); sb.Append(h.CreditsDelta.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"vanguardMarks\":"); sb.Append(h.VanguardMarks.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"marksDelta\":"); sb.Append(h.MarksDelta.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"dogTags\":"); sb.Append(h.DogTags.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"dogTagsDelta\":"); sb.Append(h.DogTagsDelta.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"dogTagsVmDelta\":"); sb.Append(h.DogTagsVmDelta.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderLevel\":"); sb.Append(h.CommanderLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderLevelDelta\":"); sb.Append(h.CommanderLevelDelta.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderXp\":"); sb.Append(h.CommanderXp.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderXpDelta\":"); sb.Append(h.CommanderXpDelta.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderProgressPct\":"); AppendDouble(sb, h.CommanderProgressPct);
            sb.Append(",\"empireLevel\":"); sb.Append(h.EmpireLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"empireLevelDelta\":"); sb.Append(h.EmpireLevelDelta.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"empireXp\":"); sb.Append(h.EmpireXp.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"empireXpDelta\":"); sb.Append(h.EmpireXpDelta.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"empireProgressPct\":"); AppendDouble(sb, h.EmpireProgressPct);
            sb.Append(",\"newModulesCount\":"); sb.Append(h.NewModulesCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"newShipBpsCount\":"); sb.Append(h.NewShipBpsCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"newModBpsCount\":"); sb.Append(h.NewModBpsCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"capturedShipsCount\":"); sb.Append(h.CapturedShipsCount.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
        }

        public static HistoryEntry DeserializeHistoryEntry(string json)
        {
            var root = Parse(json) as Dictionary<string, object>;
            if (root == null) return null;
            return new HistoryEntry
            {
                Timestamp = GetString(root, "timestamp", string.Empty),
                ElapsedHours = GetDouble(root, "elapsedHours"),
                Credits = GetLong(root, "credits"),
                CreditsDelta = GetLong(root, "creditsDelta"),
                VanguardMarks = GetLong(root, "vanguardMarks"),
                MarksDelta = GetLong(root, "marksDelta"),
                DogTags = GetInt(root, "dogTags"),
                DogTagsDelta = GetInt(root, "dogTagsDelta"),
                DogTagsVmDelta = GetInt(root, "dogTagsVmDelta"),
                CommanderLevel = GetInt(root, "commanderLevel"),
                CommanderLevelDelta = GetInt(root, "commanderLevelDelta"),
                CommanderXp = GetLong(root, "commanderXp"),
                CommanderXpDelta = GetLong(root, "commanderXpDelta"),
                CommanderProgressPct = GetDouble(root, "commanderProgressPct"),
                EmpireLevel = GetInt(root, "empireLevel"),
                EmpireLevelDelta = GetInt(root, "empireLevelDelta"),
                EmpireXp = GetLong(root, "empireXp"),
                EmpireXpDelta = GetLong(root, "empireXpDelta"),
                EmpireProgressPct = GetDouble(root, "empireProgressPct"),
                NewModulesCount = GetInt(root, "newModulesCount"),
                NewShipBpsCount = GetInt(root, "newShipBpsCount"),
                NewModBpsCount = GetInt(root, "newModBpsCount"),
                CapturedShipsCount = GetInt(root, "capturedShipsCount")
            };
        }

        public static string SerializeStateResponse(
            bool inGame,
            string saveId,
            string sessionStartedAt,
            double sessionDurationHours,
            EchoStatus echo,
            CrewStatus crew,
            DroneBayStatus droneBay,
            HangarBayStatus hangarBay,
            List<FleetShipInfo> fleet,
            AlertsStatus alerts,
            EmpireStatus empire,
            List<MissionLogEntry> missionLog,
            PlayerSnapshot current,
            PlayerSnapshot baseline,
            ComputedDeltas deltas)
        {
            var sb = new StringBuilder(4096);
            sb.Append('{');
            sb.Append("\"inGame\":"); AppendBool(sb, inGame);
            sb.Append(",\"timestamp\":"); AppendString(sb, DateTime.UtcNow.ToString("o"));
            sb.Append(",\"saveId\":"); AppendString(sb, saveId ?? string.Empty);
            sb.Append(",\"sessionStartedAt\":"); AppendString(sb, sessionStartedAt ?? string.Empty);
            sb.Append(",\"sessionDurationHours\":"); AppendDouble(sb, sessionDurationHours);

            sb.Append(",\"echo\":");
            if (echo == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append('{');
                sb.Append("\"enabled\":"); AppendBool(sb, echo.Enabled);
                sb.Append(",\"state\":"); AppendString(sb, echo.State);
                sb.Append(",\"systemName\":"); AppendString(sb, echo.SystemName);
                sb.Append(",\"poiName\":"); AppendString(sb, echo.PoiName);
                sb.Append(",\"poiType\":"); AppendString(sb, echo.PoiType);
                sb.Append(",\"docked\":"); AppendBool(sb, echo.Docked);
                sb.Append(",\"activeMission\":"); AppendString(sb, echo.ActiveMission);
                sb.Append(",\"hull\":"); AppendDouble(sb, echo.Hull);
                sb.Append(",\"maxHull\":"); AppendDouble(sb, echo.MaxHull);
                sb.Append(",\"armor\":"); AppendDouble(sb, echo.Armor);
                sb.Append(",\"maxArmor\":"); AppendDouble(sb, echo.MaxArmor);
                sb.Append(",\"shield\":"); AppendDouble(sb, echo.Shield);
                sb.Append(",\"maxShield\":"); AppendDouble(sb, echo.MaxShield);
                sb.Append('}');
            }

            sb.Append(",\"crew\":");
            if (crew == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append('{');
                sb.Append("\"totalCrew\":"); sb.Append(crew.TotalCrew.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"maxCrew\":"); sb.Append(crew.MaxCrew.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"roles\":"); AppendStringIntDict(sb, crew.Roles);
                sb.Append(",\"officers\":[");
                if (crew.Officers != null)
                {
                    for (int i = 0; i < crew.Officers.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        var o = crew.Officers[i];
                        sb.Append("{\"name\":"); AppendString(sb, o.Name);
                        sb.Append(",\"profession\":"); AppendString(sb, o.Profession);
                        sb.Append(",\"level\":"); sb.Append(o.Level.ToString(CultureInfo.InvariantCulture));
                        sb.Append(",\"levelDelta\":"); sb.Append(o.LevelDelta.ToString(CultureInfo.InvariantCulture));
                        sb.Append(",\"rarity\":"); AppendString(sb, o.Rarity);
                        sb.Append('}');
                    }
                }
                sb.Append("]}");
            }

            sb.Append(",\"droneBay\":");
            AppendDroneBay(sb, droneBay);

            sb.Append(",\"hangarBay\":");
            AppendHangarBay(sb, hangarBay);

            sb.Append(",\"fleet\":");
            if (fleet == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append('[');
                for (int i = 0; i < fleet.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var fs = fleet[i];
                    sb.Append("{\"guid\":"); AppendString(sb, fs.Guid);
                    sb.Append(",\"name\":"); AppendString(sb, fs.Name);
                    sb.Append(",\"shipClass\":"); AppendString(sb, fs.ShipClass);
                    sb.Append(",\"rarity\":"); AppendString(sb, fs.Rarity);
                    sb.Append(",\"isFlagship\":"); AppendBool(sb, fs.IsFlagship);
                    sb.Append(",\"stance\":"); AppendString(sb, fs.Stance);
                    sb.Append(",\"activity\":"); AppendString(sb, fs.Activity);
                    sb.Append(",\"awayForRepairs\":"); AppendBool(sb, fs.AwayForRepairs);
                    sb.Append(",\"repairTimer\":"); AppendDouble(sb, fs.RepairTimer);
                    sb.Append(",\"hull\":"); AppendDouble(sb, fs.Hull);
                    sb.Append(",\"maxHull\":"); AppendDouble(sb, fs.MaxHull);
                    sb.Append(",\"armor\":"); AppendDouble(sb, fs.Armor);
                    sb.Append(",\"maxArmor\":"); AppendDouble(sb, fs.MaxArmor);
                    sb.Append(",\"shield\":"); AppendDouble(sb, fs.Shield);
                    sb.Append(",\"maxShield\":"); AppendDouble(sb, fs.MaxShield);
                    sb.Append(",\"droneBay\":"); AppendDroneBay(sb, fs.DroneBay);
                    sb.Append(",\"hangarBay\":"); AppendHangarBay(sb, fs.HangarBay);
                    sb.Append('}');
                }
                sb.Append(']');
            }

            sb.Append(",\"alerts\":");
            if (alerts == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append('{');
                sb.Append("\"stuck\":"); AppendBool(sb, alerts.Stuck);
                sb.Append(",\"emergencyWarps15m\":"); sb.Append(alerts.EmergencyWarps15m.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"items\":[");
                if (alerts.Items != null)
                {
                    for (int i = 0; i < alerts.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        var a = alerts.Items[i];
                        sb.Append("{\"id\":"); AppendString(sb, a.Id);
                        sb.Append(",\"type\":"); AppendString(sb, a.Type);
                        sb.Append(",\"severity\":"); AppendString(sb, a.Severity);
                        sb.Append(",\"title\":"); AppendString(sb, a.Title);
                        sb.Append(",\"detail\":"); AppendString(sb, a.Detail);
                        sb.Append(",\"triggeredAt\":"); AppendString(sb, a.TriggeredAt);
                        sb.Append('}');
                    }
                }
                sb.Append("]}");
            }

            sb.Append(",\"empire\":");
            if (empire == null)
            {
                sb.Append("null");
            }
            else
            {
                AppendEmpireStatus(sb, empire);
            }

            sb.Append(",\"missionLog\":");
            if (missionLog == null)
            {
                sb.Append("null");
            }
            else
            {
                AppendMissionLogList(sb, missionLog);
            }

            sb.Append(",\"current\":");
            AppendSnapshotSummary(sb, current);

            sb.Append(",\"baseline\":");
            AppendSnapshotSummary(sb, baseline);

            sb.Append(",\"deltas\":");
            if (deltas == null)
            {
                sb.Append("null");
            }
            else
            {
                AppendDeltas(sb, deltas);
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendDroneBay(StringBuilder sb, DroneBayStatus droneBay)
        {
            if (droneBay == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append('{');
            sb.Append("\"equipped\":"); AppendBool(sb, droneBay.Equipped);
            sb.Append(",\"maxDrones\":"); sb.Append(droneBay.MaxDrones.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"builtDrones\":"); sb.Append(droneBay.BuiltDrones.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"deployedDrones\":"); sb.Append(droneBay.DeployedDrones.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"behaviorMode\":"); AppendString(sb, droneBay.BehaviorMode);
            sb.Append(",\"status\":"); AppendString(sb, droneBay.Status);
            sb.Append(",\"rebuilding\":"); AppendBool(sb, droneBay.Rebuilding);
            sb.Append(",\"rebuildProgress\":"); AppendDouble(sb, droneBay.RebuildProgress);
            sb.Append('}');
        }

        private static void AppendHangarBay(StringBuilder sb, HangarBayStatus hangarBay)
        {
            if (hangarBay == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append('{');
            sb.Append("\"equipped\":"); AppendBool(sb, hangarBay.Equipped);
            sb.Append(",\"maxSlots\":"); sb.Append(hangarBay.MaxSlots.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"deployed\":"); sb.Append(hangarBay.Deployed.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"inBay\":"); sb.Append(hangarBay.InBay.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"repairing\":"); sb.Append(hangarBay.Repairing.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"rebuilding\":"); sb.Append(hangarBay.Rebuilding.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"fighters\":[");
            if (hangarBay.Fighters != null)
            {
                for (int i = 0; i < hangarBay.Fighters.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var f = hangarBay.Fighters[i];
                    sb.Append("{\"slot\":"); sb.Append(f.Slot.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"name\":"); AppendString(sb, f.Name);
                    sb.Append(",\"shipClass\":"); AppendString(sb, f.ShipClass);
                    sb.Append(",\"rarity\":"); AppendString(sb, f.Rarity);
                    sb.Append(",\"status\":"); AppendString(sb, f.Status);
                    sb.Append(",\"hull\":"); AppendDouble(sb, f.Hull);
                    sb.Append(",\"maxHull\":"); AppendDouble(sb, f.MaxHull);
                    sb.Append(",\"armor\":"); AppendDouble(sb, f.Armor);
                    sb.Append(",\"maxArmor\":"); AppendDouble(sb, f.MaxArmor);
                    sb.Append(",\"shield\":"); AppendDouble(sb, f.Shield);
                    sb.Append(",\"maxShield\":"); AppendDouble(sb, f.MaxShield);
                    sb.Append(",\"repairFraction\":"); AppendDouble(sb, f.RepairFraction);
                    sb.Append('}');
                }
            }
            sb.Append("]}");
        }

        private static void AppendEmpireStatus(StringBuilder sb, EmpireStatus e)
        {
            sb.Append("{\"outposts\":[");
            if (e.Outposts != null)
            {
                for (int i = 0; i < e.Outposts.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var op = e.Outposts[i];
                    sb.Append("{\"stationGuid\":"); AppendString(sb, op.StationGuid);
                    sb.Append(",\"name\":"); AppendString(sb, op.Name);
                    sb.Append(",\"sectorName\":"); AppendString(sb, op.SectorName);
                    sb.Append(",\"systemName\":"); AppendString(sb, op.SystemName);
                    sb.Append(",\"stationType\":"); AppendString(sb, op.StationType);
                    sb.Append(",\"tier\":"); sb.Append(op.Tier.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"spaceUsed\":"); AppendDouble(sb, op.SpaceUsed);
                    sb.Append(",\"capacity\":"); AppendDouble(sb, op.Capacity);
                    sb.Append(",\"fillPct\":"); AppendDouble(sb, op.FillPct);
                    sb.Append(",\"hasActiveDepot\":"); AppendBool(sb, op.HasActiveDepot);
                    sb.Append(",\"largestStackItemId\":"); AppendString(sb, op.LargestStackItemId);
                    sb.Append(",\"largestStackItemName\":"); AppendString(sb, op.LargestStackItemName);
                    sb.Append(",\"largestStackCount\":"); sb.Append(op.LargestStackCount.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"largestStackM3\":"); AppendDouble(sb, op.LargestStackM3);
                    sb.Append(",\"state\":"); AppendString(sb, op.State);
                    sb.Append(",\"hasStalledJob\":"); AppendBool(sb, op.HasStalledJob);
                    sb.Append(",\"activeJobCount\":"); sb.Append(op.ActiveJobCount.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"jobs\":[");
                    if (op.Jobs != null)
                    {
                        for (int j = 0; j < op.Jobs.Count; j++)
                        {
                            if (j > 0) sb.Append(',');
                            var jb = op.Jobs[j];
                            sb.Append("{\"jobType\":"); AppendString(sb, jb.JobType);
                            sb.Append(",\"name\":"); AppendString(sb, jb.Name);
                            sb.Append(",\"progressPct\":"); AppendDouble(sb, jb.ProgressPct);
                            sb.Append(",\"initialAmount\":"); sb.Append(jb.InitialAmount.ToString(CultureInfo.InvariantCulture));
                            sb.Append(",\"remainingAmount\":"); sb.Append(jb.RemainingAmount.ToString(CultureInfo.InvariantCulture));
                            sb.Append(",\"repeat\":"); AppendBool(sb, jb.Repeat);
                            sb.Append(",\"stalled\":"); AppendBool(sb, jb.Stalled);
                            sb.Append(",\"paused\":"); AppendBool(sb, jb.Paused);
                            sb.Append(",\"awaitingSupplies\":"); AppendBool(sb, jb.AwaitingSupplies);
                            sb.Append(",\"timeRemainingSec\":"); AppendDouble(sb, jb.TimeRemainingSec);
                            sb.Append('}');
                        }
                    }
                    sb.Append("]}");
                }
            }
            sb.Append("],\"depotCount\":"); sb.Append(e.DepotCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"depotSpaceUsed\":"); AppendDouble(sb, e.DepotSpaceUsed);
            sb.Append(",\"depotCapacity\":"); AppendDouble(sb, e.DepotCapacity);
            sb.Append(",\"depotFillPct\":"); AppendDouble(sb, e.DepotFillPct);
            sb.Append(",\"rawBacklogTotal\":"); sb.Append(e.RawBacklogTotal.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"rawBacklogDelta\":"); sb.Append(e.RawBacklogDelta.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"rawBacklogRatePerHour\":"); AppendDouble(sb, e.RawBacklogRatePerHour);
            sb.Append(",\"rawBacklogItems\":"); AppendDepotMaterialList(sb, e.RawBacklogItems);
            sb.Append(",\"refinedStockTotal\":"); sb.Append(e.RefinedStockTotal.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"refinedStockDelta\":"); sb.Append(e.RefinedStockDelta.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"refinedStockRatePerHour\":"); AppendDouble(sb, e.RefinedStockRatePerHour);
            sb.Append(",\"refinedStockItems\":"); AppendDepotMaterialList(sb, e.RefinedStockItems);
            sb.Append(",\"activity\":");
            var act = e.Activity ?? new EmpireActivityDelta();
            sb.Append("{\"refinedTotal\":"); sb.Append(act.RefinedTotal.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"refinedByItem\":"); AppendStringIntDict(sb, act.RefinedByItem);
            sb.Append(",\"tradeRuns\":"); sb.Append(act.TradeRuns.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"tradeEarned\":"); sb.Append(act.TradeEarned.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"tradeSpent\":"); sb.Append(act.TradeSpent.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"tradeProfit\":"); sb.Append(act.TradeProfit.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"stationSalesCredits\":"); sb.Append(act.StationSalesCredits.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"outpostProducedTotal\":"); sb.Append(act.OutpostProducedTotal.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"outpostProducedByItem\":"); AppendStringIntDict(sb, act.OutpostProducedByItem);
            sb.Append(",\"craftedTotal\":"); sb.Append(act.CraftedTotal.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"craftedByItem\":"); AppendStringIntDict(sb, act.CraftedByItem);
            sb.Append("}}");
        }

        private static void AppendDepotMaterialList(StringBuilder sb, List<DepotMaterialEntry> list)
        {
            sb.Append('[');
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var item = list[i];
                    sb.Append("{\"itemId\":"); AppendString(sb, item.ItemId);
                    sb.Append(",\"name\":"); AppendString(sb, item.Name);
                    sb.Append(",\"category\":"); AppendString(sb, item.Category);
                    sb.Append(",\"baseline\":"); sb.Append(item.Baseline.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"current\":"); sb.Append(item.Current.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"delta\":"); sb.Append(item.Delta.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"ratePerHour\":"); AppendDouble(sb, item.RatePerHour);
                    sb.Append('}');
                }
            }
            sb.Append(']');
        }

        public static string SerializeDepotInventory(DepotInventoryResponse resp)
        {
            if (resp == null) return "{\"inGame\":false,\"stationCount\":0,\"stations\":[]}";
            var sb = new StringBuilder(4096);
            sb.Append("{\"inGame\":"); sb.Append(resp.InGame ? "true" : "false");
            sb.Append(",\"stationCount\":"); sb.Append(resp.StationCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"stations\":[");
            if (resp.Stations != null)
            {
                for (int i = 0; i < resp.Stations.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var st = resp.Stations[i];
                    sb.Append("{\"stationGuid\":"); AppendString(sb, st.StationGuid);
                    sb.Append(",\"name\":"); AppendString(sb, st.Name);
                    sb.Append(",\"systemName\":"); AppendString(sb, st.SystemName);
                    sb.Append(",\"sectorName\":"); AppendString(sb, st.SectorName);
                    sb.Append(",\"isActiveDepot\":"); sb.Append(st.IsActiveDepot ? "true" : "false");
                    sb.Append(",\"isHomeStation\":"); sb.Append(st.IsHomeStation ? "true" : "false");
                    sb.Append(",\"isPlayerOutpost\":"); sb.Append(st.IsPlayerOutpost ? "true" : "false");
                    sb.Append(",\"spaceUsed\":"); AppendDouble(sb, st.SpaceUsed);
                    sb.Append(",\"capacity\":"); AppendDouble(sb, st.Capacity);
                    sb.Append(",\"fillPct\":"); AppendDouble(sb, st.FillPct);
                    sb.Append(",\"totalItemCount\":"); sb.Append(st.TotalItemCount.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"rawCount\":"); sb.Append(st.RawCount.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"refinedCount\":"); sb.Append(st.RefinedCount.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"otherCount\":"); sb.Append(st.OtherCount.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"items\":[");
                    if (st.Items != null)
                    {
                        for (int j = 0; j < st.Items.Count; j++)
                        {
                            if (j > 0) sb.Append(',');
                            var it = st.Items[j];
                            sb.Append("{\"itemId\":"); AppendString(sb, it.ItemId);
                            sb.Append(",\"name\":"); AppendString(sb, it.Name);
                            sb.Append(",\"group\":"); AppendString(sb, it.Group);
                            sb.Append(",\"category\":"); AppendString(sb, it.Category);
                            sb.Append(",\"count\":"); sb.Append(it.Count.ToString(CultureInfo.InvariantCulture));
                            sb.Append(",\"spaceM3\":"); AppendDouble(sb, it.SpaceM3);
                            sb.Append('}');
                        }
                    }
                    sb.Append("]}");
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendSnapshotSummary(StringBuilder sb, PlayerSnapshot s)
        {
            if (s == null)
            {
                sb.Append("null");
                return;
            }
            int dogTags = SessionManager.SumDogTags(s.DogTagsByType);
            int dogTagsVm = SessionManager.SumDogTagsVm(s.DogTagsByType);

            sb.Append('{');
            sb.Append("\"timestamp\":"); AppendString(sb, s.Timestamp);
            sb.Append(",\"elapsedHours\":"); AppendDouble(sb, s.ElapsedHours);
            sb.Append(",\"credits\":"); sb.Append(s.Credits.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"vanguardMarks\":"); sb.Append(s.VanguardMarks.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"dogTags\":"); sb.Append(dogTags.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"dogTagsVm\":"); sb.Append(dogTagsVm.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderLevel\":"); sb.Append(s.CommanderLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderXp\":"); sb.Append(s.CommanderXp.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderProgressPct\":"); AppendDouble(sb, Math.Round(s.CommanderProgressPct, 1));
            sb.Append(",\"empireLevel\":"); sb.Append(s.EmpireLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"empireXp\":"); sb.Append(s.EmpireXp.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"empireProgressPct\":"); AppendDouble(sb, Math.Round(s.EmpireProgressPct, 1));
            sb.Append(",\"ranks\":"); AppendStringIntDict(sb, s.Ranks);
            sb.Append('}');
        }

        private static void AppendDeltas(StringBuilder sb, ComputedDeltas d)
        {
            sb.Append('{');
            sb.Append("\"elapsedHours\":"); AppendDouble(sb, d.ElapsedHours);
            sb.Append(",\"credits\":"); sb.Append(d.Credits.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"creditsPerHour\":"); sb.Append(d.CreditsPerHour.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"vanguardMarks\":"); sb.Append(d.VanguardMarks.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"dogTags\":"); sb.Append(d.DogTags.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"dogTagsVm\":"); sb.Append(d.DogTagsVm.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderLevel\":"); sb.Append(d.CommanderLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderXp\":"); sb.Append(d.CommanderXp.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"commanderXpPerHour\":"); sb.Append(d.CommanderXpPerHour.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"empireLevel\":"); sb.Append(d.EmpireLevel.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"empireXp\":"); sb.Append(d.EmpireXp.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"empireXpPerHour\":"); sb.Append(d.EmpireXpPerHour.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"ranks\":"); AppendStringIntDict(sb, d.Ranks);

            sb.Append(",\"dogTagsBreakdown\":[");
            for (int i = 0; i < d.DogTagsBreakdown.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var dt = d.DogTagsBreakdown[i];
                sb.Append("{\"id\":"); AppendString(sb, dt.Id);
                sb.Append(",\"label\":"); AppendString(sb, dt.Label);
                sb.Append(",\"vmEach\":"); sb.Append(dt.VmEach.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"baseline\":"); sb.Append(dt.Baseline.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"current\":"); sb.Append(dt.Current.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"delta\":"); sb.Append(dt.Delta.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"vmCurrent\":"); sb.Append(dt.VmCurrent.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"vmDelta\":"); sb.Append(dt.VmDelta.ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"reputation\":[");
            for (int i = 0; i < d.Reputation.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var r = d.Reputation[i];
                sb.Append("{\"faction\":"); AppendString(sb, r.Faction);
                sb.Append(",\"current\":"); sb.Append(r.Current.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"delta\":"); sb.Append(r.Delta.ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"newModules\":[");
            for (int i = 0; i < d.NewModules.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var m = d.NewModules[i];
                sb.Append("{\"name\":"); AppendString(sb, m.Name);
                sb.Append(",\"rarity\":"); AppendString(sb, m.Rarity);
                sb.Append(",\"itemLevel\":"); sb.Append(m.ItemLevel.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"size\":"); AppendString(sb, m.Size);
                sb.Append(",\"equipmentType\":"); AppendString(sb, m.EquipmentType);
                sb.Append(",\"gained\":"); sb.Append(m.Gained.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"substats\":"); AppendStringList(sb, m.Substats);
                sb.Append(",\"aspects\":[");
                if (m.Aspects != null)
                {
                    for (int j = 0; j < m.Aspects.Count; j++)
                    {
                        if (j > 0) sb.Append(',');
                        var a = m.Aspects[j];
                        sb.Append("{\"name\":"); AppendString(sb, a.Name);
                        sb.Append(",\"description\":"); AppendString(sb, a.Description);
                        sb.Append('}');
                    }
                }
                sb.Append("]}");
            }
            sb.Append(']');

            sb.Append(",\"newShipBlueprints\":[");
            for (int i = 0; i < d.NewShipBlueprints.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var s = d.NewShipBlueprints[i];
                sb.Append("{\"shipId\":"); AppendString(sb, s.ShipId);
                sb.Append(",\"displayName\":"); AppendString(sb, s.DisplayName);
                sb.Append(",\"size\":"); AppendString(sb, s.Size);
                sb.Append(",\"gained\":"); sb.Append(s.Gained.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"total\":"); sb.Append(s.Total.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"required\":"); sb.Append(s.Required.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"canComplete\":"); AppendBool(sb, s.CanComplete);
                sb.Append(",\"alreadyUnlocked\":"); AppendBool(sb, s.AlreadyUnlocked);
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"newlyUnlockedShips\":"); AppendStringList(sb, d.NewlyUnlockedShips);

            sb.Append(",\"newModuleBlueprintItems\":[");
            for (int i = 0; i < d.NewModuleBlueprintItems.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var b = d.NewModuleBlueprintItems[i];
                sb.Append("{\"displayName\":"); AppendString(sb, b.DisplayName);
                sb.Append(",\"blueprintName\":"); AppendString(sb, b.BlueprintName);
                sb.Append(",\"level\":"); sb.Append(b.Level.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"rarity\":"); AppendString(sb, b.Rarity);
                sb.Append(",\"gained\":"); sb.Append(b.Gained.ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"newUploadedBlueprints\":[");
            for (int i = 0; i < d.NewUploadedBlueprints.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var u = d.NewUploadedBlueprints[i];
                sb.Append("{\"recipe\":"); AppendString(sb, u.Recipe);
                sb.Append(",\"oldLevel\":");
                if (u.OldLevel.HasValue) sb.Append(u.OldLevel.Value.ToString(CultureInfo.InvariantCulture));
                else sb.Append("null");
                sb.Append(",\"newLevel\":"); sb.Append(u.NewLevel.ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"newCapturedShips\":[");
            for (int i = 0; i < d.NewCapturedShips.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var c = d.NewCapturedShips[i];
                sb.Append("{\"guid\":"); AppendString(sb, c.Guid);
                sb.Append(",\"shipClass\":"); AppendString(sb, c.ShipClass);
                sb.Append(",\"displayName\":"); AppendString(sb, c.DisplayName);
                sb.Append(",\"size\":"); AppendString(sb, c.Size);
                sb.Append(",\"rarity\":"); AppendString(sb, c.Rarity);
                sb.Append(",\"customName\":"); AppendString(sb, c.CustomName);
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append(",\"lockedCratesTotal\":"); sb.Append(d.LockedCratesTotal.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"newLockedCrates\":[");
            for (int i = 0; i < d.NewLockedCrates.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var lc = d.NewLockedCrates[i];
                sb.Append("{\"name\":"); AppendString(sb, lc.Name);
                sb.Append(",\"rarity\":"); AppendString(sb, lc.Rarity);
                sb.Append(",\"itemLevel\":"); sb.Append(lc.ItemLevel.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"gained\":"); sb.Append(lc.Gained.ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append('}');
        }

        public static string SerializeResetResponse(bool ok, string saveId, string sessionStartedAt)
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"ok\":"); AppendBool(sb, ok);
            sb.Append(",\"saveId\":"); AppendString(sb, saveId ?? string.Empty);
            sb.Append(",\"sessionStartedAt\":"); AppendString(sb, sessionStartedAt ?? string.Empty);
            sb.Append('}');
            return sb.ToString();
        }

        public static string SerializeRecoveryResponse(RecoveryResult r)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"ok\":"); AppendBool(sb, r != null && r.Ok);
            sb.Append(",\"message\":"); AppendString(sb, r?.Message ?? string.Empty);
            sb.Append(",\"targetSystem\":"); AppendString(sb, r?.TargetSystem ?? string.Empty);
            sb.Append(",\"targetStation\":"); AppendString(sb, r?.TargetStation ?? string.Empty);
            sb.Append(",\"abandonedMission\":"); AppendString(sb, r?.AbandonedMission ?? string.Empty);
            sb.Append(",\"echoEnabled\":"); AppendBool(sb, r != null && r.EchoEnabled);
            sb.Append(",\"itemName\":"); AppendString(sb, r?.ItemName ?? string.Empty);
            sb.Append(",\"movedCount\":"); sb.Append((r?.MovedCount ?? 0).ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"newFillPct\":"); AppendDouble(sb, r?.NewFillPct ?? 0.0);
            sb.Append('}');
            return sb.ToString();
        }

        public static string SerializeMissionLogEntry(MissionLogEntry e)
        {
            var sb = new StringBuilder(256);
            AppendMissionLogEntry(sb, e);
            return sb.ToString();
        }

        public static string SerializeMissionLogList(List<MissionLogEntry> list)
        {
            var sb = new StringBuilder(Math.Max(128, (list?.Count ?? 0) * 160));
            AppendMissionLogList(sb, list ?? new List<MissionLogEntry>());
            return sb.ToString();
        }

        private static void AppendMissionLogList(StringBuilder sb, List<MissionLogEntry> list)
        {
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendMissionLogEntry(sb, list[i]);
            }
            sb.Append(']');
        }

        private static void AppendMissionLogEntry(StringBuilder sb, MissionLogEntry e)
        {
            sb.Append("{\"timestamp\":"); AppendString(sb, e.Timestamp);
            sb.Append(",\"event\":"); AppendString(sb, e.Event);
            sb.Append(",\"missionName\":"); AppendString(sb, e.MissionName);
            sb.Append(",\"gameplayType\":"); AppendString(sb, e.GameplayType);
            sb.Append(",\"isStory\":"); AppendBool(sb, e.IsStory);
            sb.Append(",\"reason\":"); AppendString(sb, e.Reason);
            sb.Append('}');
        }

        public static MissionLogEntry DeserializeMissionLogEntry(string json)
        {
            var root = Parse(json) as Dictionary<string, object>;
            if (root == null) return null;
            return new MissionLogEntry
            {
                Timestamp = GetString(root, "timestamp", string.Empty),
                Event = GetString(root, "event", string.Empty),
                MissionName = GetString(root, "missionName", string.Empty),
                GameplayType = GetString(root, "gameplayType", string.Empty),
                IsStory = root.TryGetValue("isStory", out var v) && v is bool b && b,
                Reason = GetString(root, "reason", string.Empty)
            };
        }

        #region Minimal JSON Parser

        public static object Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            int idx = 0;
            return ParseValue(json, ref idx);
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { i += 4; return true; }
            if (c == 'f') { i += 5; return false; }
            if (c == 'n') { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var dict = new Dictionary<string, object>();
            i++; // '{'
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return dict; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') break;
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                object val = ParseValue(s, ref i);
                dict[key] = val;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return dict;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // '['
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (i < s.Length)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // '"'
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                string hex = s.Substring(i, 4);
                                if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                    sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            bool isFloat = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '.' || c == 'e' || c == 'E') isFloat = true;
                if (char.IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                    i++;
                else
                    break;
            }
            string token = s.Substring(start, i - start);
            if (!isFloat && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                return l;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return d;
            return 0L;
        }

        private static string GetString(Dictionary<string, object> d, string k, string def = "")
            => d.TryGetValue(k, out var v) && v is string s ? s : def;

        private static int GetInt(Dictionary<string, object> d, string k)
            => d.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;

        private static long GetLong(Dictionary<string, object> d, string k)
            => d.TryGetValue(k, out var v) && v != null ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : 0L;

        private static double GetDouble(Dictionary<string, object> d, string k)
            => d.TryGetValue(k, out var v) && v != null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0.0;

        private static Dictionary<string, int> GetStringIntDict(Dictionary<string, object> d, string k)
        {
            var res = new Dictionary<string, int>();
            if (d.TryGetValue(k, out var v) && v is Dictionary<string, object> sub)
            {
                foreach (var kv in sub)
                {
                    if (kv.Value != null)
                        res[kv.Key] = Convert.ToInt32(kv.Value, CultureInfo.InvariantCulture);
                }
            }
            return res;
        }

        private static Dictionary<string, string> GetStringStringDict(Dictionary<string, object> d, string k)
        {
            var res = new Dictionary<string, string>();
            if (d.TryGetValue(k, out var v) && v is Dictionary<string, object> sub)
            {
                foreach (var kv in sub)
                {
                    if (kv.Value is string s) res[kv.Key] = s;
                }
            }
            return res;
        }

        private static List<string> GetStringList(Dictionary<string, object> d, string k)
        {
            var res = new List<string>();
            if (d.TryGetValue(k, out var v) && v is List<object> list)
            {
                foreach (var item in list)
                {
                    if (item is string s) res.Add(s);
                }
            }
            return res;
        }

        #endregion
    }
}
