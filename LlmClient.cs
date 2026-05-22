using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentGroupChat;

public enum LlmProvider
{
    OpenAiCompatible,
    Groq,
    Gemini,
    HuggingFace,
    Ollama,
}

public sealed record LlmRequestSettings(
    LlmProvider Provider,
    string Endpoint,
    string Model,
    string ApiKey,
    int MaxCompletionTokens,
    string ProviderLabel,
    string ConnectionName,
    string? GeminiThinkingLevel = null,
    string? HuggingFaceReasoningEffort = null);

public sealed record LlmChatMessage(string Role, string Content);

public sealed record LlmCompletionResult(
    string Content,
    string RawResponseBody,
    string? CompletionReason,
    string? UsageSummary);

public sealed class LlmClient
{
    private readonly HttpClient _httpClient;

    public LlmClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LlmCompletionResult> CompleteAsync(
        LlmRequestSettings settings,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken)
    {
        return settings.Provider switch
        {
            LlmProvider.OpenAiCompatible => await CompleteOpenAiCompatibleAsync(settings, messages, cancellationToken),
            LlmProvider.Groq => await CompleteGroqAsync(settings, messages, cancellationToken),
            LlmProvider.Gemini => await CompleteGeminiAsync(settings, messages, cancellationToken),
            LlmProvider.HuggingFace => await CompleteHuggingFaceAsync(settings, messages, cancellationToken),
            LlmProvider.Ollama => await CompleteOllamaAsync(settings, messages, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported provider."),
        };
    }

    private async Task<LlmCompletionResult> CompleteOpenAiCompatibleAsync(
        LlmRequestSettings settings,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        var payload = new
        {
            model = settings.Model,
            messages = messages.Select(message => new { role = message.Role, content = message.Content }),
            stream = false,
            max_tokens = settings.MaxCompletionTokens,
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI-compatible request failed ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        var choice = document.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var content = message.TryGetProperty("content", out var contentElement)
            ? contentElement.GetString()
            : null;
        var finishReason = choice.TryGetProperty("finish_reason", out var finishReasonElement)
            ? finishReasonElement.GetString()
            : null;

        return new LlmCompletionResult(
            string.IsNullOrWhiteSpace(content) ? "(empty response)" : content.Trim(),
            body,
            finishReason,
            BuildOpenAiUsageSummary(document.RootElement));
    }

    private async Task<LlmCompletionResult> CompleteGroqAsync(
        LlmRequestSettings settings,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Groq requires an API key.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        var payload = new
        {
            model = settings.Model,
            messages = messages.Select(message => new { role = message.Role, content = message.Content }),
            stream = false,
            max_completion_tokens = settings.MaxCompletionTokens,
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Groq request failed ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        var finishReason = document.RootElement
            .GetProperty("choices")[0]
            .TryGetProperty("finish_reason", out var finishReasonElement)
            ? finishReasonElement.GetString()
            : null;

        return new LlmCompletionResult(
            string.IsNullOrWhiteSpace(content) ? "(empty response)" : content.Trim(),
            body,
            finishReason,
            BuildOpenAiUsageSummary(document.RootElement));
    }

    private async Task<LlmCompletionResult> CompleteOllamaAsync(
        LlmRequestSettings settings,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);

        var payload = new
        {
            model = settings.Model,
            messages = messages.Select(message => new { role = message.Role, content = message.Content }),
            stream = false,
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Ollama request failed ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        var content = document.RootElement
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        var completionReason = document.RootElement.TryGetProperty("done_reason", out var doneReasonElement)
            ? doneReasonElement.GetString()
            : null;
        var usageSummary = BuildOllamaUsageSummary(document.RootElement);

        return new LlmCompletionResult(
            string.IsNullOrWhiteSpace(content) ? "(empty response)" : content.Trim(),
            body,
            completionReason,
            usageSummary);
    }

    private async Task<LlmCompletionResult> CompleteGeminiAsync(
        LlmRequestSettings settings,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Gemini requires an API key.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ResolveGeminiEndpoint(settings));
        request.Headers.Add("X-goog-api-key", settings.ApiKey);

        var systemInstruction = string.Join(
            "\n\n",
            messages
                .Where(message => string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
                .Select(message => message.Content.Trim())
                .Where(content => !string.IsNullOrWhiteSpace(content)));

        var contents = messages
            .Where(message => !string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(message => new
            {
                role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user",
                parts = new[] { new { text = message.Content } },
            })
            .ToList();

        if (contents.Count == 0)
        {
            contents.Add(new
            {
                role = "user",
                parts = new[] { new { text = string.Empty } },
            });
        }

        var payload = new
        {
            systemInstruction = string.IsNullOrWhiteSpace(systemInstruction)
                ? null
                : new
                {
                    parts = new[] { new { text = systemInstruction } },
                },
            contents,
            generationConfig = new
            {
                maxOutputTokens = settings.MaxCompletionTokens,
                thinkingConfig = string.IsNullOrWhiteSpace(settings.GeminiThinkingLevel)
                    ? null
                    : new
                    {
                        thinkingLevel = settings.GeminiThinkingLevel,
                    },
            },
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini request failed ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return new LlmCompletionResult("(empty response)", body, null, BuildGeminiUsageSummary(document.RootElement));
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var contentElement)
            || !contentElement.TryGetProperty("parts", out var partsElement))
        {
            return new LlmCompletionResult(
                "(empty response)",
                body,
                candidate.TryGetProperty("finishReason", out var missingPartsFinishReason) ? missingPartsFinishReason.GetString() : null,
                BuildGeminiUsageSummary(document.RootElement));
        }

        var content = string.Join(
            "\n",
            partsElement
                .EnumerateArray()
                .Select(part => part.TryGetProperty("text", out var textElement) ? textElement.GetString() : null)
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        var finishReason = candidate.TryGetProperty("finishReason", out var finishReasonElement)
            ? finishReasonElement.GetString()
            : null;

        return new LlmCompletionResult(
            string.IsNullOrWhiteSpace(content) ? "(empty response)" : content.Trim(),
            body,
            finishReason,
            BuildGeminiUsageSummary(document.RootElement));
    }

    private static string ResolveGeminiEndpoint(LlmRequestSettings settings)
    {
        var endpoint = settings.Endpoint.Trim().TrimEnd('/');
        if (endpoint.Contains(":generateContent", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        if (endpoint.Contains("/models/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{endpoint}:generateContent";
        }

        return $"{endpoint}/{settings.Model}:generateContent";
    }

    private async Task<LlmCompletionResult> CompleteHuggingFaceAsync(
        LlmRequestSettings settings,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Hugging Face requires an API key.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.Model,
            ["messages"] = messages.Select(message => new { role = message.Role, content = message.Content }).ToArray(),
            ["stream"] = false,
            ["max_tokens"] = settings.MaxCompletionTokens,
        };

        if (!string.IsNullOrWhiteSpace(settings.HuggingFaceReasoningEffort))
        {
            payload["reasoning_effort"] = settings.HuggingFaceReasoningEffort;
        }

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Hugging Face request failed ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        var choice = document.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var content = message.TryGetProperty("content", out var contentElement)
            ? contentElement.GetString()
            : null;
        var reasoningContent = message.TryGetProperty("reasoning_content", out var reasoningElement)
            ? reasoningElement.GetString()
            : null;

        var finishReason = choice
            .TryGetProperty("finish_reason", out var finishReasonElement)
            ? finishReasonElement.GetString()
            : null;

        var resolvedContent = string.IsNullOrWhiteSpace(content)
            ? (string.IsNullOrWhiteSpace(reasoningContent)
                ? "(empty response)"
                : "(reasoning-only response)")
            : content.Trim();

        return new LlmCompletionResult(
            resolvedContent,
            body,
            finishReason,
            BuildOpenAiUsageSummary(document.RootElement));
    }

    private static string? BuildOpenAiUsageSummary(JsonElement rootElement)
    {
        if (!rootElement.TryGetProperty("usage", out var usageElement))
        {
            return null;
        }

        var parts = new List<string>();
        if (usageElement.TryGetProperty("prompt_tokens", out var promptTokens))
        {
            parts.Add($"prompt={promptTokens.GetInt32()}");
        }

        if (usageElement.TryGetProperty("completion_tokens", out var completionTokens))
        {
            parts.Add($"completion={completionTokens.GetInt32()}");
        }

        if (usageElement.TryGetProperty("total_tokens", out var totalTokens))
        {
            parts.Add($"total={totalTokens.GetInt32()}");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? BuildGeminiUsageSummary(JsonElement rootElement)
    {
        if (!rootElement.TryGetProperty("usageMetadata", out var usageElement))
        {
            return null;
        }

        var parts = new List<string>();
        if (usageElement.TryGetProperty("promptTokenCount", out var promptTokens))
        {
            parts.Add($"prompt={promptTokens.GetInt32()}");
        }

        if (usageElement.TryGetProperty("candidatesTokenCount", out var candidateTokens))
        {
            parts.Add($"completion={candidateTokens.GetInt32()}");
        }

        if (usageElement.TryGetProperty("totalTokenCount", out var totalTokens))
        {
            parts.Add($"total={totalTokens.GetInt32()}");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? BuildOllamaUsageSummary(JsonElement rootElement)
    {
        var parts = new List<string>();
        if (rootElement.TryGetProperty("prompt_eval_count", out var promptEvalCount))
        {
            parts.Add($"prompt={promptEvalCount.GetInt32()}");
        }

        if (rootElement.TryGetProperty("eval_count", out var evalCount))
        {
            parts.Add($"completion={evalCount.GetInt32()}");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}