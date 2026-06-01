# AgentGroupChat.Hybrid Overview

This document is the newcomer map for `AgentGroupChat.Hybrid`.

If `Core` is the engine, `Hybrid` is the actual desktop app. This project owns startup, dependency injection, navigation, UI pages, page-level workflows, app state containers, and the visual design choices the user sees.

It does **not** own the room runtime itself and it does **not** own the database schema. It wires the UI to `Core` services and Infrastructure repositories.

---

## What This Project Owns

- MAUI app startup
- Blazor WebView hosting
- navigation and page layout
- state containers for settings, rooms, and the active conversation
- UI pages for setup, models, rooms, chat, and logs
- theme and color preset decisions
- page-level user workflows such as loading a room, starting a run, testing a model, previewing a voice, and inspecting logs

---

## Read This First

For a new developer, the best reading order is:

1. `MauiProgram.cs`
2. `Components/Layout/MainLayout.razor`
3. `State/AppState.cs`
4. `State/RoomState.cs`
5. `State/ConversationState.cs`
6. `Components/Pages/Home.razor`
7. `Components/Pages/AiModels.razor`
8. `Components/Pages/Rooms.razor`
9. `Components/Pages/Chat.razor`
10. `Components/Pages/Logs.razor`
11. `Theming/AppTheme.cs`
12. `Theming/AgentColorPresets.cs`

If you only read one file to understand how the app is assembled, read `MauiProgram.cs`.

---

## Where The UI Lives

The UI is mostly in:

- `Components/Pages/*.razor`
- `Components/Layout/*.razor`
- `State/*.cs`
- `Theming/*.cs`

The native MAUI layer is intentionally thin.

Important consequence:

- most day-to-day feature work in this project happens in Razor components and state containers, not in platform-specific files

---

## Startup Path

### `MauiProgram.cs`

This is the startup composition root.

What it does:

- configures MAUI and Blazor WebView
- registers MudBlazor
- creates the SQLite path under LocalAppData
- registers `AppDbContext`
- registers all repository implementations
- registers all core services
- registers `AppState`, `RoomState`, and `ConversationState`
- ensures the database exists and runs `DataMigrator`

Why it matters:

- this file explains how the three projects fit together
- if dependency injection breaks, this is where to start

### `App.xaml` and `App.xaml.cs`

These are the MAUI app wrapper files. They are not where the feature logic lives.

### `MainPage.xaml` and `MainPage.xaml.cs`

These host the `BlazorWebView`. They matter for shell-level behavior, but not for most product features.

---

## Layout And Navigation

### `Components/Layout/MainLayout.razor`

This is the app shell a new developer should read first after startup.

What it does:

- renders the app bar and navigation drawer
- exposes the currently selected room in the layout
- provides links to the main feature pages
- hosts theme switching controls

This file shows the top-level mental model of the app better than any other UI file.

### `Components/Routes.razor`

This is the Blazor router configuration.

It is important, but mostly standard plumbing.

### `Components/Layout/NavMenu.razor`

This is low-priority. The important navigation intent is already visible in `MainLayout.razor`.

---

## State Containers

The current UI design uses simple scoped state containers rather than a heavyweight client-state framework.

### `State/AppState.cs`

Owns app-wide settings and the AI catalog.

What it holds:

- `AppSettings`
- AI connections
- AI models

What it does:

- loads settings and model catalog from repositories
- saves settings, connections, and models
- notifies the UI on changes

### `State/RoomState.cs`

Owns the room list and the selected room.

What it does:

- loads all rooms
- tracks `SelectedRoom`
- saves and deletes a room through `IRoomRepository`

### `State/ConversationState.cs`

Owns the active chat session view state.

What it holds:

- user-visible `Messages`
- persisted-like `SessionTurns`
- completed round count
- running status and status text

What it does:

- adds system, user, and agent messages to the current UI session
- resets the in-memory conversation state on clear or room load

Why these files matter:

- they explain how the UI stays simple even though the product behavior is complex
- they are the main bridge between the repository layer and the Razor pages

---

## High-Priority Pages

### `Components/Pages/Home.razor`

This is the setup guide.

What it does:

- explains what the app is
- tracks first-run progress through models, rooms, and TTS setup
- gives a newcomer a safe entry path into the app

Why it matters:

- it encodes the intended onboarding order

### `Components/Pages/AiModels.razor`

This is the reusable AI catalog UI.

What it does:

- edits AI connections such as OpenAI-compatible, Groq, Gemini, HuggingFace, and Ollama
- edits model catalog entries that point to those connections
- tests model connectivity through `LlmClient`

Why it matters:

- this page defines the app's global model configuration pattern
- future global catalogs, like image models or prompt samples, should likely follow this page's shape

