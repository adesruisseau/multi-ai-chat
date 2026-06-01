namespace AgentGroupChat.Core.Models.Llm;

public enum LlmProvider
{
    OpenAiCompatible,
    Groq,
    Gemini,
    HuggingFace,
    Ollama,
}

public static class LlmProviderMapping
{
    public static LlmProvider FromTransport(string transport) => transport switch
    {
        LlmTransports.Groq => LlmProvider.Groq,
        LlmTransports.Gemini => LlmProvider.Gemini,
        LlmTransports.HuggingFace => LlmProvider.HuggingFace,
        LlmTransports.Ollama => LlmProvider.Ollama,
        _ => LlmProvider.OpenAiCompatible,
    };
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
