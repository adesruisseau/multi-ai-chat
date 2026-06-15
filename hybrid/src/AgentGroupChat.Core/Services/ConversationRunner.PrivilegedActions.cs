using AgentGroupChat.Core.Models.Domain;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentGroupChat.Core.Services
{
    public sealed partial class ConversationRunner
    {

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
        private static readonly Regex AddTrackerRegex = new(
            @"<add_tracker\b(?<attrs>[^>]*)>(?<body>.*?)</add_tracker>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RemoveTrackerRegex = new(
            @"<remove_tracker\b(?<attrs>[^>]*)\s*/>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex UpdateTrackerValueRegex = new(
            @"<update_tracker\b(?<attrs>[^>]*)\s*/>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TrackerKeyRegex = new(
            @"^[A-Za-z][A-Za-z0-9_-]{0,39}$",
            RegexOptions.Compiled);

        private const int TrackerPromptMaxLength = 280;
        private const int TrackerPrivilegedPromptMaxLength = 280;

        private enum PrivilegedActionKind
        {
            ResumeAgent,
            DismissNpc,
            SuspendAgent,
            SpawnNpc,
            RemoveTracker,
            AddTracker,
            UpdateTrackerValue
        }

        abstract record RequestedPrivilegedAction(
            PrivilegedActionKind Kind,
            int Index,
            string? Name = null,
            string? Body = null,
            string? Gender = null,
            int? Rounds = null,
            string? Key = null,
            string? Value = null,
            string? TrackerValueType = null,
            string? MinValueText = null,
            string? MaxValueText = null,
            string? PromptText = null,
            string? PrivilegedPromptText = null);

        record SpawnNpcAction(int Index, string? Name, string? Body, string? Gender = null, int? Rounds = null) : RequestedPrivilegedAction(PrivilegedActionKind.SpawnNpc, Index: Index, Name: Name, Body: Body, Gender: Gender, Rounds: Rounds);
        record DismissNpcAction(int Index, string? Name, string? Body, string? Gender = null, int? Rounds = null) : RequestedPrivilegedAction(PrivilegedActionKind.DismissNpc, Index: Index, Name: Name, Body: Body, Gender: Gender, Rounds: Rounds);
        record SuspendAgentAction(int Index, string? Name, string? Body, string? Gender = null, int? Rounds = null) : RequestedPrivilegedAction(PrivilegedActionKind.SuspendAgent, Index: Index, Name: Name, Body: Body, Gender: Gender, Rounds: Rounds);
        record ResumeAgentAction(int Index, string? Name, string? Body, string? Gender = null, int? Rounds = null) : RequestedPrivilegedAction(PrivilegedActionKind.ResumeAgent, Index: Index, Name: Name, Body: Body, Gender: Gender, Rounds: Rounds);
        record RemoveTrackerAction(int Index, string Key) : RequestedPrivilegedAction(PrivilegedActionKind.RemoveTracker, Index: Index, Key: Key);
        record AddTrackerAction(
            int Index,
            string Key,
            string ValueType,
            string Value,
            string PromptText,
            string PrivilegedPromptText,
            string? MinValueText = null,
            string? MaxValueText = null)
            : RequestedPrivilegedAction(
                PrivilegedActionKind.AddTracker,
                Index: Index,
                Key: Key,
                Value: Value,
                TrackerValueType: ValueType,
                MinValueText: MinValueText,
                MaxValueText: MaxValueText,
                PromptText: PromptText,
                PrivilegedPromptText: PrivilegedPromptText);
        record UpdateTrackerAction(int Index, string Key, string Value) : RequestedPrivilegedAction(PrivilegedActionKind.UpdateTrackerValue, Index: Index, Key: Key, Value: Value);

        private static bool IsPrivilegedAgent(RoomConfig room, AgentConfig agent) =>
        room.EnablePrivilegedActions
        && !agent.IsNpc
        && string.Equals(agent.Id, room.PrivilegedAgentId, StringComparison.Ordinal);


        private IReadOnlyList<RequestedPrivilegedAction> ParsePrivilegedActions(
        RoomConfig room,
        AgentConfig agent,
        string privilegedActionsBlock)
        {
            if (string.IsNullOrWhiteSpace(privilegedActionsBlock))
                return [];

            var blockMatch = PrivilegedActionsBlockRegex.Match(privilegedActionsBlock);
            var actionBlock = blockMatch.Success
                ? blockMatch.Groups["body"].Value
                : privilegedActionsBlock;

            if (!IsPrivilegedAgent(room, agent))
            {
                OnLog?.Invoke($"PrivilegedActions.Rejected: {agent.Name} is not allowed to manage privileged actions.");
                return [];
            }

            var requestedActions = ParseRequestedPrivilegedActions(actionBlock)
                .OrderBy(a => a.Index)
                .ToList();
            if (requestedActions.Count == 0)
            {
                OnLog?.Invoke($"PrivilegedActions.Rejected: {agent.Name} emitted an empty privileged action block.");
                return [];
            }

            OnLog?.Invoke($"PrivilegedActions.Requested: {agent.Name} requested {string.Join(", ", requestedActions.Select(DescribePrivilegedAction))}.");
            return ValidateRequestedPrivilegedActions(room, agent, requestedActions);
        }

        private List<RequestedPrivilegedAction> ValidateRequestedPrivilegedActions(
            RoomConfig room,
            AgentConfig sourceAgent,
            IReadOnlyList<RequestedPrivilegedAction> requestedActions)
        {
            var accepted = new List<RequestedPrivilegedAction>();
            var limitedActions = requestedActions.OrderBy(a => a.Index).ToList();

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
            var trackersByKey = room.DataTrackers
                .GroupBy(t => t.DataKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var action in limitedActions.OrderBy(a => GetActionPhaseOrder(a.Kind)).ThenBy(a => a.Index))
            {
                if (!AllowsMultipleActions(action.Kind) && !seenKinds.Add(action.Kind))
                {
                    OnLog?.Invoke($"PrivilegedActions.Rejected: only one {action.Kind} action is allowed per turn.");
                    continue;
                }

                switch (action.Kind)
                {
                    case PrivilegedActionKind.ResumeAgent:
                        var resumeName = action.Name?.Trim();
                        if (string.IsNullOrWhiteSpace(resumeName)
                            || !enabledAgentsByName.TryGetValue(resumeName, out var resumeTarget)
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
                        var dismissName = action.Name?.Trim();
                        if (string.IsNullOrWhiteSpace(dismissName)
                            || !enabledAgentsByName.TryGetValue(dismissName, out var dismissTarget)
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
                        var suspendName = action.Name?.Trim();
                        if (string.IsNullOrWhiteSpace(suspendName)
                            || !enabledAgentsByName.TryGetValue(suspendName, out var suspendTarget)
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
                    case PrivilegedActionKind.RemoveTracker:
                        if (string.IsNullOrWhiteSpace(action.Key))
                        {
                            OnLog?.Invoke("PrivilegedActions.Rejected: remove_tracker requires a key.");
                            continue;
                        }

                        if (!trackersByKey.TryGetValue(action.Key.Trim(), out var removeTarget))
                        {
                            OnLog?.Invoke($"PrivilegedActions.Rejected: remove_tracker '{action.Key}' was not matched to a room DataTracker.");
                            continue;
                        }

                        trackersByKey.Remove(removeTarget.DataKey);
                        accepted.Add(new RemoveTrackerAction(action.Index, removeTarget.DataKey));
                        OnLog?.Invoke($"Tracker.RemoveQueued: {removeTarget.DataKey}");
                        break;

                    case PrivilegedActionKind.AddTracker:
                        if (!TryCreateTrackerFromAction(room, action, out var newTracker, out var addTrackerRejection))
                        {
                            OnLog?.Invoke($"PrivilegedActions.Rejected: {addTrackerRejection}");
                            continue;
                        }

                        if (trackersByKey.ContainsKey(newTracker.DataKey))
                        {
                            OnLog?.Invoke($"PrivilegedActions.Rejected: add_tracker '{newTracker.DataKey}' conflicts with an existing room DataTracker.");
                            continue;
                        }

                        trackersByKey[newTracker.DataKey] = newTracker;
                        accepted.Add(new AddTrackerAction(
                            action.Index,
                            newTracker.DataKey,
                            newTracker.ValueType,
                            newTracker.Value,
                            newTracker.PromptText,
                            newTracker.PrivilegedAgentPrompt,
                            FormatTrackerNumber(newTracker.MinValue),
                            FormatTrackerNumber(newTracker.MaxValue)));
                        OnLog?.Invoke($"Tracker.AddQueued: {newTracker.DataKey}");
                        break;

                    case PrivilegedActionKind.UpdateTrackerValue:
                        if (string.IsNullOrWhiteSpace(action.Key))
                        {
                            OnLog?.Invoke("PrivilegedActions.Rejected: update_tracker requires a key.");
                            continue;
                        }

                        if (!trackersByKey.TryGetValue(action.Key.Trim(), out var identifiedTracker))
                        {
                            OnLog?.Invoke($"PrivilegedActions.Rejected: update_tracker '{action.Key}' was not matched to a room DataTracker.");
                            continue;
                        }

                        if (!TryNormalizeTrackerValue(identifiedTracker, action.Value, out var normalizedValue, out var updateTrackerRejection))
                        {
                            OnLog?.Invoke($"PrivilegedActions.Rejected: {updateTrackerRejection}");
                            continue;
                        }

                        identifiedTracker.Value = normalizedValue;
                        accepted.Add(new UpdateTrackerAction(action.Index, identifiedTracker.DataKey, normalizedValue));
                        OnLog?.Invoke($"Tracker.UpdateQueued: {identifiedTracker.DataKey}={normalizedValue}");
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
                appSettings = await _settingsRepo.GetAsync(GetRequiredUserId(room));
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

                room.Agents.Remove(target);
                roomChanged = true;

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
                target.SuspensionReason = NormalizeReason(action.Body ?? string.Empty, "Off-scene until resumed.");
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

            foreach (var action in actions.Where(a => a.Kind == PrivilegedActionKind.RemoveTracker))
            {
                var tracker = room.DataTrackers
                    .FirstOrDefault(x => x.DataKey.Equals(action.Key, StringComparison.OrdinalIgnoreCase));

                if (tracker is null)
                    continue;

                room.DataTrackers.Remove(tracker);
                roomChanged = true;
                OnLog?.Invoke($"Tracker.RemoveApplied: {tracker.DataKey}");
            }

            foreach (var action in actions.Where(a => a.Kind == PrivilegedActionKind.AddTracker))
            {
                if (room.DataTrackers.Any(x => x.DataKey.Equals(action.Key, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var tracker = CreateTrackerFromValidatedAction(room, action);
                room.DataTrackers.Add(tracker);
                roomChanged = true;
                OnLog?.Invoke($"Tracker.AddApplied: {tracker.DataKey}");
            }

            foreach (var action in actions.Where(a => a.Kind == PrivilegedActionKind.UpdateTrackerValue))
            {
                var tracker = room.DataTrackers
                    .FirstOrDefault(x =>
                        x.DataKey.Equals(action.Key,
                        StringComparison.OrdinalIgnoreCase));

                    if (tracker == null)
                        continue;

                    tracker.Value = action.Value ?? string.Empty;
                    roomChanged = true;

                    OnLog?.Invoke(
                        $"Tracker.UpdateApplied: {tracker.DataKey}={tracker.Value}");
            }

            if (roomChanged)
                await _roomRepo.SaveAsync(room);
        }

        private static bool AllowsMultipleActions(PrivilegedActionKind kind) =>
            kind is PrivilegedActionKind.RemoveTracker
                or PrivilegedActionKind.AddTracker
                or PrivilegedActionKind.UpdateTrackerValue;

        private static DataTrackerConfig CreateTrackerFromValidatedAction(RoomConfig room, RequestedPrivilegedAction action) =>
            new()
            {
                RoomId = room.Id,
                AgentId = null,
                DataKey = action.Key?.Trim() ?? string.Empty,
                Value = action.Value?.Trim() ?? string.Empty,
                ValueType = NormalizeTrackerValueType(action.TrackerValueType),
                MinValue = ParseNullableDouble(action.MinValueText),
                MaxValue = ParseNullableDouble(action.MaxValueText),
                Enabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
                PromptText = NormalizeTrackerPrompt(action.PromptText),
                PrivilegedAgentPrompt = NormalizeTrackerPrompt(action.PrivilegedPromptText),
            };

        private static bool TryCreateTrackerFromAction(
            RoomConfig room,
            RequestedPrivilegedAction action,
            out DataTrackerConfig tracker,
            out string rejectionReason)
        {
            tracker = new DataTrackerConfig();
            rejectionReason = string.Empty;

            var key = action.Key?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                rejectionReason = "add_tracker requires a key.";
                return false;
            }

            if (!TrackerKeyRegex.IsMatch(key))
            {
                rejectionReason = $"add_tracker '{key}' must start with a letter and use only letters, numbers, underscores, or hyphens.";
                return false;
            }

            var valueType = NormalizeTrackerValueType(action.TrackerValueType);
            if (string.IsNullOrWhiteSpace(valueType))
            {
                rejectionReason = $"add_tracker '{key}' must use type='string', 'int', 'decimal', or 'bool'.";
                return false;
            }

            var promptText = NormalizeTrackerPrompt(action.PromptText);
            if (string.IsNullOrWhiteSpace(promptText))
            {
                rejectionReason = $"add_tracker '{key}' requires a non-empty <agent_prompt>.";
                return false;
            }

            if (promptText.Length > TrackerPromptMaxLength)
            {
                rejectionReason = $"add_tracker '{key}' has an <agent_prompt> longer than {TrackerPromptMaxLength} characters.";
                return false;
            }

            var privilegedPromptText = NormalizeTrackerPrompt(action.PrivilegedPromptText);
            if (string.IsNullOrWhiteSpace(privilegedPromptText))
            {
                rejectionReason = $"add_tracker '{key}' requires a non-empty <privileged_prompt>.";
                return false;
            }

            if (privilegedPromptText.Length > TrackerPrivilegedPromptMaxLength)
            {
                rejectionReason = $"add_tracker '{key}' has a <privileged_prompt> longer than {TrackerPrivilegedPromptMaxLength} characters.";
                return false;
            }

            if (promptText.Contains('<') || promptText.Contains('>') || privilegedPromptText.Contains('<') || privilegedPromptText.Contains('>'))
            {
                rejectionReason = $"add_tracker '{key}' prompt text cannot contain '<' or '>' characters.";
                return false;
            }

            tracker = new DataTrackerConfig
            {
                RoomId = room.Id,
                AgentId = null,
                DataKey = key,
                ValueType = valueType,
                Enabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
                PromptText = promptText,
                PrivilegedAgentPrompt = privilegedPromptText,
            };

            switch (valueType)
            {
                case "string":
                    if (!string.IsNullOrWhiteSpace(action.MinValueText) || !string.IsNullOrWhiteSpace(action.MaxValueText))
                    {
                        rejectionReason = $"add_tracker '{key}' cannot use min/max with type='string'.";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(action.Value))
                    {
                        rejectionReason = $"add_tracker '{key}' requires a non-empty value.";
                        return false;
                    }

                    tracker.Value = action.Value.Trim();
                    return true;

                case "bool":
                    if (!string.IsNullOrWhiteSpace(action.MinValueText) || !string.IsNullOrWhiteSpace(action.MaxValueText))
                    {
                        rejectionReason = $"add_tracker '{key}' cannot use min/max with type='bool'.";
                        return false;
                    }

                    if (!bool.TryParse(action.Value, out var boolValue))
                    {
                        rejectionReason = $"add_tracker '{key}' must use value='true' or value='false'.";
                        return false;
                    }

                    tracker.Value = boolValue ? "true" : "false";
                    return true;

                case "int":
                    if (!int.TryParse(action.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                    {
                        rejectionReason = $"add_tracker '{key}' must use an integer value.";
                        return false;
                    }

                    if (!int.TryParse(action.MinValueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minInt)
                        || !int.TryParse(action.MaxValueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxInt))
                    {
                        rejectionReason = $"add_tracker '{key}' must include integer min and max values.";
                        return false;
                    }

                    if (maxInt < minInt)
                    {
                        rejectionReason = $"add_tracker '{key}' must use max >= min.";
                        return false;
                    }

                    if (intValue < minInt || intValue > maxInt)
                    {
                        rejectionReason = $"add_tracker '{key}' must start within the [{minInt}, {maxInt}] range.";
                        return false;
                    }

                    tracker.Value = intValue.ToString(CultureInfo.InvariantCulture);
                    tracker.MinValue = minInt;
                    tracker.MaxValue = maxInt;
                    return true;

                case "decimal":
                    if (!decimal.TryParse(action.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
                    {
                        rejectionReason = $"add_tracker '{key}' must use a decimal value.";
                        return false;
                    }

                    if (!decimal.TryParse(action.MinValueText, NumberStyles.Number, CultureInfo.InvariantCulture, out var minDecimal)
                        || !decimal.TryParse(action.MaxValueText, NumberStyles.Number, CultureInfo.InvariantCulture, out var maxDecimal))
                    {
                        rejectionReason = $"add_tracker '{key}' must include decimal min and max values.";
                        return false;
                    }

                    if (maxDecimal < minDecimal)
                    {
                        rejectionReason = $"add_tracker '{key}' must use max >= min.";
                        return false;
                    }

                    if (decimalValue < minDecimal || decimalValue > maxDecimal)
                    {
                        rejectionReason = $"add_tracker '{key}' must start within the [{minDecimal.ToString(CultureInfo.InvariantCulture)}, {maxDecimal.ToString(CultureInfo.InvariantCulture)}] range.";
                        return false;
                    }

                    tracker.Value = decimalValue.ToString(CultureInfo.InvariantCulture);
                    tracker.MinValue = (double)minDecimal;
                    tracker.MaxValue = (double)maxDecimal;
                    return true;

                default:
                    rejectionReason = $"add_tracker '{key}' uses an unsupported type '{action.TrackerValueType}'.";
                    return false;
            }
        }

        private static bool TryNormalizeTrackerValue(
            DataTrackerConfig tracker,
            string? rawValue,
            out string normalizedValue,
            out string rejectionReason)
        {
            normalizedValue = string.Empty;
            rejectionReason = string.Empty;

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                rejectionReason = $"update_tracker '{tracker.DataKey}' requires a value.";
                return false;
            }

            switch (NormalizeTrackerValueType(tracker.ValueType))
            {
                case "string":
                    normalizedValue = rawValue.Trim();
                    return true;

                case "bool":
                    if (!bool.TryParse(rawValue, out var boolValue))
                    {
                        rejectionReason = $"update_tracker '{tracker.DataKey}' must use value='true' or value='false'.";
                        return false;
                    }

                    normalizedValue = boolValue ? "true" : "false";
                    return true;

                case "int":
                    if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                    {
                        rejectionReason = $"update_tracker '{tracker.DataKey}' must use an integer value.";
                        return false;
                    }

                    if (!IsWithinTrackerRange(intValue, tracker))
                    {
                        rejectionReason = $"update_tracker '{tracker.DataKey}' must stay within the configured numeric range.";
                        return false;
                    }

                    normalizedValue = intValue.ToString(CultureInfo.InvariantCulture);
                    return true;

                case "decimal":
                    if (!decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue))
                    {
                        rejectionReason = $"update_tracker '{tracker.DataKey}' must use a decimal value.";
                        return false;
                    }

                    if (!IsWithinTrackerRange((double)decimalValue, tracker))
                    {
                        rejectionReason = $"update_tracker '{tracker.DataKey}' must stay within the configured numeric range.";
                        return false;
                    }

                    normalizedValue = decimalValue.ToString(CultureInfo.InvariantCulture);
                    return true;

                default:
                    rejectionReason = $"update_tracker '{tracker.DataKey}' uses unsupported tracker type '{tracker.ValueType}'.";
                    return false;
            }
        }

        private static bool IsWithinTrackerRange(double value, DataTrackerConfig tracker)
        {
            if (tracker.MinValue.HasValue && value < tracker.MinValue.Value)
                return false;

            if (tracker.MaxValue.HasValue && value > tracker.MaxValue.Value)
                return false;

            return true;
        }

        private static string NormalizeTrackerValueType(string? value) =>
            value?.Trim().ToLowerInvariant() switch
            {
                "string" => "string",
                "int" => "int",
                "integer" => "int",
                "decimal" => "decimal",
                "number" => "decimal",
                "bool" => "bool",
                "boolean" => "bool",
                _ => string.Empty,
            };

        private static string NormalizeTrackerPrompt(string? value) =>
            string.Join(" ", (value ?? string.Empty)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Trim();

        private static string FormatTrackerNumber(double? value) =>
            value.HasValue
                ? value.Value.ToString("0.################", CultureInfo.InvariantCulture)
                : string.Empty;

        private static double? ParseNullableDouble(string? value) =>
            double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

        private AgentConfig CreateSpawnedNpc(
        RoomConfig room,
        AgentConfig sourceAgent,
        RequestedPrivilegedAction action,
        AppSettings? appSettings,
        IReadOnlyList<string> kokoroVoices)
        {
            var colorTheme = ChooseNpcColors(room);
            var voice = ResolveNpcVoice(room, action, appSettings, kokoroVoices);
            var sortOrder = room.Agents.Count == 0 ? 0 : room.Agents.Max(a => a.SortOrder) + 1;

            return new AgentConfig
            {
                RoomId = room.Id,
                Name = action.Name?.Trim() ?? string.Empty,
                ModelId = string.IsNullOrWhiteSpace(room.NpcModelId) ? sourceAgent.ModelId : room.NpcModelId.Trim(),
                SystemPrompt = BuildNpcSystemPrompt(room, action),
                IsEnabled = true,
                MaxTokensOverride = room.NpcMaxTokens,
                CompactionBudget = room.NpcCompactionBudget > 0 ? room.NpcCompactionBudget : 300,
                ColorTheme = colorTheme,
                TtsVoice = string.IsNullOrWhiteSpace(voice) ? string.Empty : voice.Trim(),
                IsNpc = true,
                SpawnedByAgentId = sourceAgent.Id,
                SortOrder = sortOrder,
                PromptSampleId = room.NpcPromptSampleId
            };
        }

        private static string BuildNpcSystemPrompt(RoomConfig room, RequestedPrivilegedAction action)
        {
            var baseInstructions = string.IsNullOrWhiteSpace(room.NpcBaseInstructions)
                ? "Stay in character and respond only as this NPC. Follow the scene being driven by the narrator and the current room state. Do not act as narrator, adjudicator, or DM. Do not emit privileged-action tags."
                : room.NpcBaseInstructions.Trim();
            var description = NormalizeReason(action.Body ?? string.Empty, "Secondary NPC in the current scene.");

            return $"""
{baseInstructions}

Character Name: {action.Name?.Trim() ?? string.Empty}
Character Description:
{description}
""";
        }

        private static string ChooseNpcColors(RoomConfig room)
        {
            var usedColors = new HashSet<string>(
                room.Agents.Where(a => a.IsEnabled).Select(a => a.ColorTheme),
                StringComparer.OrdinalIgnoreCase);

            foreach (var preset in NpcColorPresets)
            {
                if (!usedColors.Contains(preset))
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

            CollectTrackerAdds(actions, actionBlock);
            CollectTrackerRemovals(actions, actionBlock);
            CollectTrackerUpdates(actions, actionBlock);

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
                switch (kind)
                {
                    case PrivilegedActionKind.SpawnNpc:
                        {
                            actions.Add(new SpawnNpcAction(match.Index,
                                                           Name:  ExtractAttributeValue(attrs, "name"),
                                                           Body:  match.Groups["body"].Value.Trim(),
                                                           Gender: genderSelector(attrs),
                                                           Rounds: roundsSelector(attrs)));
                            break;
                        }
                    case PrivilegedActionKind.DismissNpc:
                        {
                            actions.Add(new DismissNpcAction(match.Index,
                                                           Name:  ExtractAttributeValue(attrs, "name"),
                                                           Body:  match.Groups["body"].Value.Trim(),
                                                           Gender: genderSelector(attrs),
                                                           Rounds: roundsSelector(attrs)));
                            break;
                        }
                    case PrivilegedActionKind.ResumeAgent:
                        {
                            actions.Add(new ResumeAgentAction(match.Index,
                                                           Name:  ExtractAttributeValue(attrs, "name"),
                                                           Body:  match.Groups["body"].Value.Trim(),
                                                           Gender: genderSelector(attrs),
                                                           Rounds: roundsSelector(attrs)));
                            break;
                        }
                    case PrivilegedActionKind.SuspendAgent:
                        {
                            actions.Add(new SuspendAgentAction(match.Index,
                                                           Name:  ExtractAttributeValue(attrs, "name"),
                                                           Body:  match.Groups["body"].Value.Trim(),
                                                           Gender: genderSelector(attrs),
                                                           Rounds: roundsSelector(attrs)));
                            break;
                        }
                }
            }
        }

        private static void CollectTrackerUpdates(
        ICollection<RequestedPrivilegedAction> actions,
        string block)
        {
            foreach (Match match in UpdateTrackerValueRegex.Matches(block))
            {
                var attrs = match.Groups["attrs"].Value;

                actions.Add(new UpdateTrackerAction(
                    Index: match.Index,
                    Key: ExtractAttributeValue(attrs, "key"),
                    Value: ExtractAttributeValue(attrs, "value")));
            }
        }

        private static void CollectTrackerAdds(
            ICollection<RequestedPrivilegedAction> actions,
            string block)
        {
            foreach (Match match in AddTrackerRegex.Matches(block))
            {
                var attrs = match.Groups["attrs"].Value;
                var body = match.Groups["body"].Value;

                actions.Add(new AddTrackerAction(
                    Index: match.Index,
                    Key: ExtractAttributeValue(attrs, "key"),
                    ValueType: ExtractAttributeValue(attrs, "type"),
                    Value: ExtractAttributeValue(attrs, "value"),
                    PromptText: ExtractTaggedBodyContent(body, "agent_prompt"),
                    PrivilegedPromptText: ExtractTaggedBodyContent(body, "privileged_prompt"),
                    MinValueText: ExtractAttributeValue(attrs, "min"),
                    MaxValueText: ExtractAttributeValue(attrs, "max")));
            }
        }

        private static void CollectTrackerRemovals(
            ICollection<RequestedPrivilegedAction> actions,
            string block)
        {
            foreach (Match match in RemoveTrackerRegex.Matches(block))
            {
                var attrs = match.Groups["attrs"].Value;
                actions.Add(new RemoveTrackerAction(
                    Index: match.Index,
                    Key: ExtractAttributeValue(attrs, "key")));
            }
        }

        private static string DescribePrivilegedAction(RequestedPrivilegedAction action) =>
            action.Kind switch
            {
                PrivilegedActionKind.SpawnNpc => $"spawn NPC '{action.Name}'",
                PrivilegedActionKind.DismissNpc => $"dismiss NPC '{action.Name}'",
                PrivilegedActionKind.SuspendAgent => action.Rounds.HasValue ? $"suspend '{action.Name}' for {action.Rounds.Value} round(s)" : $"suspend '{action.Name}' until resumed",
                PrivilegedActionKind.ResumeAgent => $"resume '{action.Name}'",
                PrivilegedActionKind.RemoveTracker => $"remove tracker '{action.Key}'",
                PrivilegedActionKind.AddTracker => $"add tracker '{action.Key}' ({action.TrackerValueType})",
                PrivilegedActionKind.UpdateTrackerValue => $"update tracker '{action.Key}'='{action.Value}'",
                _ => action.Kind.ToString(),
            };

        private static int GetActionPhaseOrder(PrivilegedActionKind kind) =>
            kind switch
            {
                PrivilegedActionKind.ResumeAgent => 0,
                PrivilegedActionKind.DismissNpc => 1,
                PrivilegedActionKind.SuspendAgent => 2,
                PrivilegedActionKind.SpawnNpc => 3,
                PrivilegedActionKind.RemoveTracker => 4,
                PrivilegedActionKind.AddTracker => 5,
                PrivilegedActionKind.UpdateTrackerValue => 6,
                _ => 99,
            };

        private static string ExtractTaggedBodyContent(string body, string tagName)
        {
            if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(tagName))
                return string.Empty;

            var match = Regex.Match(
                body,
                $"<{Regex.Escape(tagName)}>\\s*(?<body>.*?)\\s*</{Regex.Escape(tagName)}>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            return match.Success ? match.Groups["body"].Value.Trim() : string.Empty;
        }

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
    }
}
