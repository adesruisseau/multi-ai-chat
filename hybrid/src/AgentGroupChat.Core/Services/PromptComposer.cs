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
        var roomRound = recentTurns.Count();
        var hasRecalledContext = recalledScenes is { Count: > 0 };
        var activeParticipants = room.Agents.Where(IsActiveAgent).OrderBy(a => a.SortOrder).ToList();

        // Build a unified participant list including both humans and AI agents
        var allParticipantNames = (activeParticipants.Select(a => a.Name))
            .ToList();
        var participants = string.Join(" -> ", allParticipantNames);
            
        var transcript = string.Join(
            "\n\n",
            recentTurns.Select(t => $"{t.Speaker}:\n{t.Content}"));
        var durable = string.IsNullOrWhiteSpace(durableMemory)
            ? "(No durable world memory yet.)"
            : TakeTail(durableMemory, 1600);
        var shared = string.IsNullOrWhiteSpace(sharedRoomMemory)
            ? "(No recent shared room memory yet.)"
            : TakeTail(sharedRoomMemory, (includeDurableMemory ? 2000 : 1400) - (hasRecalledContext ? 400 : 0));
        var (offSceneNotice, cleanedShortMemory) = ExtractStructuredSection(agentShortMemory, "[Off-Scene Notice]");
        var privateMemory = BuildAgentPrivateMemory(agent, agentLongMemory, cleanedShortMemory);
        var durableSection = includeDurableMemory
            ? $"\n<durable_theme_memory>\n{durable}\n</durable_theme_memory>\n"
            : string.Empty;
        var recalledSection = BuildRecalledContextSection(recalledScenes);
        var inactiveParticipantsSection = BuildInactiveParticipantsSection(room, agent);
        var privilegedActionsSection = BuildPrivilegedActionsSection(room, agent);
        var offSceneSection = string.IsNullOrWhiteSpace(offSceneNotice)
            ? string.Empty
            : $"\n<offscene_notice>\n{TakeTail(offSceneNotice, 600)}\n</offscene_notice>\n";
        var recalledInstruction = hasRecalledContext
            ? "- Use recalled context only when it directly informs your response. Do not restate recalled details unless they materially change what you say."
            : string.Empty;
        var inactiveParticipantsInstruction = string.IsNullOrWhiteSpace(inactiveParticipantsSection)
            ? string.Empty
            : "- Treat participants listed in inactive_participants as off-scene. Do not address them as present, put dialogue in their mouths, or assume they witnessed this round.";
        var offSceneInstruction = string.IsNullOrWhiteSpace(offSceneNotice)
            ? string.Empty
            : "- You were off-scene for prior rounds. Do not act as if you directly witnessed events that occurred while you were absent unless they were explicitly conveyed to you.";
        var privilegedActionInstruction = string.IsNullOrWhiteSpace(privilegedActionsSection)
            ? string.Empty
            : "- If you request a privileged action, place it only inside a nested <privileged_actions> block within <future_note>. Never place privileged-action tags in <reply>.";

        var prompt = agent.SystemPrompt
                                 .Replace("###roomTopic###", room.Topic)
                                 .Replace("###participants###", participants)
                                 .Replace("###roomRound###", roomRound.ToString())
                                 .Replace("###agentName###", agent.Name)
                                 .Replace("###privateMemory###", privateMemory)
                                 .Replace("###recalledSection###", recalledSection)
                                 .Replace("###roomMemory###", sharedRoomMemory)
                                 .Replace("###durableMemory###", durable)
                                 .Replace("###transcript###", transcript)
                                 + privilegedActionsSection
                                 + inactiveParticipantsSection
                                 + offSceneSection
                                 + privilegedActionInstruction
                                 + inactiveParticipantsInstruction
                                 + offSceneInstruction
                                 ;




        return prompt;
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

Your job is to preserve stable campaign facts that should remain true across many rounds, scenes, and locations.

Be conservative.
Do not roleplay.
Do not narrate.
Do not speculate.
Do not write prose paragraphs.

Return exactly one tagged section and nothing else:

<durable_memory>
[Main Storyline]

* bullet(s)

[Optional Storylines]

* bullet(s)

[Sticky Facts]

* bullet(s)

[Ongoing Threads]

* bullet(s)

[Resolved Threads]

* bullet(s)
  </durable_memory>

Rules:

* Durable memory represents long-lived campaign knowledge.
* Prefer preserving information over rewriting it.
* Only modify existing entries when newer transcripts clearly supersede or resolve them.
* Do not remove facts merely because they were not mentioned recently.
* Do not invent future events, motivations, secrets, or story developments.

