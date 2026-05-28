using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Models.Llm;
using AgentGroupChat.Core.Services.Interfaces;

namespace AgentGroupChat.Core.Services;

public sealed class ConversationRunner
{
    private readonly PromptComposer _promptComposer;
    private readonly TurnExecutor _turnExecutor;
    private readonly MemorySummarizer _memorySummarizer;
    private readonly ITranscriptRepository _transcriptRepo;
    private readonly IMemoryRepository _memoryRepo;

    public ConversationRunner(
        PromptComposer promptComposer,
        TurnExecutor turnExecutor,
        MemorySummarizer memorySummarizer,
        ITranscriptRepository transcriptRepo,
        IMemoryRepository memoryRepo)
    {
        _promptComposer = promptComposer;
        _turnExecutor = turnExecutor;
        _memorySummarizer = memorySummarizer;
        _transcriptRepo = transcriptRepo;
        _memoryRepo = memoryRepo;
    }

    public event Action<string>? OnSystemMessage;
    public event Action<AgentConfig, string>? OnAgentMessageStarted;
    public event Action<AgentConfig, string>? OnAgentMessageCompleted;
    public event Action<string>? OnStatusChanged;
    public event Action<string>? OnLog;

    /// <summary>
    /// When set, the runner will call this after each agent's message is completed
    /// and await the returned Task before proceeding to the next agent.
    /// This allows TTS playback to gate the conversation flow.
    /// </summary>
    public Func<AgentConfig, string, CancellationToken, Task>? OnSpeechGate { get; set; }

    public async Task RunAsync(
        RoomConfig room,
        Func<AgentConfig, LlmRequestSettings> settingsResolver,
        LlmRequestSettings baseSummarizerSettings,
        int maxIterations,
        int completedRounds,
        CancellationToken ct,
        int startFromAgentIndex = 0)
    {
        var enabledAgents = room.Agents.Where(a => a.IsEnabled).ToList();
        if (enabledAgents.Count == 0)
            throw new InvalidOperationException("Enable at least one agent before running.");

        var sessionTurns = await _transcriptRepo.GetAsync(room.Id);
        var userMemoryRefreshed = await TryRefreshMemoryFromPendingUserTurnsAsync(
            room, baseSummarizerSettings, enabledAgents, sessionTurns,
            startFromAgentIndex, ct);

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var agentStart = (iteration == 1) ? startFromAgentIndex : 0;
            OnSystemMessage?.Invoke(agentStart > 0
                ? $"Resuming round {iteration} of {maxIterations} from agent {agentStart + 1}."
                : $"Round {iteration} of {maxIterations}.");
            var roundAnchor = sessionTurns.Count;

            Task<(string Response, string FutureNote, LlmCompletionResult RawResult)>? prefetchTask = null;

            for (var agentIndex = agentStart; agentIndex < enabledAgents.Count; agentIndex++)
            {
                ct.ThrowIfCancellationRequested();

                var agent = enabledAgents[agentIndex];
                string response;
                string futureNote;
                LlmCompletionResult rawResult;

                if (prefetchTask is not null)
                {
                    // Use the prefetched result — "Thinking..." was already shown
                    (response, futureNote, rawResult) = await prefetchTask;
                    prefetchTask = null;
                }
                else
                {
                    // No prefetch available — execute normally
                    OnStatusChanged?.Invoke($"{agent.Name} thinking");
                    OnAgentMessageStarted?.Invoke(agent, "Thinking...");

                    (response, futureNote, rawResult) = await ExecuteAgentTurnInternalAsync(
                        room, agent, settingsResolver, sessionTurns, iteration, maxIterations, ct);
                }

                // Persist transcript
                var turn = new TranscriptTurn
                {
                    RoomId = room.Id,
                    Round = completedRounds + iteration,
                    Speaker = agent.Name,
                    Content = response,
                    AccentHex = agent.AccentHex,
                    BackgroundHex = agent.BackgroundHex,
                };
                await _transcriptRepo.AppendAsync(turn);
                sessionTurns.Add(turn);
                await UpdateAgentShortMemoryAsync(room, agent, futureNote);

                OnAgentMessageCompleted?.Invoke(agent, response);
                OnLog?.Invoke($"{agent.Name}: {rawResult.UsageSummary ?? "no usage info"}");

                // Speech gate with lookahead prefetch for the next agent
                if (OnSpeechGate is not null)
                {
                    OnStatusChanged?.Invoke($"{agent.Name} speaking");

                    // Start speech (will run concurrently with prefetch below)
                    var speechTask = OnSpeechGate(agent, response, ct);

                    // While this agent speaks, fire the next agent's API call
                    if (agentIndex + 1 < enabledAgents.Count)
                    {
                        var nextAgent = enabledAgents[agentIndex + 1];
                        OnAgentMessageStarted?.Invoke(nextAgent, "Thinking...");
                        OnStatusChanged?.Invoke($"{nextAgent.Name} thinking");

                        prefetchTask = ExecuteAgentTurnInternalAsync(
                            room, nextAgent, settingsResolver, sessionTurns, iteration, maxIterations, ct);
                    }

                    // Wait for speech to finish before showing the next agent's result
                    await speechTask;
                }

                if (agentIndex == enabledAgents.Count - 1)
                {
                    var roundTurns = sessionTurns.Skip(roundAnchor).ToList();
                    var shouldPromoteDurable = (userMemoryRefreshed && iteration == 1)
                        || string.IsNullOrWhiteSpace(
                            await _memoryRepo.GetAsync(room.Id, null, MemoryKind.Durable))
                        || (completedRounds + iteration) % 3 == 0;

                    OnStatusChanged?.Invoke("Updating memory...");
                    await _memorySummarizer.RefreshAsync(
                        room, baseSummarizerSettings, roundTurns, sessionTurns,
                        shouldPromoteDurable, s => OnLog?.Invoke(s), ct);
                }
                else if (room.AgentDelaySeconds > 0 && OnSpeechGate is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(room.AgentDelaySeconds), ct);
                }
            }

