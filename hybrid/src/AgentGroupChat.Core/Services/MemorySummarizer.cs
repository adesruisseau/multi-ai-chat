using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Models.Llm;
using AgentGroupChat.Core.Services.Interfaces;

namespace AgentGroupChat.Core.Services;

public sealed class MemorySummarizer
{
    private readonly LlmClient _llmClient;
    private readonly PromptComposer _promptComposer;
    private readonly IMemoryRepository _memoryRepo;

    public MemorySummarizer(
        LlmClient llmClient,
        PromptComposer promptComposer,
        IMemoryRepository memoryRepo)
    {
        _llmClient = llmClient;
        _promptComposer = promptComposer;
        _memoryRepo = memoryRepo;
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
            var roomMemory = PromptComposer.ExtractTaggedContent(roomResult.Content, "shared_room_memory");
            if (string.IsNullOrWhiteSpace(roomMemory))
                roomMemory = PromptComposer.ExtractTaggedContent(roomResult.Content, "scene_summary");
            if (string.IsNullOrWhiteSpace(roomMemory))
                roomMemory = BuildFallback(room, sessionTurns, profile);

            roomMemory = PromptComposer.SanitizeStructuredMemoryBlock(roomMemory, profile.MaxLines, profile.MaxCharacters);
            await _memoryRepo.SaveAsync(room.Id, null, MemoryKind.SharedRoom, roomMemory);
            onLog?.Invoke($"Updated shared room memory ({roomMemory.Length} chars).");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var fallback = BuildFallback(room, sessionTurns, profile);
            await _memoryRepo.SaveAsync(room.Id, null, MemoryKind.SharedRoom, fallback);
            onLog?.Invoke($"Room memory fallback used: {ex.Message}");
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
            var durableMemory = PromptComposer.ExtractTaggedContent(durableResult.Content, "durable_memory");
            if (!string.IsNullOrWhiteSpace(durableMemory))
            {
                durableMemory = PromptComposer.SanitizeStructuredMemoryBlock(durableMemory, 32, 8200);
                await _memoryRepo.SaveAsync(room.Id, null, MemoryKind.Durable, durableMemory);
                onLog?.Invoke($"Updated durable memory ({durableMemory.Length} chars).");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            onLog?.Invoke($"Durable memory update failed: {ex.Message}");
        }
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
}
