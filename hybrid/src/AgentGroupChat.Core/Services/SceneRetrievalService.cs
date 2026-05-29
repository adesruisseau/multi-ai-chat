using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Models.Llm;
using AgentGroupChat.Core.Services.Interfaces;

namespace AgentGroupChat.Core.Services;

public sealed class SceneRetrievalService
{
    private readonly LlmClient _llmClient;
    private readonly ISceneArchiveRepository _sceneArchiveRepo;

    public SceneRetrievalService(LlmClient llmClient, ISceneArchiveRepository sceneArchiveRepo)
    {
        _llmClient = llmClient;
        _sceneArchiveRepo = sceneArchiveRepo;
    }

    public async Task<IReadOnlyList<SceneArchive>> RetrieveAsync(
        RoomConfig room,
        LlmRequestSettings settings,
        string sharedRoomMemory,
        IReadOnlyList<TranscriptTurn> recentTurns,
        int maxRecall,
        Action<string>? onLog,
        CancellationToken ct)
    {
        if (!room.EnableSceneArchive) return [];

        var candidates = await _sceneArchiveRepo.GetByRoomAsync(room.Id);
        if (candidates.Count == 0) return [];

        var candidateList = new List<string>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var entities = string.IsNullOrWhiteSpace(c.KeyEntities)
                ? string.Empty
                : $" — Entities: {c.KeyEntities}";
            candidateList.Add($"{c.Id}. [Round {c.RoundNumber}] {c.Label}{entities}");
        }

        try
        {
            var retrievalSettings = settings with
            {
                MaxCompletionTokens = Math.Min(settings.MaxCompletionTokens, 100),
            };
            var transcript = string.Join("\n",
                recentTurns.Select(t => $"{t.Speaker}: {t.Content}"));

            var messages = new List<LlmChatMessage>
            {
                new("system", BuildRetrievalSystemPrompt(maxRecall)),
                new("user", BuildRetrievalUserPrompt(sharedRoomMemory, transcript, candidateList)),
            };

            var result = await _llmClient.CompleteAsync(retrievalSettings, messages, ct);
            var selected = PromptComposer.ExtractTaggedContent(result.Content, "selected");

            if (string.IsNullOrWhiteSpace(selected)
                || selected.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                onLog?.Invoke("Scene retrieval: none selected.");
                return [];
            }

            var selectedIds = ParseSelectedIds(selected, candidates);
            if (selectedIds.Count == 0)
            {
                onLog?.Invoke("Scene retrieval: could not parse selection.");
                return [];
            }

            var retrieved = await _sceneArchiveRepo.GetByIdsAsync(selectedIds.Take(maxRecall));
            onLog?.Invoke($"Scene retrieval: selected {retrieved.Count} scene(s) — " +
                          string.Join(", ", retrieved.Select(s => s.Label)));
            return retrieved;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            onLog?.Invoke($"Scene retrieval failed: {ex.Message}");
            return [];
        }
    }

    private static List<long> ParseSelectedIds(string selected, IReadOnlyList<SceneArchive> candidates)
    {
        var validIds = new HashSet<long>(candidates.Select(c => c.Id));
        var ids = new List<long>();

        foreach (var part in selected.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(part.Trim(), out var id) && validIds.Contains(id))
                ids.Add(id);
        }

        return ids;
    }

    private static string BuildRetrievalSystemPrompt(int maxRecall) => $"""
        You select which prior scenes are relevant to the current conversation.

        Rules:
        - Select at most {maxRecall} scene(s) by their ID number.
        - Select only scenes whose entities, events, or hooks directly relate to what is happening now.
        - If no prior scene is relevant, return "none".
        - Return exactly one tagged section and nothing else.

        Format: <selected>ID1, ID2</selected> or <selected>none</selected>
        """;

    private static string BuildRetrievalUserPrompt(
        string sharedRoomMemory,
        string transcript,
        List<string> candidates) => $"""
        Current room state:
        {(string.IsNullOrWhiteSpace(sharedRoomMemory) ? "(none)" : sharedRoomMemory)}

        Recent transcript:
        {(string.IsNullOrWhiteSpace(transcript) ? "(none)" : transcript)}

        Archived scenes:
        {string.Join("\n", candidates)}

        Which archived scenes are relevant to the current state?
        """;
}
