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
        private const double OutpostFullTriggerPct = 95.0;
        private const double OutpostFullClearPct = 90.0;

        private readonly object _lock = new object();
        private readonly List<(DateTime TimeUtc, string Poi, string Mission)> _emergencyWarps = new List<(DateTime, string, string)>();
        private readonly HashSet<string> _dismissedKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _battleFirstSeenUtc = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _incomingFleetFirstSeenUtc = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _outpostFullFirstSeenUtc = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private string _badStationKey = string.Empty;
        private DateTime _badStationFirstSeenUtc = DateTime.MinValue;
        private string _lastEmergencyLoopKey = string.Empty;
        private DateTime _emergencyLoopFirstSeenUtc = DateTime.MinValue;

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
                _emergencyLoopFirstSeenUtc = DateTime.MinValue;
                foreach (var bid in _battleFirstSeenUtc.Keys)
                {
                    _dismissedKeys.Add("empire_raid_" + bid);
                }
                foreach (var fid in _incomingFleetFirstSeenUtc.Keys)
                {
                    _dismissedKeys.Add("empire_incoming_" + fid);
                }
                foreach (var guid in _outpostFullFirstSeenUtc.Keys)
                {
                    _dismissedKeys.Add("outpost_storage_full:" + guid);
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
                _incomingFleetFirstSeenUtc.Clear();
                _outpostFullFirstSeenUtc.Clear();
                _badStationKey = string.Empty;
                _badStationFirstSeenUtc = DateTime.MinValue;
                _lastEmergencyLoopKey = string.Empty;
                _emergencyLoopFirstSeenUtc = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Must ONLY be called on the Unity main thread.
        /// </summary>
        public AlertsStatus Evaluate(GamePlayer gp, Behaviour.Unit.SpaceShip ship, List<OutpostStorageInfo> outposts = null)
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
                            if (_emergencyLoopFirstSeenUtc == DateTime.MinValue)
                            {
                                _emergencyLoopFirstSeenUtc = bestTimeUtc;
                            }

                            activeItems.Add(new AlertItem
                            {
                                Id = "emergency_warp_loop",
                                Type = "EmergencyWarpLoop",
                                Severity = "critical",
                                Title = "ECHO Emergency Warp Loop Detected",
                                Detail = $"{bestCount} emergency warps in the last 15 min on {bestLabel}.",
                                TriggeredAt = _emergencyLoopFirstSeenUtc.ToString("o")
                            });
                        }
                    }
                    else
                    {
                        _emergencyLoopFirstSeenUtc = DateTime.MinValue;
                        _lastEmergencyLoopKey = string.Empty;
                    }
                }
                else
                {
                    _emergencyLoopFirstSeenUtc = DateTime.MinValue;
                    _lastEmergencyLoopKey = string.Empty;
                }
            }

            // 3. Check for active Empire Raid / Siege on player-owned stations
            EvaluateEmpireRaids(gp, now, activeItems);

            // 4. Check for player outposts at >= 95% storage capacity
            EvaluateOutpostStorage(outposts, now, activeItems);

            status.Items = activeItems;
            status.Stuck = activeItems.Count > 0;
            return status;
        }

        private void EvaluateOutpostStorage(List<OutpostStorageInfo> outposts, DateTime now, List<AlertItem> activeItems)
        {
            if (outposts == null) return;
            try
            {
                var seenGuids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var op in outposts)
                {
                    if (op == null || string.IsNullOrEmpty(op.StationGuid)) continue;
                    seenGuids.Add(op.StationGuid);

                    string alertId = "outpost_storage_full:" + op.StationGuid;
                    bool shouldEmit = false;
                    DateTime firstSeen = now;

                    lock (_lock)
                    {
                        bool alreadyTracked = _outpostFullFirstSeenUtc.TryGetValue(op.StationGuid, out firstSeen);
                        if (op.FillPct >= OutpostFullTriggerPct)
                        {
                            if (!alreadyTracked)
                            {
                                firstSeen = now;
                                _outpostFullFirstSeenUtc[op.StationGuid] = firstSeen;
                                _dismissedKeys.Remove(alertId);
                            }
                            shouldEmit = !_dismissedKeys.Contains(alertId);
                        }
                        else if (op.FillPct < OutpostFullClearPct)
                        {
                            if (alreadyTracked)
                            {
                                _outpostFullFirstSeenUtc.Remove(op.StationGuid);
                                _dismissedKeys.Remove(alertId);
                            }
                        }
                        else if (alreadyTracked)
                        {
                            // Hysteresis band (90%..95%): stay active until below 90%
                            shouldEmit = !_dismissedKeys.Contains(alertId);
                        }
                    }

                    if (!shouldEmit) continue;

                    string loc = !string.IsNullOrEmpty(op.SectorName) && !string.IsNullOrEmpty(op.SystemName)
                        ? $"{op.SectorName} / {op.SystemName}"
                        : (!string.IsNullOrEmpty(op.SystemName) ? op.SystemName : op.SectorName);
                    string stackInfo = op.LargestStackCount > 0
                        ? $" Largest stack: {op.LargestStackCount:N0}x {op.LargestStackItemName} ({op.LargestStackM3:N0} m³)."
                        : string.Empty;

                    activeItems.Add(new AlertItem
                    {
                        Id = alertId,
                        Type = "OutpostStorageFull",
                        Severity = "warning",
                        Title = $"Outpost Storage Full ({op.FillPct:F0}%): {op.Name}",
                        Detail = $"{loc} — {op.SpaceUsed:N0}/{op.Capacity:N0} m³.{stackInfo}",
                        TriggeredAt = firstSeen.ToString("o")
                    });
                }

                // Prune outposts that no longer exist
                lock (_lock)
                {
                    var removed = new List<string>();
                    foreach (var guid in _outpostFullFirstSeenUtc.Keys)
                    {
                        if (!seenGuids.Contains(guid)) removed.Add(guid);
                    }
                    foreach (var guid in removed)
                    {
                        _outpostFullFirstSeenUtc.Remove(guid);
                        _dismissedKeys.Remove("outpost_storage_full:" + guid);
                    }
                }
            }
            catch { }
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

                // Also check incoming hostile fleets traveling to attack or raid player stations/systems
                EvaluateIncomingEmpireAttacks(gp, now, activeItems);
            }
            catch { }
        }

        private void EvaluateIncomingEmpireAttacks(GamePlayer gp, DateTime now, List<AlertItem> activeItems)
        {
            try
            {
                var livingUniverse = gp.GetStoryteller<Source.Simulation.Story.LivingUniverse>();
                if (livingUniverse == null) return;

                var activeFleetIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var fleet in livingUniverse.AllFleets())
                {
                    if (fleet == null) continue;
                    if (fleet.state != ConquestFleetState.Traveling) continue;
                    if (fleet.orderReason != ConquestFleetOrderReason.Attacking &&
                        fleet.orderReason != ConquestFleetOrderReason.Raiding) continue;
                    if (fleet.faction == Faction.player) continue;

                    var targetSystem = GalaxyMapData.current?.GetSystem(fleet.destinationSystemGuid);
                    SpaceStation targetStation = ResolveIncomingTargetStation(fleet, targetSystem);

                    bool isPlayerTarget =
                        (fleet.raidTargetOwnerId == Faction.player?.identifier) ||
                        (targetSystem != null && targetSystem.faction == Faction.player) ||
                        (targetStation != null && (targetStation is PlayerStation || targetStation.faction == Faction.player));

                    if (!isPlayerTarget) continue;

                    string fid = !string.IsNullOrEmpty(fleet.guid) ? fleet.guid : (fleet.destinationSystemGuid ?? "unknown");
                    activeFleetIds.Add(fid);
                    string alertId = "empire_incoming_" + fid;

                    DateTime firstSeen;
                    bool dismissed;
                    lock (_lock)
                    {
                        if (!_incomingFleetFirstSeenUtc.TryGetValue(fid, out firstSeen))
                        {
                            firstSeen = now;
                            _incomingFleetFirstSeenUtc[fid] = firstSeen;
                            _dismissedKeys.Remove(alertId);
                        }
                        dismissed = _dismissedKeys.Contains(alertId);
                    }

                    if (dismissed) continue;

                    string rawAttacker = fleet.faction?.name ?? "Hostiles";
                    string attacker = TranslateSafe(rawAttacker);
                    string fleetName = TranslateSafe(fleet.displayName);
                    string stationName = TranslateSafe(targetStation?.name ?? targetSystem?.name ?? "Empire Station");
                    string systemName = TranslateSafe(targetSystem?.name ?? "Unknown System");
                    string actionVerb = fleet.orderReason == ConquestFleetOrderReason.Raiding ? "raiding" : "attacking";

                    string attackerLabel = !string.IsNullOrEmpty(fleetName) && !string.Equals(fleetName, attacker, StringComparison.OrdinalIgnoreCase)
                        ? $"{attacker} ({fleetName})"
                        : attacker;

                    string detail = $"{attackerLabel} incoming ({actionVerb}) at {stationName} in {systemName}";
                    float etaSeconds = Math.Max(0f, fleet.travelDuration * (1f - Math.Clamp(fleet.routeProgress, 0f, 1f)));
                    if (etaSeconds > 0f)
                    {
                        int mins = (int)(etaSeconds / 60f);
                        int secs = (int)(etaSeconds % 60f);
                        detail += mins > 0 ? $" — ETA {mins}m {secs}s" : $" — ETA {secs}s";
                    }

                    activeItems.Add(new AlertItem
                    {
                        Id = alertId,
                        Type = "EmpireRaidOrSiege",
                        Severity = "critical",
                        Title = $"Attack Incoming: {stationName}",
                        Detail = detail,
                        TriggeredAt = firstSeen.ToString("o")
                    });
                }

                // Prune arrived/cancelled incoming fleets
                lock (_lock)
                {
                    var ended = new List<string>();
                    foreach (var k in _incomingFleetFirstSeenUtc.Keys)
                    {
                        if (!activeFleetIds.Contains(k)) ended.Add(k);
                    }
                    foreach (var k in ended)
                    {
                        _incomingFleetFirstSeenUtc.Remove(k);
                        _dismissedKeys.Remove("empire_incoming_" + k);
                    }
                }
            }
            catch { }
        }

        internal static SpaceStation ResolveIncomingTargetStation(ConquestFleet fleet, SystemMapData targetSystem)
        {
            if (targetSystem?.pointsOfInterest == null) return null;

            string preferredGuid = !string.IsNullOrEmpty(fleet.raidTargetGuid)
                ? fleet.raidTargetGuid
                : fleet.assaultTargetGuid;

            if (!string.IsNullOrEmpty(preferredGuid))
            {
                foreach (var poi in targetSystem.pointsOfInterest)
                {
                    if (poi is SpaceStation ss && string.Equals(ss.guid, preferredGuid, StringComparison.Ordinal))
                    {
                        return ss;
                    }
                }
            }

            foreach (var poi in targetSystem.pointsOfInterest)
            {
                if (poi is SpaceStation ss && (ss is PlayerStation || ss.faction == Faction.player))
                {
                    return ss;
                }
            }

            return null;
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
