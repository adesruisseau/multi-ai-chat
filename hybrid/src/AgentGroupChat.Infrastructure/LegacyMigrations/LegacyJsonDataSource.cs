using System.Text.Json;
using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;

namespace AgentGroupChat.Infrastructure.LegacyMigrations;

/// <summary>
/// Reads legacy WPF JSON/TXT data from %LOCALAPPDATA%/AgentGroupChat.
/// </summary>
public sealed class LegacyJsonDataSource : ILegacyDataSource
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _rootDir;

    public LegacyJsonDataSource()
    {
        _rootDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentGroupChat");
    }

    public bool HasLegacyData()
    {
        var settingsPath = Path.Combine(_rootDir, "llm-settings.json");
        var themesPath = Path.Combine(_rootDir, "themes.json");
        return File.Exists(settingsPath) || File.Exists(themesPath);
    }

    public AppSettings LoadAppSettings()
    {
        var path = Path.Combine(_rootDir, "llm-settings.json");
        if (!File.Exists(path)) return new AppSettings();
        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        return new AppSettings
        {
            UiTheme = GetString(raw, "uiTheme", "System"),
            UiAccent = GetString(raw, "uiAccent", "Terracotta"),
            TtsEnabled = GetBool(raw, "textToSpeechEnabled"),
            TtsProvider = GetString(raw, "textToSpeechProvider", "Local"),
            TtsVoice = GetString(raw, "textToSpeechVoice", ""),
            TtsRate = GetInt(raw, "textToSpeechRate"),
            PiperExePath = GetString(raw, "piperExecutablePath", ""),
            PiperModelsDir = GetString(raw, "piperModelsDirectory", ""),
            KokoroBaseUrl = GetString(raw, "kokoroBaseUrl", "http://127.0.0.1:8880"),
            KokoroModel = GetString(raw, "kokoroModel", "kokoro"),
            KokoroVoice = GetString(raw, "kokoroVoice", "af_heart"),
            KokoroLangCode = GetString(raw, "kokoroLanguageCode", "a"),
            KokoroSpeed = GetDouble(raw, "kokoroSpeed", 1.0),
            SetupModelsCompleted = GetBool(raw, "setupGuideModelsCompleted"),
            SetupRoomsCompleted = GetBool(raw, "setupGuideRoomsCompleted"),
            SetupTtsStatus = GetString(raw, "setupGuideTtsStatus", "Pending"),
            HideSetupGuide = GetBool(raw, "hideSetupGuide"),
        };
    }

    public List<AiConnection> LoadConnections()
    {
        var path = Path.Combine(_rootDir, "llm-settings.json");
        if (!File.Exists(path)) return new();
        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        if (!raw.TryGetProperty("aiConnections", out var arr)) return new();
        var result = new List<AiConnection>();
        var order = 0;
        foreach (var item in arr.EnumerateArray())
        {
            result.Add(new AiConnection
            {
                Id = GetString(item, "id", Guid.NewGuid().ToString("N")),
                Name = GetString(item, "name", ""),
                Transport = GetString(item, "transport", "OpenAI Compatible"),
                Endpoint = GetString(item, "endpoint", ""),
                ApiKey = GetString(item, "apiKey", ""),
                SortOrder = order++,
            });
        }
        return result;
    }

    public List<AiModel> LoadModels()
    {
        var path = Path.Combine(_rootDir, "llm-settings.json");
        if (!File.Exists(path)) return new();
        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        if (!raw.TryGetProperty("aiModels", out var arr)) return new();
        var result = new List<AiModel>();
        var order = 0;
        foreach (var item in arr.EnumerateArray())
        {
            result.Add(new AiModel
            {
                Id = GetString(item, "id", Guid.NewGuid().ToString("N")),
                Name = GetString(item, "name", ""),
                ConnectionId = GetString(item, "connectionId", ""),
                ModelId = GetString(item, "modelId", ""),
                Notes = GetString(item, "notes", ""),
                SortOrder = order++,
            });
        }
        return result;
    }

    public List<RoomConfig> LoadRooms()
    {
        var path = Path.Combine(_rootDir, "themes.json");
        if (!File.Exists(path)) return new();
        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        if (!raw.TryGetProperty("themes", out var arr)) return new();
        var rooms = new List<RoomConfig>();
        var roomOrder = 0;
        foreach (var theme in arr.EnumerateArray())
        {
            var room = new RoomConfig
            {
                Id = GetString(theme, "id", Guid.NewGuid().ToString("N")),
                Name = GetString(theme, "name", ""),
                Topic = GetString(theme, "topic", ""),
                WaitForUserReply = GetBool(theme, "waitForUserReply", true),
                AgentDelaySeconds = GetInt(theme, "agentDelaySeconds", 5),
                MaxTokens = GetInt(theme, "maxTokens", 300),
                RecentTurnsWindow = GetInt(theme, "recentTurnsWindow", 6),
                UserCompactionBudget = GetInt(theme, "userCompactionBudget", 3200),
                SummarizerModelId = GetString(theme, "summarizerProvider", ""),
                SummarizationLevel = GetString(theme, "sceneSummarizationLevel", "Moderate"),
                SummarizerMaxTokens = GetInt(theme, "sceneSummarizerMaxTokens", 500),
                SummarizerMaxLines = GetInt(theme, "sceneSummarizerMaxLines", 28),
                SummarizerMaxCharacters = GetInt(theme, "sceneSummarizerMaxCharacters", 5600),
                SummarizerBroaderTurns = GetInt(theme, "sceneSummarizerBroaderTranscriptTurns", 6),
                SummarizerPromptOverride = GetString(theme, "sceneSummarizerPromptOverride", ""),
                SortOrder = roomOrder++,
            };

            if (theme.TryGetProperty("agents", out var agents))
            {
                var agentOrder = 0;
                foreach (var a in agents.EnumerateArray())
                {
                    room.Agents.Add(new AgentConfig
                    {
                        Id = GetString(a, "id", Guid.NewGuid().ToString("N")),
                        RoomId = room.Id,
                        Name = GetString(a, "name", ""),
                        ModelId = GetString(a, "provider", ""),
                        SystemPrompt = GetString(a, "systemPrompt", ""),
                        IsEnabled = GetBool(a, "isEnabled", true),
                        MaxTokensOverride = GetNullableInt(a, "maxTokensOverride"),
                        CompactionBudget = GetInt(a, "compactionBudget", 420),
                        AccentHex = GetString(a, "accentHex", "#C56A54"),
                        BackgroundHex = GetString(a, "backgroundHex", "#F9E5DE"),
                        TtsVoice = GetString(a, "textToSpeechVoice", ""),
                        SortOrder = agentOrder++,
                    });
                }
            }
            rooms.Add(room);
        }
        return rooms;
    }

    public Dictionary<string, string> LoadRoomMemory(string roomId, string roomName)
    {
        var result = new Dictionary<string, string>();
        var themeDir = FindThemeDirectory(roomId, roomName);
        if (themeDir is null) return result;

        var sharedPath = Path.Combine(themeDir, "shared-room-memory.txt");
        if (File.Exists(sharedPath))
            result["SharedRoom"] = File.ReadAllText(sharedPath);

        var durablePath = Path.Combine(themeDir, "theme-durable-memory.txt");
        if (File.Exists(durablePath))
            result["Durable"] = File.ReadAllText(durablePath);

        var legacyScenePath = Path.Combine(themeDir, "theme-scene-memory.txt");
        if (!result.ContainsKey("SharedRoom") && File.Exists(legacyScenePath))
            result["SharedRoom"] = File.ReadAllText(legacyScenePath);

        return result;
    }

    public List<TranscriptTurn> LoadTranscript(string roomId, string roomName)
    {
        var themeDir = FindThemeDirectory(roomId, roomName);
        if (themeDir is null) return new();

        var sessionPath = Path.Combine(themeDir, "chat-session.txt");
        if (!File.Exists(sessionPath)) return new();

        var content = File.ReadAllText(sessionPath).Replace("\r\n", "\n").Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(content)) return new();

        var turns = new List<TranscriptTurn>();
        var index = 0;

        if (content.StartsWith("Completed-Rounds: "))
        {
            var headerEnd = content.IndexOf('\n');
            if (headerEnd > 0) index = headerEnd + 1;
            if (index < content.Length && content[index] == '\n') index++;
        }

        while (index < content.Length)
        {
            if (!TryReadMarker(content, ref index, "=== TURN ===\n")) break;
            ReadHeader(content, ref index, "Timestamp: ");
            var speaker = ReadHeader(content, ref index, "Speaker: ");
            var lenStr = ReadHeader(content, ref index, "Content-Length: ");
            if (!int.TryParse(lenStr, out var len) || len < 0) break;
            if (index + len > content.Length) break;
            var text = content.Substring(index, len);
            index += len;
            if (index < content.Length && content[index] == '\n') index++;
            TryReadMarker(content, ref index, "=== END TURN ===\n");

            turns.Add(new TranscriptTurn
            {
                RoomId = roomId,
                Round = 0,
                Speaker = speaker,
                Content = text,
            });
        }
        return turns;
    }

    private string? FindThemeDirectory(string roomId, string roomName)
    {
        var memoriesDir = Path.Combine(_rootDir, "memories");
        if (!Directory.Exists(memoriesDir)) return null;
        foreach (var dir in Directory.GetDirectories(memoriesDir))
        {
            var name = Path.GetFileName(dir);
            if (name.EndsWith($"-{roomId}", StringComparison.OrdinalIgnoreCase))
                return dir;
        }
        return null;
    }

    private static bool TryReadMarker(string s, ref int i, string marker)
    {
        if (i + marker.Length > s.Length) return false;
        if (!s.AsSpan(i, marker.Length).SequenceEqual(marker.AsSpan())) return false;
        i += marker.Length;
        return true;
    }

    private static string ReadHeader(string s, ref int i, string prefix)
    {
        if (i >= s.Length || !s.AsSpan(i).StartsWith(prefix.AsSpan())) return string.Empty;
        i += prefix.Length;
        var end = s.IndexOf('\n', i);
        if (end < 0) { var v = s[i..]; i = s.Length; return v.Trim(); }
        var value = s[i..end].Trim();
        i = end + 1;
        return value;
    }

    private static string GetString(JsonElement e, string prop, string def) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;

    private static bool GetBool(JsonElement e, string prop, bool def = false) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False && v.GetBoolean();

    private static int GetInt(JsonElement e, string prop, int def = 0) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : def;

    private static int? GetNullableInt(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static double GetDouble(JsonElement e, string prop, double def) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
}
