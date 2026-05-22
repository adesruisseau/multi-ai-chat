# AgentGroupChat — Blazor Hybrid Rewrite Scope

## 1. Why

The WPF codebase works but has hit a maintainability wall:

- **XAML complexity** — 1,400+ lines in `MainWindow.xaml` with deeply nested grids, style triggers, and DynamicResource brush juggling. Every UI change is a fight.
- **Code-behind coupling** — `MainWindow.xaml.cs` is the ViewModel, the service layer, and half the persistence layer. Five partial classes (`*.Run.cs`, `*.Tts.cs`, `*.Logs.cs`, `*.Memory.cs`, `*.Appearance.cs`) mask a 2,000+ line god-object.
- **JSON file store brittleness** — Two flat JSON files (`llm-settings.json`, `themes.json`) plus dozens of loose `.txt` memory files with ad-hoc format parsing, manual migration logic, and truncation heuristics scattered across multiple stores.
- **No testability** — Everything depends on `MainWindow` instance state, `Dispatcher`, or static `Application.Current.Resources`. Zero unit tests.
- **Platform lock-in** — WPF is Windows-only. The app has no path to macOS/Linux/web.

## 2. Target Stack

| Layer | Technology | Why |
|-------|-----------|-----|
| **Shell** | .NET MAUI (Blazor Hybrid) | Native desktop host, cross-platform eventually |
| **UI** | Blazor (Razor components) | HTML/CSS/C# instead of XAML |
| **Component library** | MudBlazor | Material Design components, form helpers, dialogs, snackbars |
| **Database** | SQLite via EF Core | Replaces JSON files, relational integrity, migrations |
| **Audio** | NAudio (kept) | Piper/Kokoro streaming |
| **TTS voices** | System.Speech (Windows), Piper, Kokoro | Same providers, cleaner abstraction |
| **HTTP** | HttpClient (kept) | LLM + Kokoro API calls |
| **DI** | Microsoft.Extensions.DependencyInjection | Proper service registration |

## 3. Solution Architecture

Three projects. Domain and infrastructure are UI-agnostic — only the Hybrid project references MAUI/Blazor.

```
AgentGroupChat.sln
│
├── AgentGroupChat.Core/                  # Zero UI deps — domain + services
│   ├── AgentGroupChat.Core.csproj
│   ├── Models/
│   │   ├── Domain/                       # Domain models (what services operate on)
│   │   │   ├── RoomConfig.cs             # Room metadata + run settings
│   │   │   ├── AgentConfig.cs            # Agent definition + prompt
│   │   │   ├── AiConnection.cs           # Provider endpoint
│   │   │   ├── AiModel.cs               # Named model entry
│   │   │   ├── MemoryBlock.cs            # Typed memory content
│   │   │   ├── TranscriptTurn.cs         # Single speaker turn
│   │   │   ├── AppSettings.cs            # Global preferences
│   │   │   └── LogEntry.cs              # Structured log record
│   │   └── Llm/                          # LLM wire types (carried forward)
│   │       ├── LlmChatMessage.cs
│   │       ├── LlmRequestSettings.cs
│   │       └── LlmCompletionResult.cs
│   └── Services/
│       ├── Interfaces/                   # Service contracts
│       │   ├── IRoomRepository.cs
│       │   ├── ISettingsRepository.cs
│       │   ├── ITranscriptRepository.cs
│       │   ├── IMemoryRepository.cs
│       │   ├── ILogRepository.cs
│       │   └── ILegacyDataSource.cs      # Migration import interface
│       ├── LlmClient.cs                  # Multi-provider LLM (carried forward)
│       ├── ConversationRunner.cs          # Orchestrator shell
│       ├── PromptComposer.cs             # Builds agent/summarizer prompts
│       ├── TurnExecutor.cs               # Single-turn LLM call + response handling
│       ├── MemorySummarizer.cs           # Memory refresh logic
│       ├── SpeechService.cs              # TTS abstraction
│       └── LogService.cs                 # Log append/dispatch
│
├── AgentGroupChat.Infrastructure/        # Persistence — EF Core, SQLite, migration
│   ├── AgentGroupChat.Infrastructure.csproj
│   ├── Data/
│   │   ├── AppDbContext.cs               # EF Core context
│   │   ├── Migrations/                   # EF Core migrations
│   │   └── Entities/                     # EF entities (DB row shapes)
│   │       ├── RoomEntity.cs
│   │       ├── AgentEntity.cs
│   │       ├── AiConnectionEntity.cs
│   │       ├── AiModelEntity.cs
│   │       ├── MemoryBlockEntity.cs
│   │       ├── ChatMessageEntity.cs
│   │       ├── LogEntryEntity.cs
│   │       └── AppSettingsEntity.cs
│   ├── Repositories/                     # IRepository implementations
│   │   ├── RoomRepository.cs
│   │   ├── SettingsRepository.cs
│   │   ├── TranscriptRepository.cs
│   │   ├── MemoryRepository.cs
│   │   └── LogRepository.cs
│   └── Migration/
│       ├── LegacyJsonDataSource.cs       # Reads old JSON/TXT files
│       └── DataMigrator.cs               # One-time import orchestrator
│
├── AgentGroupChat.Hybrid/               # MAUI + Blazor UI
│   ├── AgentGroupChat.Hybrid.csproj
│   ├── MauiProgram.cs                    # DI registration
│   ├── wwwroot/
│   ├── State/                            # Flux-style state containers
│   │   ├── RoomState.cs                  # Selected room, edit state
│   │   ├── ConversationState.cs          # Running session, transcript, status
│   │   ├── SpeechState.cs               # TTS playback, highlights
│   │   └── AppState.cs                   # Theme, setup guide, global prefs
│   ├── ViewModels/                       # UI-specific projections
│   │   ├── RoomEditorVm.cs              # Mutable edit form for a room
│   │   ├── AgentEditorVm.cs             # Mutable edit form for an agent
│   │   ├── ChatMessageVm.cs             # Display model with speech highlight
│   │   ├── ConnectionEditorVm.cs
│   │   ├── ModelEditorVm.cs
│   │   └── LogEntryVm.cs               # Display with filter/expand state
│   ├── Components/
│   │   ├── Layout/
│   │   │   └── MainLayout.razor
│   │   ├── Pages/
│   │   │   ├── SetupGuide.razor
│   │   │   ├── Rooms.razor
│   │   │   ├── Chat.razor
│   │   │   ├── Logs.razor
│   │   │   └── AiModels.razor
│   │   └── Shared/
│   │       ├── RoomEditor.razor
│   │       ├── AgentEditor.razor
│   │       ├── ChatMessageCard.razor
│   │       ├── LogEntryRow.razor
│   │       ├── ConnectionEditor.razor
│   │       ├── ModelEditor.razor
│   │       ├── MemoryPanel.razor
│   │       └── FilePathDisplay.razor
```

