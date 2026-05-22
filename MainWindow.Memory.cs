namespace AgentGroupChat;

public partial class MainWindow
{
    private async Task RefreshAutomaticMemoryAsync(
        ThemeProfile theme,
        ConnectionSettings settings,
        IReadOnlyList<SessionTurn> roundTurns,
        IReadOnlyList<SessionTurn> sessionTurns,
        int recentTurnsWindow,
        bool shouldPromoteDurableMemory,
        CancellationToken cancellationToken,
        bool updateStatus = true)
    {
        if (roundTurns.Count == 0)
        {
            return;
        }

        try
        {
            var summarizerProvider = string.IsNullOrWhiteSpace(theme.SummarizerProvider)
                ? settings.SummarizerProvider
                : theme.SummarizerProvider;
            var baseSummarizerSettings = BuildRequestSettings(settings, summarizerProvider, theme.MaxTokens);
            var roomMemoryProfile = GetSceneSummarizerProfile(theme, recentTurnsWindow);
            var roomMemorySummarizerSettings = baseSummarizerSettings with
            {
                MaxCompletionTokens = Math.Clamp(Math.Min(baseSummarizerSettings.MaxCompletionTokens, roomMemoryProfile.MaxCompletionTokens), 140, roomMemoryProfile.MaxCompletionTokens),
                HuggingFaceReasoningEffort = baseSummarizerSettings.Provider == LlmProvider.HuggingFace
                    ? "none"
                    : baseSummarizerSettings.HuggingFaceReasoningEffort,
            };
            var durableSummarizerSettings = baseSummarizerSettings with
            {
                MaxCompletionTokens = Math.Clamp(Math.Min(baseSummarizerSettings.MaxCompletionTokens, 1200), 280, 1200),
                HuggingFaceReasoningEffort = baseSummarizerSettings.Provider == LlmProvider.HuggingFace
                    ? "none"
                    : baseSummarizerSettings.HuggingFaceReasoningEffort,
            };

            var roomMemoryResponse = await RunWithElapsedStatusAsync(
                "Updating shared room memory...",
                CompleteWithLoggingAsync(
                    "Summarizer.RoomMemory",
                    roomMemorySummarizerSettings,
                    new[]
                    {
                        new LlmChatMessage("system", BuildSharedRoomMemorySummarizerSystemPrompt(theme, roomMemoryProfile)),
                        new LlmChatMessage("user", BuildSharedRoomMemorySummarizerUserPrompt(theme, roomMemoryProfile, roundTurns, sessionTurns)),
                    },
                    cancellationToken),
                cancellationToken,
                updateStatus);

            var roomMemorySummary = ExtractSharedRoomMemoryContent(roomMemoryResponse);

            if (string.IsNullOrWhiteSpace(roomMemorySummary))
            {
                roomMemorySummary = GetSharedRoomMemoryFallback(theme, sessionTurns, recentTurnsWindow, roomMemoryProfile);
            }

            ApplySharedRoomMemory(theme, SanitizeStructuredMemoryBlock(roomMemorySummary, roomMemoryProfile.MaxLines, roomMemoryProfile.MaxCharacters));
            AddLog(LogCategory.Memory, "Summarizer.RoomMemory", "Updated shared room memory.", roomMemorySummary);

            if (!shouldPromoteDurableMemory)
            {
                return;
            }

            var durableResponse = await RunWithElapsedStatusAsync(
                "Updating durable memory...",
                CompleteWithLoggingAsync(
                    "Summarizer.Durable",
                    durableSummarizerSettings,
                    new[]
                    {
                        new LlmChatMessage("system", BuildDurableMemorySummarizerSystemPrompt()),
                        new LlmChatMessage("user", BuildDurableMemorySummarizerUserPrompt(theme, roundTurns, sessionTurns, recentTurnsWindow)),
                    },
                    cancellationToken),
                cancellationToken,
                updateStatus);

            var durableMemory = ExtractTaggedContent(durableResponse, "durable_memory");
            if (string.IsNullOrWhiteSpace(durableMemory))
            {
                durableMemory = _themeDurableMemory;
            }

            ApplyDurableThemeMemory(theme, SanitizeStructuredMemoryBlock(durableMemory, 32, 8200));
            AddLog(LogCategory.Memory, "Summarizer.Durable", "Updated durable memory.", durableMemory);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var roomMemoryProfile = GetSceneSummarizerProfile(theme, recentTurnsWindow);
            ApplySharedRoomMemory(theme, GetSharedRoomMemoryFallback(theme, sessionTurns, recentTurnsWindow, roomMemoryProfile));
            AddSystemMessage($"Memory refresh fallback used: {exception.Message}");
        }
    }

