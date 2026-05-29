using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services;

public sealed record SceneSummarizerProfile(
    int MaxCompletionTokens,
    int MaxLines,
    int MaxCharacters,
    int BroaderTranscriptTurns,
    string CompressionInstruction,
    string SectionBudgetInstruction);

public sealed class PromptComposer
{
    public string BuildAgentPrompt(
        RoomConfig room,
        AgentConfig agent,
        int iteration,
        int maxIterations,
        string sharedRoomMemory,
        string durableMemory,
        string agentLongMemory,
        string agentShortMemory,
        IReadOnlyList<TranscriptTurn> recentTurns,
        bool includeDurableMemory,
        IReadOnlyList<SceneArchive>? recalledScenes = null)
    {
        var hasRecalledContext = recalledScenes is { Count: > 0 };
        var participants = string.Join(
            " -> ",
            room.Agents.Where(a => a.IsEnabled).Select(a => a.Name));
        var transcript = string.Join(
            "\n\n",
            recentTurns.Select(t => $"{t.Speaker}:\n{t.Content}"));
        var durable = string.IsNullOrWhiteSpace(durableMemory)
            ? "(No durable world memory yet.)"
            : TakeTail(durableMemory, 1600);
        var shared = string.IsNullOrWhiteSpace(sharedRoomMemory)
            ? "(No recent shared room memory yet.)"
            : TakeTail(sharedRoomMemory, (includeDurableMemory ? 2000 : 1400) - (hasRecalledContext ? 400 : 0));
        var privateMemory = BuildAgentPrivateMemory(agent, agentLongMemory, agentShortMemory);
        var durableSection = includeDurableMemory
            ? $"\n<durable_theme_memory>\n{durable}\n</durable_theme_memory>\n"
            : string.Empty;
        var recalledSection = BuildRecalledContextSection(recalledScenes);
        var recalledInstruction = hasRecalledContext
            ? "- Use recalled context only when it directly informs your response. Do not restate recalled details unless they materially change what you say."
            : string.Empty;
        var actorFocusInstruction = includeDurableMemory
            ? "- You are later in the round. Synthesize the latest participant actions when advancing the conversation."
            : "- Focus on one concrete move from your own perspective instead of restating the whole scene.";

        return $"""
You are participating in a multi-agent conversation.

<context>
Theme name: {room.Name}
Theme topic: {room.Topic}
Participants in order: {participants}
Current round: {iteration} of {maxIterations}
You are: {agent.Name}
</context>

<agent_private_memory>
{privateMemory}
</agent_private_memory>

{durableSection}
{recalledSection}
<shared_room_memory>
{shared}
</shared_room_memory>

<recent_transcript>
{(string.IsNullOrWhiteSpace(transcript) ? "(No recent transcript yet.)" : transcript)}
</recent_transcript>

Output format:
- Return exactly two XML blocks and no other text.
- `<reply>` contains the visible in-character response for this turn.
- `<future_note>` contains a short private scratchpad for your future self and is never shown to other participants.
- Always close both XML blocks. If you have no useful scratchpad, emit `<future_note></future_note>` rather than omitting or truncating it.
- Keep `<future_note>` concrete and brief. Use only the sections that matter, with short bullets under bracketed headers like `[Listening For]`, `[Considering]`, `[Likely Next Lever]`, and `[Emotional Posture]`.

Turn requirements:
- Stay consistent with your own system prompt.
- Inside `<reply>`, respond only as {agent.Name}. 
- Continue naturally from the latest conversation turn.
- Use agent-private memory for your own priorities and unresolved personal state.
- Use shared room memory for current tactical context.
- Use durable theme memory only when it is included and only for stable facts that matter now.
- Use the transcript for immediate continuity and voice.
- Rely on the recent transcript window below instead of reconstructing omitted history.
{actorFocusInstruction}
{recalledInstruction}
- In `<future_note>`, capture what you are listening for, weighing, or likely to do next instead of summarizing the whole scene.
- Inside `<reply>`, do not repeat transcript headings, memory labels, or prompt scaffolding.
- Do not output phrases like 'Recent transcript window:' or restate the full transcript unless absolutely necessary.
""";
    }