### Model Layer Separation

Three distinct model tiers — never mix them.

```
EF Entities (Infrastructure)        Domain Models (Core)           View Models (Hybrid)
─────────────────────────────       ──────────────────────         ─────────────────────
RoomEntity                    →     RoomConfig                →    RoomEditorVm
AgentEntity                   →     AgentConfig               →    AgentEditorVm
ChatMessageEntity             →     TranscriptTurn            →    ChatMessageVm
MemoryBlockEntity             →     MemoryBlock               →    (read-only display)
LogEntryEntity                →     LogEntry                  →    LogEntryVm
AiConnectionEntity            →     AiConnection              →    ConnectionEditorVm
AiModelEntity                 →     AiModel                   →    ModelEditorVm
AppSettingsEntity              →     AppSettings               →    AppState
```

**EF Entities** — DB row shapes. Live in Infrastructure. Never exposed to UI or services.
Repositories map Entity ↔ Domain internally.

**Domain Models** — Immutable or near-immutable records/POCOs. What services accept and return.
No EF attributes, no UI concerns, no `INotifyPropertyChanged`.

**View Models** — Mutable UI-specific projections. Created from domain models when a page loads,
written back to domain models on save. Hold transient UI state (validation, dirty flags, expanded panels).

### Room Decomposition

The WPF `ThemeProfile` is a monolith. Split into separate concerns:

```
RoomConfig                          # Identity + run settings (persisted)
├── Id, Name, Topic
├── WaitForUserReply, AgentDelaySeconds
├── MaxTokens, RecentTurnsWindow, UserCompactionBudget
├── SummarizerModelId, SummarizationLevel, SummarizerMaxTokens, ...
└── Agents: List<AgentConfig>

RoomMemoryState                     # Memory blocks (persisted separately, loaded on demand)
├── SharedRoomMemory: string
├── DurableMemory: string
└── AgentMemories: Dict<agentId, (Long, Short)>

ConversationSession                 # Runtime execution state (NOT persisted as a blob)
├── RoomId
├── Transcript: List<TranscriptTurn>
├── CompletedRounds: int
├── IsRunning, IsCancelled
├── CurrentAgentIndex
└── Status: string

SpeechPlaybackState                 # TTS runtime (transient, never persisted)
├── IsPlaying, IsPaused
├── CurrentMessageId
├── HighlightPrefix, HighlightCurrent, HighlightSuffix
└── QueueDepth
```

