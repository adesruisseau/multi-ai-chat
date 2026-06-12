using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Models.Llm;

namespace AgentGroupChat.Core.Services;

public sealed class TurnExecutor
{
    private readonly LlmClient _llmClient;
    private readonly LogService _logService;

    public TurnExecutor(LlmClient llmClient, LogService logService)
    {
        _llmClient = llmClient;
        _logService = logService;
    }

    public async Task<(string Response, string ShortTermMemory, string LongTermMemory, string PrivilegedActions, LlmCompletionResult RawResult)> ExecuteAgentTurnAsync(
        string roomId,
        AgentConfig agent,
        LlmRequestSettings settings,
        string prompt,
        bool includePrivilegedActions,
        CancellationToken ct)
    {
        var fullPrompt = string.Concat(
            prompt.TrimEnd(),
            Environment.NewLine,
            Environment.NewLine,
            BuildAgentOutputTransportPrompt(agent, includePrivilegedActions));

        var messages = new List<LlmChatMessage>
        {
            new(LlmRoles.System, fullPrompt)
            
        };

        await _logService.LogAsync(
            roomId,
            LogCategory.Request,
            agent.Name,
            $"→ {settings.ConnectionName} / {settings.Model}",
            $"[System Prompt]\n{fullPrompt}\n");

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
                roomId,
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
                roomId,
                LogCategory.Response,
                agent.Name,
                $"← ERROR: {ex.GetType().Name}",
                ex.Message,
                sw.ElapsedMilliseconds);
            throw;
        }
        sw.Stop();

        var (response, shortTermMemory, longTermMemory, privilegedActions) = ParseAgentOutput(result.Content);

        await _logService.LogAsync(
            roomId,
            LogCategory.Response,
            agent.Name,
            $"← {result.UsageSummary ?? "no usage info"}",
            result.Content,
            sw.ElapsedMilliseconds);

        return (response, shortTermMemory, longTermMemory, privilegedActions, result);
    }

    private static (string Response, string ShortTermMemory, string LongTermMemory, string PrivilegedActions) ParseAgentOutput(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return ("(empty response)", string.Empty, string.Empty, string.Empty);

        var shortTermNote = TryExtractTaggedContentWithSalvage(response, XmlTags.ShortTermMemory, allowTextBeforeClosingTag: true) ?? string.Empty;
        var longTermNote = TryExtractTaggedContentWithSalvage(response, XmlTags.LongTermMemory, allowTextBeforeClosingTag: true) ?? string.Empty;
        var privilegedActions = TryExtractTaggedContentWithSalvage(response, XmlTags.PrivilegedActions, allowTextBeforeClosingTag: true) ?? string.Empty;
        var taggedReply = TryExtractTaggedContentWithSalvage(response, XmlTags.Reply, allowTextBeforeClosingTag: true);
        var replySource = string.IsNullOrWhiteSpace(taggedReply)
            ? RemoveTransportSections(response)
            : taggedReply;
        var visibleResponse = string.IsNullOrWhiteSpace(taggedReply)
            ? StripTransportTagMarkers(replySource)
            : StripTransportTagMarkers(taggedReply);

        if (string.IsNullOrWhiteSpace(visibleResponse))
        {
            visibleResponse = StripTransportTagMarkers(RemoveTransportSections(response));
        }

        return (
            SanitizeAgentResponse(visibleResponse),
            shortTermNote.Trim(),
            longTermNote.Trim(),
            privilegedActions.Trim());
    }

    private static string BuildAgentOutputTransportPrompt(AgentConfig agent, bool includePrivilegedActions)
    {
        var sectionNames = new List<string> { XmlTags.Reply };
        var rules = new List<string>
        {
            $"- Put the user-visible reply only inside <{XmlTags.Reply}>.",
        };

        if (agent.UseShortTermMemoryStorage)
        {
            sectionNames.Add(XmlTags.ShortTermMemory);
            rules.Add($"- Put private scratchpad notes for the next turn inside <{XmlTags.ShortTermMemory}>.");
        }

        if (agent.UseLongTermMemoryStorage)
        {
            sectionNames.Add(XmlTags.LongTermMemory);
            rules.Add($"- Put durable agent-private memory worth keeping across many rounds inside <{XmlTags.LongTermMemory}>.");
        }

        if (includePrivilegedActions)
        {
            sectionNames.Add(XmlTags.PrivilegedActions);
            rules.Add($"- Put privileged action tags only inside <{XmlTags.PrivilegedActions}>. Leave it empty when you have no privileged requests.");
        }

        var formatLines = sectionNames
            .Select(tagName => $"<{tagName}>...</{tagName}>")
            .ToList();

        var promptLines = new List<string>
        {
            "Return only XML using these sections in this exact order:",
        };

        promptLines.AddRange(formatLines);
        promptLines.Add("Rules:");
        promptLines.AddRange(rules);
        promptLines.Add("- Include every listed section exactly once.");
        promptLines.Add("- If a listed section has nothing to store, leave it empty instead of omitting it.");
        promptLines.Add("- Do not add markdown fences, commentary, or extra top-level tags.");

        return string.Join(Environment.NewLine, promptLines);
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

    private static string RemoveTransportSections(string value)
    {
        var stripped = RemoveTaggedSection(value, XmlTags.Reply);
        stripped = RemoveTaggedSection(stripped, XmlTags.ShortTermMemory);
        stripped = RemoveTaggedSection(stripped, XmlTags.LongTermMemory);
        stripped = RemoveTaggedSection(stripped, XmlTags.PrivilegedActions);
        return stripped.Trim();
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
        value.Replace($"<{XmlTags.Reply}>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"</{XmlTags.Reply}>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"<{XmlTags.ShortTermMemory}>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"</{XmlTags.ShortTermMemory}>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"<{XmlTags.LongTermMemory}>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"</{XmlTags.LongTermMemory}>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"<{XmlTags.PrivilegedActions}>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace($"</{XmlTags.PrivilegedActions}>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
}
