using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Models.Llm;
using AgentGroupChat.Core.Services.Interfaces;
using System.Text.RegularExpressions;
using static AgentGroupChat.Core.ChatPlaceholders;
using static AgentGroupChat.Core.XmlTags;

namespace AgentGroupChat.Core.Services;

public sealed class ConversationRunner
{
    private readonly PromptComposer _promptComposer;
    private readonly TurnExecutor _turnExecutor;
    private readonly MemorySummarizer _memorySummarizer;
    private readonly ITranscriptRepository _transcriptRepo;
    private readonly IMemoryRepository _memoryRepo;
    private readonly IRoomRepository _roomRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly SceneRetrievalService _sceneRetrievalService;
    private readonly SpeechService _speechService;

    private static readonly Regex PrivilegedActionsBlockRegex = new(
        @"<privileged_actions>\s*(?<body>.*?)\s*</privileged_actions>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex SpawnNpcRegex = new(
        @"<spawn_npc\b(?<attrs>[^>]*)>(?<body>.*?)</spawn_npc>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex DismissNpcRegex = new(
        @"<dismiss_npc\b(?<attrs>[^>]*)>(?<body>.*?)</dismiss_npc>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex SuspendAgentRegex = new(
        @"<suspend_agent\b(?<attrs>[^>]*)>(?<body>.*?)</suspend_agent>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ResumeAgentRegex = new(
        @"<resume_agent\b(?<attrs>[^>]*)>(?<body>.*?)</resume_agent>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly (string AccentHex, string BackgroundHex)[] NpcColorPresets =
    [
        ("#C56A54", "#F9E5DE"),
        ("#2E6799", "#DDE9F3"),
        ("#984566", "#F3DFEA"),
        ("#3A6C4C", "#DEF0E4"),
        ("#8F6617", "#F5EDDA"),
        ("#506070", "#E4E8EC"),
    ];

    private enum PrivilegedActionKind
    {
        ResumeAgent,
        DismissNpc,
        SuspendAgent,
        SpawnNpc,
    }

    private sealed record RequestedPrivilegedAction(
        PrivilegedActionKind Kind,
        int Index,
        string Name,
        string Body,
        string? Gender = null,
        int? Rounds = null);

    public ConversationRunner(
        PromptComposer promptComposer,
        TurnExecutor turnExecutor,
        MemorySummarizer memorySummarizer,
        ITranscriptRepository transcriptRepo,
        IMemoryRepository memoryRepo,
        IRoomRepository roomRepo,
        ISettingsRepository settingsRepo,
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
            if (room.EnableSceneArchive)
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
                    try
                    {
                        (response, futureNote, rawResult) = await prefetchTask;
                    }
                    catch
                    {
                        // Prefetch failed (e.g. rate limit) — retry normally
                        prefetchTask = null;
                        OnStatusChanged?.Invoke($"{agent.Name} thinking");
                        (response, futureNote, rawResult) = await ExecuteAgentTurnInternalAsync(
                            room, agent, settingsResolver, sessionTurns, iteration, maxIterations, ct, roundRecalledScenes);
                    }
                    prefetchTask = null;
                }
                else
                {
                    // No prefetch available — execute normally
                    OnStatusChanged?.Invoke($"{agent.Name} thinking");
                    OnAgentMessageStarted?.Invoke(agent, Thinking);

                    (response, futureNote, rawResult) = await ExecuteAgentTurnInternalAsync(
                        room, agent, settingsResolver, sessionTurns, iteration, maxIterations, ct, roundRecalledScenes);
                }

                if (IsPrivilegedAgent(room, agent))
                {
                    var parseResult = ParsePrivilegedActions(room, agent, futureNote);
                    futureNote = parseResult.CleanedFutureNote;
                    pendingActions.AddRange(parseResult.Actions);
                }

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

                    // Wait for speech to finish before showing the next agent's result
                    await speechTask;
                }

