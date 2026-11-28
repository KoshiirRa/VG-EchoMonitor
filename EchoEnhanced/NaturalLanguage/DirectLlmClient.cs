using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using LightJson;

namespace EchoEnhanced.NaturalLanguage
{
    public sealed class DirectLlmResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }

    /// <summary>
    /// Minimal OpenAI-compatible Chat Completions client (works with OpenAI, Gemini OpenAI-compat,
    /// Groq, OpenRouter, Cloudflare Workers AI, or local Ollama) grounded by live in-game catalogs.
    /// </summary>
    public sealed class DirectLlmClient
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        private readonly string _configPath;
        private readonly string _examplePath;

        public string ConfigPath => _configPath;

        public DirectLlmClient(string bepInExConfigPath)
        {
            string dir = Path.Combine(bepInExConfigPath, "EchoEnhanced");
            Directory.CreateDirectory(dir);
            _configPath = Path.Combine(dir, "llm.json");
            _examplePath = Path.Combine(dir, "llm.example.json");
            EnsureExampleFile();
        }

        private void EnsureExampleFile()
        {
            try
            {
                if (!File.Exists(_examplePath))
                {
                    string example = "{\n"
                        + "  \"endpoint\": \"https://api.openai.com/v1/chat/completions\",\n"
                        + "  \"apiKey\": \"PASTE_YOUR_API_KEY_HERE\",\n"
                        + "  \"model\": \"gpt-4o-mini\",\n"
                        + "  \"_notes\": \"Works with any OpenAI-compatible endpoint: OpenAI, Gemini (https://generativelanguage.googleapis.com/v1beta/openai/chat/completions), Groq, OpenRouter, or local Ollama (http://localhost:11434/v1/chat/completions with empty apiKey).\"\n"
                        + "}\n";
                    File.WriteAllText(_examplePath, example);
                }
            }
            catch
            {
                // Non-fatal
            }
        }

        public bool IsConfigured(out string endpoint, out string apiKey, out string model, out string error)
        {
            endpoint = Environment.GetEnvironmentVariable("ECHO_LLM_ENDPOINT") ?? string.Empty;
            apiKey = Environment.GetEnvironmentVariable("ECHO_LLM_API_KEY") ?? string.Empty;
            model = Environment.GetEnvironmentVariable("ECHO_LLM_MODEL") ?? string.Empty;
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
                        if (obj["endpoint"].IsString && !string.IsNullOrWhiteSpace(obj["endpoint"].AsString))
                            endpoint = obj["endpoint"].AsString.Trim();
                        if (obj["apiKey"].IsString)
                            apiKey = obj["apiKey"].AsString.Trim();
                        if (obj["model"].IsString && !string.IsNullOrWhiteSpace(obj["model"].AsString))
                            model = obj["model"].AsString.Trim();
                    }
                }
                catch (Exception ex)
                {
                    error = $"Failed to parse {_configPath}: {ex.Message}";
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model))
            {
                error = $"Direct LLM not configured. Copy {_examplePath} to {_configPath} and set endpoint, model, and apiKey.";
                return false;
            }

            return true;
        }

        public DirectLlmResult Complete(string systemPrompt, string userPrompt)
        {
            if (!IsConfigured(out string endpoint, out string apiKey, out string model, out string cfgErr))
            {
                return new DirectLlmResult { Ok = false, Error = cfgErr };
            }

            try
            {
                var messages = new JsonArray
                {
                    new JsonObject { { "role", "system" }, { "content", systemPrompt } },
                    new JsonObject { { "role", "user" }, { "content", userPrompt } }
                };

                var payload = new JsonObject
                {
                    { "model", model },
                    { "temperature", 0.1 },
                    { "messages", messages }
                };

                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }
                req.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

                using var resp = Http.SendAsync(req).GetAwaiter().GetResult();
                string body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!resp.IsSuccessStatusCode)
                {
                    return new DirectLlmResult
                    {
                        Ok = false,
                        Error = $"LLM HTTP {(int)resp.StatusCode}: {Truncate(body, 300)}"
                    };
                }

                JsonValue rootVal = JsonValue.Parse(body);
                if (!rootVal.IsJsonObject)
                {
                    return new DirectLlmResult { Ok = false, Error = "Invalid JSON response from LLM endpoint." };
                }

                var root = rootVal.AsJsonObject;
                if (root["choices"].IsJsonArray && root["choices"].AsJsonArray.Count > 0)
                {
                    var first = root["choices"].AsJsonArray[0];
                    if (first.IsJsonObject && first.AsJsonObject["message"].IsJsonObject)
                    {
                        var msg = first.AsJsonObject["message"].AsJsonObject;
                        if (msg["content"].IsString)
                        {
                            return new DirectLlmResult
                            {
                                Ok = true,
                                Content = msg["content"].AsString,
                                Model = model
                            };
                        }
                    }
                }

                return new DirectLlmResult { Ok = false, Error = $"Unexpected LLM response shape: {Truncate(body, 300)}" };
            }
            catch (Exception ex)
            {
                return new DirectLlmResult { Ok = false, Error = $"LLM request failed: {ex.Message}" };
            }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? string.Empty;
            return s.Substring(0, max) + "...";
        }
    }
}
