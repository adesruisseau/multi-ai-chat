using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Models.Llm;

namespace AgentGroupChat.Core.Services;

public sealed class TurnExecutor
{
    private readonly LlmClient _llmClient;

    public TurnExecutor(LlmClient llmClient) => _llmClient = llmClient;

    public async Task<(string Response, LlmCompletionResult RawResult)> ExecuteAgentTurnAsync(
        AgentConfig agent,
        LlmRequestSettings settings,
        string prompt,
        CancellationToken ct)
    {
        var messages = new List<LlmChatMessage>
        {
            new("system", agent.SystemPrompt),
            new("user", prompt),
        };

        var result = await _llmClient.CompleteAsync(settings, messages, ct);
        var sanitized = SanitizeAgentResponse(result.Content);
        return (sanitized, result);
    }

    private static string SanitizeAgentResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return "(empty response)";

        var text = response.Trim();

        // Strip common LLM response prefixes like "AgentName: "
        var colonIdx = text.IndexOf(':');
        if (colonIdx > 0 && colonIdx < 40 && !text[..colonIdx].Contains('\n'))
        {
            var prefix = text[..colonIdx].Trim();
            if (!prefix.Contains(' ') || prefix.Length < 25)
            {
                var candidate = text[(colonIdx + 1)..].TrimStart();
                if (candidate.Length > 20)
                    text = candidate;
            }
        }

        return text;
    }
}
