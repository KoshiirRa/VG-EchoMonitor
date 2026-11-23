using System;
using System.Collections.Generic;

namespace EchoMonitor
{
    public sealed class EchoStatus
    {
        public bool Enabled { get; set; }
        public string State { get; set; } = "Unknown";
        public string SystemName { get; set; } = string.Empty;
        public string PoiName { get; set; } = string.Empty;
        public string PoiType { get; set; } = string.Empty;
        public bool Docked { get; set; }
        public string ActiveMission { get; set; } = string.Empty;
        public double Hull { get; set; }
        public double MaxHull { get; set; }
        public double Armor { get; set; }
        public double MaxArmor { get; set; }
        public double Shield { get; set; }
        public double MaxShield { get; set; }
    }

    public sealed class OfficerInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Profession { get; set; } = string.Empty;
        public int Level { get; set; }
        public int LevelDelta { get; set; }
        public string Rarity { get; set; } = string.Empty;
    }

    public sealed class CrewStatus
    {
        public int TotalCrew { get; set; }
        public int MaxCrew { get; set; }
        public Dictionary<string, int> Roles { get; set; } = new Dictionary<string, int>();
        public List<OfficerInfo> Officers { get; set; } = new List<OfficerInfo>();
    }

    public sealed class DroneBayStatus
    {
        public bool Equipped { get; set; }
        public int MaxDrones { get; set; }
        public int BuiltDrones { get; set; }
        public int DeployedDrones { get; set; }
        public string BehaviorMode { get; set; } = string.Empty;
        public string Status { get; set; } = "Standby";
        public bool Rebuilding { get; set; }
        public double RebuildProgress { get; set; }
    }

    public sealed class HangarFighterStatus
    {
        public int Slot { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShipClass { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string Status { get; set; } = "Docked";
        public double Hull { get; set; }
        public double MaxHull { get; set; }
        public double Armor { get; set; }
        public double MaxArmor { get; set; }
        public double Shield { get; set; }
        public double MaxShield { get; set; }
        public double RepairFraction { get; set; }
    }

    public sealed class HangarBayStatus
    {
        public bool Equipped { get; set; }
        public int MaxSlots { get; set; }
        public int Deployed { get; set; }
        public int InBay { get; set; }
        public int Repairing { get; set; }
        public int Rebuilding { get; set; }
        public List<HangarFighterStatus> Fighters { get; set; } = new List<HangarFighterStatus>();
    }

    public sealed class CapturedShipInfo
    {
        public string ShipClass { get; set; } = "Unknown";
        public string ShipRarity { get; set; } = "None";
        public string CustomName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
    }

    public sealed class PlayerSnapshot
    {
        public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");
        public string SaveId { get; set; } = "default";
        public double ElapsedHours { get; set; }
        public long Credits { get; set; }
        public long VanguardMarks { get; set; }
        public Dictionary<string, int> DogTagsByType { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> Reputation { get; set; } = new Dictionary<string, int>();
        public int CommanderLevel { get; set; }
        public long CommanderXp { get; set; }
        public double CommanderProgressPct { get; set; }
        public int EmpireLevel { get; set; }
        public long EmpireXp { get; set; }
        public double EmpireProgressPct { get; set; }
        public Dictionary<string, int> Ranks { get; set; } = new Dictionary<string, int>
        {
            ["Patrol"] = 0,
            ["Bounty"] = 0,
            ["Industry"] = 0
        };
        // Key: "name\u001frarity\u001filvl\u001fequipmentType" -> count
        public Dictionary<string, int> Modules { get; set; } = new Dictionary<string, int>();
        // Live-only metadata for modules (not persisted in baseline.json)
        public Dictionary<string, List<string>> ModuleSubstats { get; set; } = new Dictionary<string, List<string>>();
        public Dictionary<string, List<ModuleAspectInfo>> ModuleAspects { get; set; } = new Dictionary<string, List<ModuleAspectInfo>>();
        // Key: shipId -> piece count
        public Dictionary<string, int> ShipBlueprintPieces { get; set; } = new Dictionary<string, int>();
        // Key: shipId -> displayName (resolved from game data if available)
        public Dictionary<string, string> ShipDisplayNames { get; set; } = new Dictionary<string, string>();
        // Key: shipId -> size string (e.g. "Size1".."Size6")
        public Dictionary<string, string> ShipSizes { get; set; } = new Dictionary<string, string>();
        public List<string> UnlockedShipBlueprints { get; set; } = new List<string>();
        // Key: "displayName\u001fblueprintName\u001flevel\u001frarity" -> count
        public Dictionary<string, int> ModuleBlueprintItems { get; set; } = new Dictionary<string, int>();
        // Key: recipe -> level
        public Dictionary<string, int> UploadedBlueprints { get; set; } = new Dictionary<string, int>();
        // Key: guid -> CapturedShipInfo
        public Dictionary<string, CapturedShipInfo> CapturedShips { get; set; } = new Dictionary<string, CapturedShipInfo>();
        // Key: officer full name -> baseline level
        public Dictionary<string, int> OfficerLevels { get; set; } = new Dictionary<string, int>();
        // Key: "name\u001frarity\u001filvl" -> count
        public Dictionary<string, int> LockedCrates { get; set; } = new Dictionary<string, int>();
    }

    public sealed class HistoryEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public double ElapsedHours { get; set; }
        public long Credits { get; set; }
        public long CreditsDelta { get; set; }
        public long VanguardMarks { get; set; }
        public long MarksDelta { get; set; }
        public int DogTags { get; set; }
        public int DogTagsDelta { get; set; }
        public int DogTagsVmDelta { get; set; }
        public int CommanderLevel { get; set; }
        public int CommanderLevelDelta { get; set; }
        public long CommanderXp { get; set; }
        public long CommanderXpDelta { get; set; }
        public double CommanderProgressPct { get; set; }
        public int EmpireLevel { get; set; }
        public int EmpireLevelDelta { get; set; }
        public long EmpireXp { get; set; }
        public long EmpireXpDelta { get; set; }
        public double EmpireProgressPct { get; set; }
        public int NewModulesCount { get; set; }
        public int NewShipBpsCount { get; set; }
        public int NewModBpsCount { get; set; }
        public int CapturedShipsCount { get; set; }
    }

    public sealed class DogTagBreakdownEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int VmEach { get; set; }
        public int Baseline { get; set; }
        public int Current { get; set; }
        public int Delta { get; set; }
        public int VmCurrent { get; set; }
        public int VmDelta { get; set; }
    }

    public sealed class ReputationDeltaEntry
    {
        public string Faction { get; set; } = string.Empty;
        public int Current { get; set; }
        public int Delta { get; set; }
    }

    public sealed class ModuleAspectInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class ModuleDeltaEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public int ItemLevel { get; set; }
        public string EquipmentType { get; set; } = string.Empty;
        public int Gained { get; set; }
        public List<string> Substats { get; set; } = new List<string>();
        public List<ModuleAspectInfo> Aspects { get; set; } = new List<ModuleAspectInfo>();
    }

    public sealed class ShipBlueprintDeltaEntry
    {
        public string ShipId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public int Gained { get; set; }
        public int Total { get; set; }
        public int Required { get; set; }
        public bool CanComplete { get; set; }
        public bool AlreadyUnlocked { get; set; }
    }

    public sealed class ModuleBlueprintItemDeltaEntry
    {
        public string DisplayName { get; set; } = string.Empty;
        public string BlueprintName { get; set; } = string.Empty;
        public int Level { get; set; }
        public string Rarity { get; set; } = string.Empty;
        public int Gained { get; set; }
    }

    public sealed class UploadedBlueprintDeltaEntry
    {
        public string Recipe { get; set; } = string.Empty;
        public int? OldLevel { get; set; }
        public int NewLevel { get; set; }
    }

    public sealed class CapturedShipDeltaEntry
    {
        public string Guid { get; set; } = string.Empty;
        public string ShipClass { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public string CustomName { get; set; } = string.Empty;
    }

    public sealed class LockedCrateDeltaEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public int ItemLevel { get; set; }
        public int Gained { get; set; }
    }

    public sealed class ComputedDeltas
    {
        public double ElapsedHours { get; set; }
        public long Credits { get; set; }
        public long CreditsPerHour { get; set; }
        public long VanguardMarks { get; set; }
        public int DogTags { get; set; }
        public int DogTagsVm { get; set; }
        public int CommanderLevel { get; set; }
        public long CommanderXp { get; set; }
        public long CommanderXpPerHour { get; set; }
        public int EmpireLevel { get; set; }
        public long EmpireXp { get; set; }
        public long EmpireXpPerHour { get; set; }
        public Dictionary<string, int> Ranks { get; set; } = new Dictionary<string, int>();
        public List<DogTagBreakdownEntry> DogTagsBreakdown { get; set; } = new List<DogTagBreakdownEntry>();
        public List<ReputationDeltaEntry> Reputation { get; set; } = new List<ReputationDeltaEntry>();
        public List<ModuleDeltaEntry> NewModules { get; set; } = new List<ModuleDeltaEntry>();
        public List<ShipBlueprintDeltaEntry> NewShipBlueprints { get; set; } = new List<ShipBlueprintDeltaEntry>();
        public List<string> NewlyUnlockedShips { get; set; } = new List<string>();
        public List<ModuleBlueprintItemDeltaEntry> NewModuleBlueprintItems { get; set; } = new List<ModuleBlueprintItemDeltaEntry>();
        public List<UploadedBlueprintDeltaEntry> NewUploadedBlueprints { get; set; } = new List<UploadedBlueprintDeltaEntry>();
        public List<CapturedShipDeltaEntry> NewCapturedShips { get; set; } = new List<CapturedShipDeltaEntry>();
        public int LockedCratesTotal { get; set; }
        public List<LockedCrateDeltaEntry> NewLockedCrates { get; set; } = new List<LockedCrateDeltaEntry>();
    }

    public sealed class FleetShipInfo
    {
        public string Guid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ShipClass { get; set; } = string.Empty;
        public string Rarity { get; set; } = string.Empty;
        public bool IsFlagship { get; set; }
        public string Stance { get; set; } = string.Empty;
        public string Activity { get; set; } = string.Empty;
        public bool AwayForRepairs { get; set; }
        public double RepairTimer { get; set; }
        public double Hull { get; set; }
        public double MaxHull { get; set; }
        public double Armor { get; set; }
        public double MaxArmor { get; set; }
        public double Shield { get; set; }
        public double MaxShield { get; set; }
        public DroneBayStatus DroneBay { get; set; }
        public HangarBayStatus HangarBay { get; set; }
    }

    public sealed class AlertItem
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Severity { get; set; } = "warning";
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string TriggeredAt { get; set; } = string.Empty;
    }

    public sealed class AlertsStatus
    {
        public bool Stuck { get; set; }
        public int EmergencyWarps15m { get; set; }
        public List<AlertItem> Items { get; set; } = new List<AlertItem>();
    }

    public sealed class MissionLogEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public string Event { get; set; } = string.Empty;
        public string MissionName { get; set; } = string.Empty;
        public string GameplayType { get; set; } = string.Empty;
        public bool IsStory { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class LiveTelemetry
    {
        public bool InGame { get; set; }
        public EchoStatus Echo { get; set; }
        public CrewStatus Crew { get; set; }
        public DroneBayStatus DroneBay { get; set; }
        public HangarBayStatus HangarBay { get; set; }
        public List<FleetShipInfo> Fleet { get; set; }
        public AlertsStatus Alerts { get; set; }
        public PlayerSnapshot Snapshot { get; set; }
    }
}
