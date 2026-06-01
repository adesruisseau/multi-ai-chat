# Plan: Prompt Library and Saved Prompt Samples

This feature adds a standalone prompt library for reusable agent prompts, starter samples, and user-saved variants.

The goal is not to make prompts into live linked assets that control agent behavior after assignment. The goal is to give the app a durable place to store prompt text plus short descriptions so users can:

- try built-in starter prompts on first use
- keep a safe original copy of a DM prompt before experimenting
- save a successful prompt variation for reuse later
- browse prompt ideas without tying them to any room or agent record

The key design rule is that prompt library entries stay independent from rooms and agents. Applying a library entry should copy text into an agent's `SystemPrompt`; it should not create a live reference that keeps the room coupled to the library.

---

## MVP Recommendations

- Create a separate prompt-library domain model and repository. Do not overload `AppSettings`, `RoomConfig`, or `AgentConfig`.
- Treat prompt samples as global local data, similar in spirit to the AI model catalog, but completely independent from the room runtime.
- Support both built-in samples and user-created entries in the same library surface.
- Keep application of a prompt sample copy-based, not reference-based.
- Make the first UX focus simple: browse, preview, apply to agent, duplicate, and save current prompt as sample.

Why this fits the repo cleanly:

- The current storage model already separates reusable global config from room-specific config.
- `AppSettings` is currently for singular app-level settings only and is the wrong place for a growing prompt catalog.
- `RoomConfig` and `AgentConfig` are persisted as part of the room aggregate and should not become a prompt-library database by accident.
- The current `AiModels.razor` page already shows a simple editable catalog pattern that can be mirrored for prompt samples.

---

## Non-Goals for MVP

- Live linking an agent to a prompt sample after apply
- Full prompt version diffing
- Prompt execution analytics
- Prompt scoring or automated evaluation
- Multi-part prompt composition from fragments
- Cloud sync or sharing across installations

---

## Product Shape

### User Stories

1. A new user opens the app and sees a few starter prompts they can apply to an agent immediately.
2. A user with a working DM prompt saves it as "DM Original" before experimenting.
3. The user edits the agent prompt locally, decides the change was bad, and reapplies the saved original from the library.
4. The user discovers a good variation and saves it as a new reusable prompt entry.
5. The user browses prompt samples without needing a room selected.

### Core UX Rules

- The library is available even when no room is selected.
- Applying a sample copies prompt text into the target agent only at the moment of apply.
- The library entry remains unchanged unless the user edits the library entry directly.
- Agents do not store foreign keys back to prompt-library entries for MVP.

That last rule is what keeps the feature untied to the rest of the app.

---

## Recommended UI Shape

### New Page: Prompt Library

Add a dedicated page, for example:

- route: `/prompts`
- nav label: `Prompt Library`

This page should be the primary home for prompt samples.

Suggested sections:

1. Starter Samples
2. Saved Prompts
3. Search and filter bar
4. Selected prompt preview/editor

Suggested row actions:

- Apply to agent
- Duplicate
- Save
- Delete

### Suggested Fields In The UI

- Name
- Category
- Description
- Tags
- Prompt text
- Built-in vs user-created badge

### Apply Flow

The apply flow should be intentionally explicit:

1. User opens a prompt sample.
2. User clicks `Apply to Agent`.
3. App asks for a target room and target agent, or uses the currently selected room if available.
4. The sample's prompt text is copied into `agent.SystemPrompt`.
5. The room remains a normal room record with no dependency on the library entry.

### Save Current Prompt Flow

From the Rooms page, each agent card can later gain actions such as:

- `Save Prompt to Library`
- `Apply From Library`

This is the practical workflow that solves the "save the original DM prompt before experimenting" use case.

---

## Data Model

Add a new standalone domain type.

### PromptSample

Recommended fields:

| Field | Type | Purpose |
| --- | --- | --- |
| `Id` | `string` | Primary key |
| `Name` | `string` | User-visible title |
| `Category` | `string` | `DM`, `Narrator`, `Debate`, `Interview`, `Player`, etc. |
| `Description` | `string` | Short explanation of what the prompt is for |
| `PromptText` | `string` | The actual system prompt text |
| `Tags` | `string` | Simple comma-separated tags for MVP |
| `IsBuiltIn` | `bool` | Distinguishes shipped samples from user-created ones |
| `ParentPromptSampleId` | `string` | Optional pointer when a user duplicates or forks an existing sample |
| `SourceLabel` | `string` | Optional freeform note such as `Saved from DM in Room 3` |
| `SortOrder` | `int` | UI ordering |
| `CreatedAt` | `DateTimeOffset` | Audit/useful metadata |
| `UpdatedAt` | `DateTimeOffset` | Audit/useful metadata |

Guidance:

- Keep tags simple for MVP. A normalized tag table is unnecessary.
- `ParentPromptSampleId` is optional but useful for variant tracking without building full version control.
- `SourceLabel` is deliberately text only so the library stays independent from rooms and agents.

### Why Not Put This In AppSettings

`AppSettings` currently behaves like a single-record configuration object for theme and TTS settings. A prompt library is not a single settings blob; it is a growing catalog. It should not be serialized into one wide settings row.

### Why Not Add `PromptSampleId` To AgentConfig

That would create a live dependency and raise bad questions immediately:

- what happens when the sample changes later?
- is the room supposed to track the original sample forever?
- should the UI show drift between agent prompt and source prompt?

For MVP, those questions are self-inflicted complexity. Copy-on-apply avoids them.

---

## Persistence Strategy

Use the same SQLite database and additive migration approach already used by the hybrid app.

### New Entity

Add `PromptSampleEntity` in `AgentGroupChat.Infrastructure/Entities/Entities.cs`.

Suggested table name:

- `PromptSamples`

Suggested indexes:

- `Name`
- `Category`
- `IsBuiltIn`
- optionally `UpdatedAt`

### New Repository Contract

Add a separate repository interface rather than expanding `ISettingsRepository`.

Recommended interface:

```csharp
public interface IPromptSampleRepository
{
    Task<IReadOnlyList<PromptSample>> GetAllAsync();
    Task<PromptSample?> GetAsync(string id);
    Task SaveAsync(PromptSample sample);
    Task DeleteAsync(string id);
    Task SeedBuiltInsIfEmptyAsync();
}
```

Why a separate repository is better than extending `ISettingsRepository`:

- it keeps settings responsibilities narrow
- it matches the existing repo pattern used for rooms, memory, transcript, logs, and archives
- it makes future prompt-library syncing or export easier

### Migration Strategy

Update `DataMigrator` to create the `PromptSamples` table if missing.

Built-in sample seeding should happen after schema creation and should be idempotent.

Recommended seed policy:

- insert built-ins only when there are no prompt samples yet
- do not overwrite user-edited built-ins
- if future built-ins change, ship them as new IDs rather than mutating existing user-local records

---

## Suggested Built-In Samples

MVP starter categories:

- DM / narrator
- Optimist
- Skeptic / devil's advocate
- Interviewer
- Worldbuilding assistant
- Tactical planner
- Soft-spoken companion

Each built-in sample should include:

- a short friendly description
- a clear role label
- prompt text that is actually usable without more editing

This is the key onboarding value: a first-time user should be able to build a room quickly without writing every prompt from scratch.

---

## UI Integration Points In The Current Repo

### New Page

Best first home:

- `AgentGroupChat.Hybrid/Components/Pages/PromptLibrary.razor`

Why:

- the library is global, not room-scoped
- it deserves its own navigation surface
- it should be usable before a room is configured

### Navigation

Add a nav entry in the main layout near:

- Setup Guide
- Rooms
- Chat
- Logs
- AI Models

The closest conceptual neighbor is `AI Models`, because both are reusable catalogs.

### Rooms Page Hooks

Add lightweight agent-level actions later in `Rooms.razor`:

- `Apply From Library`
- `Save Prompt to Library`

These actions should open a dialog or panel, not embed the full library editor directly into every agent card.

---

## State Management

Do not overload `AppState` with prompt-library responsibilities unless the team explicitly wants a single global catalog state object.

A cleaner split is:

- `PromptLibraryState` for prompt samples
- existing `AppState` remains focused on app settings and AI catalogs

Recommended responsibilities of `PromptLibraryState`:

- load all samples
- save a sample
- delete a sample
- notify UI when the catalog changes

This mirrors the existing `AppState` and `RoomState` patterns without bloating them.

---

## File-By-File Impact

### Core

- add `Models/Domain/PromptSample.cs`
- add `Services/Interfaces/IPromptSampleRepository.cs`

### Infrastructure

- add `PromptSampleEntity` to `Entities.cs`
- add `DbSet<PromptSampleEntity>` to `AppDbContext.cs`
- add mapping methods in `EntityMapper.cs`
- add `PromptSampleRepository.cs`
- update `DataMigrator.cs` to create the table and seed built-ins

### Hybrid

- add `State/PromptLibraryState.cs`
- add `Components/Pages/PromptLibrary.razor`
- add a nav link in the main layout
- later add small apply/save hooks inside `Rooms.razor`

---

## Delivery Order

1. Add prompt-sample domain model, entity, repository, and migration.
2. Seed a small built-in library.
3. Add a dedicated Prompt Library page for browsing and editing.
4. Add `Apply to Agent` flow from the library.
5. Add `Save Prompt to Library` from agent cards in Rooms.
6. Tune categories, starter copy, and basic search/filter behavior.

---

## Validation Plan

1. Create a built-in sample and verify it appears in the library on a fresh install.
2. Save a user-created prompt sample and verify it persists across app restarts.
3. Duplicate a sample and verify the new entry preserves the original prompt text but has a new ID.
4. Apply a library entry to an agent and verify `AgentConfig.SystemPrompt` changes while the library record remains unchanged.
5. Save a current DM prompt to the library, change the agent prompt, then reapply the saved original and confirm the revert workflow works.
6. Verify deleting a user-created sample does not affect any existing room or agent prompts.

---

## Decisions

- Prompt samples are a standalone global catalog, not room data.
- Prompt samples should live in their own table and repository.
- Applying a sample should copy text into an agent prompt, not create a live binding.
- Built-in and user-created samples should share one UI surface.
- The Prompt Library should be its own page, not buried inside Rooms.

---

## Future Extensions

- export and import prompt packs
- richer tag filtering
- prompt favorites
- prompt compare and diff view
- attach example output or usage notes
- cloud sync once the app has an online account model

The MVP does not need any of those to be useful.