                // PauseAfterEveryReply: pause after each agent, let user interject
                if (room.PauseAfterEveryReply && agentIndex < enabledAgents.Count - 1)
                {
                    OnSystemMessage?.Invoke("Agent replied. Add a message or click Continue.");
                    OnStatusChanged?.Invoke("Waiting for you");
                    return;
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

                    if (pendingActions.Count > 0)
                    {
                        OnStatusChanged?.Invoke("Applying privileged actions...");
                        await ApplyPrivilegedActionsAsync(room, agent, pendingActions, roundNumber);
                    }
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
        var agentLongMemory = await _memoryRepo.GetAsync(room.Id, agent.Id, MemoryKind.AgentLong);
        var agentShortMemory = await _memoryRepo.GetAsync(room.Id, agent.Id, MemoryKind.AgentShort);
        var recentTurns = SelectRecentTurns(sessionTurns, agentIndex, enabledAgents.Count, room.RecentTurnsWindow);

        var agentRecalledScenes = roundRecalledScenes is { Count: > 0 }
            ? (includeDurableMemory ? roundRecalledScenes.Take(2).ToList() : roundRecalledScenes.Take(1).ToList())
            : null;

        var prompt = _promptComposer.BuildAgentPrompt(
            room, agent, iteration, maxIterations,
            sharedRoomMemory, durableMemory, agentLongMemory, agentShortMemory,
            recentTurns, includeDurableMemory, agentRecalledScenes);
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
        var (existingOffSceneNotice, cleanedExistingShortMemory) = PromptComposer.ExtractStructuredSection(existingShortMemory, "[Off-Scene Notice]");
        if (string.IsNullOrWhiteSpace(futureNote))
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
        var shortMemory = PromptComposer.SanitizeStructuredMemoryBlock(futureNote, 10, maxLength);
        if (string.IsNullOrWhiteSpace(shortMemory))
        {
            if (!string.IsNullOrWhiteSpace(existingShortMemory))
                OnLog?.Invoke($"Preserved {agent.Name} short memory: future note was empty after sanitization.");
            return;
        }

        if (shortMemory.Length > maxLength)
            shortMemory = shortMemory[..maxLength].Trim();

        if (string.Equals(cleanedExistingShortMemory, shortMemory, StringComparison.Ordinal))
            return;

        await _memoryRepo.SaveAsync(room.Id, agent.Id, MemoryKind.AgentShort, shortMemory);
        OnLog?.Invoke($"Updated {agent.Name} short memory ({shortMemory.Length} chars).");
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
            var lastAgent = agents.Where(x => x.Name.Equals(sessionTurns.Last().Speaker, StringComparison.OrdinalIgnoreCase)).First();
            if (agents.OrderBy(x => x.SortOrder).Last() == lastAgent)
            {
                return 0;
            }
            else
            {
                return lastAgent.SortOrder + 1;
            }
        }
        catch
        {
            return 0;
        }


        //var enabledAgents = agents.Where(a => a.IsEnabled && !a.IsTemporarilySuspended).ToList();
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

    private static bool IsPrivilegedAgent(RoomConfig room, AgentConfig agent) =>
        room.EnablePrivilegedActions
        && !agent.IsNpc
        && string.Equals(agent.Id, room.PrivilegedAgentId, StringComparison.Ordinal);

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

    private (string CleanedFutureNote, IReadOnlyList<RequestedPrivilegedAction> Actions) ParsePrivilegedActions(
        RoomConfig room,
        AgentConfig agent,
        string futureNote)
    {
        if (string.IsNullOrWhiteSpace(futureNote))
            return (futureNote, []);

        var blockMatch = PrivilegedActionsBlockRegex.Match(futureNote);
        if (!blockMatch.Success)
            return (futureNote, []);

        var cleanedFutureNote = PrivilegedActionsBlockRegex.Replace(futureNote, string.Empty).Trim();
        var actionBlock = blockMatch.Groups["body"].Value;

        if (!IsPrivilegedAgent(room, agent))
        {
            OnLog?.Invoke($"PrivilegedActions.Rejected: {agent.Name} is not allowed to manage privileged actions.");
            return (cleanedFutureNote, []);
        }

        var requestedActions = ParseRequestedPrivilegedActions(actionBlock)
            .OrderBy(a => a.Index)
            .ToList();
        if (requestedActions.Count == 0)
        {
            OnLog?.Invoke($"PrivilegedActions.Rejected: {agent.Name} emitted an empty privileged action block.");
            return (cleanedFutureNote, []);
        }

        OnLog?.Invoke($"PrivilegedActions.Requested: {agent.Name} requested {string.Join(", ", requestedActions.Select(DescribePrivilegedAction))}.");
        var validatedActions = ValidateRequestedPrivilegedActions(room, agent, requestedActions);
        return (cleanedFutureNote, validatedActions);
    }