    private string BuildSharedRoomMemorySummarizerSystemPrompt(ThemeProfile theme, SceneSummarizerProfile profile)
    {
        if (IsCustomSceneSummarizationLevel(theme.SceneSummarizationLevel)
            && !string.IsNullOrWhiteSpace(theme.SceneSummarizerPromptOverride))
        {
            return theme.SceneSummarizerPromptOverride.Trim();
        }

        return BuildDefaultSharedRoomMemorySummarizerSystemPrompt(profile);
    }

    private static string BuildDefaultSharedRoomMemorySummarizerSystemPrompt(SceneSummarizerProfile profile)
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

    private string BuildSharedRoomMemorySummarizerUserPrompt(
        ThemeProfile theme,
        SceneSummarizerProfile profile,
        IReadOnlyList<SessionTurn> roundTurns,
        IReadOnlyList<SessionTurn> sessionTurns)
    {
        var participantNames = theme.Agents
            .Where(agent => agent.IsEnabled)
            .Select(agent => agent.Name)
            .ToList();
        var latestRoundTranscript = roundTurns
            .Select(turn => $"{turn.Speaker}: {CondenseTurnForPrompt(theme, turn)}");
        var priorRecentTurnCount = Math.Max(profile.BroaderTranscriptTurns + roundTurns.Count, roundTurns.Count);
        var broaderRecentTranscript = sessionTurns
            .TakeLast(priorRecentTurnCount)
            .Take(Math.Max(priorRecentTurnCount - roundTurns.Count, 0))
            .Select(turn => $"{turn.Speaker}: {CondenseTurnForPrompt(theme, turn)}");

        var roundTranscript = string.Join("\n", latestRoundTranscript);
        var recentTranscript = string.Join("\n", broaderRecentTranscript);
        var existingRoomMemory = string.IsNullOrWhiteSpace(_sharedRoomMemory)
            ? "(none)"
            : _sharedRoomMemory;

        return $"""
Theme topic: {theme.Topic}
Participants: {string.Join(", ", participantNames)}

Existing shared room memory:
{existingRoomMemory}

Durable memory for reference:
{(string.IsNullOrWhiteSpace(_themeDurableMemory) ? "(none)" : _themeDurableMemory)}

Latest completed round:
{roundTranscript}

Earlier recent transcript window:
{(string.IsNullOrWhiteSpace(recentTranscript) ? "(none beyond the latest round)" : recentTranscript)}

Rewrite only the shared room memory so it reflects the latest state accurately.
""";
    }

    private string GetSharedRoomMemoryFallback(
        ThemeProfile theme,
        IReadOnlyList<SessionTurn> sessionTurns,
        int recentTurnsWindow,
        SceneSummarizerProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(_sharedRoomMemory))
        {
            return SanitizeStructuredMemoryBlock(_sharedRoomMemory, profile.MaxLines, profile.MaxCharacters);
        }

