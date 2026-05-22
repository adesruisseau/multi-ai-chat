using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;

namespace AgentGroupChat;

public sealed record StoredConversationTurn(string Speaker, string Content, string TimestampText);

public sealed record StoredConversationSession(int CompletedRounds, IReadOnlyList<StoredConversationTurn> Turns);

public sealed class ThemeStore
{
    private const int MaxMemoryCharacters = 12000;
    private const int MaxShortTermMemoryCharacters = 8000;
    private const int MaxThemeSharedRoomMemoryCharacters = 7000;
    private const int MaxThemeDurableMemoryCharacters = 9000;
    private const int MaxShortTermEntries = 12;
    private readonly string _rootDirectory;
    private readonly string _themesPath;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ThemeStore()
    {
        _rootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentGroupChat");
        _themesPath = Path.Combine(_rootDirectory, "themes.json");
    }

    public string ThemesPath => _themesPath;

    public ThemeLibrary Load()
    {
        Directory.CreateDirectory(_rootDirectory);

        if (!File.Exists(_themesPath))
        {
            var seededLibrary = CreateDefaultLibrary();
            Save(seededLibrary);
            return seededLibrary;
        }

        var json = File.ReadAllText(_themesPath);
        var library = JsonSerializer.Deserialize<ThemeLibrary>(json, _serializerOptions) ?? CreateDefaultLibrary();
        Normalize(library);
        Save(library);
        return library;
    }

    public void Save(ThemeLibrary library)
    {
        Normalize(library);
        Directory.CreateDirectory(_rootDirectory);
        File.WriteAllText(_themesPath, JsonSerializer.Serialize(library, _serializerOptions));
    }

    public string GetAgentMemoryPath(ThemeProfile theme, AgentProfile agent)
    {
        var themeDirectory = GetThemeMemoryDirectory(theme);
        return Path.Combine(themeDirectory, $"{Slugify(agent.Name)}-{agent.Id}.txt");
    }

