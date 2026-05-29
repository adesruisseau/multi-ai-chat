using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Models.Llm;
using AgentGroupChat.Core.Services.Interfaces;

namespace AgentGroupChat.Core.Services;

public sealed class MemorySummarizer
{
    private readonly LlmClient _llmClient;
    private readonly PromptComposer _promptComposer;
    private readonly IMemoryRepository _memoryRepo;
    private readonly ISceneArchiveRepository _sceneArchiveRepo;

    public MemorySummarizer(
        LlmClient llmClient,
        PromptComposer promptComposer,
        IMemoryRepository memoryRepo,
        ISceneArchiveRepository sceneArchiveRepo)
    {
        _llmClient = llmClient;
        _promptComposer = promptComposer;
        _memoryRepo = memoryRepo;
        _sceneArchiveRepo = sceneArchiveRepo;
    }

    public async Task RefreshAsync(
        RoomConfig room,
        LlmRequestSettings baseSummarizerSettings,
        IReadOnlyList<TranscriptTurn> roundTurns,
        IReadOnlyList<TranscriptTurn> sessionTurns,
        bool shouldPromoteDurable,
        Action<string>? onLog,
        CancellationToken ct)
    {
        if (roundTurns.Count == 0) return;

        var existingRoomMemory = await _memoryRepo.GetAsync(room.Id, null, MemoryKind.SharedRoom);
        var existingDurableMemory = await _memoryRepo.GetAsync(room.Id, null, MemoryKind.Durable);

        if (room.EnableSceneArchive && shouldPromoteDurable && !string.IsNullOrWhiteSpace(existingRoomMemory))
        {
            await TryArchiveSceneAsync(room, baseSummarizerSettings, existingRoomMemory,
                existingDurableMemory, roundTurns, onLog, ct);
        }

        var profile = _promptComposer.GetSummarizerProfile(room);

        var roomSettings = baseSummarizerSettings with
        {
            MaxCompletionTokens = Math.Clamp(
                Math.Min(baseSummarizerSettings.MaxCompletionTokens, profile.MaxCompletionTokens),
                140, profile.MaxCompletionTokens),
        };

        try
        {
            var roomMessages = new List<LlmChatMessage>
            {
                new("system", _promptComposer.BuildSharedRoomMemorySystemPrompt(room, profile)),
                new("user", _promptComposer.BuildSharedRoomMemoryUserPrompt(
                    room, profile, existingRoomMemory, existingDurableMemory, roundTurns, sessionTurns)),
            };
            var roomResult = await _llmClient.CompleteAsync(roomSettings, roomMessages, ct);
            var roomMemory = TryExtractTrustedMemoryBlock(
                roomResult, profile.MaxLines, profile.MaxCharacters,
                out var roomRejectReason, "shared_room_memory", "scene_summary");

            if (!string.IsNullOrWhiteSpace(roomMemory))
            {
                await _memoryRepo.SaveAsync(room.Id, null, MemoryKind.SharedRoom, roomMemory);
                onLog?.Invoke($"Updated shared room memory ({roomMemory.Length} chars).");
            }
            else if (string.IsNullOrWhiteSpace(existingRoomMemory))
            {
                var fallback = BuildFallback(room, sessionTurns, profile);
                await _memoryRepo.SaveAsync(room.Id, null, MemoryKind.SharedRoom, fallback);
                onLog?.Invoke($"Room memory fallback used: {roomRejectReason}");
            }
            else
            {
                onLog?.Invoke($"Shared room memory preserved: {roomRejectReason}");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (string.IsNullOrWhiteSpace(existingRoomMemory))
            {
                var fallback = BuildFallback(room, sessionTurns, profile);
                await _memoryRepo.SaveAsync(room.Id, null, MemoryKind.SharedRoom, fallback);
                onLog?.Invoke($"Room memory fallback used: {ex.Message}");
            }
            else
            {
                onLog?.Invoke($"Shared room memory preserved after failure: {ex.Message}");
            }
        }

        if (!shouldPromoteDurable) return;

        try
        {
            var durableSettings = baseSummarizerSettings with
            {
                MaxCompletionTokens = Math.Clamp(
                    Math.Min(baseSummarizerSettings.MaxCompletionTokens, 1200), 280, 1200),
            };
            var sharedRoomMemory = await _memoryRepo.GetAsync(room.Id, null, MemoryKind.SharedRoom);
            var durableMessages = new List<LlmChatMessage>
            {
                new("system", _promptComposer.BuildDurableMemorySystemPrompt()),
                new("user", _promptComposer.BuildDurableMemoryUserPrompt(
                    room, existingDurableMemory, sharedRoomMemory, roundTurns, sessionTurns, room.RecentTurnsWindow)),
            };
            var durableResult = await _llmClient.CompleteAsync(durableSettings, durableMessages, ct);
            var durableMemory = TryExtractTrustedMemoryBlock(
                durableResult, 32, 8200, out var durableRejectReason, "durable_memory");
            if (!string.IsNullOrWhiteSpace(durableMemory))
            {
                await _memoryRepo.SaveAsync(room.Id, null, MemoryKind.Durable, durableMemory);
                onLog?.Invoke($"Updated durable memory ({durableMemory.Length} chars).");
            }
            else
            {
                onLog?.Invoke($"Durable memory preserved: {durableRejectReason}");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            onLog?.Invoke($"Durable memory update failed: {ex.Message}");
        }
    }

    private static string? TryExtractTrustedMemoryBlock(
        LlmCompletionResult result,
        int maxLines,
        int maxCharacters,
        out string rejectReason,
        params string[] tagNames)
    {
        if (HasUnsafeCompletionReason(result.CompletionReason))
        {
            rejectReason = $"completion finished with '{result.CompletionReason}'";
            return null;
        }

        foreach (var tagName in tagNames)
        {
            if (!HasCompleteTaggedSection(result.Content, tagName))
                continue;

            var value = PromptComposer.ExtractTaggedContent(result.Content, tagName);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var sanitized = PromptComposer.SanitizeStructuredMemoryBlock(value, maxLines, maxCharacters);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                rejectReason = string.Empty;
                return sanitized;
            }
        }

        rejectReason = $"missing complete tagged output ({string.Join(", ", tagNames)})";
        return null;
    }

    private static bool HasUnsafeCompletionReason(string? completionReason)
    {
        if (string.IsNullOrWhiteSpace(completionReason))
            return false;

        var normalized = completionReason.Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .ToUpperInvariant();

        return normalized is not "STOP" and not "END_TURN";
    }

    private static bool HasCompleteTaggedSection(string value, string tagName)
    {
        var openTag = $"<{tagName}>";
        var closeTag = $"</{tagName}>";
        return value.Contains(openTag, StringComparison.OrdinalIgnoreCase)
            && value.Contains(closeTag, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFallback(RoomConfig room, IReadOnlyList<TranscriptTurn> sessionTurns, SceneSummarizerProfile profile)
    {
        var recentTurns = sessionTurns
            .TakeLast(Math.Max(Math.Min(room.RecentTurnsWindow + 1, 5), 3))
            .Select(t => $"- {t.Speaker}: {t.Content}")
            .ToList();

        return PromptComposer.SanitizeStructuredMemoryBlock(
            string.Join("\n", new[]
            {
                "[Current State]",
                "- Room-memory fallback active; preserve the latest actionable turns below.",
                "",
                "[Recent Important Events]",
            }.Concat(recentTurns)),
            profile.MaxLines, profile.MaxCharacters);
    }

    private async Task TryArchiveSceneAsync(
        RoomConfig room,
        LlmRequestSettings settings,
        string sharedRoomSnapshot,
        string? durableSnapshot,
        IReadOnlyList<TranscriptTurn> roundTurns,
        Action<string>? onLog,
        CancellationToken ct)
    {
        var roundNumber = roundTurns.Count > 0 ? roundTurns.Max(t => t.Round) : 0;
        if (roundNumber <= 0)
        {
            onLog?.Invoke("Scene archive skipped: no valid round number.");
            return;
        }

        var existingArchives = await _sceneArchiveRepo.GetByRoomAsync(room.Id);
        if (existingArchives.Any(a => a.RoundNumber == roundNumber))
        {
            onLog?.Invoke($"Scene archive skipped: round {roundNumber} already archived.");
            return;
        }

        string label;
        string keyEntities;

        try
        {
            var labelSettings = settings with
            {
                MaxCompletionTokens = Math.Min(settings.MaxCompletionTokens, 200),
            };
            var messages = new List<LlmChatMessage>
            {
                new("system", BuildSceneLabelingSystemPrompt()),
                new("user", BuildSceneLabelingUserPrompt(sharedRoomSnapshot, durableSnapshot)),
            };
            var result = await _llmClient.CompleteAsync(labelSettings, messages, ct);
            label = PromptComposer.ExtractTaggedContent(result.Content, "scene_label");
            keyEntities = PromptComposer.ExtractTaggedContent(result.Content, "key_entities");

            if (string.IsNullOrWhiteSpace(label))
                label = $"Round {roundNumber} snapshot";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            label = $"Round {roundNumber} snapshot";
            keyEntities = string.Empty;
            onLog?.Invoke($"Scene labeling failed, using fallback: {ex.Message}");
        }

        var archive = new SceneArchive
        {
            RoomId = room.Id,
            RoundNumber = roundNumber,
            Label = label.Length > 200 ? label[..200] : label,
            KeyEntities = (keyEntities ?? string.Empty).Length > 500
                ? keyEntities![..500]
                : keyEntities ?? string.Empty,
            SharedRoomSnapshot = sharedRoomSnapshot,
            DurableSnapshot = durableSnapshot ?? string.Empty,
            IsMajor = true,
        };

        await _sceneArchiveRepo.AppendAsync(archive);
        await _sceneArchiveRepo.PruneAsync(room.Id, room.MaxArchivedScenes);
        onLog?.Invoke($"Archived scene: \"{label}\" (round {roundNumber})");
    }

    private static string BuildSceneLabelingSystemPrompt() => """
        You are a scene indexer. Create a short label and entity list for the current scene state.

        Return exactly two tagged sections and nothing else:
        <scene_label>short descriptive label (5-15 words, noun-heavy, retrieval-friendly)</scene_label>
        <key_entities>comma-separated proper nouns, locations, items, factions</key_entities>

        Rules:
        - The label should capture what happened, not describe the format.
        - Key entities should list only proper nouns and important named things.
        - Do not write prose, explanations, or reasoning.
        """;

    private static string BuildSceneLabelingUserPrompt(string sharedRoom, string? durable) => $"""
        Current shared room memory being archived:
        {sharedRoom}

        Current durable memory for reference:
        {(string.IsNullOrWhiteSpace(durable) ? "(none)" : durable)}

        Create a label and entity list for this scene.
        """;
}