Services operate on these individually. `ConversationRunner` takes a `RoomConfig` + reads/writes
`RoomMemoryState` via `IMemoryRepository`, emits `TranscriptTurn` events, never holds the full blob.

## 4. Database Schema (SQLite)

Replaces `llm-settings.json`, `themes.json`, and all per-room `.txt` files.

### Tables

```sql
-- Global app preferences (single-row table)
CREATE TABLE AppSettings (
    Id              INTEGER PRIMARY KEY CHECK (Id = 1),
    UiTheme         TEXT NOT NULL DEFAULT 'System',
    UiAccent        TEXT NOT NULL DEFAULT 'Terracotta',
    TtsEnabled      INTEGER NOT NULL DEFAULT 0,
    TtsProvider     TEXT NOT NULL DEFAULT 'Local',
    TtsVoice        TEXT NOT NULL DEFAULT '',
    TtsRate         INTEGER NOT NULL DEFAULT 0,
    PiperExePath    TEXT NOT NULL DEFAULT '',
    PiperModelsDir  TEXT NOT NULL DEFAULT '',
    KokoroBaseUrl   TEXT NOT NULL DEFAULT 'http://127.0.0.1:8000',
    KokoroModel     TEXT NOT NULL DEFAULT 'kokoro',
    KokoroVoice     TEXT NOT NULL DEFAULT 'af_heart',
    KokoroLangCode  TEXT NOT NULL DEFAULT 'a',
    KokoroSpeed     REAL NOT NULL DEFAULT 1.0,
    SetupModels     INTEGER NOT NULL DEFAULT 0,
    SetupRooms      INTEGER NOT NULL DEFAULT 0,
    SetupTtsStatus  TEXT NOT NULL DEFAULT 'Pending',
    HideSetupGuide  INTEGER NOT NULL DEFAULT 0
);

-- LLM provider endpoints
CREATE TABLE AiConnections (
    Id          TEXT PRIMARY KEY,
    Name        TEXT NOT NULL,
    Transport   TEXT NOT NULL DEFAULT 'OpenAI Compatible',
    Endpoint    TEXT NOT NULL DEFAULT '',
    ApiKey      TEXT NOT NULL DEFAULT '',
    SortOrder   INTEGER NOT NULL DEFAULT 0
);

-- Named model entries pointing to a connection
CREATE TABLE AiModels (
    Id              TEXT PRIMARY KEY,
    Name            TEXT NOT NULL,
    ConnectionId    TEXT NOT NULL REFERENCES AiConnections(Id),
    ModelId         TEXT NOT NULL DEFAULT '',
    Notes           TEXT NOT NULL DEFAULT '',
    SortOrder       INTEGER NOT NULL DEFAULT 0
);

-- Conversation rooms
CREATE TABLE Rooms (
    Id                          TEXT PRIMARY KEY,
    Name                        TEXT NOT NULL,
    Topic                       TEXT NOT NULL DEFAULT '',
    WaitForUserReply            INTEGER NOT NULL DEFAULT 1,
    AgentDelaySeconds           INTEGER NOT NULL DEFAULT 5,
    MaxTokens                   INTEGER NOT NULL DEFAULT 300,
    RecentTurnsWindow           INTEGER NOT NULL DEFAULT 6,
    UserCompactionBudget        INTEGER NOT NULL DEFAULT 3200,
    SummarizerModelId           TEXT REFERENCES AiModels(Id),
    SummarizationLevel          TEXT NOT NULL DEFAULT 'Moderate',
    SummarizerMaxTokens         INTEGER NOT NULL DEFAULT 500,
    SummarizerMaxLines          INTEGER NOT NULL DEFAULT 28,
    SummarizerMaxCharacters     INTEGER NOT NULL DEFAULT 5600,
    SummarizerBroaderTurns      INTEGER NOT NULL DEFAULT 6,
    SummarizerPromptOverride    TEXT NOT NULL DEFAULT '',
    SortOrder                   INTEGER NOT NULL DEFAULT 0
);

-- Agents within a room
CREATE TABLE Agents (
    Id                  TEXT PRIMARY KEY,
    RoomId              TEXT NOT NULL REFERENCES Rooms(Id) ON DELETE CASCADE,
    Name                TEXT NOT NULL,
    ModelId             TEXT REFERENCES AiModels(Id),
    SystemPrompt        TEXT NOT NULL DEFAULT '',
    IsEnabled           INTEGER NOT NULL DEFAULT 1,
    MaxTokensOverride   INTEGER,
    CompactionBudget    INTEGER NOT NULL DEFAULT 420,
    AccentHex           TEXT NOT NULL DEFAULT '#C56A54',
    BackgroundHex       TEXT NOT NULL DEFAULT '#F9E5DE',
    TtsVoice            TEXT NOT NULL DEFAULT '',
    SortOrder           INTEGER NOT NULL DEFAULT 0
);

-- Memory blocks (replaces loose .txt files)
CREATE TABLE MemoryBlocks (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    RoomId      TEXT NOT NULL REFERENCES Rooms(Id) ON DELETE CASCADE,
    AgentId     TEXT REFERENCES Agents(Id) ON DELETE CASCADE,
    Kind        TEXT NOT NULL,  -- 'SharedRoom', 'Durable', 'AgentLong', 'AgentShort'
    Content     TEXT NOT NULL DEFAULT '',
    UpdatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
);
CREATE UNIQUE INDEX IX_MemoryBlocks_Unique
    ON MemoryBlocks(RoomId, COALESCE(AgentId, ''), Kind);

-- Chat transcript
CREATE TABLE ChatMessages (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    RoomId      TEXT NOT NULL REFERENCES Rooms(Id) ON DELETE CASCADE,
    Round       INTEGER NOT NULL DEFAULT 0,
    Speaker     TEXT NOT NULL,
    Content     TEXT NOT NULL,
    AccentHex   TEXT NOT NULL DEFAULT '',
    BackgroundHex TEXT NOT NULL DEFAULT '',
    CreatedAt   TEXT NOT NULL DEFAULT (datetime('now'))
);

-- Session logs (replaces JSONL files)
CREATE TABLE LogEntries (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    Category        TEXT NOT NULL,
    Source          TEXT NOT NULL,
    Message         TEXT NOT NULL,
    Detail          TEXT NOT NULL DEFAULT '',
    DurationMs      INTEGER,
    CreatedAt       TEXT NOT NULL DEFAULT (datetime('now'))
);
```