[Main Storyline]

* The single primary objective currently driving the campaign.
* Keep this stable whenever possible.
* Do not replace it unless it has been clearly completed, abandoned, or superseded by events.
* Use concise objective-oriented wording rather than narrative summaries.

[Optional Storylines]

* Secondary objectives, side quests, investigations, obligations, relationships, or personal goals.
* Keep these stable until clearly resolved.
* Add new entries only when they persist beyond a single scene.

[Sticky Facts]

* World truths.
* Named entities.
* Persistent locations.
* Established relationships.
* Major inventory or artifacts.
* Lasting injuries, conditions, debts, promises, oaths, or affiliations.
* Explicit quest state that should survive scene transitions.
* Facts should be objective and verifiable.

[Ongoing Threads]

* Long-running mysteries.
* Unresolved dangers.
* Active investigations.
* Campaign-scale obligations.
* Goals expected to span multiple scenes.

[Resolved Threads]

* Recently completed storylines, quests, mysteries, obligations, or investigations.
* Keep resolved entries for a short period to prevent accidental reintroduction.
* Remove older resolved entries when space is needed.

Additional Rules:

* Do not store temporary emotions, dialogue, jokes, scene descriptions, combat narration, travel narration, or momentary tactics.
* Do not store information that only matters within the current scene.
* Prefer factual statements over interpretations.
* Prefer objective state over dramatic summaries.
* Preserve proper nouns whenever available.
* If no durable memory exists, create an initial [Main Storyline] based on the campaign's current primary objective.
* If the current [Main Storyline] is resolved, identify the next campaign-level objective and promote it to [Main Storyline].
* If multiple objectives compete, choose the one most central to the campaign's forward progress.
* The purpose of this memory is to prevent plot drift, forgotten commitments, forgotten characters, and forgotten objectives.
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

Your job is to preserve the active scene and enough context for the next few rounds without carrying unnecessary detail.

Be concise.
Do not roleplay.
Do not imitate character voices.
Do not narrate.
Do not write prose paragraphs.

Return exactly one tagged section and nothing else:

<shared_room_memory>

[Current Scene]
* Purpose:
* Progress:
* Exit Conditions:
[Current State]
* bullet(s)
[Immediate Threads]
* bullet(s)
[Long-Term Threads]
* bullet(s)
[Narrative Momentum]
* Current Mode:
* Desired Next Mode:
* Desired Focus:
[Recent Important Events]
* bullet(s)
</shared_room_memory>
Rules:
General:
* Shared room memory should preserve only information likely to matter within the next few rounds.
* Prefer scene continuity over campaign continuity.
* Remove information that has become irrelevant to the active scene.
* Keep entries concise, factual, and information-dense.
[Current Scene]
* Identify why the current scene exists.
* Progress should describe how close the scene is to fulfilling its purpose.
* Exit Conditions should describe what must occur before the scene naturally transitions.
* When a scene's purpose has been fulfilled, reflect that clearly.
* Do not create new scene purposes unless the transcripts establish one.
[Current State]
* Preserve location, participants, inventory, injuries, risks, ongoing actions, active NPCs, environmental conditions, and important constraints.
* Preserve only details relevant to the next few rounds.
[Actor Intent]
* Keep one or two bullets per major actor when their goals are likely to influence upcoming actions.
* Remove stale intentions once fulfilled or abandoned.
* Prefer immediate goals over personality descriptions.
[Immediate Threads]
* Questions, decisions, obstacles, or opportunities likely to be addressed within the next few rounds.
* Remove threads immediately once resolved.
[Long-Term Threads]
* References to active campaign objectives that still influence the current scene.
* Keep brief.
* Do not duplicate Durable Memory.
[Narrative Momentum]
* Current Mode should describe the current type of play:
  * Dialogue
  * Investigation
  * Travel
  * Encounter
  * Planning
  * Social
  * Exploration
  * Transition
* Desired Next Mode should indicate the most natural next phase based on recent events.
* Desired Focus should describe what the scene should naturally encourage next.
* Do not force escalation.
* Prefer resolution and transition when a scene's purpose has been achieved.
[Recent Important Events]
* Preserve only the few events most likely to affect immediate decisions.
* Remove older events once their consequences have been absorbed into Current State.

