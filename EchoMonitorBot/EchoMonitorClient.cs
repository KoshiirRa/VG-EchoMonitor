using System.Net.Http.Headers;
using System.Text.Json;

namespace EchoMonitorBot;

public sealed record ActionResult(bool Ok, string Message, JsonElement? RawData);

public sealed class EchoMonitorClient : IDisposable
{
    private readonly BotConfig _config;
    private readonly HttpClient _restClient;
    private readonly HttpClient _sseClient;

    public EchoMonitorClient(BotConfig config)
    {
        _config = config;

        _restClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _sseClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        if (!string.IsNullOrEmpty(config.EchoMonitorToken))
        {
            var auth = new AuthenticationHeaderValue("Bearer", config.EchoMonitorToken);
            _restClient.DefaultRequestHeaders.Authorization = auth;
            _sseClient.DefaultRequestHeaders.Authorization = auth;
        }
    }

    public async Task<JsonDocument?> GetStateAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _restClient.GetAsync($"{_config.EchoMonitorUrl}/api/state", ct);
            if (!resp.IsSuccessStatusCode) return null;
            string json = await resp.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<JsonDocument?> GetDepotsAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _restClient.GetAsync($"{_config.EchoMonitorUrl}/api/depots", ct);
            if (!resp.IsSuccessStatusCode) return null;
            string json = await resp.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ActionResult> PostActionAsync(string path, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _restClient.PostAsync($"{_config.EchoMonitorUrl}{path}", null, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            JsonElement? root = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var doc = JsonDocument.Parse(body);
                    root = doc.RootElement.Clone();
                }
            }
            catch
            {
                // non-JSON response
            }

            if (!resp.IsSuccessStatusCode)
            {
                string err = GetString(root, "error") ?? GetString(root, "message") ?? $"HTTP {(int)resp.StatusCode}";
                return new ActionResult(false, err, root);
            }

            bool ok = root.HasValue && root.Value.TryGetProperty("ok", out var okProp) && okProp.ValueKind == JsonValueKind.False
                ? false
                : true;
            string msg = GetString(root, "message") ?? (ok ? "Action succeeded." : "Action failed.");
            return new ActionResult(ok, msg, root);
        }
        catch (Exception ex)
        {
            return new ActionResult(false, $"Request failed: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Connects to /api/events and streams frames until disconnected or cancelled.
    /// Returns true if the server sent a clean-exit `event: shutdown` frame before closing.
    /// </summary>
    public async Task<(bool CleanShutdown, string DisconnectReason)> StreamEventsAsync(
        Func<Task> onConnected,
        Func<JsonDocument, Task> onState,
        Func<string, Task> onCleanShutdown,
        CancellationToken ct,
        Func<string, JsonDocument, Task>? onCustomEvent = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_config.EchoMonitorUrl}/api/events");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var resp = await _sseClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            return (false, $"SSE HTTP {(int)resp.StatusCode}");

        await onConnected();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? currentEvent = null;
        bool cleanShutdown = false;

        while (!ct.IsCancellationRequested)
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromSeconds(30));

            string? line;
            try
            {
                line = await reader.ReadLineAsync(readCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (cleanShutdown, "SSE read timeout (30s with no data)");
            }

            if (line == null)
                break;

            if (line.Length == 0)
            {
                currentEvent = null;
                continue;
            }

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent = line[6..].Trim();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                string payload = line[5..].Trim();
                if (string.IsNullOrEmpty(payload))
                    continue;

                if (string.Equals(currentEvent, "shutdown", StringComparison.OrdinalIgnoreCase))
                {
                    cleanShutdown = true;
                    string reason = "clean_exit";
                    try
                    {
                        using var doc = JsonDocument.Parse(payload);
                        reason = GetString(doc.RootElement, "reason") ?? "clean_exit";
                    }
                    catch { }
                    await onCleanShutdown(reason);
                    return (true, reason);
                }

                try
                {
                    var doc = JsonDocument.Parse(payload);
                    if (!string.IsNullOrEmpty(currentEvent))
                    {
                        if (onCustomEvent != null)
                            await onCustomEvent(currentEvent, doc);
                        else
                            doc.Dispose();
                    }
                    else
                    {
                        await onState(doc);
                    }
                }
                catch
                {
                    // ignore malformed JSON frame
                }
            }
        }

        return (cleanShutdown, cleanShutdown ? "clean_exit" : "SSE stream closed by server");
    }

    private static string? GetString(JsonElement? el, string prop)
    {
        if (el.HasValue && el.Value.ValueKind == JsonValueKind.Object && el.Value.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    public void Dispose()
    {
        _restClient.Dispose();
        _sseClient.Dispose();
    }
}