### Migration from JSON/TXT

A one-time `DataMigrator` service runs on first launch with SQLite.
Import interfaces are defined early in Core so migration can be tested against real-world data
before all UI pages exist.

```
ILegacyDataSource (Core interface)
├── LoadConnectionSettings()  → ConnectionSettings
├── LoadThemeLibrary()        → ThemeLibrary
├── LoadRoomMemory(themeId)   → memory files
└── LoadTranscript(themeId)   → chat turns

LegacyJsonDataSource (Infrastructure implementation)
├── Reads %LOCALAPPDATA%/AgentGroupChat/llm-settings.json
├── Reads %LOCALAPPDATA%/AgentGroupChat/themes.json
├── Reads per-room .txt files
└── Handles legacy provider dict migration

DataMigrator (Infrastructure)
├── Detects existing JSON files
├── Calls ILegacyDataSource to parse
├── Maps to EF entities and inserts
└── Renames old files to *.migrated (non-destructive)
```

## 5. Service Layer Contracts

Services operate on **domain models only** — never EF entities, never view models.
Repositories handle Entity ↔ Domain mapping internally.

### LlmClient (carried forward)
```csharp
// Stays almost identical — already well-isolated. Lives in Core.
public class LlmClient(HttpClient http)
{
    Task<LlmCompletionResult> CompleteAsync(
        LlmRequestSettings settings,
        IReadOnlyList<LlmChatMessage> messages,
        CancellationToken ct);
}
```

### ConversationRunner (orchestrator shell — designed to split)

Day-one: a thin orchestrator that delegates to focused collaborators.
Each collaborator is its own class from the start, even if small.
This prevents the next god-object.

```
ConversationRunner                     # Owns the round loop and cancellation
├── PromptComposer                     # Builds system/user messages per agent
│   ├── Reads RoomConfig, AgentConfig
│   ├── Reads MemoryBlock (shared room, durable, agent short-term)
│   ├── Reads recent TranscriptTurns
│   └── Returns IReadOnlyList<LlmChatMessage>
├── TurnExecutor                       # Single agent turn: call LLM, parse response
│   ├── Takes LlmRequestSettings + messages
│   ├── Calls LlmClient.CompleteAsync
│   ├── Logs timing + token usage
│   └── Returns TranscriptTurn
├── MemorySummarizer                   # Post-round memory refresh
│   ├── Builds summarizer prompts (system + user)
│   ├── Calls LLM for shared room memory
│   ├── Calls LLM for durable memory (every N rounds)
│   ├── Sanitizes/truncates output
│   └── Persists via IMemoryRepository
└── LogService                         # Structured logging
```