Additional Rules:
* Do not store dialogue excerpts unless the exact wording matters.
* Do not store temporary emotions or roleplay flavor.
* Do not store repeated information already represented elsewhere.
* Do not speculate about future events.
* Do not invent hidden motives or story developments.
* If an encounter, chase, negotiation, investigation, or obstacle has already served its purpose, reflect that progress rather than repeatedly restating the obstacle.
* The purpose of this memory is to prevent scene drift, repetitive encounters, forgotten immediate context, and stalled progression.
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

    public static (string Section, string Remaining) ExtractStructuredSection(string value, string sectionHeader)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(sectionHeader))
            return (string.Empty, value);

        var lines = value.Replace("\r", string.Empty).Split('\n');
        var extracted = new List<string>();
        var remaining = new List<string>();
        var inSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            var trimmed = line.Trim();

            if (string.Equals(trimmed, sectionHeader, StringComparison.OrdinalIgnoreCase))
            {
                inSection = true;
                continue;
            }

            if (inSection && trimmed.StartsWith("[") && trimmed.EndsWith("]") && !string.Equals(trimmed, sectionHeader, StringComparison.OrdinalIgnoreCase))
            {
                inSection = false;
            }

            if (inSection)
            {
                if (!string.IsNullOrWhiteSpace(trimmed))
                    extracted.Add(trimmed);
            }
            else if (!string.IsNullOrWhiteSpace(trimmed))
            {
                remaining.Add(trimmed);
            }
        }

        return (string.Join("\n", extracted).Trim(), string.Join("\n", remaining).Trim());
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

    private static bool IsActiveAgent(AgentConfig agent) =>
        agent.IsEnabled && !agent.IsTemporarilySuspended;

    private static string BuildInactiveParticipantsSection(RoomConfig room, AgentConfig currentAgent)
    {
        var inactiveAgents = room.Agents
            .Where(a => a.IsEnabled && !a.IsNpc && a.IsTemporarilySuspended && a.Id != currentAgent.Id)
            .OrderBy(a => a.SortOrder)
            .ToList();
        if (inactiveAgents.Count == 0) return string.Empty;

        var lines = inactiveAgents.Select(a => $"- {a.Name}: {FormatSuspensionDescriptor(a)}");
        return $"\n<inactive_participants>\n{string.Join("\n", lines)}\n</inactive_participants>\n";
    }

    private static string BuildPrivilegedActionsSection(RoomConfig room, AgentConfig agent)
    {
        if (!room.EnablePrivilegedActions || agent.IsNpc || !string.Equals(agent.Id, room.PrivilegedAgentId, StringComparison.Ordinal))
            return string.Empty;

        var activeNpcs = room.Agents
            .Where(a => a.IsEnabled && a.IsNpc)
            .OrderBy(a => a.SortOrder)
            .Select(a => a.Name)
            .ToList();
        var activeNpcsText = activeNpcs.Count == 0 ? "(none)" : string.Join(", ", activeNpcs);
        var npcInstructions = room.EnableNpcSpawning
            ? $"- Use `<spawn_npc name=\"Name\" gender=\"male|female\">description</spawn_npc>` to add a new long-running NPC.\n- Use `<dismiss_npc name=\"Name\">reason</dismiss_npc>` to remove an active NPC.\n- Active NPCs: {activeNpcsText}.\n- Active NPC slots: {activeNpcs.Count}/{Math.Max(1, room.MaxConcurrentNpcs)}.\n- Only spawn NPCs who should remain in play for multiple rounds."
            : "- NPC spawning is disabled for this room.";

        return $"\n<npc_management>\nYou may request privileged lifecycle changes by placing them inside a nested `<privileged_actions>` block within `<future_note>`.\n{npcInstructions}\n- Use `<suspend_agent name=\"Name\" rounds=\"N\">reason</suspend_agent>` to remove a permanent character from the active roster for N upcoming rounds.\n- Use `<resume_agent name=\"Name\">reason</resume_agent>` to return a suspended permanent character to play next round.\n- Only suspend permanent characters who are genuinely off-scene, asleep, separated, or otherwise unavailable.\n- Do not suspend yourself.\n- Use at most two privileged lifecycle actions in one turn.\n- Do not place privileged-action tags in `<reply>`.\n</npc_management>\n";
    }

    private static string FormatSuspensionDescriptor(AgentConfig agent)
    {
        var reason = string.IsNullOrWhiteSpace(agent.SuspensionReason)
            ? "off-scene and not participating this round"
            : agent.SuspensionReason.Trim().TrimEnd('.');
        return agent.SuspendedUntilRound.HasValue
            ? $"{reason} (until round {agent.SuspendedUntilRound.Value})."
            : $"{reason} (until resumed).";
    }

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
