namespace AgentGroupChat.Core.Models.Domain;

public enum ParticipationMode
{
    TurnParticipant,
    InterventionOnly,
    Observer,
}

public sealed class HumanParticipantConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RoomId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsPlayerCharacter { get; set; }
    public string AppearanceSummary { get; set; } = string.Empty;
    public string TtsVoice { get; set; } = string.Empty;
    public string AccentHex { get; set; } = "#4A90D9";
    public string BackgroundHex { get; set; } = "#DDE8F0";
    public ParticipationMode ParticipationMode { get; set; } = ParticipationMode.TurnParticipant;
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
}
