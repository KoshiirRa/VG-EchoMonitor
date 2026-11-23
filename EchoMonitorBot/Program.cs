using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EchoMonitorBot;

public static class Program
{
    private static readonly HashSet<string> StuckAlertTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DestroyedOrHostileStation",
        "EmergencyWarpLoop"
    };

    private static readonly HashSet<string> RaidAlertTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "EmpireRaidOrSiege"
    };

    private static readonly Dictionary<string, int> RarityColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Standard"] = 0x94A3B8,
        ["Enhanced"] = 0x22C55E,
        ["HighGrade"] = 0x3B82F6,
        ["Exotic"] = 0xA855F7,
        ["Legendary"] = 0xF59E0B
    };

    private static BotConfig _config = null!;
    private static EchoMonitorClient _api = null!;
    private static DiscordGateway _discord = null!;

    private static readonly object StateLock = new();
    private static JsonElement? _latestState;

    private static bool _hasBeenOnline;
    private static bool _isOnline;
    private static bool _cleanShutdownReceived;
    private static DateTimeOffset? _offlineSince;
    private static bool _offlineAlertSent;

    private static readonly HashSet<(string Id, string TriggeredAt)> SeenAlerts = new();
    private static bool _seededLoot;
    private static string _lastSaveId = string.Empty;
    private static readonly Dictionary<(string Name, string Rarity, int ItemLevel, string StatsSig), int> SeenModuleCounts = new();
    private static readonly HashSet<string> SeenReadyBps = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<int> Main()
    {
        try
        {
            _config = BotConfig.Load();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Fatal] {ex.Message}");
            return 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        using var api = new EchoMonitorClient(_config);
        using var discord = new DiscordGateway(_config);
        _api = api;
        _discord = discord;
        _discord.OnInteraction += HandleInteractionAsync;

        Console.WriteLine($"[EchoMonitorBot] Starting (EchoMonitor={_config.EchoMonitorUrl}, Channel={_config.DiscordChannelId})");

        var tasks = new List<Task>
        {
            _discord.RunAsync(cts.Token),
            RunSseLoopAsync(cts.Token)
        };

        if (_config.DigestIntervalMinutes > 0)
            tasks.Add(RunDigestLoopAsync(cts.Token));

        await Task.WhenAll(tasks);
        return 0;
    }

    private static async Task RunSseLoopAsync(CancellationToken ct)
    {
        double backoff = 2.0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (cleanShutdown, reason) = await _api.StreamEventsAsync(
                    onConnected: async () =>
                    {
                        backoff = 2.0;
                        bool wasOfflineAlerted = _offlineAlertSent;
                        bool wasCleanShutdown = _cleanShutdownReceived;
                        _hasBeenOnline = true;
                        _isOnline = true;
                        _cleanShutdownReceived = false;
                        _offlineSince = null;
                        _offlineAlertSent = false;

                        if (wasOfflineAlerted || wasCleanShutdown)
                        {
                            var embed = MakeEmbed(
                                "EchoMonitor Back Online",
                                $"Reconnected to `{_config.EchoMonitorUrl}`.",
                                0x22C55E);
                            await _discord.PostChannelMessageAsync(embed, ct: ct);
                        }
                    },
                    onState: async doc =>
                    {
                        using (doc)
                        {
                            var root = doc.RootElement.Clone();
                            lock (StateLock) { _latestState = root; }
                            await ProcessStateAsync(root, ct);
                        }
                    },
                    onCleanShutdown: async shutdownReason =>
                    {
                        _isOnline = false;
                        _cleanShutdownReceived = true;
                        _offlineSince = null;
                        _offlineAlertSent = false;
                        Console.WriteLine($"[SSE] Clean shutdown received: {shutdownReason}");

                        var embed = MakeEmbed(
                            "Vanguard Galaxy Closed Cleanly",
                            $"EchoMonitor reported a clean game exit (`{shutdownReason}`).",
                            0x64748B);
                        await _discord.PostChannelMessageAsync(embed, ct: ct);
                    },
                    ct: ct);

                if (!cleanShutdown)
                    await HandleUnexpectedDisconnectAsync(reason, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                await HandleUnexpectedDisconnectAsync(ex.Message, ct);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(backoff), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            backoff = Math.Min(30.0, backoff * 1.5);
        }
    }

    private static async Task HandleUnexpectedDisconnectAsync(string reason, CancellationToken ct)
    {
        _isOnline = false;
        if (_cleanShutdownReceived)
            return; // Game exited cleanly; do not fire a crash alert while closed.

        var now = DateTimeOffset.UtcNow;
        _offlineSince ??= now;

        if (_hasBeenOnline && !_offlineAlertSent && (now - _offlineSince.Value).TotalSeconds >= _config.OfflineAlertSeconds)
        {
            _offlineAlertSent = true;
            var embed = MakeEmbed(
                "Vanguard Galaxy / EchoMonitor Offline (Possible Crash)",
                $"EchoMonitor has been unreachable for **{_config.OfflineAlertSeconds}s+**\nURL: `{_config.EchoMonitorUrl}`\nLast error: `{Truncate(reason, 200)}`",
                0xEF4444);
            await _discord.PostChannelMessageAsync(embed, ct: ct);
        }
    }

    private static async Task ProcessStateAsync(JsonElement state, CancellationToken ct)
    {
        if (!GetBool(state, "inGame"))
            return;

        await CheckAlertsAsync(state, ct);
        await CheckLootAndBpsAsync(state, ct);
    }

    private static async Task CheckAlertsAsync(JsonElement state, CancellationToken ct)
    {
        if (!TryGetObj(state, "alerts", out var alerts) || !TryGetArr(alerts, "items", out var items))
            return;

        foreach (var item in items.EnumerateArray())
        {
            string id = GetStr(item, "id");
            string triggeredAt = GetStr(item, "triggeredAt");
            if (string.IsNullOrEmpty(id) || !SeenAlerts.Add((id, triggeredAt)))
                continue;

            string type = GetStr(item, "type");
            string title = GetStr(item, "title", type);
            string detail = GetStr(item, "detail");

            if (StuckAlertTypes.Contains(type))
            {
                var embed = MakeEmbed($"Stuck ECHO Alert — {title}", detail, 0xEF4444);
                var components = new JsonArray
                {
                    DiscordGateway.MakeActionRow(
                        DiscordGateway.MakeButton("Warp Safe Station", "em:warp_safe", 4),
                        DiscordGateway.MakeButton("Abandon Mission", "em:abandon_mission", 2),
                        DiscordGateway.MakeButton("Reset ECHO", "em:reset_echo", 1),
                        DiscordGateway.MakeButton("Dismiss", "em:dismiss", 2))
                };
                await _discord.PostChannelMessageAsync(embed, components, ct);
            }
            else if (RaidAlertTypes.Contains(type))
            {
                var embed = MakeEmbed($"Empire Raid / Siege — {title}", detail, 0xDC143C);
                var components = new JsonArray
                {
                    DiscordGateway.MakeActionRow(
                        DiscordGateway.MakeButton("Warp to Defend", "em:warp_defend", 4),
                        DiscordGateway.MakeButton("Dismiss", "em:dismiss", 2))
                };
                await _discord.PostChannelMessageAsync(embed, components, ct);
            }
            else if (string.Equals(type, "OutpostStorageFull", StringComparison.OrdinalIgnoreCase))
            {
                string guid = id.StartsWith("outpost_storage_full:", StringComparison.OrdinalIgnoreCase)
                    ? id["outpost_storage_full:".Length..]
                    : string.Empty;
                var embed = MakeEmbed($"Outpost Storage Alert — {title}", detail, 0xF59E0B);
                var components = new JsonArray
                {
                    DiscordGateway.MakeActionRow(
                        DiscordGateway.MakeButton("Move Half Largest Stack → Depot", $"em:relieve_outpost:{guid}", 1),
                        DiscordGateway.MakeButton("Dismiss", "em:dismiss", 2))
                };
                await _discord.PostChannelMessageAsync(embed, components, ct);
            }
            else
            {
                var embed = MakeEmbed($"EchoMonitor Alert — {title}", detail, 0xF59E0B);
                await _discord.PostChannelMessageAsync(embed, ct: ct);
            }
        }
    }

    private static async Task CheckLootAndBpsAsync(JsonElement state, CancellationToken ct)
    {
        if (!TryGetObj(state, "deltas", out var deltas))
            return;

        string saveId = GetStr(state, "saveId");
        bool hasMods = TryGetArr(deltas, "newModules", out var newMods);
        bool hasBps = TryGetArr(deltas, "newShipBlueprints", out var shipBps);

        bool saveChanged = !string.IsNullOrEmpty(saveId) && !string.Equals(saveId, _lastSaveId, StringComparison.Ordinal);
        if (!_seededLoot || saveChanged)
        {
            SeenModuleCounts.Clear();
            SeenReadyBps.Clear();
            if (hasMods)
            {
                foreach (var mod in newMods.EnumerateArray())
                {
                    var key = BuildModuleKey(mod);
                    SeenModuleCounts[key] = GetInt(mod, "gained");
                }
            }
            if (hasBps)
            {
                foreach (var bp in shipBps.EnumerateArray())
                {
                    if (GetBool(bp, "canComplete") && !GetBool(bp, "alreadyUnlocked"))
                    {
                        string sid = GetStr(bp, "shipId");
                        if (!string.IsNullOrEmpty(sid)) SeenReadyBps.Add(sid);
                    }
                }
            }
            _lastSaveId = saveId;
            _seededLoot = true;
            return;
        }

        if (hasMods)
        {
            var newDrops = new List<(string Name, string Rarity, int ItemLevel, int Diff, List<string> Lines)>();
            foreach (var mod in newMods.EnumerateArray())
            {
                string name = GetStr(mod, "name", "Unknown Module");
                string rarity = GetStr(mod, "rarity", "Standard");
                int ilvl = GetInt(mod, "itemLevel");
                int gained = GetInt(mod, "gained");
                var key = BuildModuleKey(mod);

                int prev = SeenModuleCounts.GetValueOrDefault(key, 0);
                if (gained > prev)
                {
                    SeenModuleCounts[key] = gained;
                    if (_config.RarityMeetsThreshold(rarity))
                    {
                        int diff = gained - prev;
                        var lines = new List<string> { $"**{name}** (iLvl {ilvl}) x{diff}" };
                        if (TryGetArr(mod, "substats", out var subs))
                        {
                            int count = 0;
                            foreach (var s in subs.EnumerateArray())
                            {
                                if (count++ >= 6) break;
                                if (s.ValueKind == JsonValueKind.String)
                                {
                                    string cleanSub = StripRichText(s.GetString() ?? string.Empty);
                                    if (!string.IsNullOrEmpty(cleanSub))
                                        lines.Add($"- {cleanSub}");
                                }
                            }
                        }
                        if (TryGetArr(mod, "aspects", out var aspects))
                        {
                            int count = 0;
                            foreach (var a in aspects.EnumerateArray())
                            {
                                if (count++ >= 4) break;
                                string aName = StripRichText(GetStr(a, "name"));
                                string aDesc = StripRichText(GetStr(a, "description"));
                                if (!string.IsNullOrEmpty(aName) && !string.IsNullOrEmpty(aDesc))
                                    lines.Add($"• **{aName}** — {aDesc}");
                                else if (!string.IsNullOrEmpty(aName))
                                    lines.Add($"• **{aName}**");
                            }
                        }
                        newDrops.Add((name, rarity, ilvl, diff, lines));
                    }
                }
            }

            if (newDrops.Count > 5)
            {
                var summaryLines = new List<string>
                {
                    $"**{newDrops.Count} qualifying modules** appeared in a single update (collapsed to prevent spam):"
                };
                foreach (var d in newDrops.Take(10))
                {
                    summaryLines.Add($"- **[{d.Rarity}] {d.Name}** (iLvl {d.ItemLevel}) x{d.Diff}");
                }
                if (newDrops.Count > 10)
                    summaryLines.Add($"*...and {newDrops.Count - 10} more*");
                var embed = MakeEmbed("Rare Loot Batch", Truncate(string.Join("\n", summaryLines), 4000), 0xA855F7);
                await _discord.PostChannelMessageAsync(embed, ct: ct);
            }
            else
            {
                foreach (var d in newDrops)
                {
                    int color = RarityColors.GetValueOrDefault(d.Rarity, 0xA855F7);
                    var embed = MakeEmbed($"Rare Loot Drop — {d.Rarity}", Truncate(string.Join("\n", d.Lines), 4000), color);
                    await _discord.PostChannelMessageAsync(embed, ct: ct);
                }
            }
        }

        if (hasBps)
        {
            foreach (var bp in shipBps.EnumerateArray())
            {
                string shipId = GetStr(bp, "shipId");
                if (string.IsNullOrEmpty(shipId)) continue;
                if (GetBool(bp, "canComplete") && !GetBool(bp, "alreadyUnlocked") && SeenReadyBps.Add(shipId))
                {
                    string display = GetStr(bp, "displayName", shipId);
                    string size = GetStr(bp, "size");
                    int total = GetInt(bp, "total");
                    int req = GetInt(bp, "required");
                    var embed = MakeEmbed(
                        "Ship Blueprint Ready to Complete!",
                        $"**{display}** ({size}) — `{total}/{req}` pieces collected.",
                        0x10B981);
                    await _discord.PostChannelMessageAsync(embed, ct: ct);
                }
            }
        }
    }

    private static async Task RunDigestLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(_config.DigestIntervalMinutes);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            JsonElement? snap;
            lock (StateLock) { snap = _latestState; }
            if (_isOnline && snap.HasValue && GetBool(snap.Value, "inGame"))
            {
                await _discord.PostChannelMessageAsync(BuildDigestEmbed(snap.Value), ct: ct);
            }
        }
    }

    private static async Task HandleInteractionAsync(DiscordInteraction interaction)
    {
        if (interaction.Type == 2) // APPLICATION_COMMAND
        {
            await HandleSlashCommandAsync(interaction);
        }
        else if (interaction.Type == 3) // MESSAGE_COMPONENT (Button)
        {
            await HandleButtonAsync(interaction);
        }
    }

    private static async Task HandleSlashCommandAsync(DiscordInteraction interaction)
    {
        switch (interaction.CommandName)
        {
            case "status":
            {
                var state = await GetFreshStateAsync();
                if (!state.HasValue)
                    await _discord.ReplyInteractionAsync(interaction, text: "EchoMonitor is currently unreachable.", ephemeral: true);
                else
                    await _discord.ReplyInteractionAsync(interaction, embed: BuildStatusEmbed(state.Value));
                break;
            }
            case "fleet":
            {
                var state = await GetFreshStateAsync();
                if (!state.HasValue)
                    await _discord.ReplyInteractionAsync(interaction, text: "EchoMonitor is currently unreachable.", ephemeral: true);
                else
                    await _discord.ReplyInteractionAsync(interaction, embed: BuildFleetEmbed(state.Value));
                break;
            }
            case "loot":
            {
                var state = await GetFreshStateAsync();
                if (!state.HasValue)
                    await _discord.ReplyInteractionAsync(interaction, text: "EchoMonitor is currently unreachable.", ephemeral: true);
                else
                    await _discord.ReplyInteractionAsync(interaction, embed: BuildLootEmbed(state.Value));
                break;
            }
            case "missions":
            {
                var state = await GetFreshStateAsync();
                if (!state.HasValue)
                    await _discord.ReplyInteractionAsync(interaction, text: "EchoMonitor is currently unreachable.", ephemeral: true);
                else
                    await _discord.ReplyInteractionAsync(interaction, embed: BuildMissionsEmbed(state.Value));
                break;
            }
            case "empire":
            {
                var state = await GetFreshStateAsync();
                if (!state.HasValue)
                    await _discord.ReplyInteractionAsync(interaction, text: "EchoMonitor is currently unreachable.", ephemeral: true);
                else
                    await _discord.ReplyInteractionAsync(interaction, embed: BuildEmpireEmbed(state.Value));
                break;
            }
            case "recover":
            {
                if (!_config.IsAllowed(interaction.UserId))
                {
                    await _discord.ReplyInteractionAsync(interaction, text: "You are not authorized to use EchoMonitor recovery controls.", ephemeral: true);
                    return;
                }
                var components = new JsonArray
                {
                    DiscordGateway.MakeActionRow(
                        DiscordGateway.MakeButton("Warp Safe Station", "em:warp_safe", 4),
                        DiscordGateway.MakeButton("Abandon Mission", "em:abandon_mission", 2),
                        DiscordGateway.MakeButton("Reset ECHO", "em:reset_echo", 1)),
                    DiscordGateway.MakeActionRow(
                        DiscordGateway.MakeButton("Toggle ECHO", "em:toggle_echo", 2),
                        DiscordGateway.MakeButton("Warp to Defend", "em:warp_defend", 4))
                };
                await _discord.ReplyInteractionAsync(
                    interaction,
                    text: "**EchoMonitor Recovery Controls**\nSelect an action below:",
                    components: components,
                    ephemeral: true);
                break;
            }
            case "reset_session":
            {
                if (!_config.IsAllowed(interaction.UserId))
                {
                    await _discord.ReplyInteractionAsync(interaction, text: "You are not authorized to reset the EchoMonitor session.", ephemeral: true);
                    return;
                }
                await _discord.DeferInteractionAsync(interaction, ephemeral: true);
                var res = await _api.PostActionAsync("/api/session/reset");
                if (res.Ok)
                {
                    _seededLoot = false;
                    SeenModuleCounts.Clear();
                    SeenReadyBps.Clear();
                    string saveId = res.RawData.HasValue ? GetStr(res.RawData.Value, "saveId", "unknown") : "unknown";
                    await _discord.EditDeferredReplyAsync(interaction, $"Session baseline reset for save `{saveId}`.");
                }
                else
                {
                    await _discord.EditDeferredReplyAsync(interaction, $"Reset failed: {res.Message}");
                }
                break;
            }
        }
    }

    private static async Task HandleButtonAsync(DiscordInteraction interaction)
    {
        if (!_config.IsAllowed(interaction.UserId))
        {
            await _discord.ReplyInteractionAsync(interaction, text: "You are not authorized to run EchoMonitor recovery actions.", ephemeral: true);
            return;
        }

        if (interaction.CustomId.StartsWith("em:relieve_outpost:", StringComparison.OrdinalIgnoreCase))
        {
            string guid = interaction.CustomId["em:relieve_outpost:".Length..];
            await _discord.DeferInteractionAsync(interaction, ephemeral: true);
            string path = string.IsNullOrEmpty(guid)
                ? "/api/action/relieve-outpost"
                : $"/api/action/relieve-outpost?stationGuid={Uri.EscapeDataString(guid)}";
            var relieveRes = await _api.PostActionAsync(path);
            string rTag = relieveRes.Ok ? "OK" : "FAILED";
            await _discord.EditDeferredReplyAsync(interaction, $"**[{rTag}] Relieve Outpost Storage**: {relieveRes.Message}");
            return;
        }

        (string Endpoint, string Label)? action = interaction.CustomId switch
        {
            "em:warp_safe" => ("/api/recovery/warp-safe-station", "Warp Safe Station"),
            "em:abandon_mission" => ("/api/recovery/abandon-mission", "Abandon Mission"),
            "em:reset_echo" => ("/api/recovery/reset-echo", "Reset ECHO"),
            "em:toggle_echo" => ("/api/recovery/toggle-echo", "Toggle ECHO"),
            "em:warp_defend" => ("/api/recovery/warp-defend", "Warp to Defend"),
            "em:dismiss" => ("/api/alerts/dismiss", "Dismiss Alerts"),
            _ => null
        };

        if (action == null)
        {
            await _discord.ReplyInteractionAsync(interaction, text: "Unknown button action.", ephemeral: true);
            return;
        }

        await _discord.DeferInteractionAsync(interaction, ephemeral: true);
        var res = await _api.PostActionAsync(action.Value.Endpoint);
        string tag = res.Ok ? "OK" : "FAILED";
        await _discord.EditDeferredReplyAsync(interaction, $"**[{tag}] {action.Value.Label}**: {res.Message}");
    }

    private static async Task<JsonElement?> GetFreshStateAsync()
    {
        using var doc = await _api.GetStateAsync();
        if (doc != null)
        {
            var root = doc.RootElement.Clone();
            lock (StateLock) { _latestState = root; }
            return root;
        }
        lock (StateLock) { return _latestState; }
    }

    // --- Embed Builders ---

    private static JsonObject BuildStatusEmbed(JsonElement state)
    {
        if (!GetBool(state, "inGame"))
            return MakeEmbed("Vanguard Galaxy — Not In Game", "EchoMonitor is online, but no active save is loaded.", 0x64748B);

        TryGetObj(state, "echo", out var echo);
        TryGetObj(state, "current", out var current);
        TryGetObj(state, "deltas", out var deltas);
        TryGetObj(state, "alerts", out var alerts);

        bool isStuck = GetBool(alerts, "stuck");
        bool echoEnabled = GetBool(echo, "enabled");
        int color = isStuck ? 0xEF4444 : (echoEnabled ? 0x22C55E : 0xF59E0B);

        string echoLabel = echoEnabled ? "ACTIVE" : "PAUSED";
        string echoState = GetStr(echo, "state", "Unknown");
        string sysName = GetStr(echo, "systemName", "Unknown");
        string poiName = GetStr(echo, "poiName", "Open Space");
        string dockedTag = GetBool(echo, "docked") ? " (Docked)" : "";
        string mission = GetStr(echo, "activeMission", "None");

        var fields = new JsonArray
        {
            MakeField("Flagship Vitals",
                $"Shield: **{FmtPct(GetDbl(echo, "shield"), GetDbl(echo, "maxShield"))}** | Armor: **{FmtPct(GetDbl(echo, "armor"), GetDbl(echo, "maxArmor"))}** | Hull: **{FmtPct(GetDbl(echo, "hull"), GetDbl(echo, "maxHull"))}**",
                false),
            MakeField("Credits",
                $"{FmtNum(GetLong(current, "credits"))}\n`{FmtSigned(GetLong(deltas, "credits"))}` ({FmtNum(GetLong(deltas, "creditsPerHour"))}/hr)",
                true),
            MakeField("Marks & Dog Tags",
                $"VM: {FmtNum(GetLong(current, "vanguardMarks"))} (`{FmtSigned(GetLong(deltas, "vanguardMarks"))}`)\nTags: {FmtNum(GetInt(current, "dogTags"))} (`{FmtSigned(GetInt(deltas, "dogTags"))}`, {FmtSigned(GetInt(deltas, "dogTagsVm"))} VM)",
                true),
            MakeField("Progression",
                $"Commander: **Lv {GetInt(current, "commanderLevel")}** ({GetDbl(current, "commanderProgressPct"):0.0}%)\nEmpire: **Lv {GetInt(current, "empireLevel")}** ({GetDbl(current, "empireProgressPct"):0.0}%)",
                true)
        };

        if (TryGetObj(state, "empire", out var empire))
        {
            fields.Add(BuildEmpireSummaryField(empire));
        }

        var embed = MakeEmbed(
            $"EchoMonitor Status — ECHO {echoLabel}",
            $"**State:** `{echoState}`\n**Location:** {sysName} — {poiName}{dockedTag}\n**Mission:** {mission}",
            color,
            fields);
        embed["footer"] = new JsonObject { ["text"] = $"Session: {FmtHours(GetDbl(state, "sessionDurationHours"))} | Save: {GetStr(state, "saveId", "unknown")}" };
        return embed;
    }

    private static JsonObject BuildFleetEmbed(JsonElement state)
    {
        if (!TryGetArr(state, "fleet", out var fleet) || fleet.GetArrayLength() == 0)
            return MakeEmbed("Active Fleet (0 ships)", "No active fleet ships reported.", 0x38BDF8);

        var fields = new JsonArray();
        int count = 0;
        foreach (var ship in fleet.EnumerateArray())
        {
            if (count++ >= 12) break;
            string name = GetStr(ship, "name", GetStr(ship, "shipClass", "Unknown"));
            string role = GetBool(ship, "isFlagship") ? "Flagship" : "Wingman";
            string rarity = GetStr(ship, "rarity");
            string title = $"{name} [{role}]" + (!string.IsNullOrEmpty(rarity) && rarity != "None" ? $" ({rarity})" : "");

            string sh = FmtPct(GetDbl(ship, "shield"), GetDbl(ship, "maxShield"));
            string ar = FmtPct(GetDbl(ship, "armor"), GetDbl(ship, "maxArmor"));
            string hu = FmtPct(GetDbl(ship, "hull"), GetDbl(ship, "maxHull"));

            string stance = GetStr(ship, "stance", "Default");
            string activity = GetBool(ship, "awayForRepairs")
                ? $"Away for Repairs ({GetDbl(ship, "repairTimer"):0}s)"
                : GetStr(ship, "activity", "Idle");

            var lines = new List<string>
            {
                $"S/A/H: **{sh}** / **{ar}** / **{hu}**",
                $"Stance: `{stance}` | Activity: `{activity}`"
            };

            if (TryGetObj(ship, "droneBay", out var db) && GetBool(db, "equipped"))
            {
                lines.Add($"Drones: {GetInt(db, "deployedDrones")} deployed ({GetInt(db, "builtDrones")}/{GetInt(db, "maxDrones")} built) — {GetStr(db, "status", "Idle")}");
            }

            fields.Add(MakeField(title, string.Join("\n", lines), false));
        }

        return MakeEmbed($"Active Fleet ({fleet.GetArrayLength()} ships)", null, 0x38BDF8, fields);
    }

    private static JsonObject BuildLootEmbed(JsonElement state)
    {
        TryGetObj(state, "deltas", out var deltas);
        var fields = new JsonArray();

        // Modules by rarity
        var byRarity = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var highlights = new List<string>();
        if (TryGetArr(deltas, "newModules", out var mods))
        {
            foreach (var m in mods.EnumerateArray())
            {
                string r = GetStr(m, "rarity", "Standard");
                int g = Math.Max(1, GetInt(m, "gained"));
                byRarity[r] = byRarity.GetValueOrDefault(r, 0) + g;
                if (r.Equals("Exotic", StringComparison.OrdinalIgnoreCase) || r.Equals("Legendary", StringComparison.OrdinalIgnoreCase))
                    highlights.Add($"- **[{r}]** {GetStr(m, "name", "Unknown")} (iLvl {GetInt(m, "itemLevel")}) x{g}");
            }
        }

        string modText;
        if (byRarity.Count > 0)
        {
            var ordered = new[] { "Legendary", "Exotic", "HighGrade", "Enhanced", "Standard" }
                .Where(r => byRarity.ContainsKey(r))
                .Select(r => $"**{r}**: {byRarity[r]}");
            modText = string.Join(" | ", ordered);
            if (highlights.Count > 0)
                modText += "\n" + string.Join("\n", highlights.Take(8));
        }
        else
        {
            modText = "No new modules yet this session.";
        }
        fields.Add(MakeField("New Modules", modText, false));

        // Locked crates
        int cratesTotal = GetInt(deltas, "lockedCratesTotal");
        if (cratesTotal > 0 && TryGetArr(deltas, "newLockedCrates", out var crates))
        {
            var lines = new List<string>();
            int cCount = 0;
            foreach (var c in crates.EnumerateArray())
            {
                if (cCount++ >= 8) break;
                lines.Add($"- **[{GetStr(c, "rarity", "Standard")}]** {GetStr(c, "name", "Crate")} (iLvl {GetInt(c, "itemLevel")}) +{GetInt(c, "gained")}");
            }
            fields.Add(MakeField($"Locked Crates (+{cratesTotal})", string.Join("\n", lines), false));
        }
        else
        {
            fields.Add(MakeField("Locked Crates", "None obtained this session.", false));
        }

        // Ready ship blueprints
        var readyLines = new List<string>();
        if (TryGetArr(deltas, "newShipBlueprints", out var bps))
        {
            foreach (var bp in bps.EnumerateArray())
            {
                if (GetBool(bp, "canComplete") && !GetBool(bp, "alreadyUnlocked"))
                {
                    readyLines.Add($"- **{GetStr(bp, "displayName", GetStr(bp, "shipId"))}** ({GetStr(bp, "size")}) — {GetInt(bp, "total")}/{GetInt(bp, "required")} pcs");
                    if (readyLines.Count >= 10) break;
                }
            }
        }
        fields.Add(MakeField("Ready Ship Blueprints", readyLines.Count > 0 ? string.Join("\n", readyLines) : "None ready to complete.", false));

        return MakeEmbed("Session Loot Summary", null, 0xA855F7, fields);
    }

    private static JsonObject BuildMissionsEmbed(JsonElement state)
    {
        if (!TryGetArr(state, "missionLog", out var log) || log.GetArrayLength() == 0)
            return MakeEmbed("Recent Mission Log (Last 10)", "No mission events recorded yet.", 0x0EA5E9);

        var items = log.EnumerateArray().ToList();
        var recent = items.Skip(Math.Max(0, items.Count - 10)).Reverse();
        var lines = new List<string>();
        foreach (var entry in recent)
        {
            string ts = GetStr(entry, "timestamp");
            string timePart = ts.Length >= 19 ? ts.Substring(11, 8) : ts;
            string ev = GetStr(entry, "event", "Event");
            string name = GetStr(entry, "missionName", "Unknown Mission");
            string gtype = GetStr(entry, "gameplayType");
            string reason = GetStr(entry, "reason");
            string extra = !string.IsNullOrEmpty(gtype) ? $" ({gtype})" : "";
            if (!string.IsNullOrEmpty(reason)) extra += $" — {reason}";
            lines.Add($"`{timePart}` **{ev}**: {name}{extra}");
        }

        return MakeEmbed("Recent Mission Log (Last 10)", Truncate(string.Join("\n", lines), 4000), 0x0EA5E9);
    }

    private static JsonObject BuildDigestEmbed(JsonElement state)
    {
        TryGetObj(state, "echo", out var echo);
        TryGetObj(state, "current", out var current);
        TryGetObj(state, "deltas", out var deltas);

        string dur = FmtHours(GetDbl(state, "sessionDurationHours"));
        int newModsCount = 0;
        if (TryGetArr(deltas, "newModules", out var mods))
            foreach (var m in mods.EnumerateArray()) newModsCount += Math.Max(1, GetInt(m, "gained"));

        int readyBps = 0;
        if (TryGetArr(deltas, "newShipBlueprints", out var bps))
            foreach (var bp in bps.EnumerateArray())
                if (GetBool(bp, "canComplete") && !GetBool(bp, "alreadyUnlocked")) readyBps++;

        var fields = new JsonArray
        {
            MakeField("Credits", $"`{FmtSigned(GetLong(deltas, "credits"))}` ({FmtNum(GetLong(deltas, "creditsPerHour"))}/hr)", true),
            MakeField("Marks / Tags", $"VM `{FmtSigned(GetLong(deltas, "vanguardMarks"))}` | Tags `{FmtSigned(GetInt(deltas, "dogTags"))}`", true),
            MakeField("Levels", $"Cmd Lv {GetInt(current, "commanderLevel")} ({GetDbl(current, "commanderProgressPct"):0.0}%)\nEmp Lv {GetInt(current, "empireLevel")} ({GetDbl(current, "empireProgressPct"):0.0}%)", true),
            MakeField("Loot", $"Modules: **+{newModsCount}** | Crates: **+{GetInt(deltas, "lockedCratesTotal")}** | Ready BPs: **{readyBps}**", false)
        };

        if (TryGetObj(state, "empire", out var empire))
        {
            fields.Add(BuildEmpireSummaryField(empire));
        }

        return MakeEmbed(
            $"EchoMonitor Session Digest ({dur})",
            $"**ECHO:** {(GetBool(echo, "enabled") ? "ACTIVE" : "PAUSED")} (`{GetStr(echo, "state", "Unknown")}`) in **{GetStr(echo, "systemName", "Unknown")}**",
            0x3B82F6,
            fields);
    }

    private static JsonObject BuildEmpireSummaryField(JsonElement empire)
    {
        TryGetObj(empire, "activity", out var act);
        long refined = GetLong(act, "refinedTotal");
        long tradeProfit = GetLong(act, "tradeProfit");
        long produced = GetLong(act, "outpostProducedTotal");

        int rawTotal = GetInt(empire, "rawBacklogTotal");
        int rawDelta = GetInt(empire, "rawBacklogDelta");
        string rawTrend = rawDelta > 0 ? "GROWING" : (rawDelta < 0 ? "DRAINING" : "STEADY");

        int refTotal = GetInt(empire, "refinedStockTotal");
        int refDelta = GetInt(empire, "refinedStockDelta");

        int fullOutposts = 0;
        int totalOutposts = 0;
        if (TryGetArr(empire, "outposts", out var outposts))
        {
            foreach (var op in outposts.EnumerateArray())
            {
                totalOutposts++;
                if (GetDbl(op, "fillPct") >= 95.0) fullOutposts++;
            }
        }

        string opWarn = fullOutposts > 0 ? $" | ⚠️ **{fullOutposts}/{totalOutposts} outposts ≥95%**" : "";
        return MakeField(
            "Empire & Refining",
            $"Refined: **{FmtNum(refined)}** | Trade Profit: `{FmtSigned(tradeProfit)}` | Produced: **{FmtNum(produced)}**{opWarn}\n" +
            $"Raw Backlog: **{FmtNum(rawTotal)}** (`{FmtSigned(rawDelta)}`, {rawTrend}) | Refined Stock: **{FmtNum(refTotal)}** (`{FmtSigned(refDelta)}`)",
            false);
    }

    private static JsonObject BuildEmpireEmbed(JsonElement state)
    {
        if (!TryGetObj(state, "empire", out var empire))
            return MakeEmbed("Empire & Refining", "No empire telemetry available.", 0x38BDF8);

        TryGetObj(empire, "activity", out var act);
        var fields = new JsonArray
        {
            MakeField(
                "Session Activity",
                $"Refined: **{FmtNum(GetLong(act, "refinedTotal"))}** ore units\n" +
                $"Trade Profit: `{FmtSigned(GetLong(act, "tradeProfit"))}` ({GetInt(act, "tradeRuns")} runs) | Sales: **{FmtNum(GetLong(act, "stationSalesCredits"))}** cr\n" +
                $"Outpost Produced: **{FmtNum(GetLong(act, "outpostProducedTotal"))}** | Crafted: **{FmtNum(GetLong(act, "craftedTotal"))}**",
                false)
        };

        if (TryGetArr(empire, "outposts", out var outposts) && outposts.GetArrayLength() > 0)
        {
            var lines = new List<string>();
            int n = 0;
            foreach (var op in outposts.EnumerateArray())
            {
                if (n++ >= 8) break;
                double pct = GetDbl(op, "fillPct");
                string icon = pct >= 95.0 ? "🔴" : (pct >= 80.0 ? "🟠" : "🟢");
                string stack = GetInt(op, "largestStackCount") > 0
                    ? $" — top: {FmtNum(GetInt(op, "largestStackCount"))}x {GetStr(op, "largestStackItemName")}"
                    : "";
                lines.Add($"{icon} **{GetStr(op, "name")}** ({GetStr(op, "systemName")}): **{pct:0.0}%**{stack}");
            }
            fields.Add(MakeField($"Outpost Storage ({outposts.GetArrayLength()})", string.Join("\n", lines), false));
        }

        int rawTotal = GetInt(empire, "rawBacklogTotal");
        int rawDelta = GetInt(empire, "rawBacklogDelta");
        double rawRate = GetDbl(empire, "rawBacklogRatePerHour");
        string rawTrend = rawDelta > 0 ? "GROWING" : (rawDelta < 0 ? "DRAINING" : "STEADY");
        var rawLines = new List<string> { $"Total: **{FmtNum(rawTotal)}** (`{FmtSigned(rawDelta)}`, {rawRate:+0.0;-0.0;0.0}/hr — **{rawTrend}**)" };
        if (TryGetArr(empire, "rawBacklogItems", out var rawItems))
        {
            int n = 0;
            foreach (var it in rawItems.EnumerateArray())
            {
                if (n++ >= 5) break;
                rawLines.Add($"• {GetStr(it, "name")}: **{FmtNum(GetInt(it, "current"))}** (`{FmtSigned(GetInt(it, "delta"))}`)");
            }
        }
        fields.Add(MakeField("Depot Raw / Salvage Backlog", string.Join("\n", rawLines), true));

        int refTotal = GetInt(empire, "refinedStockTotal");
        int refDelta = GetInt(empire, "refinedStockDelta");
        double refRate = GetDbl(empire, "refinedStockRatePerHour");
        var refLines = new List<string> { $"Total: **{FmtNum(refTotal)}** (`{FmtSigned(refDelta)}`, {refRate:+0.0;-0.0;0.0}/hr)" };
        if (TryGetArr(empire, "refinedStockItems", out var refItems))
        {
            int n = 0;
            foreach (var it in refItems.EnumerateArray())
            {
                if (n++ >= 5) break;
                refLines.Add($"• {GetStr(it, "name")}: **{FmtNum(GetInt(it, "current"))}** (`{FmtSigned(GetInt(it, "delta"))}`)");
            }
        }
        fields.Add(MakeField("Depot Refined Products", string.Join("\n", refLines), true));

        string depotDesc = GetInt(empire, "depotCount") > 0
            ? $"Active Depots: **{GetInt(empire, "depotCount")}** — **{GetDbl(empire, "depotFillPct"):0.1}%** full ({FmtNum((long)GetDbl(empire, "depotSpaceUsed"))} / {FmtNum((long)GetDbl(empire, "depotCapacity"))} m³)"
            : "No active supply depot configured.";

        return MakeEmbed("Empire & Refining Overview", depotDesc, 0x38BDF8, fields);
    }

    private static JsonObject MakeEmbed(string title, string? description, int color, JsonArray? fields = null)
    {
        var embed = new JsonObject
        {
            ["title"] = title,
            ["color"] = color,
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrEmpty(description))
            embed["description"] = description;
        if (fields != null && fields.Count > 0)
            embed["fields"] = fields;
        return embed;
    }

    private static JsonObject MakeField(string name, string value, bool inline) => new()
    {
        ["name"] = name,
        ["value"] = string.IsNullOrEmpty(value) ? "-" : value,
        ["inline"] = inline
    };

    // --- JSON & Formatting Helpers ---

    private static bool TryGetObj(JsonElement el, string prop, out JsonElement obj)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out obj) && obj.ValueKind == JsonValueKind.Object)
            return true;
        obj = default;
        return false;
    }

    private static bool TryGetArr(JsonElement el, string prop, out JsonElement arr)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out arr) && arr.ValueKind == JsonValueKind.Array)
            return true;
        arr = default;
        return false;
    }

    private static string GetStr(JsonElement el, string prop, string fallback = "")
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? fallback;
        return fallback;
    }

    private static bool GetBool(JsonElement el, string prop)
    {
        return el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;
    }

    private static int GetInt(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n))
            return n;
        return 0;
    }

    private static long GetLong(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n))
            return n;
        return 0;
    }

    private static double GetDbl(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d))
            return d;
        return 0.0;
    }

    private static string FmtNum(long n) => n.ToString("N0", CultureInfo.InvariantCulture);
    private static string FmtSigned(long n) => n >= 0 ? $"+{n:N0}" : $"{n:N0}";

    private static string FmtPct(double cur, double max)
    {
        if (max <= 0) return "N/A";
        double pct = Math.Clamp((cur / max) * 100.0, 0.0, 100.0);
        return $"{pct:0}%";
    }

    private static string FmtHours(double hours)
    {
        int totalMins = (int)(Math.Max(0, hours) * 60.0);
        int h = totalMins / 60;
        int m = totalMins % 60;
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }

    private static string Truncate(string s, int maxLen) =>
        string.IsNullOrEmpty(s) || s.Length <= maxLen ? s : s[..maxLen];

    private static string StripRichText(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return Regex.Replace(s, "<[^>]+>", string.Empty).Trim();
    }

    private static (string Name, string Rarity, int ItemLevel, string StatsSig) BuildModuleKey(JsonElement mod)
    {
        string name = GetStr(mod, "name");
        string rarity = GetStr(mod, "rarity");
        int ilvl = GetInt(mod, "itemLevel");

        var parts = new List<string>();
        if (TryGetArr(mod, "substats", out var subs))
        {
            foreach (var s in subs.EnumerateArray())
            {
                if (s.ValueKind == JsonValueKind.String)
                    parts.Add("S:" + (s.GetString() ?? string.Empty));
            }
        }
        if (TryGetArr(mod, "aspects", out var aspects))
        {
            foreach (var a in aspects.EnumerateArray())
            {
                parts.Add("A:" + GetStr(a, "name") + ":" + GetStr(a, "description"));
            }
        }

        return (name, rarity, ilvl, string.Join("|", parts));
    }
}
