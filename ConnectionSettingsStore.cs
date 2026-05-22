using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentGroupChat;

public sealed class AiConnectionProfile : ObservableEntity
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _transport = "OpenAI Compatible";
    private string _endpoint = string.Empty;
    private string _apiKey = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Transport
    {
        get => _transport;
        set => SetProperty(ref _transport, value);
    }

    public string Endpoint
    {
        get => _endpoint;
        set => SetProperty(ref _endpoint, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }
}

public sealed class AiModelCatalogEntry : ObservableEntity
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = string.Empty;
    private string _connectionId = string.Empty;
    private string _modelId = string.Empty;
    private string _notes = string.Empty;
    private string _connectionName = string.Empty;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string ConnectionId
    {
        get => _connectionId;
        set => SetProperty(ref _connectionId, value);
    }

    public string ModelId
    {
        get => _modelId;
        set => SetProperty(ref _modelId, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    [JsonIgnore]
    public string ConnectionName
    {
        get => _connectionName;
        set => SetProperty(ref _connectionName, value);
    }
}

public sealed class ConnectionSettings
{
    public string SummarizerProvider { get; set; } = "Gemini";

    public bool SetupGuideModelsCompleted { get; set; }

    public bool SetupGuideRoomsCompleted { get; set; }

    public string SetupGuideTtsStatus { get; set; } = "Pending";

    public bool HideSetupGuide { get; set; }

    public string UiTheme { get; set; } = "System";

    public string UiAccent { get; set; } = "Terracotta";

    public string TextToSpeechProvider { get; set; } = "Local";

    public bool TextToSpeechEnabled { get; set; }

    public string TextToSpeechVoice { get; set; } = string.Empty;

    public int TextToSpeechRate { get; set; }

    public string PiperExecutablePath { get; set; } = string.Empty;

    public string PiperModelsDirectory { get; set; } = string.Empty;
    public string KokoroBaseUrl { get; set; } = "http://127.0.0.1:8000";
    public string KokoroModel { get; set; } = "kokoro";
    public string KokoroVoice { get; set; } = "af_heart";
    public string KokoroLanguageCode { get; set; } = "a";
    public double KokoroSpeed { get; set; } = 1.0;
    public List<string> KokoroVoices { get; set; } = new();

    public List<AiConnectionProfile> AiConnections { get; set; } = new();

    public List<AiModelCatalogEntry> AiModels { get; set; } = new();
}

public sealed class ConnectionSettingsStore
{
    private static readonly string[] SupportedTransportNames =
    {
        "OpenAI Compatible",
        "Groq",
        "Gemini",
        "HuggingFace",
        "Ollama",
    };

    private readonly string _rootDirectory;
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ConnectionSettingsStore()
    {
        _rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentGroupChat");
        _settingsPath = Path.Combine(_rootDirectory, "llm-settings.json");
    }

    public string SettingsPath => _settingsPath;

    public static IReadOnlyList<string> AvailableTransportNames => SupportedTransportNames;

    public ConnectionSettings Load()
    {
        Directory.CreateDirectory(_rootDirectory);

        if (!File.Exists(_settingsPath))
        {
            var defaults = CreateDefaultSettings();
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<ConnectionSettings>(json, _serializerOptions) ?? CreateDefaultSettings();
            Normalize(settings);
            return settings;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Connection settings at '{_settingsPath}' are not valid JSON.", exception);
        }
    }

    public void Save(ConnectionSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(_rootDirectory);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, _serializerOptions));
    }

    private static ConnectionSettings CreateDefaultSettings()
    {
        return new ConnectionSettings
        {
            SummarizerProvider = "Gemini",
            AiConnections = new List<AiConnectionProfile>
            {
                new() { Name = "Groq", Transport = "Groq", Endpoint = "https://api.groq.com/openai/v1/chat/completions" },
                new() { Name = "Gemini", Transport = "Gemini", Endpoint = "https://generativelanguage.googleapis.com/v1beta/models" },
                new() { Name = "HuggingFace", Transport = "HuggingFace", Endpoint = "https://router.huggingface.co/v1/chat/completions" },
                new() { Name = "Ollama", Transport = "Ollama", Endpoint = "http://localhost:11434/api/chat" },
                new() { Name = "OpenRouter", Transport = "OpenAI Compatible", Endpoint = "https://openrouter.ai/api/v1/chat/completions" },
            },
        };
    }

    private static void Normalize(ConnectionSettings settings)
    {
        settings.AiConnections ??= new List<AiConnectionProfile>();
        settings.AiModels ??= new List<AiModelCatalogEntry>();

        settings.AiConnections = NormalizeAiConnections(settings.AiConnections);
        settings.AiModels = NormalizeAiModels(settings.AiModels, settings.AiConnections);

        settings.SetupGuideTtsStatus = NormalizeSetupGuideTtsStatus(settings.SetupGuideTtsStatus);
        settings.SummarizerProvider = string.IsNullOrWhiteSpace(settings.SummarizerProvider)
            ? GetDefaultModelSelection(settings.AiModels)
            : settings.SummarizerProvider.Trim();
        settings.UiTheme = NormalizeUiTheme(settings.UiTheme);
        settings.UiAccent = NormalizeUiAccent(settings.UiAccent);
        settings.TextToSpeechProvider = NormalizeTextToSpeechProvider(settings.TextToSpeechProvider);
        settings.TextToSpeechVoice = settings.TextToSpeechVoice?.Trim() ?? string.Empty;
        settings.TextToSpeechRate = Math.Clamp(settings.TextToSpeechRate, -5, 5);
        settings.PiperExecutablePath = string.IsNullOrWhiteSpace(settings.PiperExecutablePath)
            ? GetDefaultPiperExecutablePath()
            : settings.PiperExecutablePath.Trim();
        settings.PiperModelsDirectory = string.IsNullOrWhiteSpace(settings.PiperModelsDirectory)
            ? GetDefaultPiperModelsDirectory()
            : settings.PiperModelsDirectory.Trim();
        settings.KokoroBaseUrl = NormalizeKokoroBaseUrl(settings.KokoroBaseUrl);
        settings.KokoroModel = string.IsNullOrWhiteSpace(settings.KokoroModel)
            ? "kokoro"
            : settings.KokoroModel.Trim();
        settings.KokoroVoice = string.IsNullOrWhiteSpace(settings.KokoroVoice)
            ? "af_heart"
            : settings.KokoroVoice.Trim();
        settings.KokoroLanguageCode = string.IsNullOrWhiteSpace(settings.KokoroLanguageCode)
            ? "a"
            : settings.KokoroLanguageCode.Trim();
        settings.KokoroSpeed = double.IsFinite(settings.KokoroSpeed)
            ? Math.Clamp(settings.KokoroSpeed, 0.5, 2.0)
            : 1.0;
        settings.KokoroVoices = NormalizeKokoroVoices(settings.KokoroVoices, settings.KokoroVoice);

        if (settings.HideSetupGuide && !settings.SetupGuideModelsCompleted && !settings.SetupGuideRoomsCompleted && settings.SetupGuideTtsStatus == "Pending")
        {
            settings.HideSetupGuide = false;
        }
    }

    private static List<AiConnectionProfile> NormalizeAiConnections(List<AiConnectionProfile>? connections)
    {
        var normalizedConnections = new List<AiConnectionProfile>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var connection in connections ?? new List<AiConnectionProfile>())
        {
            if (connection is null)
            {
                continue;
            }

            var connectionId = string.IsNullOrWhiteSpace(connection.Id)
                ? Guid.NewGuid().ToString("N")
                : connection.Id.Trim();
            while (!usedIds.Add(connectionId))
            {
                connectionId = Guid.NewGuid().ToString("N");
            }

            connection.Id = connectionId;
            connection.Name = string.IsNullOrWhiteSpace(connection.Name)
                ? $"{NormalizeTransport(connection.Transport)} Connection"
                : connection.Name.Trim();
            connection.Transport = NormalizeTransport(connection.Transport);
            connection.Endpoint = connection.Endpoint?.Trim() ?? string.Empty;
            connection.ApiKey = connection.ApiKey?.Trim() ?? string.Empty;

            normalizedConnections.Add(connection);
        }

        if (normalizedConnections.Count == 0)
        {
            normalizedConnections.Add(new AiConnectionProfile
            {
                Name = "OpenRouter",
                Transport = "OpenAI Compatible",
                Endpoint = "https://openrouter.ai/api/v1/chat/completions",
            });
        }

        return normalizedConnections;
    }

    private static List<AiModelCatalogEntry> NormalizeAiModels(
        List<AiModelCatalogEntry>? models,
        IReadOnlyList<AiConnectionProfile> connections)
    {
        var normalizedModels = new List<AiModelCatalogEntry>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultConnectionId = connections.FirstOrDefault()?.Id ?? string.Empty;

        foreach (var model in models ?? new List<AiModelCatalogEntry>())
        {
            if (model is null)
            {
                continue;
            }

            var modelId = string.IsNullOrWhiteSpace(model.Id)
                ? Guid.NewGuid().ToString("N")
                : model.Id.Trim();
            while (!usedIds.Add(modelId))
            {
                modelId = Guid.NewGuid().ToString("N");
            }

            model.Id = modelId;

            var proposedName = string.IsNullOrWhiteSpace(model.Name)
                ? (string.IsNullOrWhiteSpace(model.ModelId) ? "Model" : model.ModelId)
                : model.Name.Trim();
            model.Name = MakeUniqueName(usedNames, proposedName);

            var connectionId = model.ConnectionId?.Trim() ?? string.Empty;
            model.ConnectionId = connections.Any(c => string.Equals(c.Id, connectionId, StringComparison.OrdinalIgnoreCase))
                ? connectionId
                : defaultConnectionId;

            model.ModelId = model.ModelId?.Trim() ?? string.Empty;
            model.Notes = model.Notes?.Trim() ?? string.Empty;

            normalizedModels.Add(model);
        }

        return normalizedModels;
    }

    private static string NormalizeTransport(string? transport)
    {
        return transport?.Trim() switch
        {
            "Groq" => "Groq",
            "Gemini" => "Gemini",
            "Hugging Face" => "HuggingFace",
            "HuggingFace" => "HuggingFace",
            "Ollama" => "Ollama",
            _ => "OpenAI Compatible",
        };
    }

    public static bool TransportRequiresApiKey(string transport)
    {
        return NormalizeTransport(transport) is "Groq" or "Gemini" or "HuggingFace";
    }

    private static string GetDefaultModelSelection(IReadOnlyList<AiModelCatalogEntry> models)
    {
        var gemini = models.FirstOrDefault(model => string.Equals(model.Name, "Gemini", StringComparison.OrdinalIgnoreCase));
        return gemini?.Name
            ?? models.FirstOrDefault()?.Name
            ?? "Gemini";
    }

    private static string MakeUniqueName(HashSet<string> usedNames, string proposedName)
    {
        var baseName = string.IsNullOrWhiteSpace(proposedName) ? "Model" : proposedName.Trim();
        if (usedNames.Add(baseName))
        {
            return baseName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} ({suffix})";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string NormalizeSetupGuideTtsStatus(string? status)
    {
        return status?.Trim() switch
        {
            "Configured" => "Configured",
            "Skipped" => "Skipped",
            _ => "Pending",
        };
    }

    private static string NormalizeTextToSpeechProvider(string? provider)
    {
        if (string.Equals(provider?.Trim(), "Kokoro", StringComparison.OrdinalIgnoreCase))
        {
            return "Kokoro";
        }

        if (string.Equals(provider?.Trim(), "Piper", StringComparison.OrdinalIgnoreCase))
        {
            return "Piper";
        }

        return "Local";
    }

    private static string NormalizeKokoroBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "http://127.0.0.1:8000";
        }

        return baseUrl.Trim().TrimEnd('/');
    }

    private static List<string> NormalizeKokoroVoices(List<string>? voices, string defaultVoice)
    {
        var normalized = (voices ?? new List<string>())
            .Select(voice => voice?.Trim())
            .Where(voice => !string.IsNullOrWhiteSpace(voice))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(voice => voice, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!normalized.Any(voice => string.Equals(voice, defaultVoice, StringComparison.OrdinalIgnoreCase)))
        {
            normalized.Insert(0, defaultVoice);
        }

        return normalized;
    }

    private static string NormalizeUiTheme(string? theme)
    {
        return theme?.Trim() switch
        {
            "Light" => "Light",
            "Dark" => "Dark",
            _ => "System",
        };
    }

    private static string NormalizeUiAccent(string? accent)
    {
        return accent?.Trim() switch
        {
            "Ocean" => "Ocean",
            "Berry" => "Berry",
            "Forest" => "Forest",
            "Gold" => "Gold",
            _ => "Terracotta",
        };
    }

    private static string GetDefaultPiperExecutablePath()
    {
        var rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentGroupChat",
            "piper");
        return Path.Combine(rootDirectory, "piper.exe");
    }

    private static string GetDefaultPiperModelsDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentGroupChat",
            "piper",
            "models");
    }
}