```csharp
// Core/Services/ConversationRunner.cs
public class ConversationRunner(
    PromptComposer prompts,
    TurnExecutor turns,
    MemorySummarizer summarizer,
    IRoomRepository rooms,
    ITranscriptRepository transcript,
    LogService log)
{
    // UI subscribes to these via state container, not direct event wiring
    event Action<TranscriptTurn>? TurnCompleted;
    event Action<string>? StatusChanged;
    event Action? RunFinished;

    Task RunAsync(
        RoomConfig room,
        string userMessage,
        int iterations,
        CancellationToken ct);
}

// Core/Services/PromptComposer.cs
public class PromptComposer(IMemoryRepository memory)
{
    IReadOnlyList<LlmChatMessage> BuildAgentPrompt(
        RoomConfig room,
        AgentConfig agent,
        IReadOnlyList<TranscriptTurn> recentTurns,
        int currentRound,
        int totalRounds);

    IReadOnlyList<LlmChatMessage> BuildRoomMemorySummarizerPrompt(
        RoomConfig room,
        IReadOnlyList<TranscriptTurn> roundTurns,
        IReadOnlyList<TranscriptTurn> sessionTurns);

    IReadOnlyList<LlmChatMessage> BuildDurableSummarizerPrompt(
        RoomConfig room,
        IReadOnlyList<TranscriptTurn> roundTurns,
        IReadOnlyList<TranscriptTurn> sessionTurns);
}

// Core/Services/TurnExecutor.cs
public class TurnExecutor(LlmClient llm, LogService log)
{
    Task<TranscriptTurn> ExecuteAsync(
        LlmRequestSettings settings,
        IReadOnlyList<LlmChatMessage> messages,
        string agentName,
        CancellationToken ct);
}

// Core/Services/MemorySummarizer.cs
public class MemorySummarizer(
    LlmClient llm,
    PromptComposer prompts,
    IMemoryRepository memory,
    LogService log)
{
    Task RefreshRoomMemoryAsync(
        RoomConfig room,
        IReadOnlyList<TranscriptTurn> roundTurns,
        IReadOnlyList<TranscriptTurn> sessionTurns,
        CancellationToken ct);

    Task PromoteDurableMemoryAsync(
        RoomConfig room,
        IReadOnlyList<TranscriptTurn> roundTurns,
        IReadOnlyList<TranscriptTurn> sessionTurns,
        CancellationToken ct);
}
```

**Future split paths** (not day-one, but the seams exist):
- `TokenBudgetManager` — extracted from PromptComposer when token counting gets complex
- `ConversationScheduler` — extracted from ConversationRunner when retry/backoff logic grows
- `TranscriptAssembler` — extracted if transcript windowing/compaction gets its own rules

### SpeechService (extracted from MainWindow.Tts.cs)
```csharp
// Core/Services/SpeechService.cs
public class SpeechService
{
    event Action<SpeechHighlight>? HighlightChanged;
    event Action? PlaybackFinished;

    Task SpeakAsync(string text, string voice, CancellationToken ct);
    void Pause();
    void Resume();
    void Stop();
    IReadOnlyList<string> GetAvailableVoices(string provider);
}

public record SpeechHighlight(string Prefix, string Current, string Suffix);
```

### Repository Interfaces (Core — implemented in Infrastructure)
```csharp
public interface IRoomRepository
{
    Task<List<RoomConfig>> GetAllAsync();
    Task<RoomConfig?> GetAsync(string id);
    Task SaveAsync(RoomConfig room);
    Task DeleteAsync(string id);
}

public interface ITranscriptRepository
{
    Task<List<TranscriptTurn>> GetAsync(string roomId);
    Task AppendAsync(TranscriptTurn turn);
    Task ClearAsync(string roomId);
}

public interface IMemoryRepository
{
    Task<string> GetAsync(string roomId, string? agentId, MemoryKind kind);
    Task SaveAsync(string roomId, string? agentId, MemoryKind kind, string content);
    Task ClearAllForRoomAsync(string roomId);
}

public interface ISettingsRepository
{
    Task<AppSettings> GetAsync();
    Task SaveAsync(AppSettings settings);
    Task<List<AiConnection>> GetConnectionsAsync();
    Task SaveConnectionAsync(AiConnection connection);
    Task DeleteConnectionAsync(string id);
    Task<List<AiModel>> GetModelsAsync();
    Task SaveModelAsync(AiModel model);
    Task DeleteModelAsync(string id);
}

public interface ILogRepository
{
    Task AppendAsync(LogEntry entry);
    Task<List<LogEntry>> QueryAsync(string? category, string? source, int limit);
    Task<List<string>> GetSourcesAsync();
}

public enum MemoryKind { SharedRoom, Durable, AgentLong, AgentShort }
```