    public string BuildSharedRoomMemorySystemPrompt(RoomConfig room, SceneSummarizerProfile profile)
    {
        if (IsCustomLevel(room.SummarizationLevel)
            && !string.IsNullOrWhiteSpace(room.SummarizerPromptOverride))
        {
            return room.SummarizerPromptOverride.Trim();
        }

        return BuildDefaultSharedRoomMemorySystemPrompt(profile);
    }

    public string BuildSharedRoomMemoryUserPrompt(
        RoomConfig room,
        SceneSummarizerProfile profile,
        string existingRoomMemory,
        string durableMemory,
        IReadOnlyList<TranscriptTurn> roundTurns,
        IReadOnlyList<TranscriptTurn> sessionTurns)
    {
        var participantNames = room.Agents.Where(a => a.IsEnabled).Select(a => a.Name).ToList();
        var latestRound = roundTurns.Select(t => $"{t.Speaker}: {t.Content}");
        var priorCount = Math.Max(profile.BroaderTranscriptTurns + roundTurns.Count, roundTurns.Count);
        var broader = sessionTurns
            .TakeLast(priorCount)
            .Take(Math.Max(priorCount - roundTurns.Count, 0))
            .Select(t => $"{t.Speaker}: {t.Content}");

        return $"""
Theme topic: {room.Topic}
Participants: {string.Join(", ", participantNames)}

Existing shared room memory:
{(string.IsNullOrWhiteSpace(existingRoomMemory) ? "(none)" : existingRoomMemory)}

Durable memory for reference:
{(string.IsNullOrWhiteSpace(durableMemory) ? "(none)" : durableMemory)}

Latest completed round:
{string.Join("\n", latestRound)}

Earlier recent transcript window:
{(broader.Any() ? string.Join("\n", broader) : "(none beyond the latest round)")}

Rewrite only the shared room memory so it reflects the latest state accurately.
""";
    }

    public string BuildDurableMemorySystemPrompt()
    {
        return """
You maintain hidden durable memory for a multi-agent conversation.

Your job is to preserve stable facts that should survive many rounds.
Be conservative. Do not roleplay. Do not write prose paragraphs.

Return exactly one tagged section and nothing else:
<durable_memory>
[Sticky Facts]
- bullet
[Ongoing Threads]
- bullet
</durable_memory>

Rules:
- Sticky Facts are world truths, named entities, persistent locations, established relationships, major inventory, injuries, promises, and explicit quest state.
- Ongoing Threads are longer-running goals, obligations, unresolved dangers, and active investigations.
- Do not include stylistic flavor, temporary reactions, or one-off wording.
- Do not remove existing durable facts unless the latest transcript clearly contradicts them.
- Prefer keeping a fact over dropping it when the fact still appears relevant.
- Put the tagged section directly in the visible answer. Do not emit reasoning traces or internal analysis.
""";
    }

    public string BuildDurableMemoryUserPrompt(
        RoomConfig room,
        string existingDurableMemory,
        string sharedRoomMemory,
        IReadOnlyList<TranscriptTurn> roundTurns,
        IReadOnlyList<TranscriptTurn> sessionTurns,
        int recentTurnsWindow)
    {
        var participantNames = room.Agents.Where(a => a.IsEnabled).Select(a => a.Name).ToList();
        var latestRound = roundTurns.Select(t => $"{t.Speaker}: {t.Content}");
        var broader = sessionTurns
            .TakeLast(Math.Max(recentTurnsWindow + (room.Agents.Count * 4), 12))
            .Select(t => $"{t.Speaker}: {t.Content}");

        return $"""
Theme topic: {room.Topic}
Participants: {string.Join(", ", participantNames)}

Existing durable memory:
{(string.IsNullOrWhiteSpace(existingDurableMemory) ? "(none)" : existingDurableMemory)}

Current shared room memory:
{(string.IsNullOrWhiteSpace(sharedRoomMemory) ? "(none)" : sharedRoomMemory)}

Latest completed round:
{string.Join("\n", latestRound)}

Broader recent transcript window:
{string.Join("\n", broader)}

Update the durable memory conservatively.
""";
    }

