using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EchoMonitor
{
    public sealed class HttpServer : IDisposable
    {
        private const string EmbeddedUiResourceName = "EchoMonitor.ui.index.html";

        private readonly EchoConfig _config;
        private readonly Func<string> _getStateJson;
        private readonly Func<string> _getHistoryJson;
        private readonly Func<string> _getMissionLogJson;
        private readonly Func<string> _getDepotsJson;
        private readonly Func<string> _resetSessionJson;
        private readonly Func<string> _warpSafeStationJson;
        private readonly Func<string> _resetEchoJson;
        private readonly Func<string> _toggleEchoJson;
        private readonly Func<string> _abandonMissionJson;
        private readonly Func<string> _warpDefendJson;
        private readonly Func<string> _dismissAlertsJson;
        private readonly Func<string, string> _relieveOutpostJson;

        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptLoopTask;
        private byte[] _cachedUiBytes;
        private readonly ConcurrentDictionary<Guid, Stream> _sseClients = new ConcurrentDictionary<Guid, Stream>();

        public HttpServer(
            EchoConfig config,
            Func<string> getStateJson,
            Func<string> getHistoryJson,
            Func<string> getMissionLogJson,
            Func<string> getDepotsJson,
            Func<string> resetSessionJson,
            Func<string> warpSafeStationJson,
            Func<string> resetEchoJson,
            Func<string> toggleEchoJson,
            Func<string> abandonMissionJson,
            Func<string> warpDefendJson,
            Func<string> dismissAlertsJson,
            Func<string, string> relieveOutpostJson)
        {
            _config = config;
            _getStateJson = getStateJson;
            _getHistoryJson = getHistoryJson;
            _getMissionLogJson = getMissionLogJson;
            _getDepotsJson = getDepotsJson;
            _resetSessionJson = resetSessionJson;
            _warpSafeStationJson = warpSafeStationJson;
            _resetEchoJson = resetEchoJson;
            _toggleEchoJson = toggleEchoJson;
            _abandonMissionJson = abandonMissionJson;
            _warpDefendJson = warpDefendJson;
            _dismissAlertsJson = dismissAlertsJson;
            _relieveOutpostJson = relieveOutpostJson;
        }

        public string Prefix { get; private set; }

        public void Start()
        {
            LoadEmbeddedUi();
            Prefix = _config.GetListenerPrefix();
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add(Prefix);
            _listener.Start();

            Plugin.Log?.LogInfo($"EchoMonitor HTTP server listening on {Prefix} (RequireAuth={_config.RequireAuth.Value})");
            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            BroadcastShutdown();
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
        }

        private void BroadcastShutdown()
        {
            if (_sseClients.IsEmpty) return;
            byte[] payload = Encoding.UTF8.GetBytes("event: shutdown\ndata: {\"reason\":\"clean_exit\"}\n\n");
            foreach (var kv in _sseClients)
            {
                try
                {
                    kv.Value.Write(payload, 0, payload.Length);
                    kv.Value.Flush();
                }
                catch { }
            }
            _sseClients.Clear();
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        private void LoadEmbeddedUi()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(EmbeddedUiResourceName);
                if (stream != null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    _cachedUiBytes = ms.ToArray();
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"Failed to load embedded UI resource: {ex.Message}");
            }

            _cachedUiBytes = Encoding.UTF8.GetBytes(
                "<!doctype html><html><body style='background:#0b101b;color:#e2e8f0;font-family:sans-serif;padding:2rem'>" +
                "<h1>EchoMonitor</h1><p>Embedded UI resource not found.</p></body></html>");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                HttpListenerContext ctx = null;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    if (ct.IsCancellationRequested) break;
                    continue;
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogError($"HttpListener accept error: {ex.Message}");
                    continue;
                }

                if (ctx != null)
                {
                    _ = Task.Run(() => HandleRequestAsync(ctx, ct));
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
        {
            var req = ctx.Request;
            var resp = ctx.Response;
            try
            {
                string path = (req.Url?.AbsolutePath ?? "/").TrimEnd('/');
                if (string.IsNullOrEmpty(path)) path = "/";

                // CORS / proxy-friendly headers
                resp.Headers["X-Content-Type-Options"] = "nosniff";

                if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    resp.StatusCode = 204;
                    resp.Close();
                    return;
                }

                if (_config.RequireAuth.Value && !IsAuthorized(req))
                {
                    WriteJson(resp, 401, "{\"error\":\"Unauthorized\"}");
                    return;
                }

                if (req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    if (path == "/" || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
                    {
                        resp.StatusCode = 200;
                        resp.ContentType = "text/html; charset=utf-8";
                        resp.Headers["Cache-Control"] = "no-cache";
                        resp.ContentLength64 = _cachedUiBytes.Length;
                        await resp.OutputStream.WriteAsync(_cachedUiBytes, 0, _cachedUiBytes.Length, ct).ConfigureAwait(false);
                        resp.Close();
                        return;
                    }

                    if (path.Equals("/api/state", StringComparison.OrdinalIgnoreCase))
                    {
                        string json = _getStateJson();
                        WriteJson(resp, 200, json);
                        return;
                    }

                    if (path.Equals("/api/history", StringComparison.OrdinalIgnoreCase))
                    {
                        string json = _getHistoryJson();
                        WriteJson(resp, 200, json);
                        return;
                    }

                    if (path.Equals("/api/missions/log", StringComparison.OrdinalIgnoreCase))
                    {
                        string json = _getMissionLogJson();
                        WriteJson(resp, 200, json);
                        return;
                    }

                    if (path.Equals("/api/depots", StringComparison.OrdinalIgnoreCase))
                    {
                        string json = _getDepotsJson();
                        WriteJson(resp, 200, json);
                        return;
                    }

                    if (path.Equals("/api/events", StringComparison.OrdinalIgnoreCase))
                    {
                        await HandleSseAsync(resp, ct).ConfigureAwait(false);
                        return;
                    }
                }
                else if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    if (path.Equals("/api/session/reset", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(resp, 200, _resetSessionJson());
                        return;
                    }
                    if (path.Equals("/api/recovery/warp-safe-station", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(resp, 200, _warpSafeStationJson());
                        return;
                    }
                    if (path.Equals("/api/recovery/reset-echo", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(resp, 200, _resetEchoJson());
                        return;
                    }
                    if (path.Equals("/api/recovery/toggle-echo", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(resp, 200, _toggleEchoJson());
                        return;
                    }
                    if (path.Equals("/api/recovery/abandon-mission", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(resp, 200, _abandonMissionJson());
                        return;
                    }
                    if (path.Equals("/api/recovery/warp-defend", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(resp, 200, _warpDefendJson());
                        return;
                    }
                    if (path.Equals("/api/alerts/dismiss", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteJson(resp, 200, _dismissAlertsJson());
                        return;
                    }
                    if (path.Equals("/api/action/relieve-outpost", StringComparison.OrdinalIgnoreCase))
                    {
                        string stationGuid = req.QueryString["stationGuid"] ?? string.Empty;
                        if (string.IsNullOrEmpty(stationGuid) && req.HasEntityBody)
                        {
                            try
                            {
                                using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
                                string body = await reader.ReadToEndAsync().ConfigureAwait(false);
                                if (JsonUtil.Parse(body) is System.Collections.Generic.Dictionary<string, object> obj &&
                                    obj.TryGetValue("stationGuid", out var val) && val != null)
                                {
                                    stationGuid = val.ToString();
                                }
                            }
                            catch { }
                        }
                        WriteJson(resp, 200, _relieveOutpostJson(stationGuid));
                        return;
                    }
                }

                WriteJson(resp, 404, "{\"error\":\"Not Found\"}");
            }
            catch (Exception ex)
            {
                try
                {
                    WriteJson(resp, 500, "{\"error\":" + QuoteError(ex.Message) + "}");
                }
                catch { }
            }
        }

        private async Task HandleSseAsync(HttpListenerResponse resp, CancellationToken ct)
        {
            resp.StatusCode = 200;
            resp.ContentType = "text/event-stream; charset=utf-8";
            resp.Headers["Cache-Control"] = "no-cache";
            resp.Headers["Connection"] = "keep-alive";
            resp.Headers["X-Accel-Buffering"] = "no";
            resp.SendChunked = true;

            var output = resp.OutputStream;
            var clientId = Guid.NewGuid();
            _sseClients[clientId] = output;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string json = _getStateJson();
                    byte[] payload = Encoding.UTF8.GetBytes($"data: {json}\n\n");
                    await output.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
                    await output.FlushAsync(ct).ConfigureAwait(false);
                    await Task.Delay(2000, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Server shutting down
            }
            catch (IOException)
            {
                // Client disconnected
            }
            catch (HttpListenerException)
            {
                // Client disconnected
            }
            finally
            {
                _sseClients.TryRemove(clientId, out _);
                try { resp.Close(); } catch { }
            }
        }

        private bool IsAuthorized(HttpListenerRequest req)
        {
            string expected = _config.AuthToken.Value;
            if (string.IsNullOrEmpty(expected)) return true;

            string authHeader = req.Headers["Authorization"];
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                string provided = authHeader.Substring(7).Trim();
                if (string.Equals(provided, expected, StringComparison.Ordinal))
                    return true;
            }

            string queryToken = req.QueryString["token"];
            if (!string.IsNullOrEmpty(queryToken) && string.Equals(queryToken, expected, StringComparison.Ordinal))
                return true;

            return false;
        }

        private static void WriteJson(HttpListenerResponse resp, int statusCode, string json)
        {
            byte[] data = Encoding.UTF8.GetBytes(json ?? "{}");
            resp.StatusCode = statusCode;
            resp.ContentType = "application/json; charset=utf-8";
            resp.Headers["Cache-Control"] = "no-cache";
            resp.ContentLength64 = data.Length;
            resp.OutputStream.Write(data, 0, data.Length);
            resp.Close();
        }

        private static string QuoteError(string msg)
        {
            var sb = new StringBuilder();
            JsonUtil.AppendString(sb, msg ?? "Internal server error");
            return sb.ToString();
        }
    }
}
