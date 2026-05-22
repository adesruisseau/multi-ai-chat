using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentGroupChat.Core.Models.Llm;

namespace AgentGroupChat.Core.Services;

public sealed class LlmClient
{
    private readonly HttpClient _httpClient;

    public LlmClient(HttpClient httpClient) => _httpClient = httpClient;

    public async Task<LlmCompletionResult> CompleteAsync(
        LlmRequestSettings settings,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken ct)
    {
        return settings.Provider switch
        {
            LlmProvider.OpenAiCompatible => await CompleteOpenAiAsync(settings, messages, ct),
            LlmProvider.Groq => await CompleteGroqAsync(settings, messages, ct),
            LlmProvider.Gemini => await CompleteGeminiAsync(settings, messages, ct),
            LlmProvider.HuggingFace => await CompleteHuggingFaceAsync(settings, messages, ct),
            LlmProvider.Ollama => await CompleteOllamaAsync(settings, messages, ct),
            _ => throw new InvalidOperationException("Unsupported provider."),
        };
    }

    private async Task<LlmCompletionResult> CompleteOpenAiAsync(
        LlmRequestSettings settings, IReadOnlyList<LlmChatMessage> messages, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        var payload = new
        {
            model = settings.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = false,
            max_tokens = settings.MaxCompletionTokens,
        };
        request.Content = JsonContent(payload);

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI-compatible request failed ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var choice = doc.RootElement.GetProperty("choices")[0];
        var msg = choice.GetProperty("message");
        var content = msg.TryGetProperty("content", out var ce) ? ce.GetString() : null;
        var finish = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
        return new LlmCompletionResult(
            string.IsNullOrWhiteSpace(content) ? "(empty response)" : content.Trim(),
            body, finish, BuildOpenAiUsage(doc.RootElement));
    }

    private async Task<LlmCompletionResult> CompleteGroqAsync(
        LlmRequestSettings settings, IReadOnlyList<LlmChatMessage> messages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Groq requires an API key.");

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        var payload = new
        {
            model = settings.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = false,
            max_completion_tokens = settings.MaxCompletionTokens,
        };
        request.Content = JsonContent(payload);

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Groq request failed ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        var finish = doc.RootElement.GetProperty("choices")[0].TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;
        return new LlmCompletionResult(
            string.IsNullOrWhiteSpace(content) ? "(empty response)" : content.Trim(),
            body, finish, BuildOpenAiUsage(doc.RootElement));
    }

    private async Task<LlmCompletionResult> CompleteOllamaAsync(
        LlmRequestSettings settings, IReadOnlyList<LlmChatMessage> messages, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        var payload = new
        {
            model = settings.Model,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = false,
        };
        request.Content = JsonContent(payload);

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama request failed ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString();
        var reason = doc.RootElement.TryGetProperty("done_reason", out var dr) ? dr.GetString() : null;
        return new LlmCompletionResult(
            string.IsNullOrWhiteSpace(content) ? "(empty response)" : content.Trim(),
            body, reason, BuildOllamaUsage(doc.RootElement));
    }

