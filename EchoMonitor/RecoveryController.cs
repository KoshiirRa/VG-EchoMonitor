using System;
using System.Collections.Generic;
using Behaviour.Gameplay;
using Behaviour.Managers;
using Behaviour.Util;
using Source.Galaxy;
using Source.Galaxy.POI;
using Source.Player;

namespace EchoMonitor
{
    public sealed class RecoveryResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
        public string TargetSystem { get; set; } = string.Empty;
        public string TargetStation { get; set; } = string.Empty;
        public string AbandonedMission { get; set; } = string.Empty;
        public bool EchoEnabled { get; set; }
    }

    public static class RecoveryController
    {
        /// <summary>
        /// Must ONLY be called on the Unity main thread.
        /// </summary>
        public static RecoveryResult WarpToSafeStation(StuckDetector detector)
        {
            var gp = GamePlayer.current;
            if (gp == null || gp.currentSystem == null)
            {
                return new RecoveryResult { Ok = false, Message = "Not currently in game." };
            }

            if (!Singleton<TravelManager>.HasInstance || Singleton<TravelManager>.Instance == null)
            {
                return new RecoveryResult { Ok = false, Message = "TravelManager is not available." };
            }

            var ship = GameplayManager.initialized && GameplayManager.Instance != null
                ? GameplayManager.Instance.spaceShip
                : null;

            SpaceStation safeStation = FindSafeStationInNeighborSystem(gp.currentSystem, ship, maxHops: 2);
            if (safeStation == null)
            {
                return new RecoveryResult
                {
                    Ok = false,
                    Message = "No safe, dockable station found within 2 jumpgate hops."
                };
            }

            bool started = Singleton<TravelManager>.Instance.TryInitiateTravel(safeStation);
            if (!started)
            {
                return new RecoveryResult
                {
                    Ok = false,
                    Message = $"TravelManager refused travel to {safeStation.name} ({safeStation.system?.name}).",
                    TargetSystem = safeStation.system?.name ?? string.Empty,
                    TargetStation = safeStation.name ?? string.Empty
                };
            }

            detector?.ClearAll();

            return new RecoveryResult
            {
                Ok = true,
                Message = $"Initiated warp to {safeStation.name} in {safeStation.system?.name}.",
                TargetSystem = safeStation.system?.name ?? string.Empty,
                TargetStation = safeStation.name ?? string.Empty,
                EchoEnabled = gp.autoPlay
            };
        }

        /// <summary>
        /// Must ONLY be called on the Unity main thread.
        /// </summary>
        public static RecoveryResult ResetEcho(StuckDetector detector)
        {
            var gp = GamePlayer.current;
            if (gp == null)
            {
                return new RecoveryResult { Ok = false, Message = "Not currently in game." };
            }

            gp.autoPlay = true;
            try
            {
                if (Singleton<DungeonManager>.HasInstance)
                {
                    Singleton<DungeonManager>.Instance?.SyncEchoAutonomous(true);
                }
            }
            catch { }

            try
            {
                IdleManager.DelayIdleActivities(0f);
                IdleManager.ForceImmediateActivityCheck();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"ResetEcho IdleManager nudge failed: {ex.Message}");
            }

            detector?.ClearAll();

            return new RecoveryResult
            {
                Ok = true,
                Message = "ECHO autopilot reset and immediate activity check triggered.",
                EchoEnabled = gp.autoPlay
            };
        }

        /// <summary>
        /// Must ONLY be called on the Unity main thread.
        /// </summary>
        public static RecoveryResult ToggleEcho(StuckDetector detector)
        {
            var gp = GamePlayer.current;
            if (gp == null)
            {
                return new RecoveryResult { Ok = false, Message = "Not currently in game." };
            }

            bool next = !gp.autoPlay;
            gp.autoPlay = next;

            try
            {
                if (Singleton<DungeonManager>.HasInstance)
                {
                    Singleton<DungeonManager>.Instance?.SyncEchoAutonomous(next);
                }
            }
            catch { }

            if (next)
            {
                try
                {
                    IdleManager.DelayIdleActivities(0f);
                    IdleManager.ForceImmediateActivityCheck();
                }
                catch { }
                detector?.ClearAll();
            }

            return new RecoveryResult
            {
                Ok = true,
                Message = next ? "ECHO autopilot enabled." : "ECHO autopilot disabled.",
                EchoEnabled = next
            };
        }

        /// <summary>
        /// Must ONLY be called on the Unity main thread.
        /// Never drops story missions (requires string.IsNullOrWhiteSpace(m.storyId) && m.canAbandon).
        /// </summary>
        public static RecoveryResult AbandonActiveMission(StuckDetector detector)
        {
            var gp = GamePlayer.current;
            if (gp == null)
            {
                return new RecoveryResult { Ok = false, Message = "Not currently in game." };
            }

            Source.MissionSystem.Mission target = null;

            bool IsAbandonable(Source.MissionSystem.Mission m)
                => m != null && string.IsNullOrWhiteSpace(m.storyId) && m.canAbandon;

            if (IsAbandonable(gp.infiniteMission))
            {
                target = gp.infiniteMission;
            }
            else if (gp.missions != null)
            {
                foreach (var m in gp.missions)
                {
                    if (IsAbandonable(m) && (m.trackedOnHud || m.idle))
                    {
                        target = m;
                        break;
                    }
                }
                if (target == null)
                {
                    foreach (var m in gp.missions)
                    {
                        if (IsAbandonable(m))
                        {
                            target = m;
                            break;
                        }
                    }
                }
            }

            if (target == null)
            {
                return new RecoveryResult
                {
                    Ok = false,
                    Message = "No abandonable dynamic mission found (story missions are protected).",
                    EchoEnabled = gp.autoPlay
                };
            }

            string rawName = target.name ?? "Unknown Mission";
            string missionName = rawName.StartsWith("@", StringComparison.Ordinal)
                ? (Source.Util.Translation.Translate(rawName) ?? rawName)
                : rawName;

            bool wasTracked = target.trackedOnHud;
            gp.RemoveMission(target, false);

            try
            {
                if (wasTracked && Singleton<Behaviour.UI.Missions.FocusedMissionHandler>.HasInstance)
                {
                    Singleton<Behaviour.UI.Missions.FocusedMissionHandler>.Instance?.ResetFocusedMission();
                }
            }
            catch { }

            try
            {
                IdleManager.DelayIdleActivities(0f);
                IdleManager.ForceImmediateActivityCheck();
            }
            catch { }

            detector?.ClearAll();

            return new RecoveryResult
            {
                Ok = true,
                Message = $"Abandoned mission '{missionName}' and nudged ECHO.",
                AbandonedMission = missionName,
                EchoEnabled = gp.autoPlay
            };
        }

        /// <summary>
        /// Must ONLY be called on the Unity main thread.
        /// Warps directly to the first active player-owned station under raid or siege.
        /// </summary>
        public static RecoveryResult WarpToDefend(StuckDetector detector)
        {
            var gp = GamePlayer.current;
            if (gp == null)
            {
                return new RecoveryResult { Ok = false, Message = "Not currently in game." };
            }

            if (!Singleton<TravelManager>.HasInstance || Singleton<TravelManager>.Instance == null)
            {
                return new RecoveryResult { Ok = false, Message = "TravelManager is not available." };
            }

            var battleManager = gp.GetStoryteller<Source.Simulation.Story.LivingUniverse>()?.battleManager;
            if (battleManager?.activeBattles == null)
            {
                return new RecoveryResult { Ok = false, Message = "No active Empire raids or sieges found." };
            }

            MapPointOfInterest targetVenue = null;
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

                if (venue != null)
                {
                    targetVenue = venue;
                    break;
                }

                var sys = GalaxyMapData.current?.GetSystem(battle.targetSystemGuid);
                if (sys?.pointsOfInterest != null)
                {
                    foreach (var poi in sys.pointsOfInterest)
                    {
                        if (poi is SpaceStation ss && (ss is PlayerStation || ss.faction == Faction.player))
                        {
                            targetVenue = ss;
                            break;
                        }
                    }
                    if (targetVenue != null) break;
                }
            }

            if (targetVenue == null)
            {
                return new RecoveryResult
                {
                    Ok = false,
                    Message = "No active Empire raid or siege target found to warp to.",
                    EchoEnabled = gp.autoPlay
                };
            }

            string stName = targetVenue.name ?? "Empire Station";
            string sysName = targetVenue.system?.name ?? string.Empty;

            bool started = Singleton<TravelManager>.Instance.TryInitiateTravel(targetVenue);
            if (!started)
            {
                return new RecoveryResult
                {
                    Ok = false,
                    Message = $"TravelManager refused travel to {stName} ({sysName}).",
                    TargetSystem = sysName,
                    TargetStation = stName,
                    EchoEnabled = gp.autoPlay
                };
            }

            return new RecoveryResult
            {
                Ok = true,
                Message = $"Warping to defend {stName} in {sysName}.",
                TargetSystem = sysName,
                TargetStation = stName,
                EchoEnabled = gp.autoPlay
            };
        }

        private static SpaceStation FindSafeStationInNeighborSystem(
            SystemMapData startSystem,
            Behaviour.Unit.SpaceShip ship,
            int maxHops)
        {
            var visited = new HashSet<SystemMapData> { startSystem };
            var queue = new Queue<(SystemMapData Sys, int Depth)>();
            queue.Enqueue((startSystem, 0));

            while (queue.Count > 0)
            {
                var (cur, depth) = queue.Dequeue();
                if (depth > 0 && cur.pointsOfInterest != null)
                {
                    foreach (var poi in cur.pointsOfInterest)
                    {
                        if (poi is SpaceStation ss && IsStationSafeAndDockable(ss, ship))
                        {
                            return ss;
                        }
                    }
                }

                if (depth >= maxHops || cur.pointsOfInterest == null) continue;

                foreach (var poi in cur.pointsOfInterest)
                {
                    if (poi is JumpGate gate && gate.canUseJumpGate && gate.targetSystem != null && visited.Add(gate.targetSystem))
                    {
                        queue.Enqueue((gate.targetSystem, depth + 1));
                    }
                }
            }

            return null;
        }

        private static bool IsStationSafeAndDockable(SpaceStation ss, Behaviour.Unit.SpaceShip ship)
        {
            if (ss == null) return false;
            try
            {
                if (!TravelManager.IsSafeStation(ss)) return false;
                if (ship != null && !ss.DockingAvailableFor(ship)) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