    public string LoadAgentMemory(ThemeProfile theme, AgentProfile agent)
    {
        var path = GetAgentMemoryPath(theme, agent);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, string.Empty);
        }

        agent.MemoryFilePath = path;
        var content = File.ReadAllText(path);
        if (LooksLikeLegacyConversationLog(content))
        {
            var shortTermPath = GetAgentShortTermMemoryPath(theme, agent);
            var existingShortTerm = File.Exists(shortTermPath) ? File.ReadAllText(shortTermPath) : string.Empty;
            if (string.IsNullOrWhiteSpace(existingShortTerm))
            {
                var migratedShortTerm = ConvertLegacyMemoryToShortTerm(content);
                File.WriteAllText(shortTermPath, migratedShortTerm);
            }

            File.WriteAllText(path, string.Empty);
            content = string.Empty;
        }

        return content;
    }

    public string GetAgentShortTermMemoryPath(ThemeProfile theme, AgentProfile agent)
    {
        var themeDirectory = GetThemeMemoryDirectory(theme);
        return Path.Combine(themeDirectory, $"{Slugify(agent.Name)}-{agent.Id}-short.txt");
    }

    public string GetThemeSharedRoomMemoryPath(ThemeProfile theme)
    {
        return Path.Combine(GetThemeMemoryDirectory(theme), "shared-room-memory.txt");
    }

    private string GetLegacyThemeSceneMemoryPath(ThemeProfile theme)
    {
        return Path.Combine(GetThemeMemoryDirectory(theme), "theme-scene-memory.txt");
    }

    public string GetThemeConversationPath(ThemeProfile theme)
    {
        return Path.Combine(GetThemeMemoryDirectory(theme), "chat-session.txt");
    }

    public StoredConversationSession LoadThemeConversation(ThemeProfile theme)
    {
        var path = GetThemeConversationPath(theme);
        if (!File.Exists(path))
        {
            return new StoredConversationSession(0, Array.Empty<StoredConversationTurn>());
        }

        var normalized = File.ReadAllText(path)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new StoredConversationSession(0, Array.Empty<StoredConversationTurn>());
        }

        var turns = new List<StoredConversationTurn>();
        var completedRounds = 0;
        var index = 0;

        if (normalized.StartsWith("Completed-Rounds: ", StringComparison.Ordinal))
        {
            var headerEnd = normalized.IndexOf('\n');
            if (headerEnd > 0)
            {
                var headerValue = normalized["Completed-Rounds: ".Length..headerEnd].Trim();
                _ = int.TryParse(headerValue, out completedRounds);
                index = headerEnd + 1;
                if (index < normalized.Length && normalized[index] == '\n')
                {
                    index++;
                }
            }
        }

        while (index < normalized.Length)
        {
            if (!TryReadConversationMarker(normalized, ref index, "=== TURN ===\n"))
            {
                break;
            }

            var timestampText = ReadConversationHeaderValue(normalized, ref index, "Timestamp: ");
            var speaker = ReadConversationHeaderValue(normalized, ref index, "Speaker: ");
            var contentLengthText = ReadConversationHeaderValue(normalized, ref index, "Content-Length: ");
            if (!int.TryParse(contentLengthText, out var contentLength) || contentLength < 0)
            {
                break;
            }

            if (index + contentLength > normalized.Length)
            {
                break;
            }

            var content = normalized.Substring(index, contentLength);
            index += contentLength;

            if (index < normalized.Length && normalized[index] == '\n')
            {
                index++;
            }

            if (!TryReadConversationMarker(normalized, ref index, "=== END TURN ===\n"))
            {
                break;
            }

            turns.Add(new StoredConversationTurn(speaker, content, timestampText));
        }

        return new StoredConversationSession(completedRounds, turns);
    }

    public void SaveThemeConversation(ThemeProfile theme, StoredConversationSession session)
    {
        var path = GetThemeConversationPath(theme);
        if (session.CompletedRounds <= 0 && session.Turns.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        var builder = new StringBuilder();
        builder.Append("Completed-Rounds: ");
        builder.Append(session.CompletedRounds);
        builder.Append("\n\n");

        foreach (var turn in session.Turns)
        {
            builder.Append("=== TURN ===\n");
            builder.Append("Timestamp: ");
            builder.Append(turn.TimestampText ?? string.Empty);
            builder.Append("\n");
            builder.Append("Speaker: ");
            builder.Append(turn.Speaker ?? string.Empty);
            builder.Append("\n");
            builder.Append("Content-Length: ");
            builder.Append(turn.Content?.Length ?? 0);
            builder.Append("\n");
            builder.Append(turn.Content ?? string.Empty);
            builder.Append("\n=== END TURN ===\n");
        }

        File.WriteAllText(path, builder.ToString());
    }

    public string LoadThemeSharedRoomMemory(ThemeProfile theme)
    {
        var path = GetThemeSharedRoomMemoryPath(theme);
        if (!File.Exists(path))
        {
            var legacyPath = GetLegacyThemeSceneMemoryPath(theme);
            if (File.Exists(legacyPath))
            {
                File.WriteAllText(path, File.ReadAllText(legacyPath));
            }
            else
            {
                File.WriteAllText(path, string.Empty);
            }
        }

        return File.ReadAllText(path);
    }

    public void SaveThemeSharedRoomMemory(ThemeProfile theme, string value)
    {
        var sanitized = TrimSummary(value, MaxThemeSharedRoomMemoryCharacters);
        var path = GetThemeSharedRoomMemoryPath(theme);
        File.WriteAllText(path, sanitized);
    }

    public string GetThemeDurableMemoryPath(ThemeProfile theme)
    {
        return Path.Combine(GetThemeMemoryDirectory(theme), "theme-durable-memory.txt");
    }

    public string LoadThemeDurableMemory(ThemeProfile theme)
    {
        var path = GetThemeDurableMemoryPath(theme);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, string.Empty);
        }

        return File.ReadAllText(path);
    }

    public void SaveThemeDurableMemory(ThemeProfile theme, string value)
    {
        var sanitized = TrimSummary(value, MaxThemeDurableMemoryCharacters);
        var path = GetThemeDurableMemoryPath(theme);
        File.WriteAllText(path, sanitized);
    }

    public string LoadAgentShortTermMemory(ThemeProfile theme, AgentProfile agent)
    {
        var path = GetAgentShortTermMemoryPath(theme, agent);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, string.Empty);
        }

        agent.ShortTermMemoryFilePath = path;
        return File.ReadAllText(path);
    }

    public void SaveAgentMemory(ThemeProfile theme, AgentProfile agent)
    {
        var trimmed = TrimMemory(agent.MemoryNotes);
        agent.MemoryNotes = trimmed;
        var path = GetAgentMemoryPath(theme, agent);
        File.WriteAllText(path, trimmed);
        agent.MemoryFilePath = path;
    }

    public void SaveAgentShortTermMemory(ThemeProfile theme, AgentProfile agent)
    {
        var sanitized = SanitizeShortTermMemory(agent.ShortTermMemory);
        agent.ShortTermMemory = sanitized;
        var path = GetAgentShortTermMemoryPath(theme, agent);
        File.WriteAllText(path, sanitized);
        agent.ShortTermMemoryFilePath = path;
    }

    public void AppendShortTermMemory(ThemeProfile theme, AgentProfile agent, string entry)
    {
        var normalizedEntry = NormalizeEntry(entry);
        if (string.IsNullOrWhiteSpace(normalizedEntry))
        {
            return;
        }

        var existing = LoadAgentShortTermMemory(theme, agent);
        var merged = string.IsNullOrWhiteSpace(existing)
            ? normalizedEntry
            : $"{existing.Trim()}\n\n---\n\n{normalizedEntry}";

        agent.ShortTermMemory = SanitizeShortTermMemory(merged);
        SaveAgentShortTermMemory(theme, agent);
    }

    public string SanitizeShortTermMemory(string value)
    {
        var entries = value
            .Split("\n\n---\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeEntry)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct()
            .TakeLast(MaxShortTermEntries)
            .ToList();

        var merged = string.Join("\n\n---\n\n", entries);
        if (merged.Length <= MaxShortTermMemoryCharacters)
        {
            return merged;
        }

        return merged[^MaxShortTermMemoryCharacters..];
    }

    private static string TrimMemory(string value)
    {
        if (value.Length <= MaxMemoryCharacters)
        {
            return value;
        }

        return value[^MaxMemoryCharacters..];
    }

    private static string TrimSummary(string value, int maxLength)
    {
        var normalized = value.Replace("\r", string.Empty).Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string NormalizeEntry(string value)
    {
        var compact = value
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !line.StartsWith("What would you like to do next", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Equals("(No recent short-term memory yet.)", StringComparison.OrdinalIgnoreCase));

        return string.Join("\n", compact);
    }

    private static bool LooksLikeLegacyConversationLog(string value)
    {
        return value.Contains("User context:", StringComparison.Ordinal)
            && value.Contains("Agent response:", StringComparison.Ordinal);
    }

    private string ConvertLegacyMemoryToShortTerm(string value)
    {
        var entries = value
            .Split("---", StringSplitOptions.RemoveEmptyEntries)
            .Select(block =>
            {
                var responseIndex = block.IndexOf("Agent response:", StringComparison.Ordinal);
                if (responseIndex < 0)
                {
                    return string.Empty;
                }

                var response = block[(responseIndex + "Agent response:".Length)..].Trim();
                return NormalizeEntry(response);
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .TakeLast(MaxShortTermEntries);

        return SanitizeShortTermMemory(string.Join("\n\n---\n\n", entries));
    }

    private string GetThemeMemoryDirectory(ThemeProfile theme)
    {
        var themeDirectory = Path.Combine(_rootDirectory, "memory", Slugify(theme.Name), theme.Id);
        Directory.CreateDirectory(themeDirectory);
        return themeDirectory;
    }

    private static bool TryReadConversationMarker(string text, ref int index, string marker)
    {
        if (index + marker.Length > text.Length || !text.AsSpan(index).StartsWith(marker, StringComparison.Ordinal))
        {
            return false;
        }

        index += marker.Length;
        return true;
    }

    private static string ReadConversationHeaderValue(string text, ref int index, string prefix)
    {
        if (index + prefix.Length > text.Length || !text.AsSpan(index).StartsWith(prefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        index += prefix.Length;
        var lineEnd = text.IndexOf('\n', index);
        if (lineEnd < 0)
        {
            var value = text[index..];
            index = text.Length;
            return value;
        }

        var result = text[index..lineEnd];
        index = lineEnd + 1;
        return result;
    }

    private void Normalize(ThemeLibrary library)
    {
        if (library.Themes.Count == 0)
        {
            var seeded = CreateDefaultLibrary();
            library.SelectedThemeId = seeded.SelectedThemeId;
            library.Themes = seeded.Themes;
            return;
        }

        foreach (var theme in library.Themes)
        {
            if (string.IsNullOrWhiteSpace(theme.Id))
            {
                theme.Id = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(theme.Name))
            {
                theme.Name = "Untitled Theme";
            }

            if (theme.AgentDelaySeconds < 1)
            {
                theme.AgentDelaySeconds = 1;
            }

            if (theme.MaxTokens < 50)
            {
                theme.MaxTokens = 300;
            }

            if (theme.RecentTurnsWindow < 1)
            {
                theme.RecentTurnsWindow = 1;
            }

            if (theme.UserCompactionBudget < 200)
            {
                theme.UserCompactionBudget = 3200;
            }

            if (!IsSupportedSceneSummarizationLevel(theme.SceneSummarizationLevel))
            {
                theme.SceneSummarizationLevel = "Moderate";
            }

            ApplySceneSummarizerDefaults(theme);

            if (theme.Agents is null)
            {
                theme.Agents = new ObservableCollection<AgentProfile>();
            }

            while (theme.Agents.Count > 4)
            {
                theme.Agents.RemoveAt(theme.Agents.Count - 1);
            }

            if (theme.Agents.Count == 0)
            {
                theme.Agents.Add(CreateAgent("Agent", "You are a helpful agent."));
            }

            foreach (var agent in theme.Agents)
            {
                if (string.IsNullOrWhiteSpace(agent.Id))
                {
                    agent.Id = Guid.NewGuid().ToString("N");
                }

                if (string.IsNullOrWhiteSpace(agent.Name))
                {
                    agent.Name = "Agent";
                }

                if (string.IsNullOrWhiteSpace(agent.Provider))
                {
                    agent.Provider = "Groq Fast";
                }

                if (agent.MaxTokensOverride is < 50)
                {
                    agent.MaxTokensOverride = null;
                }

                if (agent.CompactionBudget < 80)
                {
                    agent.CompactionBudget = 420;
                }

                if (string.IsNullOrWhiteSpace(agent.AccentHex) || string.IsNullOrWhiteSpace(agent.BackgroundHex))
                {
                    ApplyPalette(theme.Agents.IndexOf(agent), agent);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(library.SelectedThemeId) || library.Themes.All(theme => theme.Id != library.SelectedThemeId))
        {
            library.SelectedThemeId = library.Themes[0].Id;
        }
    }

    private ThemeLibrary CreateDefaultLibrary()
    {
        var coding = new ThemeProfile
        {
            Name = "Coding Agents",
            Topic = "Collaborative software design and implementation",
            WaitForUserReply = true,
            AgentDelaySeconds = 5,
            MaxTokens = 300,
            RecentTurnsWindow = 6,
            UserCompactionBudget = 3200,
            Agents = new ObservableCollection<AgentProfile>
            {
                CreateAgent(
                    "Planner",
                    "Break the problem into steps, identify constraints, and hand off precise implementation guidance to the next agent."),
                CreateAgent(
                    "Developer",
                    "Implement the current plan. Provide code or technical output directly and keep explanations brief."),
                CreateAgent(
                    "Reviewer",
                    "Review the latest technical output for bugs, edge cases, and maintainability. If it is ready, say APPROVED clearly."),
            },
        };

        var workshop = new ThemeProfile
        {
            Name = "Workshop Agents",
            Topic = "A small team responding in sequence to the user's scenario",
            WaitForUserReply = true,
            AgentDelaySeconds = 5,
            MaxTokens = 300,
            RecentTurnsWindow = 6,
            UserCompactionBudget = 3200,
            Agents = new ObservableCollection<AgentProfile>
            {
                CreateAgent(
                    "Facilitator",
                    "Clarify the user's goal, identify the next concrete decision, and keep the exchange moving toward an actionable outcome."),
                CreateAgent(
                    "Specialist",
                    "Add domain expertise, edge cases, and practical detail that improves the latest proposal without derailing it."),
            },
        };

        var writing = new ThemeProfile
        {
            Name = "Artistic Agents",
            Topic = "Creative drafting, rewriting, and polishing",
            WaitForUserReply = true,
            AgentDelaySeconds = 5,
            MaxTokens = 300,
            RecentTurnsWindow = 6,
            UserCompactionBudget = 3200,
            Agents = new ObservableCollection<AgentProfile>
            {
                CreateAgent(
                    "Author",
                    "Draft the creative piece with bold voice and concrete imagery."),
                CreateAgent(
                    "Proofreader",
                    "Polish clarity, flow, grammar, and rhythm while preserving the author's voice."),
            },
        };

        var themes = new ObservableCollection<ThemeProfile> { coding, workshop, writing };
        for (var themeIndex = 0; themeIndex < themes.Count; themeIndex++)
        {
            for (var agentIndex = 0; agentIndex < themes[themeIndex].Agents.Count; agentIndex++)
            {
                ApplyPalette(agentIndex, themes[themeIndex].Agents[agentIndex]);
            }
        }

        return new ThemeLibrary
        {
            SelectedThemeId = coding.Id,
            Themes = themes,
        };
    }

    private static AgentProfile CreateAgent(string name, string prompt)
    {
        return new AgentProfile
        {
            Name = name,
            Provider = "Groq Fast",
            SystemPrompt = prompt,
        };
    }

    private static bool IsSupportedSceneSummarizationLevel(string? value)
    {
        return value is "Aggressive"
            or "Custom"
            or "Semi-Aggressive"
            or "Moderate"
            or "Semi-Relaxed"
            or "Relaxed";
    }

    private static void ApplySceneSummarizerDefaults(ThemeProfile theme)
    {
        var defaults = GetSceneSummarizerDefaults(theme.SceneSummarizationLevel);

        if (theme.SceneSummarizerMaxTokens < 140)
        {
            theme.SceneSummarizerMaxTokens = defaults.MaxTokens;
        }

        if (theme.SceneSummarizerMaxLines < 12)
        {
            theme.SceneSummarizerMaxLines = defaults.MaxLines;
        }

        if (theme.SceneSummarizerMaxCharacters < 1800)
        {
            theme.SceneSummarizerMaxCharacters = defaults.MaxCharacters;
        }

        if (theme.SceneSummarizerBroaderTranscriptTurns < 1)
        {
            theme.SceneSummarizerBroaderTranscriptTurns = defaults.BroaderTranscriptTurns;
        }
    }

    private static (int MaxTokens, int MaxLines, int MaxCharacters, int BroaderTranscriptTurns) GetSceneSummarizerDefaults(string? level)
    {
        return level switch
        {
            "Aggressive" => (300, 18, 3600, 3),
            "Custom" => (500, 28, 5600, 6),
            "Semi-Aggressive" => (400, 22, 4200, 3),
            "Semi-Relaxed" => (600, 34, 6400, 9),
            "Relaxed" => (700, 40, 8000, 9),
            _ => (500, 28, 5600, 6),
        };
    }

    private static void ApplyPalette(int index, AgentProfile agent)
    {
        var palette = new[]
        {
            (Accent: "#C56A54", Background: "#F9E5DE"),
            (Accent: "#367A72", Background: "#E1F1EE"),
            (Accent: "#6E5AA6", Background: "#ECE6FA"),
            (Accent: "#A67C33", Background: "#F5EBCF"),
        };

        var selected = palette[index % palette.Length];
        agent.AccentHex = selected.Accent;
        agent.BackgroundHex = selected.Background;
    }

    private static string Slugify(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var cleaned = new string(value
            .ToLowerInvariant()
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray());
        return string.Join('-', cleaned
            .Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries));
    }
}