    private async Task<LlmCompletionResult> CompleteGeminiAsync(
        LlmRequestSettings settings, IReadOnlyList<LlmChatMessage> messages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Gemini requires an API key.");

        var endpoint = ResolveGeminiEndpoint(settings);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("X-goog-api-key", settings.ApiKey);

        var systemInstruction = string.Join("\n\n",
            messages.Where(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Content.Trim()).Where(c => !string.IsNullOrWhiteSpace(c)));

        var contents = messages
            .Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
            .Select(m => new
            {
                role = m.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user",
                parts = new[] { new { text = m.Content } },
            }).ToList();

        if (contents.Count == 0)
            contents.Add(new { role = "user", parts = new[] { new { text = string.Empty } } });

        var payload = new
        {
            systemInstruction = string.IsNullOrWhiteSpace(systemInstruction) ? null
                : new { parts = new[] { new { text = systemInstruction } } },
            contents,
            generationConfig = new
            {
                maxOutputTokens = settings.MaxCompletionTokens,
                thinkingConfig = string.IsNullOrWhiteSpace(settings.GeminiThinkingLevel) ? null
                    : new { thinkingLevel = settings.GeminiThinkingLevel },
            },
        };
        request.Content = JsonContent(payload);

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gemini request failed ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
            return new LlmCompletionResult("(empty response)", body, null, BuildGeminiUsage(doc.RootElement));

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var ce) || !ce.TryGetProperty("parts", out var parts))
        {
            var fr2 = candidate.TryGetProperty("finishReason", out var fr2e) ? fr2e.GetString() : null;
            return new LlmCompletionResult("(empty response)", body, fr2, BuildGeminiUsage(doc.RootElement));
        }

        var content = string.Join("\n", parts.EnumerateArray()
            .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null)
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        var finish = candidate.TryGetProperty("finishReason", out var fre) ? fre.GetString() : null;
        return new LlmCompletionResult(
            string.IsNullOrWhiteSpace(content) ? "(empty response)" : content.Trim(),
            body, finish, BuildGeminiUsage(doc.RootElement));
    }

    private async Task<LlmCompletionResult> CompleteHuggingFaceAsync(
        LlmRequestSettings settings, IReadOnlyList<LlmChatMessage> messages, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("Hugging Face requires an API key.");

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.Model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            ["stream"] = false,
            ["max_tokens"] = settings.MaxCompletionTokens,
        };
        if (!string.IsNullOrWhiteSpace(settings.HuggingFaceReasoningEffort))
            payload["reasoning_effort"] = settings.HuggingFaceReasoningEffort;

        request.Content = JsonContent(payload);

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Hugging Face request failed ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var choice = doc.RootElement.GetProperty("choices")[0];
        var msg = choice.GetProperty("message");
        var content = msg.TryGetProperty("content", out var ce) ? ce.GetString() : null;
        var reasoning = msg.TryGetProperty("reasoning_content", out var re) ? re.GetString() : null;
        var finish = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

        var resolved = string.IsNullOrWhiteSpace(content)
            ? (string.IsNullOrWhiteSpace(reasoning) ? "(empty response)" : "(reasoning-only response)")
            : content.Trim();
        return new LlmCompletionResult(resolved, body, finish, BuildOpenAiUsage(doc.RootElement));
    }

    private static string ResolveGeminiEndpoint(LlmRequestSettings settings)
    {
        var ep = settings.Endpoint.Trim().TrimEnd('/');
        if (ep.Contains(":generateContent", StringComparison.OrdinalIgnoreCase)) return ep;
        if (ep.Contains("/models/", StringComparison.OrdinalIgnoreCase)) return $"{ep}:generateContent";
        return $"{ep}/{settings.Model}:generateContent";
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static string? BuildOpenAiUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u)) return null;
        var parts = new List<string>();
        if (u.TryGetProperty("prompt_tokens", out var pt)) parts.Add($"prompt={pt.GetInt32()}");
        if (u.TryGetProperty("completion_tokens", out var ct)) parts.Add($"completion={ct.GetInt32()}");
        if (u.TryGetProperty("total_tokens", out var tt)) parts.Add($"total={tt.GetInt32()}");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? BuildGeminiUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var u)) return null;
        var parts = new List<string>();
        if (u.TryGetProperty("promptTokenCount", out var pt)) parts.Add($"prompt={pt.GetInt32()}");
        if (u.TryGetProperty("candidatesTokenCount", out var ct)) parts.Add($"completion={ct.GetInt32()}");
        if (u.TryGetProperty("totalTokenCount", out var tt)) parts.Add($"total={tt.GetInt32()}");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? BuildOllamaUsage(JsonElement root)
    {
        var parts = new List<string>();
        if (root.TryGetProperty("prompt_eval_count", out var pe)) parts.Add($"prompt={pe.GetInt32()}");
        if (root.TryGetProperty("eval_count", out var ec)) parts.Add($"completion={ec.GetInt32()}");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
