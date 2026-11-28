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
        // Key: "itemId\u001fname\u001fcategory" -> count across all active depots
        public Dictionary<string, int> DepotRawMaterials { get; set; } = new Dictionary<string, int>();
        // Key: "itemId\u001fname" -> count across all active depots
        public Dictionary<string, int> DepotRefinedProducts { get; set; } = new Dictionary<string, int>();
        // Unmerged immutable cached inventory snapshots (merged off the main thread in SessionManager)
        public List<CachedInventorySnapshot> PendingInventorySnapshots { get; set; }
    }

    public sealed class CachedInventorySnapshot
    {
        public Dictionary<string, int> DogTagsByType { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> Modules { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, List<string>> ModuleSubstats { get; } = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public Dictionary<string, List<ModuleAspectInfo>> ModuleAspects { get; } = new Dictionary<string, List<ModuleAspectInfo>>(StringComparer.Ordinal);
        public Dictionary<string, int> LockedCrates { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, int> ShipBlueprintPieces { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, string> ShipSizes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> ShipDisplayNames { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, int> ModuleBlueprintItems { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
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
        public string Size { get; set; } = string.Empty;
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

    public sealed class MissionBoardEntry
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
    }

    public sealed class MissionLogEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public string Event { get; set; } = string.Empty;
        public string MissionName { get; set; } = string.Empty;
        public string GameplayType { get; set; } = string.Empty;
        public bool IsStory { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string StationName { get; set; } = string.Empty;
        public string MissionPoiName { get; set; } = string.Empty;
        public string SystemName { get; set; } = string.Empty;
        public string SectorName { get; set; } = string.Empty;
        public List<MissionBoardEntry> BoardMissions { get; set; } = new List<MissionBoardEntry>();
    }

    public sealed class OutpostStorageInfo
    {
        public string StationGuid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SectorName { get; set; } = string.Empty;
        public string SystemName { get; set; } = string.Empty;
        public string StationType { get; set; } = string.Empty;
        public int Tier { get; set; }
        public double SpaceUsed { get; set; }
        public double Capacity { get; set; }
        public double FillPct { get; set; }
        public bool HasActiveDepot { get; set; }
        public string LargestStackItemId { get; set; } = string.Empty;
        public string LargestStackItemName { get; set; } = string.Empty;
        public int LargestStackCount { get; set; }
        public double LargestStackM3 { get; set; }
        public string State { get; set; } = string.Empty;
        public bool HasStalledJob { get; set; }
        public int ActiveJobCount { get; set; }
        public List<OutpostJobInfo> Jobs { get; set; } = new List<OutpostJobInfo>();
    }

    public sealed class OutpostJobInfo
    {
        public string JobType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double ProgressPct { get; set; }
        public int InitialAmount { get; set; }
        public int RemainingAmount { get; set; }
        public bool Repeat { get; set; }
        public bool Stalled { get; set; }
        public bool Paused { get; set; }
        public bool AwaitingSupplies { get; set; }
        public double TimeRemainingSec { get; set; } = -1.0;
    }

    public sealed class DepotMaterialEntry
    {
        public string ItemId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int Baseline { get; set; }
        public int Current { get; set; }
        public int Delta { get; set; }
        public double RatePerHour { get; set; }
    }

    public sealed class EmpireActivityDelta
    {
        public long RefinedTotal { get; set; }
        public Dictionary<string, int> RefinedByItem { get; set; } = new Dictionary<string, int>();
        public int TradeRuns { get; set; }
        public long TradeEarned { get; set; }
        public long TradeSpent { get; set; }
        public long TradeProfit { get; set; }
        public long StationSalesCredits { get; set; }
        public long OutpostProducedTotal { get; set; }
        public Dictionary<string, int> OutpostProducedByItem { get; set; } = new Dictionary<string, int>();
        public long CraftedTotal { get; set; }
        public Dictionary<string, int> CraftedByItem { get; set; } = new Dictionary<string, int>();
    }

    public sealed class EmpireStatus
    {
        public List<OutpostStorageInfo> Outposts { get; set; } = new List<OutpostStorageInfo>();
        public int DepotCount { get; set; }
        public double DepotSpaceUsed { get; set; }
        public double DepotCapacity { get; set; }
        public double DepotFillPct { get; set; }
        public int RawBacklogTotal { get; set; }
        public int RawBacklogDelta { get; set; }
        public double RawBacklogRatePerHour { get; set; }
        public int BacklogWindowMinutes { get; set; }
        public int RawBacklogWindowDelta { get; set; }
        public double RawBacklogWindowRatePerHour { get; set; }
        public List<DepotMaterialEntry> RawBacklogItems { get; set; } = new List<DepotMaterialEntry>();
        public int RefinedStockTotal { get; set; }
        public int RefinedStockDelta { get; set; }
        public double RefinedStockRatePerHour { get; set; }
        public int RefinedStockWindowDelta { get; set; }
        public double RefinedStockWindowRatePerHour { get; set; }
        public List<DepotMaterialEntry> RefinedStockItems { get; set; } = new List<DepotMaterialEntry>();
        public EmpireActivityDelta Activity { get; set; } = new EmpireActivityDelta();
    }

    public sealed class StationInventoryItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Group { get; set; } = "other"; // "raw", "refined", "other"
        public string Category { get; set; } = string.Empty;
        public int Count { get; set; }
        public double SpaceM3 { get; set; }
    }

    public sealed class StationInventoryInfo
    {
        public string StationGuid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SystemName { get; set; } = string.Empty;
        public string SectorName { get; set; } = string.Empty;
        public bool IsActiveDepot { get; set; }
        public bool IsHomeStation { get; set; }
        public bool IsPlayerOutpost { get; set; }
        public double SpaceUsed { get; set; }
        public double Capacity { get; set; }
        public double FillPct { get; set; }
        public int TotalItemCount { get; set; }
        public int RawCount { get; set; }
        public int RefinedCount { get; set; }
        public int OtherCount { get; set; }
        public List<StationInventoryItem> Items { get; set; } = new List<StationInventoryItem>();
    }

    public sealed class DepotInventoryResponse
    {
        public bool InGame { get; set; }
        public int StationCount { get; set; }
        public List<StationInventoryInfo> Stations { get; set; } = new List<StationInventoryInfo>();
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
        public EmpireStatus Empire { get; set; }
        public PlayerSnapshot Snapshot { get; set; }
    }
}