        return BuildFallbackSharedRoomMemory(theme, sessionTurns, recentTurnsWindow, profile);
    }

    private string BuildDurableMemorySummarizerSystemPrompt()
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

    private string BuildDurableMemorySummarizerUserPrompt(
        ThemeProfile theme,
        IReadOnlyList<SessionTurn> roundTurns,
        IReadOnlyList<SessionTurn> sessionTurns,
        int recentTurnsWindow)
    {
        var participantNames = theme.Agents
            .Where(agent => agent.IsEnabled)
            .Select(agent => agent.Name)
            .ToList();
        var latestRoundTranscript = roundTurns
            .Select(turn => $"{turn.Speaker}: {CondenseTurnForPrompt(theme, turn)}");
        var broaderRecentTranscript = sessionTurns
            .TakeLast(Math.Max(recentTurnsWindow + (theme.Agents.Count * 4), 12))
            .Select(turn => $"{turn.Speaker}: {CondenseTurnForPrompt(theme, turn)}");

        return $"""
Theme topic: {theme.Topic}
Participants: {string.Join(", ", participantNames)}

Existing durable memory:
{(string.IsNullOrWhiteSpace(_themeDurableMemory) ? "(none)" : _themeDurableMemory)}

Current shared room memory:
{(string.IsNullOrWhiteSpace(_sharedRoomMemory) ? "(none)" : _sharedRoomMemory)}

Latest completed round:
{string.Join("\n", latestRoundTranscript)}

Broader recent transcript window:
{string.Join("\n", broaderRecentTranscript)}

Update the durable memory conservatively.
""";
    }

    private void LoadAutomaticThemeMemory(ThemeProfile theme)
    {
        _sharedRoomMemory = _themeStore.LoadThemeSharedRoomMemory(theme);
        _themeDurableMemory = _themeStore.LoadThemeDurableMemory(theme);
        OnPropertyChanged(nameof(SharedRoomMemoryDisplay));
        OnPropertyChanged(nameof(ThemeDurableMemoryDisplay));

        foreach (var agent in theme.Agents)
        {
            agent.ShortTermMemory = _sharedRoomMemory;
            agent.ShortTermMemoryFilePath = _themeStore.GetThemeSharedRoomMemoryPath(theme);
        }
    }

    private void ApplySharedRoomMemory(ThemeProfile theme, string roomMemory)
    {
        _themeStore.SaveThemeSharedRoomMemory(theme, roomMemory);
        _sharedRoomMemory = _themeStore.LoadThemeSharedRoomMemory(theme);
        OnPropertyChanged(nameof(SharedRoomMemoryDisplay));

        foreach (var agent in theme.Agents)
        {
            agent.ShortTermMemory = _sharedRoomMemory;
            agent.ShortTermMemoryFilePath = _themeStore.GetThemeSharedRoomMemoryPath(theme);
        }
    }

    private void ApplyDurableThemeMemory(ThemeProfile theme, string durableMemory)
    {
        _themeStore.SaveThemeDurableMemory(theme, durableMemory);
        _themeDurableMemory = _themeStore.LoadThemeDurableMemory(theme);
        OnPropertyChanged(nameof(ThemeDurableMemoryDisplay));
    }

    private static string BuildFallbackSharedRoomMemory(
        ThemeProfile theme,
        IReadOnlyList<SessionTurn> sessionTurns,
        int recentTurnsWindow,
        SceneSummarizerProfile profile)
    {
        var recentTurns = sessionTurns
            .TakeLast(Math.Max(Math.Min(recentTurnsWindow + 1, 5), 3))
            .Select(turn => $"- {turn.Speaker}: {CondenseTurnForPrompt(theme, turn)}")
            .ToList();

        return SanitizeStructuredMemoryBlock(
            string.Join(
                "\n",
                new[]
                {
                    "[Current State]",
                    "- Room-memory fallback active; preserve the latest actionable turns below.",
                    string.Empty,
                    "[Recent Important Events]",
                }
                .Concat(recentTurns)),
            profile.MaxLines,
            profile.MaxCharacters);
    }

    private static string ExtractSharedRoomMemoryContent(string value)
    {
        var sharedRoomMemory = ExtractTaggedContent(value, "shared_room_memory");
        return !string.IsNullOrWhiteSpace(sharedRoomMemory)
            ? sharedRoomMemory
            : ExtractTaggedContent(value, "scene_summary");
    }

    private static string ExtractTaggedContent(string value, string tagName)
    {
        var openTag = $"<{tagName}>";
        var closeTag = $"</{tagName}>";
        var start = value.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        start += openTag.Length;
        var end = value.IndexOf(closeTag, start, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return value[start..].Trim();
        }

        return value[start..end].Trim();
    }

    private static string NormalizeSceneSummarizationLevel(string? value)
    {
        return value?.Trim() switch
        {
            "Aggressive" => "Aggressive",
            "Custom" => "Custom",
            "Semi-Aggressive" => "Semi-Aggressive",
            "Semi-Relaxed" => "Semi-Relaxed",
            "Relaxed" => "Relaxed",
            _ => "Moderate",
        };
    }

    private static bool IsCustomSceneSummarizationLevel(string? value)
    {
        return string.Equals(NormalizeSceneSummarizationLevel(value), "Custom", StringComparison.Ordinal);
    }

    private static SceneSummarizerProfile GetSceneSummarizerProfile(ThemeProfile theme, int recentTurnsWindow)
    {
        var normalizedLevel = NormalizeSceneSummarizationLevel(theme.SceneSummarizationLevel);
        var defaults = normalizedLevel switch
        {
            "Aggressive" => (
                MaxTokens: 300,
                MaxLines: 18,
                MaxCharacters: 3600,
                BroaderTranscriptTurns: 3,
                CompressionInstruction: "Favor immediate tactical state over flavor or recap. Drop detail that will not affect the next one or two turns.",
                SectionBudgetInstruction: "Use at most 1-2 bullets per section."),
            "Custom" => (
                MaxTokens: 500,
                MaxLines: 28,
                MaxCharacters: 5600,
                BroaderTranscriptTurns: 6,
                CompressionInstruction: "Preserve concrete conversation facts and current state, but trim repetition and low-value flavor.",
                SectionBudgetInstruction: "Use the available line budget carefully and keep each section concise."),
            "Semi-Aggressive" => (
                MaxTokens: 400,
                MaxLines: 22,
                MaxCharacters: 4200,
                BroaderTranscriptTurns: 3,
                CompressionInstruction: "Keep only details that materially change near-term choices, risks, or positioning.",
                SectionBudgetInstruction: "Use at most 2 bullets per section."),
            "Semi-Relaxed" => (
                MaxTokens: 600,
                MaxLines: 34,
                MaxCharacters: 6400,
                BroaderTranscriptTurns: 9,
                CompressionInstruction: "Keep more tactical and atmospheric detail when it is likely to matter in the next few exchanges.",
                SectionBudgetInstruction: "Use at most 4 bullets per section."),
            "Relaxed" => (
                MaxTokens: 700,
                MaxLines: 40,
                MaxCharacters: 8000,
                BroaderTranscriptTurns: 9,
                CompressionInstruction: "Retain richer short-term continuity, but still avoid transcript-style restatement.",
                SectionBudgetInstruction: "Use at most 5 bullets per section."),
            _ => (
                MaxTokens: 500,
                MaxLines: 28,
                MaxCharacters: 5600,
                BroaderTranscriptTurns: 6,
                CompressionInstruction: "Preserve concrete conversation facts and current state, but trim repetition and low-value flavor.",
                SectionBudgetInstruction: "Use at most 3 bullets per section."),
        };

            if (!IsCustomSceneSummarizationLevel(normalizedLevel))
            {
                return new SceneSummarizerProfile(
                defaults.MaxTokens,
                defaults.MaxLines,
                defaults.MaxCharacters,
                defaults.BroaderTranscriptTurns,
                defaults.CompressionInstruction,
                defaults.SectionBudgetInstruction);
            }

        return new SceneSummarizerProfile(
            theme.SceneSummarizerMaxTokens >= 140 ? theme.SceneSummarizerMaxTokens : defaults.MaxTokens,
            theme.SceneSummarizerMaxLines >= 12 ? theme.SceneSummarizerMaxLines : defaults.MaxLines,
            theme.SceneSummarizerMaxCharacters >= 1800 ? theme.SceneSummarizerMaxCharacters : defaults.MaxCharacters,
            theme.SceneSummarizerBroaderTranscriptTurns >= 1 ? theme.SceneSummarizerBroaderTranscriptTurns : defaults.BroaderTranscriptTurns,
            defaults.CompressionInstruction,
            defaults.SectionBudgetInstruction);
    }

    private static string SanitizeStructuredMemoryBlock(string value, int maxLines, int maxCharacters)
    {
        var cleanedLines = new List<string>();
        var seenBullets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in value
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("<", StringComparison.Ordinal))
            {
                continue;
            }

            var isHeading = line.StartsWith("[") && line.EndsWith("]");
            if (isHeading)
            {
                if (cleanedLines.Count > 0 && cleanedLines[^1].Length > 0)
                {
                    cleanedLines.Add(string.Empty);
                }

                cleanedLines.Add(line);
                continue;
            }

            var bullet = line.TrimStart('-', '*', '•').Trim();
            if (string.IsNullOrWhiteSpace(bullet) || !seenBullets.Add(bullet))
            {
                continue;
            }

            cleanedLines.Add($"- {bullet}");
            if (cleanedLines.Count(item => !string.IsNullOrWhiteSpace(item)) >= maxLines)
            {
                break;
            }
        }

        while (cleanedLines.Count > 0 && string.IsNullOrWhiteSpace(cleanedLines[^1]))
        {
            cleanedLines.RemoveAt(cleanedLines.Count - 1);
        }

        var merged = string.Join("\n", cleanedLines);
        if (merged.Length <= maxCharacters)
        {
            return merged;
        }

        return merged[..maxCharacters].Trim();
    }
}