### LogService
```csharp
// Core/Services/LogService.cs
public class LogService(ILogRepository repo)
{
    event Action<LogEntry>? EntryAdded;

    Task AppendAsync(LogEntry entry);
    Task<List<LogEntry>> QueryAsync(string? category, string? source, int limit = 200);
    Task<List<string>> GetSourcesAsync();
}
```

## 6. Flux-Style State Containers

Blazor components subscribe to state containers instead of wiring events directly to services.
This avoids event spaghetti and excessive `StateHasChanged()` calls.

Not full Redux — just centralized observable state for the things that cross component boundaries.

```csharp
// Hybrid/State/ConversationState.cs
public class ConversationState
{
    public string? ActiveRoomId { get; private set; }
    public List<ChatMessageVm> Transcript { get; } = new();
    public bool IsRunning { get; private set; }
    public string Status { get; private set; } = "";
    public int CompletedRounds { get; private set; }

    public event Action? OnChange;

    public void AddMessage(ChatMessageVm message) { Transcript.Add(message); Notify(); }
    public void SetRunning(bool running) { IsRunning = running; Notify(); }
    public void SetStatus(string status) { Status = status; Notify(); }
    public void Clear() { Transcript.Clear(); CompletedRounds = 0; Notify(); }

    private void Notify() => OnChange?.Invoke();
}

// Hybrid/State/RoomState.cs
public class RoomState
{
    public List<RoomConfig> Rooms { get; private set; } = new();
    public string? SelectedRoomId { get; private set; }

    public event Action? OnChange;

    public void SetRooms(List<RoomConfig> rooms) { Rooms = rooms; Notify(); }
    public void SelectRoom(string? id) { SelectedRoomId = id; Notify(); }

    private void Notify() => OnChange?.Invoke();
}

// Hybrid/State/SpeechState.cs
public class SpeechState
{
    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public string? CurrentMessageId { get; private set; }
    public SpeechHighlight? Highlight { get; private set; }

    public event Action? OnChange;
    // ... mutators
}

// Hybrid/State/AppState.cs
public class AppState
{
    public string UiTheme { get; private set; } = "System";
    public string UiAccent { get; private set; } = "Terracotta";
    public bool SetupComplete { get; private set; }

    public event Action? OnChange;
    // ... mutators
}
```

Components subscribe in `OnInitialized` and unsubscribe in `Dispose`:
```csharp
@implements IDisposable
@inject ConversationState ConvoState

protected override void OnInitialized()
{
    ConvoState.OnChange += StateHasChanged;
}

public void Dispose()
{
    ConvoState.OnChange -= StateHasChanged;
}
```

State containers are registered as **singletons** in DI (scoped to MAUI app lifetime).

## 7. Component Map (Plain HTML, No Styling)

Each Blazor page maps 1:1 to an existing WPF tab. Components use MudBlazor layout primitives
(`MudTabs`, `MudGrid`, `MudTextField`, `MudSelect`, `MudButton`, `MudExpansionPanels`, etc.)
but NO custom CSS. The initial port renders functionally identical UI with MudBlazor defaults.

### Page: SetupGuide.razor
```
MudCard
  MudProgressLinear (3-step: Models, Rooms, TTS)
  MudText "Recommended order: AI Models → Rooms → Agents → Chat"
  MudList
    Step 1: AI Models — status icon + "Configure" link
    Step 2: Rooms — status icon + "Configure" link
    Step 3: TTS — status icon + "Skip" button
  MudButton "Hide Setup Guide"
```

### Page: Rooms.razor
```
MudGrid
  Left (4 cols): Room browser
    MudList (selectable)
      foreach room:
        MudListItem: room.Name, agent count badge, compression level chip
    MudButton "Add Room"

  Right (8 cols): RoomEditor component (if room selected)
```

### Component: RoomEditor.razor
```
MudTextField "Room Name"
MudTextField "Topic / Direction" (multiline)
MudGrid (3 cols)
  MudNumericField "Iterations"
  MudNumericField "Seconds Between Agents"
  MudNumericField "Recent Turns Window"
MudGrid (3 cols)
  MudNumericField "Max Tokens"
  MudNumericField "User Compaction Budget"
  MudCheckBox "Wait for User Reply"

MudDivider

MudText "Room Memory Summarizer"
MudSelect "Summarizer Model" (bound to AiModels list)
MudSelect "Compression Level" (Aggressive..Custom)
MudGrid (2 cols)
  MudNumericField "Summarizer Max Tokens"
  MudNumericField "Broader Transcript Turns"
MudGrid (2 cols)
  MudNumericField "Max Summary Lines"
  MudNumericField "Max Summary Characters"
MudTextField "Summarizer Agent Prompt" (multiline, readonly unless Custom)

MudDivider

MudText "Agents"
MudButtonGroup: Add, Remove, Move Up, Move Down
MudExpansionPanels
  foreach agent:
    AgentEditor component

MudDivider
MudButtonGroup: Save, Delete Room, Clear Memories
```

