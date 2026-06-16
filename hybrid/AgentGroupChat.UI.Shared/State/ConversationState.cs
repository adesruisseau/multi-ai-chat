using AgentGroupChat.Core;
using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.UI.Shared.Theming;

namespace AgentGroupChat.UI.Shared.State;

public sealed class ChatMessage
{
    public string Speaker { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ColorTheme { get; set; } = "Terracotta";
    public bool IsSystem { get; set; }
    public bool IsUser { get; set; }
}

public sealed class ConversationState
{
    public List<ChatMessage> Messages { get; } = new();
    public List<TranscriptTurn> SessionTurns { get; } = new();
    public int CompletedRounds { get; set; }
    public bool IsRunning { get; set; }
    public string Status { get; set; } = "Ready";

    public event Action? OnChange;

    public void AddSystemMessage(string text)
    {
        Messages.Add(new ChatMessage { Speaker = "System", Content = text, IsSystem = true });
        NotifyChanged();
    }

    public void ShowAgentThinking(AgentConfig agent)
    {
        Messages.Add(new ChatMessage
        {
            Speaker = agent.Name,
            Content = ChatPlaceholders.Thinking,
            ColorTheme = agent.ColorTheme
        });
        NotifyChanged();
    }

    public void LoadTranscript(IEnumerable<TranscriptTurn> turns, RoomConfig room, AppSettings settings)
    {
        Messages.Clear();
        SessionTurns.Clear();

        foreach (var turn in turns)
        {
            SessionTurns.Add(turn);
            Messages.Add(CreateTranscriptMessage(turn, room, settings));
        }

        NotifyChanged();
    }

    public void ApplyPersistedTurn(TranscriptTurn turn, RoomConfig room, AppSettings settings)
    {
        if (turn.Id > 0 && SessionTurns.Any(existingTurn => existingTurn.Id == turn.Id))
            return;

        SessionTurns.Add(turn);

        var message = CreateTranscriptMessage(turn, room, settings);
        var placeholder = Messages.LastOrDefault(existingMessage =>
            existingMessage.Speaker == turn.Speaker &&
            existingMessage.Content == ChatPlaceholders.Thinking);

        if (placeholder is not null)
        {
            placeholder.Content = message.Content;
            placeholder.ColorTheme = message.ColorTheme;
            placeholder.IsUser = message.IsUser;
            placeholder.IsSystem = message.IsSystem;
        }
        else
        {
            Messages.Add(message);
        }

        NotifyChanged();
    }

    public void RemoveThinkingPlaceholders()
    {
        Messages.RemoveAll(m => m.Content == ChatPlaceholders.Thinking);
    }

    public void Clear()
    {
        Messages.Clear();
        SessionTurns.Clear();
        CompletedRounds = 0;
        Status = "Ready";
        NotifyChanged();
    }

    public void SetStatus(string status)
    {
        Status = status;
        NotifyChanged();
    }

    private static ChatMessage CreateTranscriptMessage(TranscriptTurn turn, RoomConfig room, AppSettings settings)
    {
        var humanParticipant = room.Agents.FirstOrDefault(agent =>
            agent.IsHumanParticipant &&
            string.Equals(agent.Name, turn.Speaker, StringComparison.OrdinalIgnoreCase));
        var isHuman = humanParticipant is not null || turn.Speaker == Core.SpeakerNames.DefaultHuman;

        return new ChatMessage
        {
            Speaker = turn.Speaker,
            Content = turn.Content,
            IsUser = isHuman,
            ColorTheme = ResolveColorTheme(turn, room, humanParticipant, settings, isHuman)
        };
    }

    private static string ResolveColorTheme(
        TranscriptTurn turn,
        RoomConfig room,
        AgentConfig? humanParticipant,
        AppSettings settings,
        bool isHuman)
    {
        if (!string.IsNullOrWhiteSpace(turn.ColorTheme))
            return turn.ColorTheme;

        if (isHuman)
        {
            return humanParticipant?.ColorTheme
                ?? AgentColorPresets.FindByName(settings.UiAccent, settings.UiTheme != "Light").Name
                ?? "Terracotta";
        }

        return room.Agents.FirstOrDefault(agent =>
                   string.Equals(agent.Name, turn.Speaker, StringComparison.OrdinalIgnoreCase))?.ColorTheme
               ?? "Terracotta";
    }

    public void NotifyChanged() => OnChange?.Invoke();
}
