using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using LightJson;

namespace EchoEnhanced.AiSearch
{
    public sealed class AiSearchChunk
    {
        public string Filename { get; set; } = string.Empty;
        public double Score { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public sealed class AiSearchResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public string ResponseText { get; set; } = string.Empty;
        public List<AiSearchChunk> Chunks { get; set; } = new List<AiSearchChunk>();
    }

    public sealed class CloudflareAiSearchClient
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        private readonly string _configPath;
        private readonly string _examplePath;

        public string ConfigPath => _configPath;

        public CloudflareAiSearchClient(string bepInExConfigPath)
        {
            string dir = Path.Combine(bepInExConfigPath, "EchoEnhanced");
            Directory.CreateDirectory(dir);
            _configPath = Path.Combine(dir, "cloudflare.json");
            _examplePath = Path.Combine(dir, "cloudflare.example.json");
            EnsureExampleFile();
        }

        private void EnsureExampleFile()
        {
            try
            {
                if (!File.Exists(_examplePath))
                {
                    string example = "{\n"
                        + "  \"accountId\": \"5c214edff77bbcf4dee0445f5e204066\",\n"
                        + "  \"apiToken\": \"PASTE_YOUR_CLOUDFLARE_API_TOKEN_HERE\",\n"
                        + "  \"instanceId\": \"vg-codebase-private\"\n"
                        + "}\n";
                    File.WriteAllText(_examplePath, example);
                }
            }
            catch
            {
                // Non-fatal
            }
        }

        public bool IsConfigured(out string accountId, out string apiToken, out string instanceId, out string error)
        {
            accountId = Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID") ?? string.Empty;
            apiToken = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN") ?? string.Empty;
            instanceId = Environment.GetEnvironmentVariable("CLOUDFLARE_AI_SEARCH_INSTANCE") ?? "vg-codebase-private";
            error = string.Empty;

            if (File.Exists(_configPath))
            {
                try
                {
                    string raw = File.ReadAllText(_configPath);
                    JsonValue root = JsonValue.Parse(raw);
                    if (root.IsJsonObject)
                    {
                        var obj = root.AsJsonObject;
                        if (obj["accountId"].IsString && !string.IsNullOrWhiteSpace(obj["accountId"].AsString))
                            accountId = obj["accountId"].AsString.Trim();
                        if (obj["apiToken"].IsString && !string.IsNullOrWhiteSpace(obj["apiToken"].AsString))
                            apiToken = obj["apiToken"].AsString.Trim();
                        if (obj["instanceId"].IsString && !string.IsNullOrWhiteSpace(obj["instanceId"].AsString))
                            instanceId = obj["instanceId"].AsString.Trim();
                    }
                }
                catch (Exception ex)
                {
                    error = $"Failed to parse {_configPath}: {ex.Message}";
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(apiToken))
            {
                error = $"Cloudflare AI Search not configured. Copy {_examplePath} to {_configPath} and set accountId + apiToken.";
                return false;
            }

            return true;
        }

        public AiSearchResult Search(string query, int maxResults = 5)
        {
            if (!IsConfigured(out string accountId, out string apiToken, out string instanceId, out string cfgErr))
            {
                return new AiSearchResult { Ok = false, Error = cfgErr };
            }

            string url = $"https://api.cloudflare.com/client/v4/accounts/{accountId}/ai-search/instances/{instanceId}/search";
            var payload = new JsonObject
            {
                { "messages", new JsonArray { new JsonObject { { "role", "user" }, { "content", query } } } },
                { "ai_search_options", new JsonObject { { "retrieval", new JsonObject { { "max_num_results", Math.Max(1, Math.Min(20, maxResults)) } } } } }
            };

            return SendRequest(url, apiToken, payload);
        }

        public AiSearchResult AiSearch(string systemPrompt, string userPrompt, int maxResults = 6)
        {
            if (!IsConfigured(out string accountId, out string apiToken, out string instanceId, out string cfgErr))
            {
                return new AiSearchResult { Ok = false, Error = cfgErr };
            }

            string url = $"https://api.cloudflare.com/client/v4/accounts/{accountId}/ai-search/instances/{instanceId}/ai-search";
            var messages = new JsonArray();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new JsonObject { { "role", "system" }, { "content", systemPrompt } });
            }
            messages.Add(new JsonObject { { "role", "user" }, { "content", userPrompt } });

            var payload = new JsonObject
            {
                { "messages", messages },
                { "ai_search_options", new JsonObject { { "retrieval", new JsonObject { { "max_num_results", Math.Max(1, Math.Min(20, maxResults)) } } } } }
            };

            return SendRequest(url, apiToken, payload);
        }

        private static AiSearchResult SendRequest(string url, string apiToken, JsonObject payload)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
                req.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

                using var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!resp.IsSuccessStatusCode)
                {
                    return new AiSearchResult
                    {
                        Ok = false,
                        Error = $"Cloudflare AI Search HTTP {(int)resp.StatusCode}: {Truncate(body, 300)}"
                    };
                }

                JsonValue rootVal = JsonValue.Parse(body);
                if (!rootVal.IsJsonObject)
                {
                    return new AiSearchResult { Ok = false, Error = "Invalid JSON response from Cloudflare AI Search." };
                }

                var root = rootVal.AsJsonObject;
                if (root["success"].IsBoolean && !root["success"].AsBoolean)
                {
                    return new AiSearchResult { Ok = false, Error = $"Cloudflare API error: {Truncate(body, 300)}" };
                }

                var res = new AiSearchResult { Ok = true };
                if (root["result"].IsJsonObject)
                {
                    var resultObj = root["result"].AsJsonObject;
                    if (resultObj["response"].IsString)
                    {
                        res.ResponseText = resultObj["response"].AsString;
                    }

                    JsonArray chunksArr = null;
                    if (resultObj["chunks"].IsJsonArray) chunksArr = resultObj["chunks"].AsJsonArray;
                    else if (resultObj["data"].IsJsonArray) chunksArr = resultObj["data"].AsJsonArray;

                    if (chunksArr != null)
                    {
                        for (int i = 0; i < chunksArr.Count; i++)
                        {
                            if (!chunksArr[i].IsJsonObject) continue;
                            var c = chunksArr[i].AsJsonObject;
                            string filename = string.Empty;
                            if (c["item"].IsJsonObject && c["item"].AsJsonObject["key"].IsString)
                                filename = c["item"].AsJsonObject["key"].AsString;
                            else if (c["filename"].IsString)
                                filename = c["filename"].AsString;
                            else if (c["id"].IsString)
                                filename = c["id"].AsString;

                            double score = c["score"].IsNumber ? c["score"].AsNumber : 0.0;
                            string text = c["text"].IsString ? c["text"].AsString : string.Empty;

                            res.Chunks.Add(new AiSearchChunk
                            {
                                Filename = filename,
                                Score = score,
                                Text = text
                            });
                        }
                    }
                }

                return res;
            }
            catch (Exception ex)
            {
                return new AiSearchResult { Ok = false, Error = $"AI Search request failed: {ex.Message}" };
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? string.Empty;
            return s.Substring(0, max) + "...";
        }
    }
}
