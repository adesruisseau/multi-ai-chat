# Plan: Manual Scene Image Generation and Agent Appearance Profiles

This feature lets a user manually generate an image of the current or just-completed scene when the moment feels worth capturing.

The MVP goal is not "full character art pipeline"; it is "turn the room's current narrative state into an editable prompt, send it to a selected image backend, and persist the result without disturbing the conversation runner."

## MVP Recommendations

These are the design constraints that make the feature fit the current architecture cleanly:

- Keep scene-image generation **manual only** for MVP. Do not auto-generate at the end of every round.
- Introduce a separate **image provider catalog** parallel to AI Models. Do not overload the existing text model records.
- Start with a **local-first transport**, with ComfyUI as the first supported provider.
- Add one persisted `AppearanceSummary` field to agents before any structured visual taxonomy.
- Build image prompts from shared room memory, recent transcript turns, room topic, active participants, and appearance summaries.
- Let the user review and edit the drafted prompt before generation.
- Keep `LlmClient` text-only; add a separate `ImageClient` and `SceneImageService` for image work.

Why these matter:

- `LlmClient` and `LlmRequestSettings` are currently text-completion shaped and are already used by the runner, summarizer, and retrieval flow.
- The existing AI Models page already separates connections from model catalog entries, which gives a clean pattern to mirror for image generation.
- Adding visual data to `AgentConfig` avoids repeating bulky tags in every per-turn agent prompt.
- Manual invocation keeps latency, cost, and noise under control while still enabling the "epic moment" use case.

---

## Non-Goals for MVP

- Automatic image generation every round
- Per-turn visual XML tags like `<gender>` or `<hair_color>` inside every agent response prompt
- Character sheet or portrait generation as a separate workflow
- Image-to-image, inpainting, control nets, or multi-shot continuity enforcement
- Feeding generated images back into agent prompts or memory summarization

---

## Product Shape

### User Flow

1. The user finishes or pauses a scene in Chat.
2. The user clicks `Generate Scene Image`.
3. The app builds a scene dossier from recent transcript, shared room memory, active cast, and agent appearance summaries.
4. The app drafts a prompt:
   - first by deterministic composition
   - optionally by a text-model rewrite when a prompt-drafter model is available
5. The app opens a dialog with:
   - selected image model
   - editable positive prompt
   - editable negative prompt
   - size or preset controls
   - `Generate` button
6. The app sends the prompt to the chosen image provider.
7. The generated image is saved locally and shown in the UI, with metadata persisted for later viewing.

### Prompt Sources

Use these in order of value:

- the most recent completed round transcript
- current shared room memory
- room name and topic
- currently active participants
- each active participant's `AppearanceSummary` when available

Use these cautiously or not at all for MVP:

- durable memory, only as a small optional excerpt if the scene needs stable world context
- private agent memories, which should stay private
- the full transcript beyond the recent scene window

---

## Why Not Prompt Tags First?

The idea of `<gender>`, `<race>`, `<hair_color>`, and similar tags is directionally good, but the first place to put that data is the agent record, not the recurring turn prompt.

Problems with prompt-first tags:

- They add repeated token cost across every turn and every agent.
- They blur the boundary between behavioral prompting and rendering metadata.
- They make appearance harder to edit, persist, validate, and reuse in non-chat contexts.

Recommended first step:

- Add persisted visual fields to the agent model.
- Use those fields only when generating image prompts.
- If future NPC spawning needs richer appearance control, add a one-time `<appearance>` block to spawn payloads rather than repeating tags every turn.

---

## Data Model Changes

### AgentConfig

Add a single MVP field:

| Field | Type | Default | Purpose |
|---|---|---|---|
| `AppearanceSummary` | `string` | `""` | Natural-language visual description used by the image prompt pipeline |

Guidance:

- Keep it in plain language, not rigid tags.
- Aim for 1 to 4 sentences.
- Focus on stable visual traits, clothing, silhouette, and notable features.

Possible future structured fields, but not MVP:

- `GenderPresentation`
- `SpeciesOrAncestry`
- `Build`
- `HairDescription`
- `SkinTone`
- `EyeDescription`
- `DistinctiveFeatures`
- `TypicalClothing`

### RoomConfig

Add room-level image defaults:

| Field | Type | Default | Purpose |
|---|---|---|---|
| `EnableSceneImageGeneration` | `bool` | `false` | Master room toggle so image controls can stay dormant until the feature is wanted in that room |
| `SceneImageModelId` | `string` | `""` | Default image model for this room |
| `SceneImageStyleNotes` | `string` | `""` | Optional room-level art direction or cinematography note |
| `SceneImageNegativePrompt` | `string` | `""` | Default negative prompt or prompt exclusions for this room |

Rationale:

- The image provider catalog should be global.
- The room should choose how that provider is used in this specific campaign or scene style.
- A room-level master toggle is a safe forward-planning seam for later manual, guided, or automated generation modes.

### New Domain Types

Add separate image-generation catalog and asset records instead of overloading text-model types.

#### ImageConnection

| Field | Type | Purpose |
|---|---|---|
| `Id` | `string` | Primary key |
| `Name` | `string` | Display name |
| `Transport` | `string` | `ComfyUI`, `OpenAI Images`, `Stability`, later others |
| `Endpoint` | `string` | Base endpoint |
| `ApiKey` | `string` | Optional secret |
| `SortOrder` | `int` | UI ordering |

#### ImageModel

| Field | Type | Purpose |
|---|---|---|
| `Id` | `string` | Primary key |
| `Name` | `string` | Display name |
| `ConnectionId` | `string` | FK to image connection |
| `ModelId` | `string` | Provider-specific model or preset id |
| `WorkflowId` | `string` | Optional workflow or profile id, especially useful for ComfyUI |
| `Width` | `int` | Default image width |
| `Height` | `int` | Default image height |
| `Steps` | `int?` | Optional provider hint |
| `GuidanceScale` | `double?` | Optional provider hint |
| `NegativePrompt` | `string` | Model-level default negative prompt |
| `Notes` | `string` | Freeform notes |
| `SortOrder` | `int` | UI ordering |

#### SceneImageAsset

| Field | Type | Purpose |
|---|---|---|
| `Id` | `long` | Primary key |
| `RoomId` | `string` | Owning room |
| `RoundNumber` | `int?` | Round snapshot reference |
| `Prompt` | `string` | Final prompt sent to provider |
| `NegativePrompt` | `string` | Final negative prompt |
| `ImageModelId` | `string` | Selected image model |
| `ImagePath` | `string` | Local file path |
| `Width` | `int` | Generated width |
| `Height` | `int` | Generated height |
| `Seed` | `string` | Provider seed or request id |
| `CreatedAt` | `DateTimeOffset` | Audit timestamp |

---

## Provider Architecture

Do not extend the existing text completion types for image generation. `LlmClient` should remain text-only.

Recommended new types:

- `ImageProvider` enum
- `ImageRequestSettings`
- `SceneImageRequest`
- `SceneImageResult`
- `ImageClient`
- `SceneImageService`

### Service Split

#### ImageClient

Responsible for raw provider HTTP interactions:

- serialize provider-specific requests
- poll async jobs if needed
- return image bytes plus metadata

#### SceneImageService

Responsible for app-side orchestration:

- resolve selected room, image model, and image connection
- build the scene dossier
- draft the prompt
- merge model defaults and room defaults
- call `ImageClient`
- save the resulting asset and file
- emit logs and status messages

### Local-First Provider

Support `ComfyUI` first.

Reasons:

- local-first matches the current power-user direction
- strong long-term flexibility
- easy to grow into workflows, style presets, and advanced controls later

Recommended ComfyUI contract for MVP:

- submit a workflow with injected positive prompt, negative prompt, seed, width, and height
- poll until completion
- download final image bytes
- save the final image locally
- store the returned prompt id or seed in metadata

### Future Cloud Providers

Add later behind the same `ImageClient` abstraction:

- OpenAI Images
- Stability
- Gemini image endpoints
- Hugging Face image backends

The UI and storage should not care which provider generated the image.

---

## Prompt Composition Pipeline

### Deterministic Scene Dossier

Build a structured internal payload from:

- room name
- room topic
- latest completed round number
- recent transcript turns, capped to the last major beat
- shared room memory
- active participants
- each participant's `AppearanceSummary`

Example internal structure:

```text
Scene:
- Rain-soaked bridge confrontation at dusk

Recent action:
- The party corners the cult courier
- The DM introduces a masked ranger on the east parapet
- Lantern light, wet stone, wind, crossbows drawn

Active visual subjects:
- Rowan: lean scout, auburn hair, leather duster, scar on left cheek
- Mira: dark braided hair, bronze skin, mail shirt under green cloak
- Masked ranger: hooded silhouette, pale bow hand, raven-feather mantle

Visual emphasis:
- tense standoff, cinematic framing, torchlight and rain reflections
```

### Prompt Drafting

Use a two-stage approach:

1. Deterministic assembly always works.
2. Optional text-model rewrite improves quality when available.

Recommended text-model selection for prompt drafting:

- room `MemoryModelId` if configured
- else room `SummarizerModelId`
- else deterministic prompt only

Important:

- If the prompt-drafter model fails, the user should still get a usable draft.
- Do not make image generation dependent on an additional LLM round-trip.

### User Editing

Before generation, expose:

- positive prompt
- negative prompt
- width and height
- selected image model

The user should always be able to override the draft before the provider call.

---

## Appearance Foundation

### MVP Agent Visual Profile

Add `AppearanceSummary` to the room editor for every agent.

Suggested field help text:

- `Visible appearance for scene art: face, build, clothing, silhouette, and distinctive details.`

Examples:

- `Lean half-elf scout with wind-tangled auburn hair, pale freckles, a weathered leather duster, and a thin scar across the left cheek.`
- `Broad-shouldered dwarf woman with dark braided hair, soot-stained mail, a green traveling cloak, and a brass lantern clipped to her belt.`

### NPC Spawning and Appearance

Do not add six new XML appearance tags to every agent turn.

If richer NPC appearance is needed later, extend spawn payloads with an optional one-time block:

```xml
<spawn_npc name="Captain Vale" gender="female">
Veteran caravan guard captain. Direct, skeptical, observant.
<appearance>
Tall, weathered, dark-skinned woman with tightly braided black hair, a scarred jawline, layered leather armor, and a crimson officer's sash.
</appearance>
</spawn_npc>
```

That keeps appearance data attached to creation events instead of bloating the full turn loop.

---

## UI Plan

### New Page: Image Models

Add a page parallel to the current AI Models page.

Sections:

- `Image Connections`
- `Image Model Catalog`

It should mirror the current AI Models UX:

- add, edit, save, and delete connections
- add, edit, save, and delete models
- test connection and model
- persist the catalog globally

### Rooms Page

Add a `Scene Images` section to room settings:

- default image model
- room style notes
- room negative prompt

### Chat Page

Add:

- `Generate Scene Image` button
- optional room-default image model display
- scene image dialog for prompt review and editing
- latest generated image preview
- later, a per-room gallery panel

Recommended trigger placement:

- near the run and stop controls
- available whenever a room is loaded and transcript or memory exist

---

## Persistence and Storage

### Database

Add tables for:

- `ImageConnections`
- `ImageModels`
- `SceneImageAssets`

Update:

- `Entities.cs`
- `EntityMapper.cs`
- `AppDbContext.cs`
- `DataMigrator.cs`

### Filesystem

Save generated image files under:

`%LOCALAPPDATA%/AgentGroupChat/scene-images/{roomId}/`

Filename pattern recommendation:

- `{yyyyMMdd-HHmmss}-{assetId}.png`

Why local files instead of blobs in SQLite:

- simpler inspection and backup
- easier future export and share flow
- better fit for larger binary payloads

---

## Logging and Failure Behavior

Image generation should log to the existing log pipeline, but it should not disrupt the conversation loop.

Required behaviors:

- connection or model test errors surface cleanly in the Image Models page
- generation errors show in the image dialog and logs
- no transcript mutation is required for MVP
- no memory write-back is required for MVP
- runner state is unaffected by image generation success or failure

---

## File-by-File Map

### Domain Models

- `AgentGroupChat.Core/Models/Domain/AgentConfig.cs`
- `AgentGroupChat.Core/Models/Domain/RoomConfig.cs`
- `AgentGroupChat.Core/Models/Domain/ImageConnection.cs`
- `AgentGroupChat.Core/Models/Domain/ImageModel.cs`
- `AgentGroupChat.Core/Models/Domain/SceneImageAsset.cs`

