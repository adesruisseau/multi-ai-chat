using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Models.Llm;

namespace AgentGroupChat.Core.Services;

public sealed class TurnExecutor
{
    private const string AgentOutputTransportPrompt = @"
    ========================
    TRANSPORT FORMAT (STRICT)
    ========================

    Return a response with exactly two XML blocks:

    <reply>
    (in-character narration only)
    </reply>

    <future_note>
    (short private planning notes only)
    </future_note>

    No extra text.";

    private readonly LlmClient _llmClient;
    private readonly LogService _logService;

    public TurnExecutor(LlmClient llmClient, LogService logService)
    {
        _llmClient = llmClient;
        _logService = logService;
    }

    public async Task<(string Response, string FutureNote, LlmCompletionResult RawResult)> ExecuteAgentTurnAsync(
        AgentConfig agent,
        LlmRequestSettings settings,
        string prompt,
        CancellationToken ct)
    {
        var messages = new List<LlmChatMessage>
        {
            new(LlmRoles.System, prompt),
            new(LlmRoles.System, AgentOutputTransportPrompt),
        };

        await _logService.LogAsync(
            LogCategory.Request,
            agent.Name,
            $"→ {settings.ConnectionName} / {settings.Model}",
            $"[System Prompt]\n{prompt}\n\n[Transport Prompt]\n{AgentOutputTransportPrompt}");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        LlmCompletionResult result;
        try
        {
            result = await _llmClient.CompleteAsync(settings, messages, ct);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            await _logService.LogAsync(
                LogCategory.Response,
                agent.Name,
                "← Cancelled",
                "Request was cancelled.",
                sw.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _logService.LogAsync(
                LogCategory.Response,
                agent.Name,
                $"← ERROR: {ex.GetType().Name}",
                ex.Message,
                sw.ElapsedMilliseconds);
            throw;
        }
        sw.Stop();

        var (response, futureNote) = ParseAgentOutput(result.Content);

        await _logService.LogAsync(
            LogCategory.Response,
            agent.Name,
            $"← {result.UsageSummary ?? "no usage info"}",
            result.Content,
            sw.ElapsedMilliseconds);

        return (response, futureNote, result);
    }

    private static (string Response, string FutureNote) ParseAgentOutput(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return ("(empty response)", string.Empty);

        var futureNote = TryExtractTaggedContentWithSalvage(response, "future_note", allowTextBeforeClosingTag: false) ?? string.Empty;
        var replySource = RemoveTaggedSection(response, "future_note");
        var taggedReply = TryExtractTaggedContentWithSalvage(replySource, "reply", allowTextBeforeClosingTag: true);
        var visibleResponse = string.IsNullOrWhiteSpace(taggedReply)
            ? StripTransportTagMarkers(replySource)
            : taggedReply;

        if (string.IsNullOrWhiteSpace(visibleResponse))
        {
            visibleResponse = StripTransportTagMarkers(response);
            if (!string.IsNullOrWhiteSpace(futureNote)
                && visibleResponse.EndsWith(futureNote, StringComparison.Ordinal))
            {
                visibleResponse = visibleResponse[..^futureNote.Length].TrimEnd();
            }
        }

        return (SanitizeAgentResponse(visibleResponse), futureNote.Trim());
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

    private static string? TryExtractTaggedContentWithSalvage(string value, string tagName, bool allowTextBeforeClosingTag)
    {
        var openTag = $"<{tagName}>";
        var closeTag = $"</{tagName}>";
        var start = value.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        var end = value.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);

        if (start >= 0)
        {
            var contentStart = start + openTag.Length;
            if (end >= contentStart)
            {
                var extracted = value[contentStart..end].Trim();
                return string.IsNullOrWhiteSpace(extracted) ? null : extracted;
            }

            var trailing = value[contentStart..].Trim();
            return string.IsNullOrWhiteSpace(trailing) ? null : trailing;
        }

        if (allowTextBeforeClosingTag && end > 0)
        {
            var leading = value[..end].Trim();
            return string.IsNullOrWhiteSpace(leading) ? null : leading;
        }

        return null;
    }

    private static string RemoveTaggedSection(string value, string tagName)
    {
        var openTag = $"<{tagName}>";
        var closeTag = $"</{tagName}>";
        var start = value.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return value;

        var end = value.IndexOf(closeTag, start, StringComparison.OrdinalIgnoreCase);
        return end < 0
            ? value[..start].TrimEnd()
            : (value[..start] + value[(end + closeTag.Length)..]).Trim();
    }

    private static string StripTransportTagMarkers(string value) =>
        value.Replace("<reply>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</reply>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<future_note>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</future_note>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
}
