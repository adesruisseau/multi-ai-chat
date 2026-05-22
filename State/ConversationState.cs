using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Hybrid.State;

public sealed class ChatMessage
{
    public string Speaker { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string AccentHex { get; set; } = string.Empty;
    public string BackgroundHex { get; set; } = string.Empty;
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

    public void AddUserMessage(string text)
    {
        Messages.Add(new ChatMessage { Speaker = "You", Content = text, IsUser = true });
        SessionTurns.Add(new TranscriptTurn { Speaker = "You", Content = text });
        NotifyChanged();
    }

    public void AddAgentMessage(AgentConfig agent, string content)
    {
        Messages.Add(new ChatMessage
        {
            Speaker = agent.Name,
            Content = content,
            AccentHex = agent.AccentHex,
            BackgroundHex = agent.BackgroundHex,
        });
        NotifyChanged();
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

    public void NotifyChanged() => OnChange?.Invoke();
}
