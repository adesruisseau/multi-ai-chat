using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Models.Llm;
using AgentGroupChat.Core.Services.Interfaces;
using NAudio.MediaFoundation;
using System.Text.RegularExpressions;
using static AgentGroupChat.Core.ChatPlaceholders;
using static AgentGroupChat.Core.XmlTags;

namespace AgentGroupChat.Core.Services;

public sealed partial class ConversationRunner
{
    private readonly PromptComposer _promptComposer;
    private readonly TurnExecutor _turnExecutor;
    private readonly MemorySummarizer _memorySummarizer;
    private readonly ITranscriptRepository _transcriptRepo;
    private readonly IMemoryRepository _memoryRepo;
    private readonly IRoomRepository _roomRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IPromptSampleRepository _promptSampleRepository;
    private readonly SceneRetrievalService _sceneRetrievalService;
    private readonly SpeechService _speechService;
    


    private static readonly (string AccentHex, string BackgroundHex)[] NpcColorPresets =
    [
        ("#C56A54", "#F9E5DE"),
        ("#2E6799", "#DDE9F3"),
        ("#984566", "#F3DFEA"),
        ("#3A6C4C", "#DEF0E4"),
        ("#8F6617", "#F5EDDA"),
        ("#506070", "#E4E8EC"),
    ];

    