    private List<RequestedPrivilegedAction> ValidateRequestedPrivilegedActions(
        RoomConfig room,
        AgentConfig sourceAgent,
        IReadOnlyList<RequestedPrivilegedAction> requestedActions)
    {
        var accepted = new List<RequestedPrivilegedAction>();
        var limitedActions = requestedActions.OrderBy(a => a.Index).Take(2).ToList();
        if (requestedActions.Count > limitedActions.Count)
            OnLog?.Invoke($"PrivilegedActions.Rejected: ignored {requestedActions.Count - limitedActions.Count} excess action(s) beyond the two-action limit.");

        var seenKinds = new HashSet<PrivilegedActionKind>();
        var enabledAgentsByName = room.Agents
            .Where(a => a.IsEnabled)
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var suspendedPermanentNames = new HashSet<string>(
            room.Agents.Where(a => a.IsEnabled && !a.IsNpc && a.IsTemporarilySuspended).Select(a => a.Name),
            StringComparer.OrdinalIgnoreCase);
        var activeNpcNames = new HashSet<string>(
            room.Agents.Where(a => a.IsEnabled && a.IsNpc).Select(a => a.Name),
            StringComparer.OrdinalIgnoreCase);
        var activeNpcCount = activeNpcNames.Count;
        var maxNpcCount = Math.Max(1, room.MaxConcurrentNpcs);

        foreach (var action in limitedActions.OrderBy(a => GetActionPhaseOrder(a.Kind)).ThenBy(a => a.Index))
        {
            if (!seenKinds.Add(action.Kind))
            {
                OnLog?.Invoke($"PrivilegedActions.Rejected: only one {action.Kind} action is allowed per turn.");
                continue;
            }

            switch (action.Kind)
            {
                case PrivilegedActionKind.ResumeAgent:
                    if (!enabledAgentsByName.TryGetValue(action.Name, out var resumeTarget)
                        || resumeTarget.IsNpc
                        || !resumeTarget.IsTemporarilySuspended)
                    {
                        OnLog?.Invoke($"PrivilegedActions.Rejected: cannot resume '{action.Name}' because they are not a suspended permanent agent.");
                        continue;
                    }

                    suspendedPermanentNames.Remove(resumeTarget.Name);
                    accepted.Add(action);
                    OnLog?.Invoke($"PermanentAgent.ResumeQueued: {resumeTarget.Name}");
                    break;

                case PrivilegedActionKind.DismissNpc:
                    if (!enabledAgentsByName.TryGetValue(action.Name, out var dismissTarget)
                        || !dismissTarget.IsNpc)
                    {
                        OnLog?.Invoke($"PrivilegedActions.Rejected: cannot dismiss '{action.Name}' because they are not an active NPC.");
                        continue;
                    }

                    activeNpcNames.Remove(dismissTarget.Name);
                    enabledAgentsByName.Remove(dismissTarget.Name);
                    activeNpcCount = Math.Max(0, activeNpcCount - 1);
                    accepted.Add(action);
                    OnLog?.Invoke($"NpcAgent.DismissQueued: {dismissTarget.Name}");
                    break;

                case PrivilegedActionKind.SuspendAgent:
                    if (!enabledAgentsByName.TryGetValue(action.Name, out var suspendTarget)
                        || suspendTarget.IsNpc)
                    {
                        OnLog?.Invoke($"PrivilegedActions.Rejected: cannot suspend '{action.Name}' because they are not an enabled permanent agent.");
                        continue;
                    }

                    if (string.Equals(suspendTarget.Id, room.PrivilegedAgentId, StringComparison.Ordinal))
                    {
                        OnLog?.Invoke($"PrivilegedActions.Rejected: the privileged agent cannot suspend itself.");
                        continue;
                    }

                    if (suspendedPermanentNames.Contains(suspendTarget.Name))
                    {
                        OnLog?.Invoke($"PrivilegedActions.Rejected: '{suspendTarget.Name}' is already suspended.");
                        continue;
                    }

                    suspendedPermanentNames.Add(suspendTarget.Name);
                    accepted.Add(action);
                    OnLog?.Invoke($"PermanentAgent.SuspendQueued: {suspendTarget.Name}");
                    break;

                case PrivilegedActionKind.SpawnNpc:
                    if (!room.EnableNpcSpawning)
                    {
                        OnLog?.Invoke($"PrivilegedActions.Rejected: NPC spawning is disabled for this room.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(action.Name) || string.IsNullOrWhiteSpace(action.Body))
                    {
                        OnLog?.Invoke("PrivilegedActions.Rejected: spawn_npc requires a name and character description.");
                        continue;
                    }

                    if (!string.Equals(action.Gender, "male", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(action.Gender, "female", StringComparison.OrdinalIgnoreCase))
                    {
                        OnLog?.Invoke($"PrivilegedActions.Rejected: spawn_npc '{action.Name}' must use gender='male' or gender='female'.");
                        continue;
                    }

                    if (enabledAgentsByName.ContainsKey(action.Name))
                    {
                        OnLog?.Invoke($"PrivilegedActions.Rejected: an enabled agent named '{action.Name}' already exists.");
                        continue;
                    }

                    if (activeNpcCount >= maxNpcCount)
                    {
                        OnLog?.Invoke($"PrivilegedActions.Rejected: NPC slot limit {activeNpcCount}/{maxNpcCount} has been reached.");
                        continue;
                    }

                    enabledAgentsByName[action.Name] = new AgentConfig { Name = action.Name, IsNpc = true };
                    activeNpcNames.Add(action.Name);
                    activeNpcCount++;
                    accepted.Add(action);
                    OnLog?.Invoke($"NpcAgent.SpawnQueued: {action.Name}");
                    break;
            }
        }

        return accepted;
    }

    private async Task ApplyPrivilegedActionsAsync(
        RoomConfig room,
        AgentConfig sourceAgent,
        IReadOnlyList<RequestedPrivilegedAction> actions,
        int roundNumber)
    {
        if (actions.Count == 0)
            return;

        var roomChanged = false;
        AppSettings? appSettings = null;
        IReadOnlyList<string> kokoroVoices = [];

        if (actions.Any(a => a.Kind == PrivilegedActionKind.SpawnNpc))
        {
            appSettings = await _settingsRepo.GetAsync();
            if (RoomSpeechResolver.UsesKokoro(room, appSettings))
                kokoroVoices = await _speechService.FetchKokoroVoicesAsync(appSettings.KokoroBaseUrl);
        }

        foreach (var action in actions.Where(a => a.Kind == PrivilegedActionKind.ResumeAgent))
        {
            var target = room.Agents.FirstOrDefault(a => a.IsEnabled && !a.IsNpc && a.IsTemporarilySuspended && string.Equals(a.Name, action.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null)
                continue;

            var reason = target.SuspensionReason;
            target.IsTemporarilySuspended = false;
            target.SuspendedByAgentId = string.Empty;
            target.SuspendedUntilRound = null;
            target.SuspensionReason = string.Empty;
            roomChanged = true;

            await AddOffSceneNoticeAsync(
                room,
                target,
                BuildOffSceneNotice(
                    reason,
                    string.IsNullOrWhiteSpace(action.Body)
                        ? "You have rejoined the active scene. You did not directly witness the rounds that occurred while you were off-scene."
                        : action.Body));
            OnLog?.Invoke($"PermanentAgent.ResumeApplied: {target.Name}");
        }

        foreach (var action in actions.Where(a => a.Kind == PrivilegedActionKind.DismissNpc))
        {
            var target = room.Agents.FirstOrDefault(a => a.IsEnabled && a.IsNpc && string.Equals(a.Name, action.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null)
                continue;

            target.IsEnabled = false;
            target.IsTemporarilySuspended = false;
            target.SuspendedByAgentId = string.Empty;
            target.SuspendedUntilRound = null;
            target.SuspensionReason = string.Empty;
            roomChanged = true;

            await _memoryRepo.SaveAsync(room.Id, target.Id, MemoryKind.AgentShort, string.Empty);
            await AppendDurableDepartureNoteAsync(room, target.Name, action.Body);
            OnLog?.Invoke($"NpcAgent.DismissApplied: {target.Name}");
        }

        foreach (var action in actions.Where(a => a.Kind == PrivilegedActionKind.SuspendAgent))
        {
            var target = room.Agents.FirstOrDefault(a => a.IsEnabled && !a.IsNpc && !a.IsTemporarilySuspended && string.Equals(a.Name, action.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null)
                continue;

            target.IsTemporarilySuspended = true;
            target.SuspendedByAgentId = sourceAgent.Id;
            target.SuspendedUntilRound = action.Rounds.HasValue && action.Rounds.Value > 0
                ? roundNumber + action.Rounds.Value
                : null;
            target.SuspensionReason = NormalizeReason(action.Body, "Off-scene until resumed.");
            roomChanged = true;

            OnLog?.Invoke(target.SuspendedUntilRound.HasValue
                ? $"PermanentAgent.SuspendApplied: {target.Name} through round {target.SuspendedUntilRound.Value}"
                : $"PermanentAgent.SuspendApplied: {target.Name} until resumed");
        }

        foreach (var action in actions.Where(a => a.Kind == PrivilegedActionKind.SpawnNpc))
        {
            var npc = CreateSpawnedNpc(room, sourceAgent, action, appSettings, kokoroVoices);
            room.Agents.Add(npc);
            roomChanged = true;
            OnLog?.Invoke($"NpcAgent.SpawnApplied: {npc.Name}");
        }

        if (roomChanged)
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

    private AgentConfig CreateSpawnedNpc(
        RoomConfig room,
        AgentConfig sourceAgent,
        RequestedPrivilegedAction action,
        AppSettings? appSettings,
        IReadOnlyList<string> kokoroVoices)
    {
        var (accentHex, backgroundHex) = ChooseNpcColors(room);
        var voice = ResolveNpcVoice(room, action, appSettings, kokoroVoices);
        var sortOrder = room.Agents.Count == 0 ? 0 : room.Agents.Max(a => a.SortOrder) + 1;

        return new AgentConfig
        {
            RoomId = room.Id,
            Name = action.Name.Trim(),
            ModelId = string.IsNullOrWhiteSpace(room.NpcModelId) ? sourceAgent.ModelId : room.NpcModelId.Trim(),
            SystemPrompt = BuildNpcSystemPrompt(room, action),
            IsEnabled = true,
            MaxTokensOverride = room.NpcMaxTokens,
            CompactionBudget = room.NpcCompactionBudget > 0 ? room.NpcCompactionBudget : 300,
            AccentHex = accentHex,
            BackgroundHex = backgroundHex,
            TtsVoice = string.IsNullOrWhiteSpace(voice) ? string.Empty : voice.Trim(),
            IsNpc = true,
            SpawnedByAgentId = sourceAgent.Id,
            SortOrder = sortOrder,
        };
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

        return matchingVoices[GetDeterministicVoiceIndex(action.Name, matchingVoices.Count)];
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

    private static string BuildNpcSystemPrompt(RoomConfig room, RequestedPrivilegedAction action)
    {
        var baseInstructions = string.IsNullOrWhiteSpace(room.NpcBaseInstructions)
            ? "Stay in character and respond only as this NPC. Follow the scene being driven by the narrator and the current room state. Do not act as narrator, adjudicator, or DM. Do not emit privileged-action tags."
            : room.NpcBaseInstructions.Trim();
        var description = NormalizeReason(action.Body, "Secondary NPC in the current scene.");

        return $"""
{baseInstructions}

Character Name: {action.Name.Trim()}
Character Description:
{description}
""";
    }

    private static (string AccentHex, string BackgroundHex) ChooseNpcColors(RoomConfig room)
    {
        var usedColors = new HashSet<string>(
            room.Agents.Where(a => a.IsEnabled).Select(a => a.AccentHex),
            StringComparer.OrdinalIgnoreCase);

        foreach (var preset in NpcColorPresets)
        {
            if (!usedColors.Contains(preset.AccentHex))
                return preset;
        }

        return NpcColorPresets[^1];
    }

    private static List<RequestedPrivilegedAction> ParseRequestedPrivilegedActions(string actionBlock)
    {
        var actions = new List<RequestedPrivilegedAction>();
        CollectActions(actions, actionBlock, ResumeAgentRegex, PrivilegedActionKind.ResumeAgent, attrs => null, attrs => null);
        CollectActions(actions, actionBlock, DismissNpcRegex, PrivilegedActionKind.DismissNpc, attrs => null, attrs => null);
        CollectActions(actions, actionBlock, SuspendAgentRegex, PrivilegedActionKind.SuspendAgent, attrs => null, attrs => ParseNullableInt(ExtractAttributeValue(attrs, "rounds")));
        CollectActions(actions, actionBlock, SpawnNpcRegex, PrivilegedActionKind.SpawnNpc, attrs => ExtractAttributeValue(attrs, "gender"), attrs => null);
        return actions.OrderBy(a => a.Index).ToList();
    }

    private static void CollectActions(
        ICollection<RequestedPrivilegedAction> actions,
        string block,
        Regex regex,
        PrivilegedActionKind kind,
        Func<string, string?> genderSelector,
        Func<string, int?> roundsSelector)
    {
        foreach (Match match in regex.Matches(block))
        {
            var attrs = match.Groups["attrs"].Value;
            actions.Add(new RequestedPrivilegedAction(
                kind,
                match.Index,
                ExtractAttributeValue(attrs, "name"),
                match.Groups["body"].Value.Trim(),
                genderSelector(attrs),
                roundsSelector(attrs)));
        }
    }

    private static string DescribePrivilegedAction(RequestedPrivilegedAction action) =>
        action.Kind switch
        {
            PrivilegedActionKind.SpawnNpc => $"spawn NPC '{action.Name}'",
            PrivilegedActionKind.DismissNpc => $"dismiss NPC '{action.Name}'",
            PrivilegedActionKind.SuspendAgent => action.Rounds.HasValue
                ? $"suspend '{action.Name}' for {action.Rounds.Value} round(s)"
                : $"suspend '{action.Name}' until resumed",
            PrivilegedActionKind.ResumeAgent => $"resume '{action.Name}'",
            _ => action.Kind.ToString(),
        };

    private static int GetActionPhaseOrder(PrivilegedActionKind kind) =>
        kind switch
        {
            PrivilegedActionKind.ResumeAgent => 0,
            PrivilegedActionKind.DismissNpc => 1,
            PrivilegedActionKind.SuspendAgent => 2,
            PrivilegedActionKind.SpawnNpc => 3,
            _ => 99,
        };

    private static string ExtractAttributeValue(string attrs, string attributeName)
    {
        if (string.IsNullOrWhiteSpace(attrs) || string.IsNullOrWhiteSpace(attributeName))
            return string.Empty;

        var match = Regex.Match(
            attrs,
            $"\\b{Regex.Escape(attributeName)}\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)')",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
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
