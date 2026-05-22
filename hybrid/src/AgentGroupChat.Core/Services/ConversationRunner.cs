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
        CancellationToken ct)
    {
        var enabledAgents = room.Agents.Where(a => a.IsEnabled).ToList();
        if (enabledAgents.Count == 0)
            throw new InvalidOperationException("Enable at least one agent before running.");

        var sessionTurns = await _transcriptRepo.GetAsync(room.Id);

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            OnSystemMessage?.Invoke($"Round {iteration} of {maxIterations}.");
            var roundAnchor = sessionTurns.Count;

            for (var agentIndex = 0; agentIndex < enabledAgents.Count; agentIndex++)
            {
                ct.ThrowIfCancellationRequested();

                var agent = enabledAgents[agentIndex];
                OnStatusChanged?.Invoke($"{agent.Name} thinking");
                OnAgentMessageStarted?.Invoke(agent, "Thinking...");

                var sharedRoomMemory = await _memoryRepo.GetAsync(room.Id, null, MemoryKind.SharedRoom);
                var durableMemory = await _memoryRepo.GetAsync(room.Id, null, MemoryKind.Durable);
                var recentTurns = sessionTurns.TakeLast(Math.Max(1, room.RecentTurnsWindow)).ToList();

                var prompt = _promptComposer.BuildAgentPrompt(
                    room, agent, iteration, maxIterations, sharedRoomMemory, durableMemory, recentTurns);
                var agentSettings = settingsResolver(agent);

                var (response, rawResult) = await _turnExecutor.ExecuteAgentTurnAsync(agent, agentSettings, prompt, ct);

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

                OnAgentMessageCompleted?.Invoke(agent, response);
                OnLog?.Invoke($"{agent.Name}: {rawResult.UsageSummary ?? "no usage info"}");

                // Wait for TTS speech to finish before proceeding
                if (OnSpeechGate is not null)
                {
                    OnStatusChanged?.Invoke($"{agent.Name} speaking");
                    await OnSpeechGate(agent, response, ct);
                }

                if (agentIndex == enabledAgents.Count - 1)
                {
                    var roundTurns = sessionTurns.Skip(roundAnchor).ToList();
                    var shouldPromoteDurable = string.IsNullOrWhiteSpace(
                            await _memoryRepo.GetAsync(room.Id, null, MemoryKind.Durable))
                        || (completedRounds + iteration) % 3 == 0;

                    OnStatusChanged?.Invoke("Updating memory...");
                    await _memorySummarizer.RefreshAsync(
                        room, baseSummarizerSettings, roundTurns, sessionTurns,
                        shouldPromoteDurable, s => OnLog?.Invoke(s), ct);
                }
                else if (room.AgentDelaySeconds > 0)
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
}