### Core Services

- `AgentGroupChat.Core/Services/SceneImageService.cs`
- `AgentGroupChat.Core/Services/SceneImagePromptComposer.cs`
- `AgentGroupChat.Core/Services/ImageClient.cs`
- `AgentGroupChat.Core/Models/Image/ImageTypes.cs`
- `AgentGroupChat.Core/Services/Interfaces/ISceneImageRepository.cs`
- `AgentGroupChat.Core/Services/Interfaces/ISettingsRepository.cs` or a dedicated `IImageCatalogRepository` if stricter separation is preferred

### Infrastructure

- `AgentGroupChat.Infrastructure/Entities/Entities.cs`
- `AgentGroupChat.Infrastructure/Data/AppDbContext.cs`
- `AgentGroupChat.Infrastructure/Data/EntityMapper.cs`
- `AgentGroupChat.Infrastructure/Data/SettingsRepository.cs`
- `AgentGroupChat.Infrastructure/Data/SceneImageRepository.cs`
- `AgentGroupChat.Infrastructure/Migration/DataMigrator.cs`

### Hybrid UI and State

- `AgentGroupChat.Hybrid/State/AppState.cs`
- `AgentGroupChat.Hybrid/Components/Pages/ImageModels.razor`
- `AgentGroupChat.Hybrid/Components/Pages/Rooms.razor`
- `AgentGroupChat.Hybrid/Components/Pages/Chat.razor`
- `AgentGroupChat.Hybrid/Components/Layout/MainLayout.razor`
- `AgentGroupChat.Hybrid/MauiProgram.cs`

---

## Delivery Order

1. **Appearance foundation**  
   Add `AppearanceSummary` to agent domain, entity, mapping, migration, and `Rooms.razor`.

2. **Image catalog**  
   Add image connections and image models, plus the new Image Models page and `AppState` support.

3. **Asset persistence**  
   Add `SceneImageAsset` storage, local file save rules, and repository support.

4. **Provider client**  
   Implement `ImageClient` with `ComfyUI` first and model or connection test actions.

5. **Prompt pipeline**  
   Implement deterministic dossier assembly and optional text-model prompt drafting.

6. **Chat UX**  
   Add the manual `Generate Scene Image` flow, prompt edit dialog, and preview.

7. **Room defaults**  
   Add room-level default image model and style fields.

8. **Cloud transports**  
   Add OpenAI, Stability, and later transports without changing the chat or storage surfaces.

---

## Validation Plan

### Appearance

- Save an agent with `AppearanceSummary`, reload the room, and confirm the value persists.
- Confirm that normal agent turn prompts do not grow just because appearance data exists.

### Catalog

- Add, save, reload, and delete image connections and image models.
- Test a local ComfyUI entry and confirm the test path surfaces connection errors cleanly.

### Generation

- Generate an image from a room with only transcript context.
- Generate an image from a room with transcript plus shared room memory.
- Generate an image from a room with agent appearance summaries.
- Edit the prompt before generation and confirm the saved asset stores the edited prompt.

### Persistence

- Confirm the output image file lands in the expected room folder.
- Restart the app and confirm generated assets can still be listed and opened.

### Failure Handling

- Turn off the image provider and confirm the UI reports failure without affecting chat state.
- Fail the prompt-drafting LLM path and confirm deterministic prompt fallback still works.

---

## Definition of Done

The feature is done when:

- users can configure local image providers and models from a dedicated management page
- a room can have a default image model and style guidance
- each agent can carry a reusable `AppearanceSummary`
- chat users can manually generate a scene image from current context
- the drafted prompt is user-editable before generation
- generated images are saved locally with metadata
- the image system is architecturally separate from text completion
- no per-turn visual XML tagging is required to make the MVP useful

---

## Recommended First Implementation Slice

If the goal is the smallest valuable first pass, build this exact slice:

- `AppearanceSummary` on agents
- `Image Connections` and `Image Models` catalog
- `ComfyUI` only
- `Generate Scene Image` dialog in Chat
- prompt built from recent round, shared room memory, and appearance summaries
- local file save plus metadata persistence

That gives a real, demonstrable feature without committing too early to a giant visual-tag taxonomy or cloud-specific transport decisions.