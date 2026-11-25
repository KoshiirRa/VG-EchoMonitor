using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EchoMonitorBot;

public sealed record DiscordInteraction(
    string Id,
    string Token,
    int Type,
    string? CommandName,
    string? CustomId,
    string? UserId,
    IReadOnlyDictionary<string, string> Options);

public sealed class DiscordGateway : IDisposable
{
    private const string ApiBase = "https://discord.com/api/v10";

    private readonly BotConfig _config;
    private readonly HttpClient _http;

    private string? _applicationId;
    private string? _sessionId;
    private string? _resumeGatewayUrl;
    private int? _lastSequence;

    public event Func<DiscordInteraction, Task>? OnInteraction;

    public DiscordGateway(BotConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", config.DiscordBotToken);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("EchoMonitorBot/1.0 (+https://github.com)");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        double backoffSeconds = 2.0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                string gatewayUrl = _resumeGatewayUrl ?? await FetchGatewayUrlAsync(ct);
                if (!gatewayUrl.Contains("?v=", StringComparison.Ordinal))
                    gatewayUrl = gatewayUrl.TrimEnd('/') + "/?v=10&encoding=json";

                await ConnectAndPumpAsync(gatewayUrl, ct);
                backoffSeconds = 2.0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Discord] Gateway error: {ex.Message} (reconnecting in {backoffSeconds:0.0}s)");
                await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);
                backoffSeconds = Math.Min(30.0, backoffSeconds * 1.5);
            }
        }
    }

    private async Task<string> FetchGatewayUrlAsync(CancellationToken ct)
    {
        using var resp = await _http.GetAsync($"{ApiBase}/gateway/bot", ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"GET /gateway/bot failed ({(int)resp.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("url").GetString() ?? "wss://gateway.discord.gg";
    }

    private async Task ConnectAndPumpAsync(string gatewayUrl, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(gatewayUrl), ct);
        Console.WriteLine("[Discord] WebSocket connected.");

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task? heartbeatTask = null;

        try
        {
            var buffer = new byte[16384];
            using var ms = new MemoryStream();

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine($"[Discord] Gateway closed: {result.CloseStatus} {result.CloseStatusDescription}");
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                string json = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                int op = root.GetProperty("op").GetInt32();
                if (root.TryGetProperty("s", out var seqEl) && seqEl.ValueKind == JsonValueKind.Number)
                    _lastSequence = seqEl.GetInt32();

                switch (op)
                {
                    case 10: // Hello
                    {
                        int intervalMs = root.GetProperty("d").GetProperty("heartbeat_interval").GetInt32();
                        heartbeatTask = RunHeartbeatAsync(ws, intervalMs, heartbeatCts.Token);

                        if (!string.IsNullOrEmpty(_sessionId) && _lastSequence.HasValue)
                            await SendResumeAsync(ws, ct);
                        else
                            await SendIdentifyAsync(ws, ct);
                        break;
                    }
                    case 1: // Heartbeat request
                        await SendHeartbeatAsync(ws, ct);
                        break;
                    case 7: // Reconnect
                        Console.WriteLine("[Discord] Server requested reconnect.");
                        return;
                    case 9: // Invalid Session
                    {
                        bool resumable = root.TryGetProperty("d", out var dEl) && dEl.ValueKind == JsonValueKind.True;
                        if (!resumable)
                        {
                            _sessionId = null;
                            _resumeGatewayUrl = null;
                            _lastSequence = null;
                        }
                        Console.WriteLine($"[Discord] Invalid session (resumable={resumable}).");
                        await Task.Delay(2000, ct);
                        return;
                    }
                    case 0: // Dispatch
                        await HandleDispatchAsync(root, ct);
                        break;
                }
            }
        }
        finally
        {
            heartbeatCts.Cancel();
            if (heartbeatTask != null)
            {
                try { await heartbeatTask; } catch { }
            }

            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                try
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", closeCts.Token);
                }
                catch { }
            }
        }
    }

    private async Task HandleDispatchAsync(JsonElement root, CancellationToken ct)
    {
        string? eventName = root.TryGetProperty("t", out var tEl) && tEl.ValueKind == JsonValueKind.String
            ? tEl.GetString()
            : null;
        if (!root.TryGetProperty("d", out var data))
            return;

        if (eventName == "READY")
        {
            _sessionId = data.TryGetProperty("session_id", out var sid) ? sid.GetString() : null;
            _resumeGatewayUrl = data.TryGetProperty("resume_gateway_url", out var rurl) ? rurl.GetString() : null;
            if (data.TryGetProperty("application", out var appEl) && appEl.TryGetProperty("id", out var appId))
                _applicationId = appId.GetString();

            string username = data.TryGetProperty("user", out var u) && u.TryGetProperty("username", out var un)
                ? un.GetString() ?? "bot"
                : "bot";
            Console.WriteLine($"[Discord] READY as {username} (appId={_applicationId}).");

            if (!string.IsNullOrEmpty(_applicationId))
                _ = Task.Run(() => RegisterSlashCommandsAsync(_applicationId, ct), ct);
        }
        else if (eventName == "INTERACTION_CREATE")
        {
            string id = data.GetProperty("id").GetString() ?? string.Empty;
            string token = data.GetProperty("token").GetString() ?? string.Empty;
            int type = data.GetProperty("type").GetInt32();

            string? cmdName = null;
            string? customId = null;
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (data.TryGetProperty("data", out var idata) && idata.ValueKind == JsonValueKind.Object)
            {
                if (idata.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String)
                    cmdName = nEl.GetString();
                if (idata.TryGetProperty("custom_id", out var cEl) && cEl.ValueKind == JsonValueKind.String)
                    customId = cEl.GetString();
                if (idata.TryGetProperty("options", out var optsEl) && optsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var opt in optsEl.EnumerateArray())
                    {
                        if (opt.TryGetProperty("name", out var on) && on.ValueKind == JsonValueKind.String &&
                            opt.TryGetProperty("value", out var ov))
                        {
                            string? key = on.GetString();
                            string val = ov.ValueKind == JsonValueKind.String ? (ov.GetString() ?? string.Empty) : ov.ToString();
                            if (!string.IsNullOrEmpty(key))
                                options[key] = val;
                        }
                    }
                }
            }

            string? userId = null;
            if (data.TryGetProperty("member", out var mem) && mem.TryGetProperty("user", out var mu) && mu.TryGetProperty("id", out var muid))
                userId = muid.GetString();
            else if (data.TryGetProperty("user", out var du) && du.TryGetProperty("id", out var duid))
                userId = duid.GetString();

            var interaction = new DiscordInteraction(id, token, type, cmdName, customId, userId, options);
            if (OnInteraction != null)
            {
                _ = Task.Run(async () =>
                {
                    try { await OnInteraction.Invoke(interaction); }
                    catch (Exception ex) { Console.WriteLine($"[Discord] Interaction handler error: {ex.Message}"); }
                }, ct);
            }
        }
    }

    private async Task RunHeartbeatAsync(ClientWebSocket ws, int intervalMs, CancellationToken ct)
    {
        try
        {
            await Task.Delay(Random.Shared.Next(500, Math.Max(1000, intervalMs / 2)), ct);
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                await SendHeartbeatAsync(ws, ct);
                await Task.Delay(intervalMs, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private Task SendHeartbeatAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var payload = new JsonObject
        {
            ["op"] = 1,
            ["d"] = _lastSequence.HasValue ? JsonValue.Create(_lastSequence.Value) : null
        };
        return SendWsJsonAsync(ws, payload, ct);
    }

    private Task SendIdentifyAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var payload = new JsonObject
        {
            ["op"] = 2,
            ["d"] = new JsonObject
            {
                ["token"] = _config.DiscordBotToken,
                ["intents"] = 1, // GUILDS (sufficient for slash commands + button interactions)
                ["properties"] = new JsonObject
                {
                    ["os"] = Environment.OSVersion.Platform.ToString(),
                    ["browser"] = "EchoMonitorBot",
                    ["device"] = "EchoMonitorBot"
                }
            }
        };
        return SendWsJsonAsync(ws, payload, ct);
    }

    private Task SendResumeAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var payload = new JsonObject
        {
            ["op"] = 6,
            ["d"] = new JsonObject
            {
                ["token"] = _config.DiscordBotToken,
                ["session_id"] = _sessionId,
                ["seq"] = _lastSequence
            }
        };
        return SendWsJsonAsync(ws, payload, ct);
    }

    private static async Task SendWsJsonAsync(ClientWebSocket ws, JsonObject obj, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(obj.ToJsonString());
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    public async Task RegisterSlashCommandsAsync(string appId, CancellationToken ct)
    {
        var commands = new JsonArray
        {
            MakeCommand("status", "Show current EchoMonitor status, vitals, and session gains."),
            MakeCommand("fleet", "Show active fleet ships, vitals, stance, and drone status."),
            MakeCommand("loot", "Show session modules by rarity, locked crates, and ready ship BPs."),
            MakeCommand("missions", "Show the last 10 entries from the EchoMonitor mission log."),
            MakeCommand("empire", "Show outpost storage, refining output, depot backlog, and refined products."),
            MakeDepotCommand(),
            MakeCommand("recover", "Open an ephemeral recovery control panel."),
            MakeCommand("reset_session", "Reset the EchoMonitor session baseline (allowed users only).")
        };

        using var content = new StringContent(commands.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await _http.PutAsync($"{ApiBase}/applications/{appId}/commands", content, ct);
        if (resp.IsSuccessStatusCode)
            Console.WriteLine("[Discord] Global slash commands registered.");
        else
            Console.WriteLine($"[Discord] Failed to register slash commands ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync(ct)}");
    }

    private static JsonObject MakeCommand(string name, string description) => new()
    {
        ["name"] = name,
        ["type"] = 1,
        ["description"] = description
    };

    private static JsonObject MakeDepotCommand() => new()
    {
        ["name"] = "depot",
        ["type"] = 1,
        ["description"] = "View station/depot inventory by location, with optional raw/refined/search filters.",
        ["options"] = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "location",
                ["description"] = "Station, system, or sector name (omit to list all stations with storage)",
                ["type"] = 3,
                ["required"] = false
            },
            new JsonObject
            {
                ["name"] = "filter",
                ["description"] = "Filter by item category (raw includes ores, crystals & salvage)",
                ["type"] = 3,
                ["required"] = false,
                ["choices"] = new JsonArray
                {
                    new JsonObject { ["name"] = "All", ["value"] = "all" },
                    new JsonObject { ["name"] = "Raw (Ores, Crystals & Salvage)", ["value"] = "raw" },
                    new JsonObject { ["name"] = "Refined Products", ["value"] = "refined" },
                    new JsonObject { ["name"] = "Other", ["value"] = "other" }
                }
            },
            new JsonObject
            {
                ["name"] = "search",
                ["description"] = "Search for a specific item name across stations",
                ["type"] = 3,
                ["required"] = false
            },
            new JsonObject
            {
                ["name"] = "sort",
                ["description"] = "Sort order for items",
                ["type"] = 3,
                ["required"] = false,
                ["choices"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Quantity (High to Low)", ["value"] = "count" },
                    new JsonObject { ["name"] = "Volume m³ (High to Low)", ["value"] = "volume" },
                    new JsonObject { ["name"] = "Name (A-Z)", ["value"] = "name" }
                }
            }
        }
    };

    public async Task PostChannelMessageAsync(JsonObject embed, JsonArray? components = null, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["embeds"] = new JsonArray { embed }
        };
        if (components != null)
            body["components"] = components;

        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{ApiBase}/channels/{_config.DiscordChannelId}/messages", content, ct);
        if (!resp.IsSuccessStatusCode)
            Console.WriteLine($"[Discord] Post message failed ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync(ct)}");
    }

    public async Task ReplyInteractionAsync(
        DiscordInteraction interaction,
        string? text = null,
        JsonObject? embed = null,
        JsonArray? components = null,
        bool ephemeral = false,
        CancellationToken ct = default)
    {
        var data = new JsonObject();
        if (!string.IsNullOrEmpty(text))
            data["content"] = text;
        if (embed != null)
            data["embeds"] = new JsonArray { embed };
        if (components != null)
            data["components"] = components;
        if (ephemeral)
            data["flags"] = 64; // EPHEMERAL

        var payload = new JsonObject
        {
            ["type"] = 4, // CHANNEL_MESSAGE_WITH_SOURCE
            ["data"] = data
        };

        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"{ApiBase}/interactions/{interaction.Id}/{interaction.Token}/callback", content, ct);
        if (!resp.IsSuccessStatusCode)
            Console.WriteLine($"[Discord] Reply interaction failed ({(int)resp.StatusCode}): {await resp.Content.ReadAsStringAsync(ct)}");
    }

    public async Task DeferInteractionAsync(DiscordInteraction interaction, bool ephemeral = true, CancellationToken ct = default)
    {
        var data = new JsonObject();
        if (ephemeral)
            data["flags"] = 64;

        var payload = new JsonObject
        {
            ["type"] = 5, // DEFERRED_CHANNEL_MESSAGE_WITH_SOURCE
            ["data"] = data
        };

        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        await _http.PostAsync($"{ApiBase}/interactions/{interaction.Id}/{interaction.Token}/callback", content, ct);
    }

    public async Task EditDeferredReplyAsync(DiscordInteraction interaction, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_applicationId)) return;
        var payload = new JsonObject { ["content"] = text };
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Patch, $"{ApiBase}/webhooks/{_applicationId}/{interaction.Token}/messages/@original")
        {
            Content = content
        };
        await _http.SendAsync(req, ct);
    }

    public static JsonObject MakeButton(string label, string customId, int style) => new()
    {
        ["type"] = 2, // BUTTON
        ["style"] = style, // 1=Primary, 2=Secondary, 3=Success, 4=Danger
        ["label"] = label,
        ["custom_id"] = customId
    };

    public static JsonObject MakeActionRow(params JsonObject[] buttons)
    {
        var arr = new JsonArray();
        foreach (var b in buttons) arr.Add(b);
        return new JsonObject
        {
            ["type"] = 1, // ACTION_ROW
            ["components"] = arr
        };
    }

    public void Dispose() => _http.Dispose();
}
