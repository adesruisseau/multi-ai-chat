using AgentGroupChat.Core;
using AgentGroupChat.Core.Models.Domain;

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

    public void AddUserMessage(string text, string speakerName = "You",
        string colorTheme = "")
    {
        Messages.Add(new ChatMessage
        {
            Speaker = speakerName,
            Content = text,
            IsUser = false,
            ColorTheme = colorTheme
        });
        SessionTurns.Add(new TranscriptTurn { Speaker = speakerName, Content = text });
        NotifyChanged();
    }

    public void AddAgentMessage(AgentConfig agent, string content)
    {
        Messages.Add(new ChatMessage
        {
            Speaker = agent.Name,
            Content = content,
            ColorTheme = agent.ColorTheme
        });
        NotifyChanged();
    }

    public void CompleteAgentMessage(AgentConfig agent, string content)
    {
        var placeholder = Messages.LastOrDefault(
            m => m.Speaker == agent.Name && m.Content == ChatPlaceholders.Thinking);
        if (placeholder is not null)
        {
            placeholder.Content = content;
        }
        else
        {
            Messages.Add(new ChatMessage
            {
                Speaker = agent.Name,
                Content = content,
                    ColorTheme = agent.ColorTheme
            });
        }
        SessionTurns.Add(new TranscriptTurn { Speaker = agent.Name, Content = content });
        NotifyChanged();
    }

    public void RemoveThinkingPlaceholders()
    {
        Messages.RemoveAll(m => m.Content == ChatPlaceholders.Thinking);
    }

    public void AddTranscriptMessage(string speaker, string content, string colorTheme, bool isUser)
    {
        Messages.Add(new ChatMessage
        {
            Speaker = speaker,
            Content = content,
            IsUser = isUser,
            ColorTheme = colorTheme
        });
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