    public ConversationRunner(
        PromptComposer promptComposer,
        TurnExecutor turnExecutor,
        MemorySummarizer memorySummarizer,
        ITranscriptRepository transcriptRepo,
        IMemoryRepository memoryRepo,
        IRoomRepository roomRepo,
        ISettingsRepository settingsRepo,
        IPromptSampleRepository promptSampleRepository,
        SceneRetrievalService sceneRetrievalService,
        SpeechService speechService)
    {
        _promptComposer = promptComposer;
        _turnExecutor = turnExecutor;
        _memorySummarizer = memorySummarizer;
        _transcriptRepo = transcriptRepo;
        _memoryRepo = memoryRepo;
        _roomRepo = roomRepo;
        _settingsRepo = settingsRepo;
        _promptSampleRepository = promptSampleRepository;
        _sceneRetrievalService = sceneRetrievalService;
        _speechService = speechService;
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
        await AutoResumeExpiredSuspensionsAsync(room, completedRounds + 1);
        var appSettings = await _settingsRepo.GetAsync();

        var enabledAgents = GetActiveAgents(room);
        if (enabledAgents.Count == 0)
            throw new InvalidOperationException("Enable at least one agent before running.");

        var sessionTurns = await _transcriptRepo.GetAsync(room.Id);
        
        var userMemoryRefreshed = await TryRefreshMemoryFromPendingUserTurnsAsync(
            room, baseSummarizerSettings, enabledAgents, sessionTurns,
            startFromAgentIndex, ct);

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var roundNumber = completedRounds + iteration;
            await AutoResumeExpiredSuspensionsAsync(room, roundNumber);

            enabledAgents = GetActiveAgents(room);
            if (enabledAgents.Count == 0)
                throw new InvalidOperationException("No active agents are available for this round.");

            var agentStart = (iteration == 1)
                ? Math.Clamp(startFromAgentIndex, 0, Math.Max(0, enabledAgents.Count - 1))
                : 0;
            OnSystemMessage?.Invoke(agentStart > 0
                ? $"Resuming round {iteration} of {maxIterations} from agent {agentStart + 1}."
                : $"Round {iteration} of {maxIterations}.");
            var roundAnchor = sessionTurns.Count;

            IReadOnlyList<SceneArchive>? roundRecalledScenes = null;
            if (room.StoreLongTermArchives)
            {
                try
                {
                    var retrievalMemory = await _memoryRepo.GetAsync(room.Id, null, MemoryKind.SharedRoom);
                    var retrievalTurns = sessionTurns.Where(x => !x.Speaker.Trim().Equals("Image", StringComparison.OrdinalIgnoreCase)).TakeLast(Math.Max(room.RecentTurnsWindow, 3)).ToList();
                    roundRecalledScenes = await _sceneRetrievalService.RetrieveAsync(
                        room, baseSummarizerSettings, retrievalMemory ?? "", retrievalTurns,
                        maxRecall: 2, s => OnLog?.Invoke(s), ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"Scene retrieval skipped: {ex.Message}");
                }
            }

            var pendingActions = new List<RequestedPrivilegedAction>();
            Task<(string Response, string ShortTermMemory, string LongTermMemory, string PrivilegedActions, LlmCompletionResult RawResult)>? prefetchTask = null;
            Task? endOfRoundWork = null;
            for (var agentIndex = agentStart; agentIndex < enabledAgents.Count; agentIndex++)
            {
                ct.ThrowIfCancellationRequested();

                var agent = enabledAgents[agentIndex];
                string response;
                string shortTermMemory;
                string longTermMemory;
                string privilegedActions;
                LlmCompletionResult rawResult;

                if (prefetchTask is not null)
                {
                    // Use the prefetched result — "Thinking..." was already shown
                    try
                    {
                        (response, shortTermMemory, longTermMemory, privilegedActions, rawResult) = await prefetchTask;
                    }
                    catch
                    {
                        // Prefetch failed (e.g. rate limit) — retry normally
                        prefetchTask = null;
                        OnStatusChanged?.Invoke($"{agent.Name} thinking");
                        (response, shortTermMemory, longTermMemory, privilegedActions, rawResult) = await ExecuteAgentTurnInternalAsync(
                            room, agent, settingsResolver, sessionTurns, iteration, maxIterations, ct, roundRecalledScenes);
                    }
                    prefetchTask = null;
                }
                else
                {
                    // No prefetch available — execute normally
                    OnStatusChanged?.Invoke($"{agent.Name} thinking");
                    OnAgentMessageStarted?.Invoke(agent, Thinking);

                    (response, shortTermMemory, longTermMemory, privilegedActions, rawResult) = await ExecuteAgentTurnInternalAsync(
                        room, agent, settingsResolver, sessionTurns, iteration, maxIterations, ct, roundRecalledScenes);
                }

                if (!string.IsNullOrWhiteSpace(privilegedActions))
                    pendingActions.AddRange(ParsePrivilegedActions(room, agent, privilegedActions));

                // Persist transcript
                var turn = new TranscriptTurn
                {
                    RoomId = room.Id,
                    Round = roundNumber,
                    Speaker = agent.Name,
                    Content = response,
                    AccentHex = agent.AccentHex,
                    BackgroundHex = agent.BackgroundHex,
                };
                await _transcriptRepo.AppendAsync(turn);
                sessionTurns.Add(turn);
                await UpdateAgentShortMemoryAsync(room, agent, shortTermMemory);
                await UpdateAgentLongMemoryAsync(room, agent, longTermMemory);

                OnAgentMessageCompleted?.Invoke(agent, response);
                OnLog?.Invoke($"{agent.Name}: {rawResult.UsageSummary ?? "no usage info"}");

                var currentSpeechGate = OnSpeechGate;
                var shouldSpeak = currentSpeechGate is not null || RoomSpeechResolver.IsSpeechEnabled(room, appSettings);
                Task? speechTask = null;

                // Speech gate with lookahead prefetch for the next agent
                if (shouldSpeak)
                {
                    OnStatusChanged?.Invoke($"{agent.Name} speaking");

                    // Start speech (will run concurrently with prefetch below)
                    speechTask = currentSpeechGate is not null
                        ? currentSpeechGate(agent, response, ct)
                        : SpeakWithRoomSettingsAsync(room, agent, response, appSettings, ct);

                    // While this agent speaks, fire the next agent's API call
                    // Skip prefetch if PauseAfterEveryReply is on (user wants to interject)
                    if (agentIndex + 1 < enabledAgents.Count && !room.PauseAfterEveryReply)
                    {
                        var nextAgent = enabledAgents[agentIndex + 1];
                        if (!nextAgent.IsHumanParticipant)
                        {
                            OnAgentMessageStarted?.Invoke(nextAgent, Thinking);
                            OnStatusChanged?.Invoke($"{nextAgent.Name} thinking");
                        }
                        try
                        {
                            prefetchTask = ExecuteAgentTurnInternalAsync(
                                room, nextAgent, settingsResolver, sessionTurns, iteration, maxIterations, ct, roundRecalledScenes);
                        }
                        catch
                        {
                            prefetchTask = null;
                        }
                    }
                }

                if (agentIndex == enabledAgents.Count - 1)
                {
                    endOfRoundWork = RefreshRoundMemoryAsync(room, baseSummarizerSettings, completedRounds, sessionTurns, userMemoryRefreshed, iteration, roundNumber, roundAnchor, agent, ct);
                }

                if (speechTask is not null)
                {
                    // Wait for speech to finish before showing the next agent's result
                    await speechTask;
                }

                if (endOfRoundWork is not null)
                {
                    OnStatusChanged?.Invoke("Updating memory...");
                    await endOfRoundWork;

                    if (pendingActions.Count > 0)
                    {
                        OnStatusChanged?.Invoke("Applying privileged actions...");
                        await ApplyPrivilegedActionsAsync(room, agent, pendingActions, roundNumber);
                    }
                }

                // PauseAfterEveryReply: pause after each agent, let user interject
                if (room.PauseAfterEveryReply && agentIndex < enabledAgents.Count - 1)
                {
                    OnSystemMessage?.Invoke("Agent replied. Add a message or click Continue.");
                    OnStatusChanged?.Invoke("Waiting for you");
                    return;
                }
                
                else if (room.AgentDelaySeconds > 0 && speechTask is null)
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

    private Task SpeakWithRoomSettingsAsync(
        RoomConfig room,
        AgentConfig agent,
        string content,
        AppSettings appSettings,
        CancellationToken ct)
    {
        var settings = RoomSpeechResolver.BuildSettings(room, appSettings);
        if (!settings.Enabled)
            return Task.CompletedTask;

        var voice = string.IsNullOrWhiteSpace(agent.TtsVoice)
            ? RoomSpeechResolver.GetFallbackVoice(room, appSettings)
            : agent.TtsVoice;
        return _speechService.SpeakAsync(settings, content, voice, ct);
    }

    private async Task RefreshRoundMemoryAsync(RoomConfig room, LlmRequestSettings baseSummarizerSettings, int completedRounds, List<TranscriptTurn> sessionTurns, bool userMemoryRefreshed, int iteration, int roundNumber, int roundAnchor, AgentConfig agent, CancellationToken ct)
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
        if (agentNames.Contains(sessionTurns[^1].Speaker) || sessionTurns[^1].Speaker == "Image")
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

    private async Task<(string Response, string ShortTermMemory, string LongTermMemory, string PrivilegedActions, LlmCompletionResult RawResult)> ExecuteAgentTurnInternalAsync(
        RoomConfig room,
        AgentConfig agent,
        Func<AgentConfig, LlmRequestSettings> settingsResolver,
        List<TranscriptTurn> sessionTurns,
        int iteration,
        int maxIterations,
        CancellationToken ct,
        IReadOnlyList<SceneArchive>? roundRecalledScenes = null)
    {
        var enabledAgents = GetActiveAgents(room);
        var agentIndex = enabledAgents.FindIndex(a => a.Id == agent.Id);
        var includeDurableMemory = agentIndex == enabledAgents.Count - 1;
        var sharedRoomMemory = await _memoryRepo.GetAsync(room.Id, null, MemoryKind.SharedRoom);
        var durableMemory = includeDurableMemory
            ? await _memoryRepo.GetAsync(room.Id, null, MemoryKind.Durable)
            : string.Empty;
        var agentLongMemory = agent.UseLongTermMemoryStorage
            ? await _memoryRepo.GetAsync(room.Id, agent.Id, MemoryKind.AgentLong)
            : string.Empty;
        var agentShortMemory = await _memoryRepo.GetAsync(room.Id, agent.Id, MemoryKind.AgentShort);
        var recentTurns = SelectRecentTurns(sessionTurns, agentIndex, enabledAgents.Count, room.RecentTurnsWindow);

        var promptTemplate = await _promptSampleRepository.GetAsync(agent.PromptSampleId);
        var promptText = promptTemplate?.PromptText ?? string.Empty;

        var agentRecalledScenes = roundRecalledScenes is { Count: > 0 }
            ? (includeDurableMemory ? roundRecalledScenes.Take(2).ToList() : roundRecalledScenes.Take(1).ToList())
            : null;

        var prompt = _promptComposer.BuildAgentPrompt(
            room, agent, promptText, iteration, maxIterations,
            sharedRoomMemory, durableMemory, agentLongMemory, agentShortMemory,
            recentTurns, includeDurableMemory, agentRecalledScenes);
        var agentSettings = settingsResolver(agent);

        return await _turnExecutor.ExecuteAgentTurnAsync(agent, agentSettings, prompt, IsPrivilegedAgent(room, agent), ct);
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
        string shortTermMemory)
    {
        var existingShortMemory = await _memoryRepo.GetAsync(room.Id, agent.Id, MemoryKind.AgentShort);
        var (existingOffSceneNotice, cleanedExistingShortMemory) = PromptComposer.ExtractStructuredSection(existingShortMemory, "[Off-Scene Notice]");

        if (!agent.UseShortTermMemoryStorage)
        {
            if (!string.IsNullOrWhiteSpace(existingOffSceneNotice))
            {
                await _memoryRepo.SaveAsync(room.Id, agent.Id, MemoryKind.AgentShort, cleanedExistingShortMemory);
                OnLog?.Invoke($"Cleared {agent.Name} off-scene notice after their return turn.");
            }
            else if (!string.IsNullOrWhiteSpace(shortTermMemory))
            {
                OnLog?.Invoke($"Skipped {agent.Name} short memory update: storage disabled.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(shortTermMemory))
        {
            if (!string.IsNullOrWhiteSpace(existingOffSceneNotice))
            {
                await _memoryRepo.SaveAsync(room.Id, agent.Id, MemoryKind.AgentShort, cleanedExistingShortMemory);
                OnLog?.Invoke($"Cleared {agent.Name} off-scene notice after their return turn.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(existingShortMemory))
                OnLog?.Invoke($"Preserved {agent.Name} short memory: missing future note.");
            return;
        }

        var maxLength = Math.Clamp(agent.CompactionBudget > 0 ? agent.CompactionBudget : 420, 180, 900);
        var shortMemory = PromptComposer.SanitizeStructuredMemoryBlock(shortTermMemory, 10, maxLength);
        if (string.IsNullOrWhiteSpace(shortMemory))
        {
            if (!string.IsNullOrWhiteSpace(existingShortMemory))
                OnLog?.Invoke($"Preserved {agent.Name} short memory: short-term memory was empty after sanitization.");
            return;
        }

        if (shortMemory.Length > maxLength)
            shortMemory = shortMemory[..maxLength].Trim();

        if (string.Equals(cleanedExistingShortMemory, shortMemory, StringComparison.Ordinal))
            return;

        await _memoryRepo.SaveAsync(room.Id, agent.Id, MemoryKind.AgentShort, shortMemory);
        OnLog?.Invoke($"Updated {agent.Name} short memory ({shortMemory.Length} chars).");
    }

    private async Task UpdateAgentLongMemoryAsync(
        RoomConfig room,
        AgentConfig agent,
        string longTermMemory)
    {
        if (!agent.UseLongTermMemoryStorage)
        {
            if (!string.IsNullOrWhiteSpace(longTermMemory))
                OnLog?.Invoke($"Skipped {agent.Name} long memory update: storage disabled.");
            return;
        }

        var existingLongMemory = await _memoryRepo.GetAsync(room.Id, agent.Id, MemoryKind.AgentLong);
        if (string.IsNullOrWhiteSpace(longTermMemory))
        {
            if (!string.IsNullOrWhiteSpace(existingLongMemory))
                OnLog?.Invoke($"Preserved {agent.Name} long memory: missing long-term memory block.");
            return;
        }

        var maxLength = Math.Clamp((agent.CompactionBudget > 0 ? agent.CompactionBudget : 420) * 4, 480, 12000);
        var sanitizedLongMemory = PromptComposer.SanitizeStructuredMemoryBlock(longTermMemory, 18, maxLength);
        if (string.IsNullOrWhiteSpace(sanitizedLongMemory))
        {
            if (!string.IsNullOrWhiteSpace(existingLongMemory))
                OnLog?.Invoke($"Preserved {agent.Name} long memory: long-term memory was empty after sanitization.");
            return;
        }

        if (string.Equals(existingLongMemory, sanitizedLongMemory, StringComparison.Ordinal))
            return;

        await _memoryRepo.SaveAsync(room.Id, agent.Id, MemoryKind.AgentLong, sanitizedLongMemory);
        OnLog?.Invoke($"Updated {agent.Name} long memory ({sanitizedLongMemory.Length} chars).");
    }

    private static List<AgentConfig> GetActiveAgents(RoomConfig room) =>
        room.Agents
            .Where(a => a.IsEnabled && !a.IsTemporarilySuspended)
            .OrderBy(a => a.SortOrder)
            .ToList();

    public static int ComputeResumeAgentIndex(
        IReadOnlyList<AgentConfig> agents,
        IReadOnlyList<TranscriptTurn> sessionTurns)
    {
        try
        {
            var enabledAgents = agents.OrderBy(x=>x.SortOrder).Where(a => a.IsEnabled && !a.IsTemporarilySuspended).ToList();
            var lastAgentSpoken = agents.Where(x => x.Name.Equals(sessionTurns.Last().Speaker, StringComparison.OrdinalIgnoreCase)).First();
            if (agents.OrderBy(x => x.SortOrder).Last() == lastAgentSpoken)
            {
                return 0;
            }
            else
            {
                var nextAgent = enabledAgents.Where(x => x.SortOrder > lastAgentSpoken.SortOrder).First();
                return nextAgent.SortOrder;
            }
        }
        catch
        {
            return 0;
        }


        //
        //if (enabledAgents.Count <= 1) return 0;
        //if (sessionTurns.Count == 0) return 0;

        

        //var spokenThisRound = new List<string>();
        //for (int i = sessionTurns.Count - 1; i >= 0; i--)
        //{
        //    var t = sessionTurns[i];
        //    if (enabledAgents.Any(a => a.Name == t.Speaker))
        //        spokenThisRound.Insert(0, t.Speaker);
        //    if (spokenThisRound.Count >= enabledAgents.Count) break;
        //}

        //if (spokenThisRound.Count == 0) return 0;
        //if (spokenThisRound.Count >= enabledAgents.Count) return 0;

        //var lastSpeaker = spokenThisRound[^1];
        //var lastIdx = enabledAgents.FindIndex(a => a.Name == lastSpeaker);
        //if (lastIdx < 0) return 0;

        //var nextIdx = lastIdx + 1;
        //return nextIdx < enabledAgents.Count ? nextIdx : 0;
    }

    
    private async Task AutoResumeExpiredSuspensionsAsync(RoomConfig room, int roundNumber)
    {
        var resumedAgents = room.Agents
            .Where(a => a.IsEnabled
                && !a.IsNpc
                && a.IsTemporarilySuspended
                && a.SuspendedUntilRound.HasValue
                && roundNumber > a.SuspendedUntilRound.Value)
            .ToList();
        if (resumedAgents.Count == 0)
            return;

        foreach (var agent in resumedAgents)
        {
            var reason = agent.SuspensionReason;
            agent.IsTemporarilySuspended = false;
            agent.SuspendedByAgentId = string.Empty;
            agent.SuspendedUntilRound = null;
            agent.SuspensionReason = string.Empty;

            await AddOffSceneNoticeAsync(
                room,
                agent,
                BuildOffSceneNotice(
                    reason,
                    "Your suspension expired and you are now active again. You did not directly witness the rounds that occurred while you were off-scene."));
            OnLog?.Invoke($"PermanentAgent.ResumeAutoApplied: {agent.Name}");
        }

        await _roomRepo.SaveAsync(room);
    }

    

    private async Task AppendDurableDepartureNoteAsync(RoomConfig room, string npcName, string reason)
    {
        var note = string.IsNullOrWhiteSpace(reason)
            ? $"{npcName} departed the scene and is no longer an active participant."
            : $"{npcName} departed the scene: {NormalizeReason(reason, string.Empty).TrimEnd('.')} .".Replace(" .", ".");
        var existingDurableMemory = await _memoryRepo.GetAsync(room.Id, null, MemoryKind.Durable);
        var merged = AppendStructuredBullet(existingDurableMemory, "[Ongoing Threads]", note);
        merged = PromptComposer.SanitizeStructuredMemoryBlock(merged, 18, 2400);
        await _memoryRepo.SaveAsync(room.Id, null, MemoryKind.Durable, merged);
    }

    private async Task AddOffSceneNoticeAsync(RoomConfig room, AgentConfig agent, string notice)
    {
        if (string.IsNullOrWhiteSpace(notice))
            return;

        var existingShortMemory = await _memoryRepo.GetAsync(room.Id, agent.Id, MemoryKind.AgentShort);
        var (_, cleanedShortMemory) = PromptComposer.ExtractStructuredSection(existingShortMemory, "[Off-Scene Notice]");
        var combined = string.IsNullOrWhiteSpace(cleanedShortMemory)
            ? $"[Off-Scene Notice]\n- {notice.Trim()}"
            : $"[Off-Scene Notice]\n- {notice.Trim()}\n\n{cleanedShortMemory}";
        var maxLength = Math.Clamp(agent.CompactionBudget > 0 ? agent.CompactionBudget : 420, 180, 900);
        var sanitized = PromptComposer.SanitizeStructuredMemoryBlock(combined, 10, maxLength);
        await _memoryRepo.SaveAsync(room.Id, agent.Id, MemoryKind.AgentShort, sanitized);
    }

    private static string BuildOffSceneNotice(string priorReason, string returnReason)
    {
        var prior = NormalizeReason(priorReason, "You were off-scene for prior rounds.");
        var current = NormalizeReason(returnReason, "You are active again.");
        return $"{prior} {current} You did not directly witness the rounds that occurred while you were absent; react only from your current knowledge and what has now been conveyed to you.";
    }

    

    private static string ResolveNpcVoice(
        RoomConfig room,
        RequestedPrivilegedAction action,
        AppSettings? appSettings,
        IReadOnlyList<string> kokoroVoices)
    {
        var configuredVoice = string.Equals(action.Gender, "female", StringComparison.OrdinalIgnoreCase)
            ? room.NpcDefaultFemaleVoice
            : room.NpcDefaultMaleVoice;

        if (!string.IsNullOrWhiteSpace(configuredVoice))
            return configuredVoice.Trim();

        if (appSettings is null || !RoomSpeechResolver.UsesKokoro(room, appSettings))
            return string.Empty;

        var matchingVoices = kokoroVoices
            .Where(voice => IsKokoroVoiceForGender(voice, action.Gender))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(voice => voice, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matchingVoices.Count == 0)
            return RoomSpeechResolver.GetFallbackVoice(room, appSettings);

        return matchingVoices[GetDeterministicVoiceIndex(action.Name ?? string.Empty, matchingVoices.Count)];
    }

    private static bool IsKokoroVoiceForGender(string voice, string? gender)
    {
        if (string.IsNullOrWhiteSpace(voice))
            return false;

        return string.Equals(gender, "female", StringComparison.OrdinalIgnoreCase)
            ? voice.StartsWith("af_", StringComparison.OrdinalIgnoreCase)
                || voice.StartsWith("bf_", StringComparison.OrdinalIgnoreCase)
            : voice.StartsWith("am_", StringComparison.OrdinalIgnoreCase)
                || voice.StartsWith("bm_", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetDeterministicVoiceIndex(string seed, int count)
    {
        if (count <= 1)
            return 0;

        unchecked
        {
            uint hash = 2166136261;
            foreach (var ch in seed.Trim())
            {
                hash ^= char.ToUpperInvariant(ch);
                hash *= 16777619;
            }

            return (int)(hash % (uint)count);
        }
    }

    

    private static int? ParseNullableInt(string value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private static string NormalizeReason(string value, string fallback)
    {
        var normalized = string.Join(" ", value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized.Trim();
    }

    private static string AppendStructuredBullet(string existingBlock, string sectionHeader, string bulletText)
    {
        var lines = existingBlock.Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var bullet = $"- {bulletText.Trim()}";
        var sectionIndex = lines.FindIndex(l => string.Equals(l, sectionHeader, StringComparison.OrdinalIgnoreCase));
        if (sectionIndex < 0)
        {
            if (lines.Count > 0)
                lines.Add(string.Empty);
            lines.Add(sectionHeader);
            lines.Add(bullet);
            return string.Join("\n", lines.Where(l => l.Length > 0));
        }

        var insertIndex = lines.Count;
        for (var i = sectionIndex + 1; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("[") && lines[i].EndsWith("]"))
            {
                insertIndex = i;
                break;
            }
        }

        lines.Insert(insertIndex, bullet);
        return string.Join("\n", lines.Where(l => l.Length > 0));
    }
}
