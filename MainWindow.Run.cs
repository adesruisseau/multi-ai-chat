using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace AgentGroupChat;

public partial class MainWindow
{
    private async Task RunWorkflowAsync()
    {
        if (_isRunning)
        {
            return;
        }

        var userPrompt = PromptTextBox.Text.Trim();
        var hasConversation = _sessionTurns.Count > 0;
        if (string.IsNullOrWhiteSpace(userPrompt) && !hasConversation)
        {
            MessageBox.Show(this, "Enter a request before running the room.", "Missing request", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ConnectionSettings settings;
        int maxIterations;
        int agentDelaySeconds;
        bool waitForUserReply;
        int recentTurnsWindow;
        int roomMaxTokens;
        int userCompactionBudget;
        List<AgentProfile> enabledAgents;

        try
        {
            if (SelectedTheme is null || SelectedTheme.Agents.Count == 0)
            {
                throw new InvalidOperationException("Select a theme with at least one agent.");
            }

            settings = ReadSettings();
            maxIterations = int.Parse(IterationsTextBox.Text.Trim());
            if (maxIterations < 1)
            {
                throw new InvalidOperationException("Iterations must be at least 1.");
            }

            roomMaxTokens = SelectedTheme.MaxTokens;
            if (roomMaxTokens < 50)
            {
                throw new InvalidOperationException("Max tokens must be a number greater than or equal to 50.");
            }

            enabledAgents = SelectedTheme.Agents
                .Where(agent => agent.IsEnabled)
                .ToList();

            if (enabledAgents.Count == 0)
            {
                throw new InvalidOperationException("Enable at least one agent before running the room.");
            }

            foreach (var providerName in enabledAgents
                        .Select(agent => string.IsNullOrWhiteSpace(agent.Provider) ? "Groq Fast" : agent.Provider)
                         .Append(settings.SummarizerProvider)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                _ = BuildRequestSettings(settings, providerName, roomMaxTokens);
            }

            waitForUserReply = SelectedTheme.WaitForUserReply;
            agentDelaySeconds = SelectedTheme.AgentDelaySeconds;
            recentTurnsWindow = SelectedTheme.RecentTurnsWindow;
            if (agentDelaySeconds < 1)
            {
                throw new InvalidOperationException("Seconds between agents must be at least 1.");
            }

            if (recentTurnsWindow < 1)
            {
                throw new InvalidOperationException("Recent transcript turns must be at least 1.");
            }

            userCompactionBudget = SelectedTheme.UserCompactionBudget;
            if (userCompactionBudget < 200)
            {
                throw new InvalidOperationException("User compaction budget must be at least 200.");
            }

            foreach (var agent in enabledAgents)
            {
                if (agent.MaxTokensOverride is < 50)
                {
                    throw new InvalidOperationException($"{agent.Name} needs a max tokens override of at least 50, or leave it blank.");
                }

                if (agent.CompactionBudget < 80)
                {
                    throw new InvalidOperationException($"{agent.Name} needs an agent compaction budget of at least 80.");
                }
            }

            PersistCurrentConfiguration();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Invalid settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _isRunning = true;
        _runCancellationTokenSource = new CancellationTokenSource();
        StopSpeaking();
        RunButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusTextBlock.Text = "Running";

        try
        {
            var cancellationToken = _runCancellationTokenSource.Token;

            if (!hasConversation)
            {
                var providerSummary = string.Join(
                    ", ",
                    SelectedTheme!.Agents.Select(agent => agent.IsEnabled ? $"{agent.Name}={agent.Provider}" : $"{agent.Name}=disabled"));
                AddSystemMessage($"Theme '{SelectedTheme.Name}' is running. Agent models: {providerSummary}. Summarizer model: {settings.SummarizerProvider}.");
            }

            if (!string.IsNullOrWhiteSpace(userPrompt))
            {
                AddUserMessage(userPrompt);
                _sessionTurns.Add(new SessionTurn("You", userPrompt));
                PersistConversationSession(SelectedTheme);
                await NarrateUserMessageAsync(Messages[^1], cancellationToken);
                PromptTextBox.Clear();
            }
            else
            {
                AddSystemMessage("Continuing the current conversation without a new user message.");
            }

            UpdateRunButtonText();

            for (var iteration = 1; iteration <= maxIterations; iteration++)
            {
                AddSystemMessage($"Round {iteration} of {maxIterations}.");
                var roundMemoryAnchorIndex = Math.Max(0, _sessionTurns.Count - 1);
                Task<string>? prefetchedResponseTask = null;
                AgentProfile? prefetchedAgent = null;
                Task roundMemoryRefreshTask = Task.CompletedTask;
                var completedConversationRoundsAfterCurrentRound = _completedConversationRounds;

                for (var agentIndex = 0; agentIndex < enabledAgents.Count; agentIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var agent = enabledAgents[agentIndex];
                    var agentMaxTokens = ResolveAgentMaxTokens(agent, roomMaxTokens);
                    var agentSettings = BuildRequestSettings(settings, string.IsNullOrWhiteSpace(agent.Provider) ? "Groq Fast" : agent.Provider, agentMaxTokens);
                    var prompt = BuildAgentPrompt(SelectedTheme, agent, iteration, maxIterations, _sessionTurns);
                    Task<string> responseTask;
                    ChatMessage? pendingMessage = null;

                    if (ReferenceEquals(prefetchedAgent, agent) && prefetchedResponseTask is not null)
                    {
                        responseTask = prefetchedResponseTask;
                        prefetchedAgent = null;
                        prefetchedResponseTask = null;

                        if (!responseTask.IsCompleted)
                        {
                            StatusTextBlock.Text = $"{agent.Name} thinking";
                            pendingMessage = AddPendingAgentMessage(agent);
                        }
                    }
                    else
                    {
                        StatusTextBlock.Text = $"{agent.Name} thinking";
                        pendingMessage = AddPendingAgentMessage(agent);
                        responseTask = GenerateAgentResponseAsync(agent, agentSettings, prompt, cancellationToken);
                    }

                    var response = await responseTask;

                    if (pendingMessage is null)
                    {
                        pendingMessage = AddPendingAgentMessage(agent, string.Empty);
                    }
                    else
                    {
                        pendingMessage.Content = string.Empty;
                        ScrollTranscriptToEnd();
                    }

                    _sessionTurns.Add(new SessionTurn(agent.Name, response));
                    PersistConversationSession(SelectedTheme);

                    var moreAgentsRemainInRound = agentIndex < enabledAgents.Count - 1;
                    var moreRoundsRemain = iteration < maxIterations;
                    var shouldDelay = moreAgentsRemainInRound || (!waitForUserReply && moreRoundsRemain);

                    if (moreAgentsRemainInRound)
                    {
                        var nextAgent = enabledAgents[agentIndex + 1];
                        var nextAgentMaxTokens = ResolveAgentMaxTokens(nextAgent, roomMaxTokens);
                        var nextAgentSettings = BuildRequestSettings(settings, string.IsNullOrWhiteSpace(nextAgent.Provider) ? "Groq Fast" : nextAgent.Provider, nextAgentMaxTokens);
                        var nextPrompt = BuildAgentPrompt(SelectedTheme, nextAgent, iteration, maxIterations, _sessionTurns);
                        prefetchedAgent = nextAgent;
                        prefetchedResponseTask = GenerateAgentResponseAsync(nextAgent, nextAgentSettings, nextPrompt, cancellationToken);
                    }
                    else
                    {
                        prefetchedAgent = null;
                        prefetchedResponseTask = null;

                        completedConversationRoundsAfterCurrentRound = _completedConversationRounds + 1;
                        var roundTurns = _sessionTurns.Skip(roundMemoryAnchorIndex).ToList();
                        var shouldPromoteDurableMemory = string.IsNullOrWhiteSpace(_themeDurableMemory)
                            || completedConversationRoundsAfterCurrentRound % DurableMemoryPromotionInterval == 0;
                        roundMemoryRefreshTask = RefreshAutomaticMemoryAsync(
                            SelectedTheme,
                            settings,
                            roundTurns,
                            _sessionTurns,
                            recentTurnsWindow,
                            shouldPromoteDurableMemory,
                            cancellationToken,
                            updateStatus: false);
                    }

                    var renderTask = AnimateMessageAsync(pendingMessage, response, cancellationToken);
                    var speechTask = QueueSpeech(pendingMessage, agent.TextToSpeechVoice, response, cancellationToken);
                    await WaitForTurnPacingAsync(agent.Name, renderTask, speechTask, shouldDelay ? agentDelaySeconds : 0, cancellationToken);

                    if (!moreAgentsRemainInRound)
                    {
                        _completedConversationRounds = completedConversationRoundsAfterCurrentRound;

                        if (!roundMemoryRefreshTask.IsCompleted)
                        {
                            StatusTextBlock.Text = "Finalizing round...";
                        }

                        await roundMemoryRefreshTask;
                    }
                }

                if (waitForUserReply)
                {
                    AddSystemMessage("Round complete. Add a user reply or click Continue to keep the agents going.");
                    StatusTextBlock.Text = "Waiting for you";
                    UpdateRunButtonText();
                    return;
                }
            }

            AddSystemMessage("Theme run complete. Add another message or click Continue to keep the conversation moving.");
            StatusTextBlock.Text = "Complete";
            UpdateRunButtonText();
        }
        catch (OperationCanceledException)
        {
            AddSystemMessage("Theme run stopped.");
            StatusTextBlock.Text = "Stopped";
        }
        catch (Exception exception)
        {
            AddSystemMessage($"Error: {exception.Message}");
            StatusTextBlock.Text = "Error";
        }
        finally
        {
            _isRunning = false;
            RunButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            _runCancellationTokenSource?.Dispose();
            _runCancellationTokenSource = null;

            if (StatusTextBlock.Text == "Running")
            {
                StatusTextBlock.Text = "Ready";
            }
        }
    }

    private ConnectionSettings ReadSettings()
    {
        if (!TryPersistAiSettings(out var errorMessage))
        {
            throw new InvalidOperationException(errorMessage);
        }

        var configuredSettings = _connectionSettingsStore.Load();
        ConnectionSettingsPath = _connectionSettingsStore.SettingsPath;
        ApplyLoadedConnectionSettings(configuredSettings);
        return configuredSettings;
    }

    private async Task<string> GenerateAgentResponseAsync(AgentProfile agent, LlmRequestSettings settings, string prompt, CancellationToken cancellationToken)
    {
        var effectiveSettings = AdjustAgentRequestSettings(agent, settings);

        var response = await CompleteWithLoggingAsync(
            agent.Name,
            effectiveSettings,
            new[]
            {
                new LlmChatMessage("system", agent.SystemPrompt),
                new LlmChatMessage("user", prompt),
            },
            cancellationToken);

        return SanitizeAgentResponse(response);
    }

    private ChatMessage AddPendingAgentMessage(AgentProfile agent, string content = "Thinking...")
    {
        var message = CreateAgentChatMessage(agent, agent.Name, content);
        AddMessage(message);
        return message;
    }

    private string BuildAgentPrompt(
        ThemeProfile theme,
        AgentProfile agent,
        int iteration,
        int maxIterations,
        IReadOnlyList<SessionTurn> sessionTurns)
    {
        var participants = string.Join(
            " -> ",
            theme.Agents.Where(item => item.IsEnabled).Select(item => item.Name));
        var recentTurns = sessionTurns
            .TakeLast(Math.Max(1, theme.RecentTurnsWindow))
            .Select(turn => $"{turn.Speaker}:\n{CondenseTurnForPrompt(theme, turn)}");
        var transcript = string.Join(
            "\n\n",
            recentTurns);
        var durableMemory = string.IsNullOrWhiteSpace(_themeDurableMemory)
            ? "(No durable world memory yet.)"
            : TakeTail(_themeDurableMemory, 3200);
        var sharedRoomMemory = string.IsNullOrWhiteSpace(_sharedRoomMemory)
            ? "(No recent shared room memory yet.)"
            : TakeTail(_sharedRoomMemory, 2600);

        return $"""
You are participating in a multi-agent conversation.

<context>
Theme name: {theme.Name}
Theme topic: {theme.Topic}
Participants in order: {participants}
Current round: {iteration} of {maxIterations}
You are: {agent.Name}
</context>

<durable_theme_memory>
{durableMemory}
</durable_theme_memory>

<shared_room_memory>
{sharedRoomMemory}
</shared_room_memory>

<recent_transcript>
{transcript}
</recent_transcript>

Reply requirements:
- Stay consistent with your own system prompt.
- Respond only as {agent.Name}. 
- Continue naturally from the latest conversation turn.
- Use durable theme memory for stable facts and shared room memory for current state.
- Use the transcript for immediate continuity and voice.
- Do not repeat transcript headings, XML tags, memory labels, or prompt scaffolding.
- Do not output phrases like 'Recent transcript window:' or restate the full transcript unless absolutely necessary.
""";
    }

    private void AddUserMessage(string content)
    {
        AddMessage(CreateUserChatMessage(content));
    }

    private void LoadConversationSession(ThemeProfile theme)
    {
        Messages.Clear();
        _sessionTurns.Clear();
        _completedConversationRounds = 0;

        var storedSession = _themeStore.LoadThemeConversation(theme);
        _completedConversationRounds = Math.Max(0, storedSession.CompletedRounds);

        foreach (var turn in storedSession.Turns)
        {
            var message = CreateChatMessageForStoredTurn(theme, turn);
            Messages.Add(message);
            _sessionTurns.Add(new SessionTurn(turn.Speaker, turn.Content));
        }

        UpdateRunButtonText();
        ScrollTranscriptToEnd();
    }

    private void PersistConversationSession(ThemeProfile? theme)
    {
        if (theme is null)
        {
            return;
        }

        var persistedTurns = new List<StoredConversationTurn>();
        for (var index = 0; index < _sessionTurns.Count; index++)
        {
            var turn = _sessionTurns[index];
            var timestampText = index < Messages.Count ? Messages[index].TimestampText : DateTime.Now.ToString("h:mm tt");
            persistedTurns.Add(new StoredConversationTurn(turn.Speaker, turn.Content, timestampText));
        }

        _themeStore.SaveThemeConversation(theme, new StoredConversationSession(_completedConversationRounds, persistedTurns));
    }

    private ChatMessage CreateChatMessageForStoredTurn(ThemeProfile theme, StoredConversationTurn turn)
    {
        if (string.Equals(turn.Speaker, "You", StringComparison.OrdinalIgnoreCase))
        {
            return CreateUserChatMessage(turn.Content, turn.TimestampText);
        }

        var agent = theme.Agents.FirstOrDefault(candidate => string.Equals(candidate.Name, turn.Speaker, StringComparison.OrdinalIgnoreCase));
        return CreateAgentChatMessage(agent, turn.Speaker, turn.Content, turn.TimestampText);
    }

    private ChatMessage CreateUserChatMessage(string content, string? timestampText = null)
    {
        return new ChatMessage(
            "You",
            content,
            GetApplicationBrush("AppUserMessageAccentBrush", "#34536B"),
            GetApplicationBrush("AppUserMessageBackgroundBrush", "#E4EEF6"),
            timestampText);
    }

    private ChatMessage CreateAgentChatMessage(AgentProfile? agent, string author, string content, string? timestampText = null)
    {
        var accentHex = agent?.AccentHex ?? "#C56A54";
        var backgroundHex = agent?.BackgroundHex ?? "#F9E5DE";

        return new ChatMessage(
            author,
            content,
            CreateBrush(accentHex),
            CreateAgentCardBackgroundBrush(backgroundHex),
            timestampText);
    }

    private Brush CreateAgentCardBackgroundBrush(string backgroundHex)
    {
        return UiThemeManager.IsDarkTheme(UiThemeSelection)
            ? GetApplicationBrush("AppSectionBackgroundBrush", "#1A232C")
            : CreateBrush(backgroundHex);
    }

    private void AddMessage(ChatMessage message)
    {
        Messages.Add(message);
        ScrollTranscriptToEnd();
    }

    private void FocusTranscriptMessage(ChatMessage message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TranscriptItemsControl.UpdateLayout();
            if (TranscriptItemsControl.ItemContainerGenerator.ContainerFromItem(message) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        }, DispatcherPriority.Background);
    }

    private void ScrollTranscriptToEnd()
    {
        Dispatcher.BeginInvoke(
            () => TranscriptScrollViewer.ScrollToEnd(),
            DispatcherPriority.Background);
    }

    private async Task WaitForTurnPacingAsync(string speaker, Task renderTask, Task speechTask, int delaySeconds, CancellationToken cancellationToken)
    {
        if (!TextToSpeechEnabled)
        {
            if (delaySeconds > 0)
            {
                await Task.WhenAll(renderTask, RunAgentDelayCountdownAsync(delaySeconds, cancellationToken));
            }
            else
            {
                await renderTask;
            }

            return;
        }

        Task? delayTask = null;
        DateTimeOffset delayDeadline = default;

        if (delaySeconds > 0)
        {
            delayTask = Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            delayDeadline = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
        }

        var pacingTasks = delayTask is null
            ? Task.WhenAll(renderTask, speechTask)
            : Task.WhenAll(renderTask, speechTask, delayTask);

        while (!pacingTasks.IsCompleted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!speechTask.IsCompleted)
            {
                StatusTextBlock.Text = $"{speaker} speaking";
            }
            else if (delayTask is not null && !delayTask.IsCompleted)
            {
                var remainingSeconds = Math.Max(1, (int)Math.Ceiling((delayDeadline - DateTimeOffset.UtcNow).TotalSeconds));
                StatusTextBlock.Text = $"Waiting {remainingSeconds}s for next turn";
            }

            var tickDelay = Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var completedTask = await Task.WhenAny(pacingTasks, tickDelay);
            if (completedTask == pacingTasks)
            {
                break;
            }
        }

        await pacingTasks;
    }

    private async Task RunAgentDelayCountdownAsync(int seconds, CancellationToken cancellationToken)
    {
        for (var remainingSeconds = seconds; remainingSeconds >= 1; remainingSeconds--)
        {
            StatusTextBlock.Text = $"Waiting {remainingSeconds}s for next turn";
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private async Task<T> RunWithElapsedStatusAsync<T>(string label, Task<T> operation, CancellationToken cancellationToken)
        => await RunWithElapsedStatusAsync(label, operation, cancellationToken, updateStatus: true);

    private async Task<T> RunWithElapsedStatusAsync<T>(string label, Task<T> operation, CancellationToken cancellationToken, bool updateStatus)
    {
        if (updateStatus)
        {
            StatusTextBlock.Text = label;
        }

        var stopwatch = Stopwatch.StartNew();

        while (!operation.IsCompleted)
        {
            var tickDelay = Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            var completedTask = await Task.WhenAny(operation, tickDelay);
            if (completedTask == operation)
            {
                break;
            }

            if (updateStatus)
            {
                StatusTextBlock.Text = $"{label} {(int)stopwatch.Elapsed.TotalSeconds}s";
            }
        }

        return await operation;
    }

    private async Task AnimateMessageAsync(ChatMessage message, string fullContent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fullContent) || fullContent == "(empty response)")
        {
            message.Content = fullContent;
            ScrollTranscriptToEnd();
            return;
        }

        var chunkSize = fullContent.Length switch
        {
            >= 1200 => 10,
            >= 700 => 7,
            >= 350 => 5,
            >= 140 => 3,
            _ => 2,
        };

        var delayMilliseconds = fullContent.Length switch
        {
            >= 1200 => 8,
            >= 700 => 10,
            >= 350 => 12,
            >= 140 => 16,
            _ => 20,
        };

        try
        {
            for (var index = 0; index < fullContent.Length; index += chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nextLength = Math.Min(index + chunkSize, fullContent.Length);
                message.Content = fullContent[..nextLength];
                ScrollTranscriptToEnd();

                if (nextLength < fullContent.Length)
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            message.Content = fullContent;
            ScrollTranscriptToEnd();
            throw;
        }
    }

    private void ResetConversationSession()
    {
        Messages.Clear();
        _sessionTurns.Clear();
        _completedConversationRounds = 0;
        UpdateRunButtonText();
    }

    private void UpdateRunButtonText()
    {
        RunButtonText = _sessionTurns.Count == 0 ? "Run" : "Continue";
    }

    private LlmRequestSettings BuildRequestSettings(ConnectionSettings configuredSettings, string providerName, int maxTokens)
    {
        var selectionName = NormalizeModelSelection(providerName, configuredSettings);
        if (TryResolveConfiguredModel(configuredSettings, selectionName, out var modelEntry, out var connectionProfile))
        {
            var resolvedModel = modelEntry!;
            var resolvedConnection = connectionProfile!;
            var resolvedEndpoint = resolvedConnection.Endpoint.Trim();
            var resolvedModelId = resolvedModel.ModelId.Trim();
            var resolvedApiKey = resolvedConnection.ApiKey.Trim();

            if (string.IsNullOrWhiteSpace(resolvedEndpoint))
            {
                throw new InvalidOperationException($"Endpoint is required for connection '{resolvedConnection.Name}' in {ConnectionSettingsPath}.");
            }

            if (string.IsNullOrWhiteSpace(resolvedModelId))
            {
                throw new InvalidOperationException($"Model ID is required for '{resolvedModel.Name}' in {ConnectionSettingsPath}.");
            }

            if (maxTokens < 50)
            {
                throw new InvalidOperationException("Max tokens must be a number greater than or equal to 50.");
            }

            if (ConnectionSettingsStore.TransportRequiresApiKey(resolvedConnection.Transport) && string.IsNullOrWhiteSpace(resolvedApiKey))
            {
                throw new InvalidOperationException($"{resolvedConnection.Name} requires an API key in {ConnectionSettingsPath}.");
            }

            return new LlmRequestSettings(
                ParseTransport(resolvedConnection.Transport),
                resolvedEndpoint,
                resolvedModelId,
                resolvedApiKey,
                maxTokens,
                resolvedModel.Name,
                resolvedConnection.Name);
        }

        throw new InvalidOperationException($"Model '{providerName}' is not configured in {ConnectionSettingsPath}.");
    }

    private async Task<string> CompleteWithLoggingAsync(
        string source,
        LlmRequestSettings settings,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken cancellationToken)
    {
        AddLog(
            LogCategory.Request,
            source,
            $"Sending {messages.Count} message(s) to {settings.ProviderLabel}.",
            BuildRequestLogDetail(settings, messages));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await _llmClient.CompleteAsync(settings, messages, cancellationToken);
            stopwatch.Stop();

            var reasonSuffix = string.IsNullOrWhiteSpace(response.CompletionReason)
                ? string.Empty
                : $" Finish reason: {response.CompletionReason}.";
            var usageSuffix = string.IsNullOrWhiteSpace(response.UsageSummary)
                ? string.Empty
                : $" Usage: {response.UsageSummary}.";

            AddLog(
                LogCategory.Timing,
                source,
                $"{settings.ProviderLabel} completed in {stopwatch.ElapsedMilliseconds} ms.{reasonSuffix}{usageSuffix}",
                $"Model: {settings.Model}\nEndpoint: {settings.Endpoint}",
                stopwatch.ElapsedMilliseconds);

            AddLog(
                LogCategory.Response,
                source,
                $"Received {response.Content.Length} characters from {settings.ProviderLabel}.{reasonSuffix}",
                BuildResponseLogDetail(response));

            return response.Content;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();

            AddLog(
                LogCategory.Timing,
                source,
                $"{settings.ProviderLabel} canceled after {stopwatch.ElapsedMilliseconds} ms.",
                $"Model: {settings.Model}\nEndpoint: {settings.Endpoint}",
                stopwatch.ElapsedMilliseconds);

            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            AddLog(
                LogCategory.Timing,
                source,
                $"{settings.ProviderLabel} failed after {stopwatch.ElapsedMilliseconds} ms.",
                $"Model: {settings.Model}\nEndpoint: {settings.Endpoint}",
                stopwatch.ElapsedMilliseconds);

            AddLog(LogCategory.System, source, $"Request failed: {exception.Message}");
            throw;
        }
    }

    private static string BuildRequestLogDetail(LlmRequestSettings settings, IReadOnlyList<LlmChatMessage> messages)
    {
        var builder = new List<string>
        {
            $"Model selection: {settings.ProviderLabel}",
            $"Connection: {settings.ConnectionName}",
            $"Transport: {settings.Provider}",
            $"Model ID: {settings.Model}",
            $"Endpoint: {settings.Endpoint}",
            $"Max tokens: {settings.MaxCompletionTokens}",
            string.Empty,
            "Messages:",
        };

        if (settings.Provider == LlmProvider.Gemini && !string.IsNullOrWhiteSpace(settings.GeminiThinkingLevel))
        {
            builder.Insert(4, $"Gemini thinking level: {settings.GeminiThinkingLevel}");
        }

        if (settings.Provider == LlmProvider.HuggingFace && !string.IsNullOrWhiteSpace(settings.HuggingFaceReasoningEffort))
        {
            builder.Insert(4, $"Hugging Face reasoning effort: {settings.HuggingFaceReasoningEffort}");
        }

        foreach (var message in messages)
        {
            builder.Add($"[{message.Role}]");
            builder.Add(message.Content);
            builder.Add(string.Empty);
        }

        return string.Join("\n", builder).TrimEnd();
    }

    private static string BuildResponseLogDetail(LlmCompletionResult response)
    {
        var builder = new List<string>();

        if (!string.IsNullOrWhiteSpace(response.CompletionReason))
        {
            builder.Add($"Completion reason: {response.CompletionReason}");
        }

        if (!string.IsNullOrWhiteSpace(response.UsageSummary))
        {
            builder.Add($"Usage: {response.UsageSummary}");
        }

        if (builder.Count > 0)
        {
            builder.Add(string.Empty);
        }

        builder.Add("Content:");
        builder.Add(response.Content);
        builder.Add(string.Empty);
        builder.Add("Raw provider response:");
        builder.Add(TrimForLog(response.RawResponseBody, 40000));

        return string.Join("\n", builder);
    }

    private static string TrimForLog(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "\n... (truncated in log)";
    }

    private static int ResolveAgentMaxTokens(AgentProfile agent, int roomMaxTokens)
    {
        return agent.MaxTokensOverride is >= 50 ? agent.MaxTokensOverride.Value : roomMaxTokens;
    }

    private static LlmRequestSettings AdjustAgentRequestSettings(AgentProfile agent, LlmRequestSettings settings)
    {
        return settings;
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private static Brush GetApplicationBrush(string resourceKey, string fallbackColor)
    {
        if (Application.Current?.Resources[resourceKey] is Brush brush)
        {
            return brush;
        }

        return CreateBrush(fallbackColor);
    }

    private static string CondenseForPrompt(string value, int maxLength)
    {
        var normalized = string.Join(
            " ",
            value
                .Replace("\r", string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !line.StartsWith("What would you like to do next", StringComparison.OrdinalIgnoreCase)));

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        const string omissionMarker = " ...[middle omitted]... ";
        if (maxLength <= omissionMarker.Length + 20)
        {
            return normalized[..maxLength];
        }

        var remainingBudget = maxLength - omissionMarker.Length;
        var headLength = Math.Max(20, (int)Math.Ceiling(remainingBudget * 0.45));
        var tailLength = Math.Max(20, remainingBudget - headLength);

        if (headLength + tailLength > remainingBudget)
        {
            tailLength = Math.Max(20, remainingBudget - headLength);
        }

        return normalized[..headLength] + omissionMarker + normalized[^tailLength..];
    }

    private static string CondenseTurnForPrompt(ThemeProfile theme, SessionTurn turn)
    {
        var maxLength = theme.UserCompactionBudget;
        if (!string.Equals(turn.Speaker, "You", StringComparison.OrdinalIgnoreCase))
        {
            var agent = theme.Agents.FirstOrDefault(candidate => string.Equals(candidate.Name, turn.Speaker, StringComparison.OrdinalIgnoreCase));
            maxLength = agent?.CompactionBudget ?? 420;
        }

        return CondenseForPrompt(turn.Content, maxLength);
    }

    private static string TakeTail(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[^maxLength..];
    }

    private static string SanitizeAgentResponse(string value)
    {
        var scaffoldPrefixes = new[]
        {
            "Recent transcript window:",
            "Long-term memory:",
            "Short-term memory:",
            "Instructions:",
            "Reply requirements:",
            "<recent_transcript>",
            "</recent_transcript>",
            "<long_term_memory>",
            "</long_term_memory>",
            "<short_term_memory>",
            "</short_term_memory>",
            "<context>",
            "</context>",
        };

        var lines = value
            .Replace("\r", string.Empty)
            .Split('\n');

        var keptLines = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (scaffoldPrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            keptLines.Add(line);
        }

        var cleaned = string.Join("\n", keptLines).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "(empty response)" : cleaned;
    }

    private static string BuildCopiedChatMessageText(ChatMessage message)
    {
        return $"{message.Author} ({message.TimestampText})\n{message.Content}";
    }

    private static string BuildCopiedLogEntryText(LogEntry entry)
    {
        var lines = new List<string>
        {
            $"[{entry.CategoryText}] {entry.SourceText}",
            $"Timestamp: {entry.Timestamp:O}",
            $"Message: {entry.Message}",
        };

        if (entry.DurationMilliseconds is long durationMilliseconds)
        {
            lines.Add($"Duration: {durationMilliseconds} ms");
        }

        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            lines.Add(string.Empty);
            lines.Add("Details:");
            lines.Add(entry.Detail);
        }

        return string.Join("\n", lines);
    }
}