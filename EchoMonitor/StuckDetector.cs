using System;
using System.Collections.Generic;
using Behaviour.Managers;
using Behaviour.Util;
using EchoMonitor.Patches;
using Source.Galaxy;
using Source.Galaxy.POI;
using Source.Player;

namespace EchoMonitor
{
    public sealed class StuckDetector
    {
        private static readonly TimeSpan EmergencyWarpWindow = TimeSpan.FromMinutes(15);
        private const int EmergencyWarpThreshold = 2;

        private readonly object _lock = new object();
        private readonly List<(DateTime TimeUtc, string Poi, string Mission)> _emergencyWarps = new List<(DateTime, string, string)>();
        private readonly HashSet<string> _dismissedKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _battleFirstSeenUtc = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private string _badStationKey = string.Empty;
        private DateTime _badStationFirstSeenUtc = DateTime.MinValue;
        private string _lastEmergencyLoopKey = string.Empty;

        public void RecordEmergencyWarp(string poiName, string missionName)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                PruneOldEmergencyWarpsLocked(now);
                _emergencyWarps.Add((now, poiName ?? string.Empty, missionName ?? string.Empty));
                // A new emergency warp un-dismisses the emergency warp loop alert
                _dismissedKeys.Remove("emergency_warp_loop");
            }
        }

        public void DismissAll()
        {
            lock (_lock)
            {
                _dismissedKeys.Add("destroyed_hostile_station");
                _dismissedKeys.Add("emergency_warp_loop");
                foreach (var bid in _battleFirstSeenUtc.Keys)
                {
                    _dismissedKeys.Add("empire_raid_" + bid);
                }
            }
        }

        public void ClearAll()
        {
            lock (_lock)
            {
                _emergencyWarps.Clear();
                _dismissedKeys.Clear();
                _battleFirstSeenUtc.Clear();
                _badStationKey = string.Empty;
                _badStationFirstSeenUtc = DateTime.MinValue;
                _lastEmergencyLoopKey = string.Empty;
            }
        }

        /// <summary>
        /// Must ONLY be called on the Unity main thread.
        /// </summary>
        public AlertsStatus Evaluate(GamePlayer gp, Behaviour.Unit.SpaceShip ship)
        {
            var status = new AlertsStatus();
            if (gp == null) return status;

            var now = DateTime.UtcNow;
            var activeItems = new List<AlertItem>();

            // 1. Check for Destroyed / Hostile Station dock loop while ECHO is enabled
            if (gp.autoPlay)
            {
                SpaceStation badStation = FindProblematicStation(gp, ship, out string reason);
                if (badStation != null)
                {
                    string stName = badStation.name ?? "Unknown Station";
                    string sysName = badStation.system?.name ?? gp.currentSystem?.name ?? "Unknown System";
                    string key = $"{sysName}:{stName}:{reason}";

                    bool isDockActivity =
                        string.Equals(EchoPatches.LastIdleRawKey, "@IdleDockWithSS", StringComparison.Ordinal) ||
                        string.Equals(EchoPatches.LastIdleRawKey, "@IdleGoToSpaceStation", StringComparison.Ordinal) ||
                        string.Equals(EchoPatches.LastIdleRawKey, "@IdleDockBlockedHostiles", StringComparison.Ordinal);

                    lock (_lock)
                    {
                        if (_badStationKey != key)
                        {
                            _badStationKey = key;
                            _badStationFirstSeenUtc = now;
                            _dismissedKeys.Remove("destroyed_hostile_station");
                        }

                        bool trigger = isDockActivity || (now - _badStationFirstSeenUtc).TotalSeconds >= 8.0;
                        if (trigger && !_dismissedKeys.Contains("destroyed_hostile_station"))
                        {
                            activeItems.Add(new AlertItem
                            {
                                Id = "destroyed_hostile_station",
                                Type = "DestroyedOrHostileStation",
                                Severity = "critical",
                                Title = "ECHO Stuck on Destroyed / Hostile Station",
                                Detail = $"Station '{stName}' in {sysName} is {reason} and cannot be docked.",
                                TriggeredAt = _badStationFirstSeenUtc.ToString("o")
                            });
                        }
                    }
                }
                else
                {
                    lock (_lock)
                    {
                        _badStationKey = string.Empty;
                        _badStationFirstSeenUtc = DateTime.MinValue;
                    }
                }
            }

            // 2. Check for Emergency Warp Loop (>= 2 warps on the same mission or same POI in 15m)
            lock (_lock)
            {
                PruneOldEmergencyWarpsLocked(now);
                status.EmergencyWarps15m = _emergencyWarps.Count;

                if (_emergencyWarps.Count >= EmergencyWarpThreshold)
                {
                    var groupCounts = new Dictionary<string, (int Count, string Label, DateTime LatestUtc)>(StringComparer.OrdinalIgnoreCase);
                    foreach (var w in _emergencyWarps)
                    {
                        string groupKey;
                        string label;
                        if (!string.IsNullOrWhiteSpace(w.Mission))
                        {
                            groupKey = "mission:" + w.Mission.Trim();
                            label = $"mission '{w.Mission.Trim()}'";
                        }
                        else if (!string.IsNullOrWhiteSpace(w.Poi))
                        {
                            groupKey = "poi:" + w.Poi.Trim();
                            label = $"POI '{w.Poi.Trim()}'";
                        }
                        else
                        {
                            continue;
                        }

                        if (groupCounts.TryGetValue(groupKey, out var cur))
                        {
                            groupCounts[groupKey] = (cur.Count + 1, label, w.TimeUtc > cur.LatestUtc ? w.TimeUtc : cur.LatestUtc);
                        }
                        else
                        {
                            groupCounts[groupKey] = (1, label, w.TimeUtc);
                        }
                    }

                    string bestKey = null;
                    int bestCount = 0;
                    string bestLabel = string.Empty;
                    DateTime bestTimeUtc = DateTime.MinValue;
                    foreach (var kv in groupCounts)
                    {
                        if (kv.Value.Count >= EmergencyWarpThreshold &&
                            (kv.Value.Count > bestCount || (kv.Value.Count == bestCount && kv.Value.LatestUtc > bestTimeUtc)))
                        {
                            bestKey = kv.Key;
                            bestCount = kv.Value.Count;
                            bestLabel = kv.Value.Label;
                            bestTimeUtc = kv.Value.LatestUtc;
                        }
                    }

                    if (bestKey != null)
                    {
                        string loopKey = $"{bestCount}:{bestKey}";
                        if (_lastEmergencyLoopKey != loopKey)
                        {
                            _lastEmergencyLoopKey = loopKey;
                        }

                        if (!_dismissedKeys.Contains("emergency_warp_loop"))
                        {
                            activeItems.Add(new AlertItem
                            {
                                Id = "emergency_warp_loop",
                                Type = "EmergencyWarpLoop",
                                Severity = "critical",
                                Title = "ECHO Emergency Warp Loop Detected",
                                Detail = $"{bestCount} emergency warps in the last 15 min on {bestLabel}.",
                                TriggeredAt = bestTimeUtc.ToString("o")
                            });
                        }
                    }
                }
            }

            // 3. Check for active Empire Raid / Siege on player-owned stations
            EvaluateEmpireRaids(gp, now, activeItems);

            status.Items = activeItems;
            status.Stuck = activeItems.Count > 0;
            return status;
        }

        private void EvaluateEmpireRaids(GamePlayer gp, DateTime now, List<AlertItem> activeItems)
        {
            try
            {
                var battleManager = gp.GetStoryteller<Source.Simulation.Story.LivingUniverse>()?.battleManager;
                if (battleManager?.activeBattles == null) return;

                var activeIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var battle in battleManager.activeBattles)
                {
                    if (battle == null || battle.stage == 0) continue;

                    var venue = BattleManager.GetVenue(battle);
                    bool isPlayerTarget =
                        (battle.targetOwnerFactionId == Faction.player?.identifier) ||
                        (battle.defenderFactionId == Faction.player?.identifier) ||
                        (venue != null && venue.faction == Faction.player) ||
                        (venue is PlayerStation);

                    if (!isPlayerTarget) continue;

                    string bid = !string.IsNullOrEmpty(battle.battleId) ? battle.battleId : (venue?.name ?? "unknown");
                    activeIds.Add(bid);
                    string alertId = "empire_raid_" + bid;

                    DateTime firstSeen;
                    bool dismissed;
                    lock (_lock)
                    {
                        if (!_battleFirstSeenUtc.TryGetValue(bid, out firstSeen))
                        {
                            firstSeen = now;
                            _battleFirstSeenUtc[bid] = firstSeen;
                            _dismissedKeys.Remove(alertId);
                        }
                        dismissed = _dismissedKeys.Contains(alertId);
                    }

                    if (dismissed) continue;

                    string stage = battle.stage.ToString();
                    string rawAttacker = Faction.Get(battle.attackerFactionId)?.name ?? battle.attackerFactionId ?? "Hostiles";
                    string attacker = TranslateSafe(rawAttacker);
                    string stationName = TranslateSafe(venue?.name ?? "Empire Station");
                    string systemName = TranslateSafe(
                        venue?.system?.name
                        ?? GalaxyMapData.current?.GetSystem(battle.targetSystemGuid)?.name
                        ?? "Unknown System");

                    string detail = $"{attacker} attacking {stationName} in {systemName}";
                    if (battle.stationHullMax > 0f)
                    {
                        int hullPct = (int)Math.Round(Math.Clamp(battle.stationHull / battle.stationHullMax * 100f, 0f, 100f));
                        detail += $" (Station Hull: {hullPct}%)";
                    }
                    if (battle.timeRemaining > 0f)
                    {
                        int mins = (int)(battle.timeRemaining / 60f);
                        int secs = (int)(battle.timeRemaining % 60f);
                        detail += mins > 0 ? $" — {mins}m {secs}s remaining" : $" — {secs}s remaining";
                    }

                    activeItems.Add(new AlertItem
                    {
                        Id = alertId,
                        Type = "EmpireRaidOrSiege",
                        Severity = "critical",
                        Title = $"Empire {stage}: {stationName}",
                        Detail = detail,
                        TriggeredAt = firstSeen.ToString("o")
                    });
                }

                // Prune ended battles
                lock (_lock)
                {
                    var ended = new List<string>();
                    foreach (var k in _battleFirstSeenUtc.Keys)
                    {
                        if (!activeIds.Contains(k)) ended.Add(k);
                    }
                    foreach (var k in ended)
                    {
                        _battleFirstSeenUtc.Remove(k);
                        _dismissedKeys.Remove("empire_raid_" + k);
                    }
                }
            }
            catch { }
        }

        private static string TranslateSafe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (!value.StartsWith("@", StringComparison.Ordinal)) return value;
            try
            {
                return Source.Util.Translation.Translate(value) ?? value;
            }
            catch
            {
                return value;
            }
        }

        private static SpaceStation FindProblematicStation(GamePlayer gp, Behaviour.Unit.SpaceShip ship, out string reason)
        {
            reason = string.Empty;
            SpaceStation candidate = null;

            if (Singleton<TravelManager>.HasInstance && Singleton<TravelManager>.Instance?.targetPoi is SpaceStation targetSs)
            {
                candidate = targetSs;
            }
            else if (gp.currentPointOfInterest is SpaceStation curSs)
            {
                candidate = curSs;
            }

            if (candidate == null) return null;

            try
            {
                if (!candidate.PlayerIsFriendly())
                {
                    reason = "hostile";
                    return candidate;
                }
                if (ship != null && !candidate.DockingAvailableFor(ship))
                {
                    reason = "destroyed / docking unavailable";
                    return candidate;
                }
            }
            catch { }

            return null;
        }

        private void PruneOldEmergencyWarpsLocked(DateTime now)
        {
            DateTime cutoff = now - EmergencyWarpWindow;
            _emergencyWarps.RemoveAll(e => e.TimeUtc < cutoff);
        }
    }
}