    public SceneSummarizerProfile GetSummarizerProfile(RoomConfig room)
    {
        var level = NormalizeLevel(room.SummarizationLevel);
        var defaults = level switch
        {
            "Aggressive" => (500, 18, 3600, 3,
                "Favor immediate tactical state over flavor or recap. Drop detail that will not affect the next one or two turns.",
                "Use at most 1-2 bullets per section."),
            "Semi-Aggressive" => (400, 22, 4200, 3,
                "Keep only details that materially change near-term choices, risks, or positioning.",
                "Use at most 2 bullets per section."),
            "Semi-Relaxed" => (600, 34, 6400, 9,
                "Keep more tactical and atmospheric detail when it is likely to matter in the next few exchanges.",
                "Use at most 4 bullets per section."),
            "Relaxed" => (700, 40, 8000, 9,
                "Retain richer short-term continuity, but still avoid transcript-style restatement.",
                "Use at most 5 bullets per section."),
            "Custom" => (500, 28, 5600, 6,
                "Preserve concrete conversation facts and current state, but trim repetition and low-value flavor.",
                "Use the available line budget carefully and keep each section concise."),
            _ => (500, 28, 5600, 6,
                "Preserve concrete conversation facts and current state, but trim repetition and low-value flavor.",
                "Use at most 3 bullets per section."),
        };

        if (!IsCustomLevel(level))
        {
            return new SceneSummarizerProfile(
                defaults.Item1, defaults.Item2, defaults.Item3, defaults.Item4,
                defaults.Item5, defaults.Item6);
        }

        return new SceneSummarizerProfile(
            room.SummarizerMaxTokens >= 140 ? room.SummarizerMaxTokens : defaults.Item1,
            room.SummarizerMaxLines >= 12 ? room.SummarizerMaxLines : defaults.Item2,
            room.SummarizerMaxCharacters >= 1800 ? room.SummarizerMaxCharacters : defaults.Item3,
            room.SummarizerBroaderTurns >= 1 ? room.SummarizerBroaderTurns : defaults.Item4,
            defaults.Item5, defaults.Item6);
    }

    private static string BuildDefaultSharedRoomMemorySystemPrompt(SceneSummarizerProfile profile)
    {
        return """
You maintain hidden shared room memory for a multi-agent conversation.

Your job is to preserve enough context for the next few rounds without carrying fluff.
Do not roleplay. Do not imitate any character voice. Do not produce prose paragraphs.

Return exactly one tagged section and nothing else:
<shared_room_memory>
- [Current State]
- bullet
- [Actor Intent]
- bullet
- [Open Threads]
- bullet
- [Recent Important Events]
- bullet
</shared_room_memory>

Rules:
- Preserve the current situation with moderate detail for the next few rounds.
- Keep proper nouns, locations, distances, inventory, injuries, risks, recent agreements, and unresolved actions when they matter.
- Actor Intent should keep one or two bullets per major actor when relevant.
- Open Threads should preserve unresolved hooks and pending questions.
- Recent Important Events should keep the few events most likely to matter immediately.
- Remove fluff, style words, filler, and repeated phrasing.
- Do not flatten everything into a single generic list.
- Keep bullet fragments concise and information-dense.
- Put the tagged section directly in the visible answer. Do not emit reasoning traces or internal analysis.
"""
        + $"\n- {profile.CompressionInstruction}"
        + $"\n- {profile.SectionBudgetInstruction}";
    }

    public static string ExtractTaggedContent(string value, string tagName)
    {
        var openTag = $"<{tagName}>";
        var closeTag = $"</{tagName}>";
        var start = value.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;
        start += openTag.Length;
        var end = value.IndexOf(closeTag, start, StringComparison.OrdinalIgnoreCase);
        return end < 0 ? value[start..].Trim() : value[start..end].Trim();
    }

