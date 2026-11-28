using BepInEx.Configuration;

namespace EchoEnhanced
{
    public sealed class EchoEnhancedConfig
    {
        public ConfigEntry<bool> Enabled { get; }
        public ConfigEntry<bool> AutoFavoriteEnabled { get; }
        public ConfigEntry<bool> AutoFavoriteArmoryOnly { get; }
        public ConfigEntry<bool> AutoFavoriteDryRun { get; }
        public ConfigEntry<bool> AutoFavoriteScanOnLoad { get; }
        public ConfigEntry<bool> AutoFavoriteVerboseLog { get; }
        public ConfigEntry<KeyboardShortcut> RescanHotkey { get; }

        public ConfigEntry<bool> AutoSellEnabled { get; }
        public ConfigEntry<bool> AutoSellArmoryOnly { get; }
        public ConfigEntry<bool> AutoSellDryRun { get; }
        public ConfigEntry<bool> AutoSellScanOnLoad { get; }
        public ConfigEntry<bool> AutoSellDiscordAlert { get; }
        public ConfigEntry<bool> AutoSellVerboseLog { get; }
        public ConfigEntry<KeyboardShortcut> AutoSellScanHotkey { get; }

        public ConfigEntry<bool> AutoBuyEnabled { get; }
        public ConfigEntry<bool> AutoBuyDryRun { get; }
        public ConfigEntry<long> AutoBuyMinCreditsReserve { get; }
        public ConfigEntry<bool> AutoBuyDiscordAlert { get; }
        public ConfigEntry<bool> AutoBuyVerboseLog { get; }
        public ConfigEntry<KeyboardShortcut> AutoBuyScanHotkey { get; }

        public ConfigEntry<bool> DroneLoadoutsEnabled { get; }
        public ConfigEntry<float> DroneLoadoutUiScale { get; }
        public ConfigEntry<KeyboardShortcut> DroneLoadoutUiHotkey { get; }
        public ConfigEntry<KeyboardShortcut> DroneCombatHotkey { get; }
        public ConfigEntry<KeyboardShortcut> DroneSalvageHotkey { get; }
        public ConfigEntry<KeyboardShortcut> DroneMiningHotkey { get; }

