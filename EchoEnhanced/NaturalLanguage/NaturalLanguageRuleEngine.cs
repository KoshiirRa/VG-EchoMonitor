using System;
using System.Collections.Generic;
using EchoEnhanced.AiSearch;
using EchoEnhanced.AutoBuy;
using EchoEnhanced.AutoFavorite;
using EchoEnhanced.AutoSell;
using LightJson;

namespace EchoEnhanced.NaturalLanguage
{
    public sealed class PendingRuleDraft
    {
        public string DraftId { get; set; } = string.Empty;
        public string Target { get; set; } = "autofav"; // autofav | autobuy | autosell
        public string Instruction { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public JsonObject RuleJson { get; set; } = new JsonObject();
        public List<string> Sources { get; set; } = new List<string>();
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class NaturalLanguageRuleEngine
    {
        private const string SystemPrompt =
            "You are ECHO Enhanced's rule compiler for the game Vanguard Galaxy.\n"
            + "Convert the user's natural-language instruction into ONE valid JSON object with NO markdown fences and NO extra prose.\n"
            + "Use the retrieved Vanguard Galaxy localization and C# code context to resolve exact in-game item names, aspect names, and substat names.\n"
            + "Output format:\n"
            + "{\n"
            + "  \"target\": \"autofav\" | \"autobuy\" | \"autosell\",\n"
            + "  \"summary\": \"One concise human-readable sentence describing what this rule does\",\n"
            + "  \"rule\": {\n"
            + "    \"name\": \"Short descriptive rule name\",\n"
            + "    \"enabled\": true,\n"
            + "    \"categories\": [\"Turret\", \"Module\", \"Booster\", \"Blueprint\", \"Drone\"],\n"
            + "    \"nameContains\": [\"exact or partial in-game item name\"],\n"
            + "    \"nameExcludes\": [],\n"
            + "    \"minRarity\": \"Standard|Enhanced|HighGrade|Exotic|Legendary\",\n"
            + "    \"maxRarity\": \"Standard|Enhanced|HighGrade|Exotic|Legendary\",\n"
            + "    \"minItemLevel\": 90,\n"
            + "    \"maxItemLevel\": 100,\n"
            + "    \"hasAspect\": true,\n"
            + "    \"aspectContains\": [\"Punch Through\"],\n"
            + "    \"substats\": { \"minMatches\": 2, \"anyOf\": [\"Turret Rotation Speed\", \"Critical Damage\"], \"requireAll\": [] },\n"
            + "    \"onlyUnknownBlueprints\": true,\n"
            + "    \"buyFullStack\": false,\n"
            + "    \"maxPrice\": 500000\n"
            + "  }\n"
            + "}\n"
            + "Omit any optional fields inside \"rule\" that the user did not specify. Return ONLY valid JSON.";

        private readonly DirectLlmClient _directLlm;
        private readonly CloudflareAiSearchClient _aiSearch;
        private readonly AutoFavoriteEngine _autoFavorite;
        private readonly AutoSellEngine _autoSell;
        private readonly AutoBuyEngine _autoBuy;
        private readonly object _lock = new object();
        private readonly Dictionary<string, PendingRuleDraft> _drafts = new Dictionary<string, PendingRuleDraft>(StringComparer.OrdinalIgnoreCase);

        public NaturalLanguageRuleEngine(
            DirectLlmClient directLlm,
            CloudflareAiSearchClient aiSearch,
            AutoFavoriteEngine autoFavorite,
            AutoSellEngine autoSell,
            AutoBuyEngine autoBuy)
        {
            _directLlm = directLlm;
            _aiSearch = aiSearch;
            _autoFavorite = autoFavorite;
            _autoSell = autoSell;
            _autoBuy = autoBuy;
        }

        public JsonObject GetStatusObject()
        {
            bool llmOk = _directLlm.IsConfigured(out _, out _, out string llmModel, out _);
            bool cfOk = _aiSearch.IsConfigured(out _, out _, out string instanceId, out _);
            int pendingCount;
            lock (_lock)
            {
                PruneExpiredLocked();
                pendingCount = _drafts.Count;
            }

            return new JsonObject
            {
                { "activeProvider", llmOk ? "directLlm" : (cfOk ? "cloudflareAiSearch" : "none") },
                { "directLlmConfigured", llmOk },
                { "directLlmModel", llmModel },
                { "directLlmConfigPath", _directLlm.ConfigPath },
                { "aiSearchConfigured", cfOk },
                { "aiSearchInstanceId", instanceId },
                { "aiSearchConfigPath", _aiSearch.ConfigPath },
                { "pendingDrafts", pendingCount }
            };
        }

        public string QueryAiSearchJson(string rawBody)
        {
            try
            {
                JsonValue root = string.IsNullOrWhiteSpace(rawBody) ? JsonValue.Null : JsonValue.Parse(rawBody);
                string query = root.IsJsonObject && root.AsJsonObject["query"].IsString
                    ? root.AsJsonObject["query"].AsString.Trim()
                    : string.Empty;
                int maxResults = root.IsJsonObject && root.AsJsonObject["maxResults"].IsNumber
                    ? root.AsJsonObject["maxResults"].AsInteger
                    : 5;

                if (string.IsNullOrWhiteSpace(query))
                {
                    return new JsonObject { { "ok", false }, { "error", "Missing 'query' field." } }.ToString();
                }

                var res = _aiSearch.Search(query, maxResults);
                if (!res.Ok)
                {
                    return new JsonObject { { "ok", false }, { "error", res.Error } }.ToString();
                }

                var chunks = new JsonArray();
                for (int i = 0; i < res.Chunks.Count; i++)
                {
                    var c = res.Chunks[i];
                    chunks.Add(new JsonObject
                    {
                        { "filename", c.Filename },
                        { "score", c.Score },
                        { "text", c.Text }
                    });
                }

                return new JsonObject
                {
                    { "ok", true },
                    { "query", query },
                    { "chunks", chunks }
                }.ToString();
            }
            catch (Exception ex)
            {
                return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
            }
        }

        public string ParseRuleJson(string rawBody)
        {
            try
            {
                JsonValue reqVal = string.IsNullOrWhiteSpace(rawBody) ? JsonValue.Null : JsonValue.Parse(rawBody);
                if (!reqVal.IsJsonObject)
                {
                    return new JsonObject { { "ok", false }, { "error", "Expected JSON object body." } }.ToString();
                }

                var reqObj = reqVal.AsJsonObject;
                string instruction = reqObj["instruction"].IsString ? reqObj["instruction"].AsString.Trim() : string.Empty;
                string requestedTarget = reqObj["target"].IsString ? reqObj["target"].AsString.Trim().ToLowerInvariant() : "auto";

                if (string.IsNullOrWhiteSpace(instruction))
                {
                    return new JsonObject { { "ok", false }, { "error", "Missing 'instruction' field." } }.ToString();
                }

                string userPrompt = requestedTarget != "auto" && !string.IsNullOrEmpty(requestedTarget)
                    ? $"Target engine: {requestedTarget}\nInstruction: {instruction}"
                    : $"Instruction: {instruction}";

                string rawModelResponse;
                var sources = new List<string>();

                if (_directLlm.IsConfigured(out _, out _, out string modelName, out _))
                {
                    string catalogJson = "{}";
                    try
                    {
                        catalogJson = EchoMonitor.Plugin.RunOnMainThread(() => _autoFavorite.GetCatalogJson(), 5000) ?? "{}";
                    }
                    catch
                    {
                        // Non-fatal; proceed without live catalog if main thread busy
                    }

                    string groundedPrompt = $"{userPrompt}\n\nLive in-game Vanguard Galaxy catalog (use exact category, rarity, substat, and aspect names from this JSON):\n{catalogJson}";
                    var llmRes = _directLlm.Complete(SystemPrompt, groundedPrompt);
                    if (!llmRes.Ok)
                    {
                        return new JsonObject { { "ok", false }, { "error", llmRes.Error } }.ToString();
                    }

                    rawModelResponse = llmRes.Content;
                    sources.Add($"live-catalog ({modelName})");
                }
                else if (_aiSearch.IsConfigured(out _, out _, out _, out _))
                {
                    var aiRes = _aiSearch.AiSearch(SystemPrompt, userPrompt, maxResults: 6);
                    if (!aiRes.Ok)
                    {
                        return new JsonObject { { "ok", false }, { "error", aiRes.Error } }.ToString();
                    }

                    rawModelResponse = aiRes.ResponseText;
                    for (int i = 0; i < aiRes.Chunks.Count; i++)
                    {
                        string fn = aiRes.Chunks[i].Filename;
                        if (!string.IsNullOrWhiteSpace(fn) && !sources.Contains(fn))
                        {
                            sources.Add(fn);
                        }
                    }
                }
                else
                {
                    return new JsonObject
                    {
                        { "ok", false },
                        { "error", $"No LLM provider configured. Copy {_directLlm.ConfigPath.Replace("llm.json", "llm.example.json")} to {_directLlm.ConfigPath} and add your API key (OpenAI, Gemini, Groq, OpenRouter, or local Ollama)." }
                    }.ToString();
                }

                string jsonCandidate = ExtractJsonObject(rawModelResponse);
                if (string.IsNullOrWhiteSpace(jsonCandidate))
                {
                    return new JsonObject
                    {
                        { "ok", false },
                        { "error", "LLM did not return a valid JSON rule object." },
                        { "rawResponse", rawModelResponse }
                    }.ToString();
                }

                JsonValue parsedVal = JsonValue.Parse(jsonCandidate);
                if (!parsedVal.IsJsonObject)
                {
                    return new JsonObject { { "ok", false }, { "error", "Parsed output was not a JSON object." } }.ToString();
                }

                var parsedObj = parsedVal.AsJsonObject;
                string target = NormalizeTarget(
                    requestedTarget != "auto" && !string.IsNullOrEmpty(requestedTarget)
                        ? requestedTarget
                        : (parsedObj["target"].IsString ? parsedObj["target"].AsString : InferTarget(instruction)));

                JsonObject ruleObj = parsedObj["rule"].IsJsonObject ? parsedObj["rule"].AsJsonObject : parsedObj;
                if (!ruleObj["name"].IsString || string.IsNullOrWhiteSpace(ruleObj["name"].AsString))
                {
                    ruleObj["name"] = $"NL Rule ({target})";
                }
                if (!ruleObj["enabled"].IsBoolean)
                {
                    ruleObj["enabled"] = true;
                }

                // Validate against actual EchoEnhanced rule parsers
                if (target == "autobuy")
                {
                    AutoBuyRule.ParseSingleRule(ruleObj, 0);
                }
                else
                {
                    AutoFavoriteRule.ParseSingleRule(ruleObj, 0);
                }

                string summary = parsedObj["summary"].IsString && !string.IsNullOrWhiteSpace(parsedObj["summary"].AsString)
                    ? parsedObj["summary"].AsString.Trim()
                    : $"Add {target} rule '{ruleObj["name"].AsString}'";

                string draftId = Guid.NewGuid().ToString("N").Substring(0, 8);
                var draft = new PendingRuleDraft
                {
                    DraftId = draftId,
                    Target = target,
                    Instruction = instruction,
                    Summary = summary,
                    RuleJson = ruleObj,
                    Sources = sources,
                    CreatedUtc = DateTime.UtcNow
                };

                lock (_lock)
                {
                    PruneExpiredLocked();
                    _drafts[draftId] = draft;
                }

                var srcArr = new JsonArray();
                for (int i = 0; i < sources.Count; i++) srcArr.Add(sources[i]);

                return new JsonObject
                {
                    { "ok", true },
                    { "draftId", draftId },
                    { "target", target },
                    { "summary", summary },
                    { "rule", ruleObj },
                    { "sources", srcArr }
                }.ToString();
            }
            catch (Exception ex)
            {
                return new JsonObject { { "ok", false }, { "error", $"Failed to parse NL rule: {ex.Message}" } }.ToString();
            }
        }

        public string ConfirmDraftJson(string rawBody)
        {
            try
            {
                string draftId = ReadDraftId(rawBody);
                if (string.IsNullOrWhiteSpace(draftId))
                {
                    return new JsonObject { { "ok", false }, { "error", "Missing 'draftId'." } }.ToString();
                }

                PendingRuleDraft draft;
                lock (_lock)
                {
                    PruneExpiredLocked();
                    if (!_drafts.TryGetValue(draftId, out draft))
                    {
                        return new JsonObject { { "ok", false }, { "error", $"Draft '{draftId}' not found or expired." } }.ToString();
                    }
                    _drafts.Remove(draftId);
                }

                string existingJson = draft.Target switch
                {
                    "autobuy" => _autoBuy.GetRulesJson(),
                    "autosell" => _autoSell.GetRulesJson(),
                    _ => _autoFavorite.GetRulesJson()
                };

                JsonValue rootVal = string.IsNullOrWhiteSpace(existingJson) ? new JsonObject { { "rules", new JsonArray() } } : JsonValue.Parse(existingJson);
                JsonArray rulesArr;
                JsonObject wrapper;

                if (rootVal.IsJsonObject)
                {
                    wrapper = rootVal.AsJsonObject;
                    if (!wrapper["rules"].IsJsonArray)
                    {
                        wrapper["rules"] = new JsonArray();
                    }
                    rulesArr = wrapper["rules"].AsJsonArray;
                }
                else if (rootVal.IsJsonArray)
                {
                    rulesArr = rootVal.AsJsonArray;
                    wrapper = new JsonObject { { "rules", rulesArr } };
                }
                else
                {
                    rulesArr = new JsonArray();
                    wrapper = new JsonObject { { "rules", rulesArr } };
                }

                rulesArr.Add(draft.RuleJson);
                string updatedJson = wrapper.ToString(pretty: true);

                string saveResult = draft.Target switch
                {
                    "autobuy" => _autoBuy.SaveRulesJson(updatedJson),
                    "autosell" => _autoSell.SaveRulesJson(updatedJson),
                    _ => _autoFavorite.SaveRulesJson(updatedJson)
                };

                JsonValue saveVal = JsonValue.Parse(saveResult);
                if (saveVal.IsJsonObject && saveVal.AsJsonObject["ok"].IsBoolean && !saveVal.AsJsonObject["ok"].AsBoolean)
                {
                    return saveResult;
                }

                string ruleName = draft.RuleJson["name"].IsString ? draft.RuleJson["name"].AsString : "Unnamed Rule";
                return new JsonObject
                {
                    { "ok", true },
                    { "target", draft.Target },
                    { "ruleName", ruleName },
                    { "ruleCount", rulesArr.Count },
                    { "message", $"Saved '{ruleName}' to {draft.Target} rules ({rulesArr.Count} total)." }
                }.ToString();
            }
            catch (Exception ex)
            {
                return new JsonObject { { "ok", false }, { "error", ex.Message } }.ToString();
            }
        }

        public string CancelDraftJson(string rawBody)
        {
            string draftId = ReadDraftId(rawBody);
            if (string.IsNullOrWhiteSpace(draftId))
            {
                return new JsonObject { { "ok", false }, { "error", "Missing 'draftId'." } }.ToString();
            }

            bool removed;
            lock (_lock)
            {
                removed = _drafts.Remove(draftId);
            }

            return new JsonObject
            {
                { "ok", true },
                { "removed", removed },
                { "message", removed ? $"Discarded draft '{draftId}'." : $"Draft '{draftId}' was already cleared." }
            }.ToString();
        }

        private void PruneExpiredLocked()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-15);
            var expired = new List<string>();
            foreach (var kv in _drafts)
            {
                if (kv.Value.CreatedUtc < cutoff) expired.Add(kv.Key);
            }
            for (int i = 0; i < expired.Count; i++) _drafts.Remove(expired[i]);
        }

        private static string ReadDraftId(string rawBody)
        {
            if (string.IsNullOrWhiteSpace(rawBody)) return string.Empty;
            JsonValue val = JsonValue.Parse(rawBody);
            if (val.IsJsonObject && val.AsJsonObject["draftId"].IsString)
            {
                return val.AsJsonObject["draftId"].AsString.Trim();
            }
            return string.Empty;
        }

        private static string NormalizeTarget(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return "autofav";
            t = t.Trim().ToLowerInvariant();
            if (t.Contains("buy") || t.Contains("shop")) return "autobuy";
            if (t.Contains("sell")) return "autosell";
            return "autofav";
        }

        private static string InferTarget(string instruction)
        {
            string lower = (instruction ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("auto-buy") || lower.Contains("autobuy") || lower.Contains("buy ") || lower.Contains("shop"))
                return "autobuy";
            if (lower.Contains("auto-sell") || lower.Contains("autosell") || lower.Contains("sell "))
                return "autosell";
            return "autofav";
        }

        private static string ExtractJsonObject(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return string.Empty;
            return text.Substring(start, end - start + 1);
        }
    }
}