            if (room.WaitForUserReply)
            {
                OnSystemMessage?.Invoke("Round complete. Add a user reply or click Continue.");
                OnStatusChanged?.Invoke("Waiting for you");
                return;
            }
        }

        OnSystemMessage?.Invoke("Run complete.");
        OnStatusChanged?.Invoke("Complete");
    }

    private async Task<bool> TryRefreshMemoryFromPendingUserTurnsAsync(
        RoomConfig room,
        LlmRequestSettings baseSummarizerSettings,
        IReadOnlyList<AgentConfig> enabledAgents,
        IReadOnlyList<TranscriptTurn> sessionTurns,
        int startFromAgentIndex,
        CancellationToken ct)
    {
        if (startFromAgentIndex != 0 || sessionTurns.Count == 0)
            return false;

        var agentNames = new HashSet<string>(
            enabledAgents.Select(a => a.Name),
            StringComparer.OrdinalIgnoreCase);
        if (agentNames.Contains(sessionTurns[^1].Speaker))
            return false;

        var seedTurns = sessionTurns
            .Reverse()
            .TakeWhile(t => !agentNames.Contains(t.Speaker))
            .Reverse()
            .Where(t => !string.IsNullOrWhiteSpace(t.Content))
            .ToList();
        if (seedTurns.Count == 0)
            return false;

        OnStatusChanged?.Invoke("Refreshing memory from user input...");
        OnLog?.Invoke($"Refreshing scene and durable memory from {seedTurns.Count} pending user turn(s) before agents respond.");
        await _memorySummarizer.RefreshAsync(
            room,
            baseSummarizerSettings,
            seedTurns,
            sessionTurns,
            shouldPromoteDurable: true,
            s => OnLog?.Invoke(s),
            ct);
        return true;
    }

    private async Task<(string Response, string FutureNote, LlmCompletionResult RawResult)> ExecuteAgentTurnInternalAsync(
        RoomConfig room,
        AgentConfig agent,
        Func<AgentConfig, LlmRequestSettings> settingsResolver,
        List<TranscriptTurn> sessionTurns,
        int iteration,
        int maxIterations,
        CancellationToken ct)
    {
        var enabledAgents = room.Agents.Where(a => a.IsEnabled).ToList();
        var agentIndex = enabledAgents.FindIndex(a => a.Id == agent.Id);
        var includeDurableMemory = agentIndex == enabledAgents.Count - 1;
        var sharedRoomMemory = await _memoryRepo.GetAsync(room.Id, null, MemoryKind.SharedRoom);
        var durableMemory = includeDurableMemory
            ? await _memoryRepo.GetAsync(room.Id, null, MemoryKind.Durable)
            : string.Empty;
        var agentLongMemory = await _memoryRepo.GetAsync(room.Id, agent.Id, MemoryKind.AgentLong);
        var agentShortMemory = await _memoryRepo.GetAsync(room.Id, agent.Id, MemoryKind.AgentShort);
        var recentTurns = SelectRecentTurns(sessionTurns, agentIndex, enabledAgents.Count, room.RecentTurnsWindow);

        var prompt = _promptComposer.BuildAgentPrompt(
            room, agent, iteration, maxIterations,
            sharedRoomMemory, durableMemory, agentLongMemory, agentShortMemory,
            recentTurns, includeDurableMemory);
        var agentSettings = settingsResolver(agent);

        return await _turnExecutor.ExecuteAgentTurnAsync(agent, agentSettings, prompt, ct);
    }

    private static List<TranscriptTurn> SelectRecentTurns(
        IReadOnlyList<TranscriptTurn> sessionTurns,
        int agentIndex,
        int enabledAgentCount,
        int configuredWindow)
    {
        if (sessionTurns.Count == 0)
            return [];

        var window = Math.Max(1, configuredWindow);
        var turnsToTake = agentIndex switch
        {
            < 0 => Math.Min(window, 2),
            var index when index == enabledAgentCount - 1 => Math.Clamp(window, 2, 4),
            _ => Math.Clamp(Math.Min(window, agentIndex + 1), 1, 3),
        };

        return sessionTurns.TakeLast(turnsToTake).ToList();
    }

    private async Task UpdateAgentShortMemoryAsync(
        RoomConfig room,
        AgentConfig agent,
        string futureNote)
    {
        var existingShortMemory = await _memoryRepo.GetAsync(room.Id, agent.Id, MemoryKind.AgentShort);
        if (string.IsNullOrWhiteSpace(futureNote))
        {
            if (!string.IsNullOrWhiteSpace(existingShortMemory))
                OnLog?.Invoke($"Preserved {agent.Name} short memory: missing future note.");
            return;
        }

        var maxLength = Math.Clamp(agent.CompactionBudget > 0 ? agent.CompactionBudget : 420, 180, 900);
        var shortMemory = PromptComposer.SanitizeStructuredMemoryBlock(futureNote, 10, maxLength);
        if (string.IsNullOrWhiteSpace(shortMemory))
        {
            if (!string.IsNullOrWhiteSpace(existingShortMemory))
                OnLog?.Invoke($"Preserved {agent.Name} short memory: future note was empty after sanitization.");
            return;
        }

        if (shortMemory.Length > maxLength)
            shortMemory = shortMemory[..maxLength].Trim();

        if (string.Equals(existingShortMemory, shortMemory, StringComparison.Ordinal))
            return;

        await _memoryRepo.SaveAsync(room.Id, agent.Id, MemoryKind.AgentShort, shortMemory);
        OnLog?.Invoke($"Updated {agent.Name} short memory ({shortMemory.Length} chars).");
    }
}