    public static string SanitizeStructuredMemoryBlock(string value, int maxLines, int maxCharacters)
    {
        var cleanedLines = new List<string>();
        var seenBullets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in value.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("<")) continue;

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                if (cleanedLines.Count > 0 && cleanedLines[^1].Length > 0) cleanedLines.Add(string.Empty);
                cleanedLines.Add(line);
                continue;
            }

            var bullet = line.TrimStart('-', '*', '•').Trim();
            if (string.IsNullOrWhiteSpace(bullet) || !seenBullets.Add(bullet)) continue;
            cleanedLines.Add($"- {bullet}");
            if (cleanedLines.Count(i => !string.IsNullOrWhiteSpace(i)) >= maxLines) break;
        }

        while (cleanedLines.Count > 0 && string.IsNullOrWhiteSpace(cleanedLines[^1]))
            cleanedLines.RemoveAt(cleanedLines.Count - 1);

        var merged = string.Join("\n", cleanedLines);
        return merged.Length <= maxCharacters ? merged : merged[..maxCharacters].Trim();
    }

    private static string NormalizeLevel(string? value) =>
        value?.Trim() switch
        {
            "Aggressive" => "Aggressive",
            "Custom" => "Custom",
            "Semi-Aggressive" => "Semi-Aggressive",
            "Semi-Relaxed" => "Semi-Relaxed",
            "Relaxed" => "Relaxed",
            _ => "Moderate",
        };

    private static bool IsCustomLevel(string? value) =>
        string.Equals(NormalizeLevel(value), "Custom", StringComparison.Ordinal);

    private static string BuildAgentPrivateMemory(
        AgentConfig agent,
        string agentLongMemory,
        string agentShortMemory)
    {
        var privateBudget = Math.Clamp(
            agent.CompactionBudget > 0 ? agent.CompactionBudget : 420,
            120, 900);
        var hasShortMemory = !string.IsNullOrWhiteSpace(agentShortMemory);
        var hasLongMemory = !string.IsNullOrWhiteSpace(agentLongMemory);
        var longBudget = hasShortMemory && hasLongMemory
            ? Math.Clamp(privateBudget / 3, 80, 320)
            : privateBudget;
        var shortBudget = hasShortMemory && hasLongMemory
            ? Math.Clamp(privateBudget - longBudget, 120, 600)
            : privateBudget;
        var sections = new List<string>();

        if (hasShortMemory)
        {
            sections.Add($"[Short-Term]\n{TakeTail(agentShortMemory, shortBudget)}");
        }

        if (hasLongMemory)
        {
            sections.Add($"[Long-Term]\n{TakeTail(agentLongMemory, longBudget)}");
        }

        if (sections.Count == 0)
            return "(No agent-private memory yet.)";

        var combined = string.Join("\n\n", sections);
        return combined.Length <= privateBudget ? combined : combined[..privateBudget].Trim();
    }

    private static string TakeTail(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[^maxLength..];

    private static string BuildRecalledContextSection(IReadOnlyList<SceneArchive>? scenes)
    {
        if (scenes is null || scenes.Count == 0) return string.Empty;

        var entries = new List<string>();
        var totalLength = 0;
        const int budget = 1200;

        foreach (var scene in scenes)
        {
            var header = $"[Round {scene.RoundNumber}: {scene.Label}]";
            var snapshot = scene.SharedRoomSnapshot;
            if (snapshot.Length > 500)
                snapshot = snapshot[..500].Trim();

            var entry = $"{header}\n{snapshot}";
            if (totalLength + entry.Length > budget && entries.Count > 0)
                break;

            entries.Add(entry);
            totalLength += entry.Length;
        }

        if (entries.Count == 0) return string.Empty;

        return $"\n<recalled_context>\n{string.Join("\n\n", entries)}\n</recalled_context>\n";
    }
}
