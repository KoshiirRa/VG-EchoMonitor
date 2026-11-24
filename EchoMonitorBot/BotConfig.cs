using System.Text.Json;

namespace EchoMonitorBot;

public sealed class BotConfig
{
    public string DiscordBotToken { get; init; } = string.Empty;
    public string DiscordChannelId { get; init; } = string.Empty;
    public HashSet<string> AllowedUserIds { get; init; } = new(StringComparer.Ordinal);
    public string EchoMonitorUrl { get; init; } = "http://127.0.0.1:8778";
    public string EchoMonitorToken { get; init; } = string.Empty;
    public int OfflineAlertSeconds { get; init; } = 30;
    public int DigestIntervalMinutes { get; init; } = 60;
    public string MinLootAlertRarity { get; init; } = "Exotic";
    public bool ExitOnCleanShutdown { get; init; } = true;

    private static readonly Dictionary<string, int> RarityOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Standard"] = 1,
        ["Enhanced"] = 2,
        ["HighGrade"] = 3,
        ["Exotic"] = 4,
        ["Legendary"] = 5
    };

    public bool IsAllowed(string? userId)
    {
        return !string.IsNullOrEmpty(userId) && AllowedUserIds.Contains(userId);
    }

    public bool RarityMeetsThreshold(string? rarity)
    {
        if (string.Equals(MinLootAlertRarity, "Off", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrEmpty(rarity))
            return false;
        int minRank = RarityOrder.TryGetValue(MinLootAlertRarity, out int m) ? m : 4;
        int rank = RarityOrder.TryGetValue(rarity, out int r) ? r : 0;
        return rank >= minRank;
    }

    public static BotConfig Load()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string exeDir = AppContext.BaseDirectory;
        string cwd = Directory.GetCurrentDirectory();

        // 1. Load echomonitor_bot.json (exe dir first, then CWD)
        LoadJsonFile(Path.Combine(exeDir, "echomonitor_bot.json"), values);
        if (!string.Equals(exeDir.TrimEnd('\\', '/'), cwd.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            LoadJsonFile(Path.Combine(cwd, "echomonitor_bot.json"), values);

        // 2. Load .env (exe dir first, then CWD)
        LoadDotEnv(Path.Combine(exeDir, ".env"), values);
        if (!string.Equals(exeDir.TrimEnd('\\', '/'), cwd.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            LoadDotEnv(Path.Combine(cwd, ".env"), values);

        // 3. Environment variables override file values
        string[] keys =
        {
            "DISCORD_BOT_TOKEN",
            "DISCORD_CHANNEL_ID",
            "DISCORD_ALLOWED_USER_IDS",
            "ECHOMONITOR_URL",
            "ECHOMONITOR_TOKEN",
            "OFFLINE_ALERT_SECONDS",
            "DIGEST_INTERVAL_MINUTES",
            "MIN_LOOT_ALERT_RARITY",
            "EXIT_ON_CLEAN_SHUTDOWN"
        };

        foreach (string key in keys)
        {
            string? envVal = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(envVal))
                values[key] = envVal.Trim();
        }

        string Get(string key, string fallback = "") =>
            values.TryGetValue(key, out string? v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

        string token = Get("DISCORD_BOT_TOKEN");
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("DISCORD_BOT_TOKEN is required (set in echomonitor_bot.json, .env, or environment).");

        string channelId = Get("DISCORD_CHANNEL_ID");
        if (string.IsNullOrEmpty(channelId))
            throw new InvalidOperationException("DISCORD_CHANNEL_ID is required (set in echomonitor_bot.json, .env, or environment).");

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string part in Get("DISCORD_ALLOWED_USER_IDS").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrEmpty(part))
                allowed.Add(part);
        }

        int offlineSecs = int.TryParse(Get("OFFLINE_ALERT_SECONDS", "30"), out int o) ? Math.Max(5, o) : 30;
        int digestMins = int.TryParse(Get("DIGEST_INTERVAL_MINUTES", "60"), out int d) ? Math.Max(0, d) : 60;
        bool defaultExitOnCleanShutdown = OperatingSystem.IsWindows();
        string exitRaw = Get("EXIT_ON_CLEAN_SHUTDOWN", defaultExitOnCleanShutdown ? "true" : "false");
        bool exitOnCleanShutdown = string.Equals(exitRaw, "true", StringComparison.OrdinalIgnoreCase);

        return new BotConfig
        {
            DiscordBotToken = token,
            DiscordChannelId = channelId,
            AllowedUserIds = allowed,
            EchoMonitorUrl = Get("ECHOMONITOR_URL", "http://127.0.0.1:8778").TrimEnd('/'),
            EchoMonitorToken = Get("ECHOMONITOR_TOKEN"),
            OfflineAlertSeconds = offlineSecs,
            DigestIntervalMinutes = digestMins,
            MinLootAlertRarity = Get("MIN_LOOT_ALERT_RARITY", "Exotic"),
            ExitOnCleanShutdown = exitOnCleanShutdown
        };
    }

    private static void LoadJsonFile(string path, Dictionary<string, string> target)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                string val = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => string.Empty
                };
                if (!string.IsNullOrWhiteSpace(val))
                    target[prop.Name] = val.Trim();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Warning: failed to parse {path}: {ex.Message}");
        }
    }

    private static void LoadDotEnv(string path, Dictionary<string, string> target)
    {
        if (!File.Exists(path)) return;
        try
        {
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();
                if (val.Length >= 2 && ((val[0] == '"' && val[^1] == '"') || (val[0] == '\'' && val[^1] == '\'')))
                    val = val[1..^1];
                if (!string.IsNullOrEmpty(val))
                    target[key] = val;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Warning: failed to parse {path}: {ex.Message}");
        }
    }
}
