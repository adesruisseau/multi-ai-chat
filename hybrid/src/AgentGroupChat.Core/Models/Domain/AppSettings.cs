namespace AgentGroupChat.Core.Models.Domain;

public sealed class AppSettings
{
    public string UiTheme { get; set; } = "System";
    public string UiAccent { get; set; } = "Terracotta";
    public bool TtsEnabled { get; set; }
    public string TtsProvider { get; set; } = "Local";
    public string TtsVoice { get; set; } = string.Empty;
    public int TtsRate { get; set; }
    public string PiperExePath { get; set; } = string.Empty;
    public string PiperModelsDir { get; set; } = string.Empty;
    public string KokoroBaseUrl { get; set; } = "http://127.0.0.1:8000";
    public string KokoroModel { get; set; } = "kokoro";
    public string KokoroVoice { get; set; } = "af_heart";
    public string KokoroLangCode { get; set; } = "a";
    public double KokoroSpeed { get; set; } = 1.0;
    public bool SetupModelsCompleted { get; set; }
    public bool SetupRoomsCompleted { get; set; }
    public string SetupTtsStatus { get; set; } = "Pending";
    public bool HideSetupGuide { get; set; }
}