### `Components/Pages/Rooms.razor`

This is the room and agent editor.

What it currently mixes:

- room basics
- turn-flow settings
- summarizer settings
- scene-image defaults
- privileged-action and NPC settings
- the full agent editor

Why it matters:

- this is one of the densest pages in the app
- it is the primary configuration surface for the product's behavior
- it is also the page most likely to benefit from the planned `Room Settings` vs. `Agents` split

### `Components/Pages/Chat.razor`

This is the main runtime UI.

What it does:

- loads a room into the current conversation session
- captures user input
- starts and stops the room runner
- rehydrates transcript history from persistence
- exposes speech controls and voice testing
- wires `ConversationRunner` events into `ConversationState`
- uses `SpeechService` as a speech gate for narration and playback

Why it matters:

- this is the best UI file for understanding how the user reaches the core loop logic
- it is also where the UI meets the backend agent calls most directly

### `Components/Pages/Logs.razor`

This is the inspection surface.

What it does:

- shows logs by category and source
- acts as the memory inspector
- shows scene archives for the selected room

Why it matters:

- this page exposes the hidden system state that supports the chat experience

---

## UI Design Choices

These are the main UI decisions visible in the project today.

### MAUI Shell + Blazor WebView

The app uses a thin native shell and a Blazor UI hosted inside it.

Why this matters:

- most product work feels like building a web UI
- the native layer stays small
- the UI can remain strongly component-based while still shipping as a desktop app

### MudBlazor-First Components

The UI is built around MudBlazor controls such as:

- papers
- grids
- tables
- selects
- alerts
- tabs and stacks when used

Why this matters:

- the app favors practical, form-heavy configuration screens over custom visual widgets

### Page-Per-Surface Navigation

The app is split into large, task-oriented pages:

- Setup Guide
- AI Models
- Rooms
- Chat
- Logs

This is a clear product choice: each page is a feature area, not a tiny route.

### Warm Theme + Agent Color Identity

The visual design intentionally uses warm surfaces and accent-driven agent colors.

Important files:

- `Theming/AppTheme.cs`
- `Theming/AgentColorPresets.cs`

`AppTheme.cs` defines the warm MudBlazor palette and typography.

`AgentColorPresets.cs` gives agents recognizable message colors so the chat transcript remains easy to scan.

### State Container Simplicity

The UI avoids complex state frameworks. Instead it uses simple scoped services with `OnChange` events. That keeps the app understandable for a small team.

---

## Supporting Files By Priority

### Important support files

- `Components/_Imports.razor` — shared Razor imports
- `AgentGroupChat.Hybrid.csproj` — project dependencies and MAUI target configuration
- `Theming/AppTheme.cs` — global palette and typography
- `Theming/AgentColorPresets.cs` — per-agent chat colors

### Low-priority pages

- `Components/Pages/Counter.razor`
- `Components/Pages/Weather.razor`
- `Components/Pages/NotFound.razor`

These are either template leftovers or minor support pages and should not be a new developer's starting point.

### Platform and shell boilerplate

- `Platforms/Android/*`
- `Platforms/iOS/*`
- `Platforms/MacCatalyst/*`
- `Platforms/Windows/*`
- `Resources/*`
- `wwwroot/*`

These files matter for packaging and host behavior, but they are not the best place to learn how the product works.

---

## Project Structure Summary

### Highest priority

- `MauiProgram.cs`
- `Components/Layout/MainLayout.razor`
- `State/AppState.cs`
- `State/RoomState.cs`
- `State/ConversationState.cs`
- `Components/Pages/Chat.razor`
- `Components/Pages/Rooms.razor`
- `Components/Pages/AiModels.razor`
- `Components/Pages/Logs.razor`
- `Components/Pages/Home.razor`

### Second priority

- `Theming/AppTheme.cs`
- `Theming/AgentColorPresets.cs`
- `Components/Routes.razor`
- `AgentGroupChat.Hybrid.csproj`

### Lowest priority for first orientation

- `App.xaml*`
- `MainPage.xaml*`
- `Components/_Imports.razor`
- `Components/Layout/NavMenu.razor`
- `Platforms/*`
- `Resources/*`
- `wwwroot/*`
- template pages like `Counter` and `Weather`

---

## Best First Tasks For A New Developer

If you want a safe onboarding path:

1. Read `MauiProgram.cs` to understand how the app is composed.
2. Read the three state containers to understand data flow.
3. Read `Chat.razor` to see how the UI reaches `ConversationRunner`.
4. Read `Rooms.razor` to understand how room and agent configuration is edited.
5. Read `AiModels.razor` to understand the global catalog pattern.
6. Read `Logs.razor` to understand how inspection surfaces work.

That path explains the UI much faster than starting from platform files or MAUI boilerplate.