### Component: AgentEditor.razor
```
MudExpansionPanel header=agent.Name
  MudTextField "Name"
  MudSelect "AI Model" (bound to AiModels)
  MudCheckBox "Enabled"
  MudSelect "Voice" (bound to available voices, optional)
  MudTextField "Prompt / Instructions" (multiline)
  MudGrid (2 cols)
    MudNumericField "Compaction Budget"
    MudNumericField "Max Tokens Override"
  MudText "Short-term Memory" (readonly display)
  MudButtonGroup: Sanitize, Clear
```

### Page: Chat.razor
```
MudGrid
  Top bar:
    MudText room name
    MudSlider "Text Size" (14-24)
    MudCheckBox "Enable TTS"
    MudSelect "TTS Provider"
    MudSlider "Rate" (-5 to +5)

  Transcript area (scrollable div):
    foreach message:
      ChatMessageCard component

  Bottom input:
    MudTextField "Your message" (multiline)
    MudButtonGroup: Run, Pause, Stop, Clear
```

### Component: ChatMessageCard.razor
```
MudCard (colored border from agent accent)
  MudCardHeader: Speaker name, timestamp
  MudCardContent: message text (with speech highlight spans if active)
  MudCardActions: Play button, Copy button
```

### Page: Logs.razor
```
MudGrid
  Left (8 cols):
    MudChipSet: System, Speech, Timing, Request, Response, Memory (toggle filters)
    MudSelect "Source Filter"
    MudList
      foreach entry:
        LogEntryRow component

  Right (4 cols):
    MemoryPanel "Shared Room Memory"
    MemoryPanel "Durable Memory"
```

### Component: LogEntryRow.razor
```
MudExpansionPanel
  Header: Category chip, Source, Timestamp, Message, Duration
  Content: Detail (preformatted)
  MudIconButton Copy
```

### Page: AiModels.razor
```
MudGrid
  Left (6 cols): Connections
    MudList (selectable)
      foreach connection:
        MudListItem: connection.Name
    ConnectionEditor component (if selected)
    MudButtonGroup: Add, Remove

  Right (6 cols): Model Catalog
    MudList (filtered by selected connection)
      foreach model:
        MudListItem: model.Name
    ModelEditor component (if selected)
    MudButtonGroup: Add, Remove, Test
    MudButton "Save AI Settings"
```

### Component: ConnectionEditor.razor
```
MudTextField "Name"
MudSelect "Transport"
MudTextField "Endpoint URL"
MudTextField "API Key" (InputType.Password)
```

### Component: ModelEditor.razor
```
MudTextField "Display Name"
MudSelect "Connection"
MudTextField "Model ID"
MudTextField "Notes" (multiline)
```

### Component: MemoryPanel.razor
```
MudText title
FilePathDisplay component
MudTextField (readonly, multiline) content
```

### Component: FilePathDisplay.razor
```
MudTextField (readonly) path
MudIconButton "Open in Explorer"
MudIconButton "Copy"
```

## 8. What Carries Forward As-Is

These pieces are well-isolated and transfer into **Core/Services** directly:

| WPF Source | Core Destination | Notes |
|---|---|---|
| `LlmClient.cs` | `Core/Services/LlmClient.cs` | Drop WPF deps, same HTTP logic |
| `LlmRequestSettings`, `LlmChatMessage`, `LlmCompletionResult` | `Core/Models/Llm/` | Records, no changes |
| Provider dispatch (OpenAI, Groq, Gemini, HuggingFace, Ollama) | Same | 5 provider builders stay |
| Prompt building (`BuildAgentPrompt`) | `Core/Services/PromptComposer.cs` | Logic moves, no rewrite |
| Memory prompt templates | `Core/Services/PromptComposer.cs` | Text templates, move directly |
| Summarizer presets (Aggressive → Relaxed) | `Core/Services/MemorySummarizer.cs` | Lookup table, no changes |
| NAudio streaming (Piper, Kokoro) | `Core/Services/SpeechService.cs` | Audio code stays |
| Sentence segmentation | `Core/Services/SpeechService.cs` | Regex splitting stays |

## 9. What Gets Rewritten