        public EchoEnhancedConfig(ConfigFile config)
        {
            Enabled = config.Bind(
                "General",
                "Enabled",
                true,
                "Master switch for EchoEnhanced.");

            AutoFavoriteEnabled = config.Bind(
                "AutoFavorite",
                "Enabled",
                true,
                "Automatically favourite modules/equipment matching rules in BepInEx/config/EchoEnhanced/auto_favorite_rules.json when added to player inventories.");

            AutoFavoriteArmoryOnly = config.Bind(
                "AutoFavorite",
                "ArmoryOnly",
                true,
                "When true (recommended), only auto-favourite items when they enter the Armory (globalInventory) so ECHO can still auto-deposit loot from ship cargo.");

            AutoFavoriteDryRun = config.Bind(
                "AutoFavorite",
                "DryRun",
                false,
                "When true, logs which items WOULD be favourited without modifying the favourite flag.");

            AutoFavoriteScanOnLoad = config.Bind(
                "AutoFavorite",
                "ScanExistingOnLoad",
                false,
                "When true, scans all player inventories once after a save finishes loading and applies Auto-Favorite rules.");

            AutoFavoriteVerboseLog = config.Bind(
                "AutoFavorite",
                "VerboseLog",
                false,
                "Log detailed evaluation info for every equippable item added to player inventories.");

            RescanHotkey = config.Bind(
                "AutoFavorite",
                "RescanHotkey",
                new KeyboardShortcut(UnityEngine.KeyCode.F9, UnityEngine.KeyCode.LeftControl),
                "Hotkey to reload auto_favorite_rules.json and scan all player inventories immediately.");

            AutoSellEnabled = config.Bind(
                "AutoSell",
                "Enabled",
                false,
                "Automatically sell non-favourited modules/equipment matching rules in BepInEx/config/EchoEnhanced/auto_sell_rules.json.");

            AutoSellArmoryOnly = config.Bind(
                "AutoSell",
                "ArmoryOnly",
                true,
                "When true (recommended), only auto-sell items in the Armory (globalInventory).");

            AutoSellDryRun = config.Bind(
                "AutoSell",
                "DryRun",
                true,
                "When true, logs which items WOULD be sold without selling them. Set to false when ready for live auto-selling.");

            AutoSellScanOnLoad = config.Bind(
                "AutoSell",
                "ScanExistingOnLoad",
                false,
                "When true, scans player inventories once after a save finishes loading and applies Auto-Sell rules.");

            AutoSellDiscordAlert = config.Bind(
                "AutoSell",
                "DiscordAlert",
                true,
                "When true, sends a Discord alert via EchoMonitorBot whenever live Auto-Sell transactions occur.");

            AutoSellVerboseLog = config.Bind(
                "AutoSell",
                "VerboseLog",
                false,
                "Log detailed Auto-Sell evaluation info for equippable items.");

            AutoSellScanHotkey = config.Bind(
                "AutoSell",
                "ScanHotkey",
                new KeyboardShortcut(UnityEngine.KeyCode.F10, UnityEngine.KeyCode.LeftControl),
                "Hotkey to reload auto_sell_rules.json and run an Auto-Sell scan immediately.");

            AutoBuyEnabled = config.Bind(
                "AutoBuy",
                "Enabled",
                false,
                "Automatically buy blueprints and modules/equipment from docked station stores matching rules in BepInEx/config/EchoEnhanced/auto_buy_rules.json.");

            AutoBuyDryRun = config.Bind(
                "AutoBuy",
                "DryRun",
                true,
                "When true, logs which store items WOULD be bought without spending credits. Set to false when ready for live auto-buying.");

            AutoBuyMinCreditsReserve = config.Bind(
                "AutoBuy",
                "MinCreditsReserve",
                100000L,
                "Minimum credits to keep in reserve; Auto-Buy will never make a purchase that drops credits below this amount.");

            AutoBuyDiscordAlert = config.Bind(
                "AutoBuy",
                "DiscordAlert",
                true,
                "When true, sends a Discord alert via EchoMonitorBot whenever live Auto-Buy purchases occur.");

            AutoBuyVerboseLog = config.Bind(
                "AutoBuy",
                "VerboseLog",
                false,
                "Log detailed Auto-Buy evaluation info for station store items.");

            AutoBuyScanHotkey = config.Bind(
                "AutoBuy",
                "ScanHotkey",
                new KeyboardShortcut(UnityEngine.KeyCode.F11, UnityEngine.KeyCode.LeftControl),
                "Hotkey to reload auto_buy_rules.json and scan the docked station's store immediately.");

            DroneLoadoutsEnabled = config.Bind(
                "DroneLoadouts",
                "Enabled",
                true,
                "Enable Fleet Drone Loadout presets and ECHO automatic drone swapping.");

            DroneLoadoutUiScale = config.Bind(
                "DroneLoadouts",
                "UiScale",
                0f,
                "UI scale multiplier for the Fleet Drone Loadouts overlay (0 = auto-scale based on screen height, or set 0.75 to 3.0).");

            DroneLoadoutUiHotkey = config.Bind(
                "DroneLoadouts",
                "ToggleUiHotkey",
                new KeyboardShortcut(UnityEngine.KeyCode.D, UnityEngine.KeyCode.LeftAlt),
                "Hotkey to toggle the in-game Fleet Drone Loadouts overlay.");

            DroneCombatHotkey = config.Bind(
                "DroneLoadouts",
                "CombatPresetHotkey",
                new KeyboardShortcut(UnityEngine.KeyCode.Alpha1, UnityEngine.KeyCode.LeftAlt),
                "Hotkey to apply the bound Combat drone preset to the active fleet.");

            DroneSalvageHotkey = config.Bind(
                "DroneLoadouts",
                "SalvagePresetHotkey",
                new KeyboardShortcut(UnityEngine.KeyCode.Alpha2, UnityEngine.KeyCode.LeftAlt),
                "Hotkey to apply the bound Salvage drone preset to the active fleet.");

            DroneMiningHotkey = config.Bind(
                "DroneLoadouts",
                "MiningPresetHotkey",
                new KeyboardShortcut(UnityEngine.KeyCode.Alpha3, UnityEngine.KeyCode.LeftAlt),
                "Hotkey to apply the bound Mining drone preset to the active fleet.");
        }
    }
}