| WPF Source | Destination | Why |
|---|---|---|
| `MainWindow.xaml` (1,400 lines) | `Hybrid/Components/Pages/*.razor` | XAML → Razor components |
| `MainWindow.xaml.cs` (2,100+ lines) | Core services + Hybrid state | God-object → DI services |
| `ConnectionSettingsStore.cs` | `Infrastructure/Repositories/` | JSON → SQLite via EF Core |
| `ThemeStore.cs` | `Infrastructure/Repositories/` | JSON + txt → SQLite via EF Core |
| `SessionLogStore.cs` | `Infrastructure/Repositories/LogRepository.cs` | JSONL → SQLite table |
| `ThemeModels.cs` | `Core/Models/Domain/` + `Infrastructure/Entities/` | WPF ObservableEntity → POCOs + EF entities |
| `ChatMessage.cs` | `Core/Models/Domain/TranscriptTurn.cs` | WPF bindings → domain record |
| `LogEntry.cs` | `Core/Models/Domain/LogEntry.cs` | WPF bindings → domain record |
| `UiThemeManager.cs` | `Hybrid/` (CSS variables) | WPF Resources → CSS custom properties |

## 10. Execution Order

### Phase 1: Foundation + Migration Interfaces
1. Create 3-project solution (`Core`, `Infrastructure`, `Hybrid`)
2. Add NuGet packages: MudBlazor, EF Core SQLite, NAudio, System.Speech
3. Define domain models in `Core/Models/Domain/`
4. Define repository interfaces in `Core/Services/Interfaces/`
5. Define `ILegacyDataSource` import interface in Core
6. Implement `LegacyJsonDataSource` in Infrastructure (reads existing JSON/TXT files)
7. Verify legacy data loads correctly against real `%LOCALAPPDATA%` files

> Migration interfaces come first because legacy data always reveals hidden domain rules.
> Testing real-world import now validates schema assumptions before UI exists.

### Phase 2: Database + Repositories
8. Define EF entities in `Infrastructure/Data/Entities/`
9. Create `AppDbContext` with schema
10. Create initial EF migration
11. Implement repository classes (Entity ↔ Domain mapping)
12. Implement `DataMigrator` (reads via `ILegacyDataSource`, writes via repositories)
13. Verify full migration round-trip: JSON → SQLite → domain models

### Phase 3: Skeleton App (get a window with tabs)
14. Set up MAUI Blazor Hybrid project shell
15. Register all services and repositories in `MauiProgram.cs`
16. Create state containers (`RoomState`, `ConversationState`, `SpeechState`, `AppState`)
17. Create `MainLayout.razor` with MudTabs (5 tabs, placeholder content)
18. Wire up migration to run on first launch
19. Verify app launches, migrates data, shows tabs

### Phase 4: Settings & Models (Configure AI Models tab)
20. Build `AiModels.razor` page with connection/model editors
21. Build `ConnectionEditor.razor` and `ModelEditor.razor` components
22. Port `LlmClient.cs` into Core (copy + strip WPF deps)
23. Wire up Test Model button
24. Wire up Save/Add/Remove with `ISettingsRepository`

### Phase 5: Rooms (Rooms tab)
25. Build `Rooms.razor` page with browser + room selection via `RoomState`
26. Create `RoomEditorVm` and `AgentEditorVm` view models
27. Build `RoomEditor.razor` with all fields
28. Build `AgentEditor.razor` with accordion
29. Wire up Save/Delete/Clear with `IRoomRepository` + `IMemoryRepository`

### Phase 6: Chat (Chat tab — the core loop)
30. Port prompt building to `PromptComposer`
31. Port single-turn execution to `TurnExecutor`
32. Port memory summarization to `MemorySummarizer`
33. Implement `ConversationRunner` orchestrator
34. Implement `LogService`
35. Build `Chat.razor` page with transcript + input, driven by `ConversationState`
36. Build `ChatMessageCard.razor` component
37. Wire up Run/Stop/Clear
38. Verify multi-agent conversation completes end-to-end

### Phase 7: Logs
39. Build `Logs.razor` page with category/source filters
40. Build `LogEntryRow.razor` with expandable detail
41. Build `MemoryPanel.razor` for shared room + durable memory display

### Phase 8: TTS
42. Port `SpeechService` into Core (Local, Piper, Kokoro)
43. Wire up speech controls in Chat page via `SpeechState`
44. Add speech highlighting to `ChatMessageCard`

### Phase 9: Polish
45. Build `SetupGuide.razor` page
46. Theme switching (CSS variables, light/dark) via `AppState`

## 11. Out of Scope (for initial port)

- Custom CSS / visual polish (MudBlazor defaults only)
- Streaming LLM responses (keep `stream: false`)
- Web deployment (MAUI desktop only for now)
- macOS/Linux builds (later)
- Unit tests (second pass, but architecture supports it from day one)
- SignalR / real-time push (state containers + `StateHasChanged` sufficient)
- `TokenBudgetManager`, `ConversationScheduler` extraction (future split when complexity warrants)
