# Plan: Privileged Actions for NPC Lifecycle and Temporary Agent Suspension

This feature lets one designated privileged agent, typically the DM or narrator, manage two kinds of room-level lifecycle changes:

- temporary NPC agents that join for a longer stretch of play and later leave
- temporary suspension of permanent agents who are off-scene, asleep, unconscious, separated, or otherwise unavailable for a few rounds

The important MVP goal is not just “change who participates,” but “fit lifecycle changes into the current runner, prompt transport, room persistence, and memory flow without introducing mid-round inconsistencies.”

## MVP Recommendations

These are the two design constraints that make the feature fit the current architecture cleanly:

- Keep the existing two-block transport contract intact: `<reply>` and `<future_note>` remain the only top-level response blocks.
- Require the privileged agent to be the last permanent enabled agent in turn order for MVP.

Why these matter:

- `TurnExecutor` currently instructs models to return exactly two XML blocks and parses around that assumption.
- `ConversationRunner` snapshots `enabledAgents` once per run invocation and can pause or resume mid-round, so mid-round room mutations are risky.
- The runner also prefetches the next agent during speech, which makes immediate same-round roster changes harder to reason about.

For MVP, NPC spawn and dismiss actions should therefore be staged during the privileged agent’s turn and applied only after the current round completes.

---

## Constraints

- Max **2 concurrent active NPCs** per room by default via `MaxConcurrentNpcs`
- Only the designated privileged agent can request spawn or dismiss actions
- Only the designated privileged agent can request temporary suspension or resumption of permanent agents
- NPCs use room-level defaults for model, voice, token budget, and compaction budget, but those defaults are copied into the `AgentConfig` at creation time
- NPC memory uses the existing memory system; no special archive or resurrection logic is required for MVP
- Spawn and dismiss actions take effect **next round**, never mid-round
- Suspension and resume actions also take effect **next round**, never mid-round
- Active NPC names must be unique within a room, case-insensitively
- For MVP, the privileged agent should be validated as the last permanent enabled agent in turn order
- Suspended permanent agents remain part of the room but are excluded from the active turn roster until resumed or until their suspension expires

---

## Model Changes

### AgentConfig — new fields

| Field | Type | Default | Purpose |
|---|---|---|---|
| `IsNpc` | `bool` | `false` | Distinguishes spawned NPCs from permanent room agents |
| `SpawnedByAgentId` | `string` | `""` | Records which privileged agent created the NPC |
| `IsTemporarilySuspended` | `bool` | `false` | Removes a permanent agent from the active roster without treating them as permanently disabled |
| `SuspendedByAgentId` | `string` | `""` | Records which privileged agent suspended the agent |
| `SuspendedUntilRound` | `int?` | `null` | Inclusive last round the agent should remain suspended; `null` means until explicitly resumed |
| `SuspensionReason` | `string` | `""` | Off-scene reason shown to prompts, logs, and UI |

Notes:

- A separate `NpcGender` field is not required for MVP if the selected `TtsVoice` is already copied into the spawned `AgentConfig`.
- The room-level NPC defaults are templates for newly spawned NPCs only; existing NPCs keep the values they were created with.
- The suspension fields only apply to permanent agents. NPC lifecycle continues to use spawn and dismiss.

### RoomConfig — new fields

| Field | Type | Default | Purpose |
|---|---|---|---|
| `EnablePrivilegedActions` | `bool` | `false` | Master toggle |
| `EnableNpcSpawning` | `bool` | `false` | NPC sub-toggle |
| `PrivilegedAgentId` | `string` | `""` | The permanent agent allowed to manage NPCs |
| `NpcModelId` | `string` | `""` | Model for newly spawned NPCs |
| `NpcDefaultMaleVoice` | `string` | `""` | Default voice for spawn requests tagged `gender="male"` |
| `NpcDefaultFemaleVoice` | `string` | `""` | Default voice for spawn requests tagged `gender="female"` |
| `NpcMaxTokens` | `int?` | `null` | Max tokens override for NPC turns |
| `NpcCompactionBudget` | `int` | `300` | Compaction budget for NPCs |
| `NpcBaseInstructions` | `string` | `""` | Shared system-prompt instructions applied to all spawned NPCs |
| `MaxConcurrentNpcs` | `int` | `2` | Active NPC cap |

Resolution policy:

- If `NpcModelId` is blank, fall back to the privileged agent’s model or a room-level default.
- If a gendered NPC voice is blank, fall back to the global Kokoro default voice.
- If `PrivilegedAgentId` is blank or points to a disabled agent, privileged actions are treated as off.

---

## Transport Contract

To avoid fighting the existing `TurnExecutor` transport format, keep exactly two top-level XML blocks and place any management actions inside a nested `<privileged_actions>` section within `<future_note>`.

### Spawn Example

```xml
<reply>The caravan guard steps forward, hand on his sword and watches the party closely.</reply>
<future_note>
[Listening For]
- whether the party trusts the caravan

<privileged_actions>
<spawn_npc name="Captain Aldric" gender="male">Gruff but loyal caravan guard. Protective of his crew. Speaks bluntly, avoids small talk. Has a scar across his left cheek from a bandit raid years ago.</spawn_npc>
</privileged_actions>
</future_note>
```

### Dismiss Example

```xml
<reply>Aldric tips his hat, thanks the group, and turns his horse toward the eastern pass.</reply>
<future_note>
[Considering]
- transition back to core party travel

<privileged_actions>
<dismiss_npc name="Captain Aldric">Caravan delivered safely. Aldric continues east alone.</dismiss_npc>
</privileged_actions>
</future_note>
```

### Replacement Example

This should be allowed in one turn so the DM can free a slot and fill it immediately:

```xml
<reply>The priestess stays behind to tend the wounded, while a scout volunteers to guide the party north.</reply>
<future_note>
<privileged_actions>
<dismiss_npc name="Sister Mara">She remains in town to care for refugees.</dismiss_npc>
<spawn_npc name="Tarin Vale" gender="male">Young ranger scout. Alert, dry humor, practical, speaks in short observations.</spawn_npc>
</privileged_actions>
</future_note>
```

### Suspend Example

```xml
<reply>Rowan settles in beside the embers, wraps up in a blanket, and falls asleep while Mira keeps watch.</reply>
<future_note>
<privileged_actions>
<suspend_agent name="Rowan Vale" rounds="2">Sleeping through the next two watch rounds.</suspend_agent>
</privileged_actions>
</future_note>
```

### Resume Example

```xml
<reply>Rowan wakes at dawn, rubs the sleep from his eyes, and rejoins the group.</reply>
<future_note>
<privileged_actions>
<resume_agent name="Rowan Vale">Wakes at dawn and returns to the scene.</resume_agent>
</privileged_actions>
</future_note>
```

Rules:

- `<spawn_npc>` requires `name` and `gender` attributes; the body is the personality and description payload.
- `<dismiss_npc>` requires `name`; the body is a short dismissal reason.
- `<suspend_agent>` requires `name`; the body is a brief reason. The optional `rounds` attribute means “keep this agent out for the next N rounds, then auto-resume them before the following round begins.” If omitted, the suspension remains in force until `<resume_agent>` is used.
- `<resume_agent>` requires `name`; the body is a brief re-entry reason or transition.
- `<suspend_agent>` and `<resume_agent>` apply only to permanent agents with `IsNpc = false`.
- Do not allow suspending the privileged agent in MVP.
- Allow at most two privileged lifecycle actions per turn in MVP.
- Apply actions in this order: resumes, NPC dismisses, permanent-agent suspensions, NPC spawns.
- After parsing, remove the `<privileged_actions>` block before persisting the actual future note into agent short memory.
- Rejected actions should be logged explicitly and should not break the visible reply.

---

## NPC System Prompt Generation

When an NPC is spawned, auto-generate its `SystemPrompt` by composing:

```
{NpcBaseInstructions}

Character Name: {name}
Character Description:
{personality description from spawn tag}
```

Recommended base instructions should include:

- Stay in character and respond only as this NPC.
- Follow the scene being driven by the privileged narrator and the current room state.
- Do not introduce privileged actions or management tags.
- Do not try to act as narrator, adjudicator, or DM.
- Make concrete contributions from this NPC’s own perspective rather than summarizing the whole scene.

The DM supplies flavor. The system supplies behavioral guardrails.

---

## Identity and Validation Rules

These rules are worth making explicit because name matching and lifecycle changes are otherwise ambiguous:

- Active NPC names must be unique, case-insensitively.
- A dismiss action only targets active NPCs with `IsNpc = true`.
- A suspend or resume action only targets permanent agents with `IsNpc = false`.
- If a spawn tries to reuse the name of an active NPC, reject it and log the rejection.
- If a spawn uses the name of a previously dismissed NPC, treat it as a brand-new NPC for MVP rather than trying to resurrect memory or state.
- Do not allow the privileged agent selector to target a spawned NPC.
- Do not allow the privileged agent to suspend itself.
- If a suspend targets an already suspended permanent agent, treat it as an update only if the new request extends or clarifies the suspension; otherwise reject it for MVP.
- If a resume targets a permanent agent who is not currently suspended, reject it and log the rejection.

---

## Color and Voice Assignment

When an NPC is created:

- Assign `TtsVoice` immediately from the room defaults based on `gender`.
- Assign chat colors from the first unused `AgentColorPresets` entry.
- Copy `NpcModelId`, `NpcMaxTokens`, and `NpcCompactionBudget` into the new `AgentConfig`.

This avoids hidden dependency on room defaults after the NPC is already live.

---

## Turn Order and Lifecycle Timing

Because the runner currently snapshots `enabledAgents` once and may resume a partial round later, lifecycle changes must be staged and applied at the round boundary. For this feature to work across multi-round runs, `ConversationRunner` should be updated to recompute the active roster at the start of each round iteration rather than once per `RunAsync` call.

For MVP:

- The privileged agent should be the last permanent enabled agent.
- Any spawn or dismiss requested during the privileged turn is queued as a pending action.
- Any suspension or resume requested during the privileged turn is also queued as a pending action.
- Pending actions are applied after the round’s transcript write and memory refresh finish, but before the next round begins.
- Before each new round iteration, automatically clear expired suspensions whose `SuspendedUntilRound` has passed and rebuild the active roster.
- Spawned NPCs participate starting next round.
- Suspended permanent agents are removed from the active roster starting next round.
- Resumed or auto-resumed permanent agents rejoin starting next round.
- Dismissed NPCs remain eligible for the current round if they already had a slot in the snapshotted roster, but because the privileged agent is last, that case is eliminated in MVP.

This is the cleanest fit with the current prefetching and pause/resume flow.

---

## Prompt Visibility and Off-Scene Awareness

Temporary suspension needs prompt support or the remaining active agents may still improvise the sleeping or absent character back into the scene.

### Inactive Participants Section

Add an explicit section to active agents’ prompts whenever any permanent agents are temporarily suspended:

```xml
<inactive_participants>
- Rowan Vale: asleep during the current watch and not participating this round.
</inactive_participants>
```

Guidance should tell active agents:

- do not address suspended participants as if they are currently present
- do not put dialogue in their mouths
- do not assume they directly witnessed the current round unless they are explicitly resumed

### Resume Catch-Up Notice

When a suspended agent returns, their first prompt back should include a one-turn reminder that they were off-scene and did not directly witness missed rounds.

Example:

```xml
<offscene_notice>
You were inactive for rounds 6-7 because you were asleep during the night watch. You did not directly witness those turns. React from your current knowledge and what has now been conveyed to you, not as if you personally observed everything that happened.
</offscene_notice>
```

This is important for preventing the resumed agent from hallucinating direct awareness of the rounds they missed.

---

## DM Prompt Injection

When NPC spawning is enabled, `PromptComposer.BuildAgentPrompt` should add an NPC management section to the privileged agent’s prompt only.

Suggested content:

```xml
<npc_management>
You may request NPC lifecycle changes inside <future_note> using a nested <privileged_actions> block.
Active NPCs: Captain Aldric, Sister Mara.
Active slots used: 2/2.

Rules:
- Only request a new NPC if they are likely to stay involved for multiple rounds.
- For brief one-scene NPCs, narrate them directly inside <reply> instead of spawning them.
- Use at most two privileged lifecycle actions in a turn.
- If all slots are full, dismiss an NPC before spawning another.
- Use exact active NPC names when dismissing.
- To temporarily remove a permanent character from the active roster, use <suspend_agent name="Name" rounds="N">reason</suspend_agent> inside <privileged_actions>.
- To bring a suspended permanent character back, use <resume_agent name="Name">reason</resume_agent> inside <privileged_actions>.
- Do not suspend yourself.
- Do not place management tags in <reply>.
</npc_management>
```

Non-privileged agents never see this section.

---

## Processing Flow

After the privileged agent’s turn completes:

1. Parse the privileged action block from the returned `futureNote` or from the raw LLM content.
2. Validate the requested actions:
   - privileged actions enabled
   - current agent is the designated privileged agent
   - name uniqueness
   - slot capacity
   - allowed action count for the turn
   - valid permanent-agent suspension targets
   - valid permanent-agent resume targets
3. Strip the action block from the note that gets saved into `MemoryKind.AgentShort`.
4. Queue valid actions for round-end application.
5. At round end, apply queued actions in this order:
   - resumes
   - dismisses
   - durable-memory note for dismissals
   - clear dismissed NPC short memory by saving empty content for `MemoryKind.AgentShort`
   - permanent-agent suspensions
   - spawns
   - save room
6. Before the next round starts, recompute active participants from the updated room state rather than reusing the original run-level snapshot.
7. When a permanent agent is resumed, queue a one-turn off-scene reminder so their next prompt acknowledges they missed prior rounds.
8. Log the final applied operations.

Important detail:

- The visible reply and transcript should never include privileged action markup.
- Invalid actions should degrade gracefully to “narrative only,” not break the run.

---

## Dismiss Memory Strategy

The plan should prefer a lightweight durable-memory note rather than direct special-case persistence. That fits the existing summarization model better.

Example durable note:

> Captain Aldric departed after safely escorting the caravan to Eastmarch and remains on good terms with the party.

This should be appended through the existing memory flow rather than inventing a new NPC archive for MVP.

---

## UI (Rooms.razor)

Under room settings, add a privileged actions section:

```
[Toggle] Enable Privileged Actions
  └─ [Dropdown] Privileged Agent (permanent enabled agents only)
  └─ [Helper text] For MVP, the privileged agent should be the last enabled permanent agent.
  └─ [Toggle] Generate NPC Models as Agents
       └─ [Dropdown] NPC Model
       └─ [Dropdown] Default Male Voice
       └─ [Dropdown] Default Female Voice
       └─ [NumericField] NPC Max Tokens
       └─ [NumericField] NPC Compaction Budget
       └─ [TextField] NPC Base Instructions (Lines="6")
       └─ [Read-only] Active NPC count: 0 / 2
```

UI notes:

- Only show the nested NPC settings when both toggles are on.
- Exclude spawned NPCs from the privileged-agent selector.
- Show suspended permanent agents with a clear status chip such as `Suspended`, the reason, and either `until round N` or `until resumed`.
- If possible, hide dismissed NPCs from the default room editor list or group them under a collapsed “Inactive NPCs” section to avoid long-term clutter.

---

## Schema Migration

`DataMigrator.EvolveSchemaAsync` adds:

### Agents table

- `IsNpc` — `INTEGER NOT NULL DEFAULT 0`
- `SpawnedByAgentId` — `TEXT NOT NULL DEFAULT ''`
- `IsTemporarilySuspended` — `INTEGER NOT NULL DEFAULT 0`
- `SuspendedByAgentId` — `TEXT NOT NULL DEFAULT ''`
- `SuspendedUntilRound` — `INTEGER`
- `SuspensionReason` — `TEXT NOT NULL DEFAULT ''`

### Rooms table

- `EnablePrivilegedActions` — `INTEGER NOT NULL DEFAULT 0`
- `EnableNpcSpawning` — `INTEGER NOT NULL DEFAULT 0`
- `PrivilegedAgentId` — `TEXT NOT NULL DEFAULT ''`
- `NpcModelId` — `TEXT NOT NULL DEFAULT ''`
- `NpcDefaultMaleVoice` — `TEXT NOT NULL DEFAULT ''`
- `NpcDefaultFemaleVoice` — `TEXT NOT NULL DEFAULT ''`
- `NpcMaxTokens` — `INTEGER`
- `NpcCompactionBudget` — `INTEGER NOT NULL DEFAULT 300`
- `NpcBaseInstructions` — `TEXT NOT NULL DEFAULT ''`
- `MaxConcurrentNpcs` — `INTEGER NOT NULL DEFAULT 2`

No separate NPC table is required for MVP.

---

## Observability

Recommended log events:

- `PrivilegedActions.Requested`
- `PrivilegedActions.Rejected`
- `NpcAgent.SpawnQueued`
- `NpcAgent.SpawnApplied`
- `NpcAgent.DismissQueued`
- `NpcAgent.DismissApplied`
- `PermanentAgent.SuspendQueued`
- `PermanentAgent.SuspendApplied`
- `PermanentAgent.ResumeQueued`
- `PermanentAgent.ResumeApplied`
- `PermanentAgent.ResumeAutoApplied`

The key operational need is to know:

- what the privileged agent requested
- what was rejected and why
- what actually changed in the room roster

---

## Validation Plan

1. Transport validation
- Confirm the privileged agent still returns only `<reply>` and `<future_note>` as top-level blocks.
- Confirm nested `<privileged_actions>` content is removed before future-note persistence.

2. Roster validation
- Confirm an NPC spawned by the privileged agent does not speak until the next round.
- Confirm a dismissed NPC is absent from the next round.
- Confirm duplicate active names are rejected.

3. Slot validation
- Confirm the active NPC cap is enforced.
- Confirm a dismiss-plus-spawn replacement in the same privileged turn works in dismiss-then-spawn order.

4. Suspension validation
- Confirm a suspended permanent agent is removed from the active roster for the specified rounds.
- Confirm a suspension without `rounds` remains active until an explicit resume action is applied.
- Confirm the privileged agent cannot suspend itself.

5. Prompt validation
- Confirm only the privileged agent sees NPC management instructions.
- Confirm active agents see the inactive-participants section when a permanent agent is suspended.
- Confirm a resumed agent receives an off-scene reminder on their first round back.
- Confirm non-privileged agents and spawned NPCs never receive privileged-action instructions.

6. Pause and prefetch validation
- Confirm the feature behaves correctly with `PauseAfterEveryReply` on.
- Confirm requiring the privileged agent to be last avoids mid-round resume anomalies.
- Confirm active-roster recomputation between iterations allows “next round” lifecycle changes to apply even during multi-round runs.

---

## Implementation Phases

1. **Schema** — Add new room and agent fields to domain models, entities, mappers, and migration
2. **Prompting** — Add the privileged management section to the privileged agent prompt only
3. **Transport-safe parsing** — Parse nested `<privileged_actions>` from `futureNote` and strip them before short-memory persistence
4. **Active-roster recomputation** — Refactor `ConversationRunner` to derive the active roster at the start of each round iteration from current room state rather than from a single run-level snapshot
5. **Round-end lifecycle application** — Queue lifecycle actions during the privileged turn and apply them only after round completion
6. **NPC creation** — Build `AgentConfig` instances from room defaults plus DM-provided character flavor
7. **NPC dismissal** — Disable the NPC, clear its short memory via `SaveAsync(..., string.Empty)`, and add a durable departure note
8. **Permanent-agent suspension** — Apply suspension state to permanent agents, preserve their memory, and remove them from the active roster until resumed
9. **Prompt support** — Add inactive-participants and off-scene reminder sections so sleeping or absent agents are not hallucinated back into scene presence
10. **UI** — Add the privileged actions configuration section, active-count display, suspended-agent state, and privileged-agent validation hints
11. **Logging** — Emit request, rejection, queue, auto-resume, and applied lifecycle events

---

## Final MVP Decisions

These decisions are now considered locked for the first implementation pass:

- Privileged actions are emitted only inside `<future_note><privileged_actions>...</privileged_actions></future_note>`.
- Lifecycle changes are queued during the privileged turn and applied only at the round boundary.
- The privileged agent must be the last permanent enabled agent for MVP.
- Permanent-agent suspension uses dedicated suspension fields, not `IsEnabled`.
- Dismissed NPCs are disabled and retained for history; they are not deleted.
- Suspended permanent agents keep their existing memory; they are simply removed from the active roster.
- The runner must recompute the active roster at the start of each round iteration.
- A resumed permanent agent receives a one-turn off-scene reminder.

If any of these decisions changes, the implementation should be re-planned before coding because they directly affect runner control flow.

---

## File-by-File Implementation Map

### Domain Models

- `AgentGroupChat.Core/Models/Domain/AgentConfig.cs`
   - Add NPC and suspension fields
- `AgentGroupChat.Core/Models/Domain/RoomConfig.cs`
   - Add privileged action and NPC template settings

### Persistence Layer

- `AgentGroupChat.Infrastructure/Entities/Entities.cs`
   - Add new `RoomEntity` and `AgentEntity` columns
- `AgentGroupChat.Infrastructure/Data/EntityMapper.cs`
   - Map all new room and agent fields both directions
- `AgentGroupChat.Infrastructure/Migration/DataMigrator.cs`
   - Add missing room and agent columns for existing databases
- `AgentGroupChat.Infrastructure/Data/RoomRepository.cs`
   - No schema redesign expected, but verify save/load continues to hydrate agent collections correctly with the new fields

### Runtime and Orchestration

- `AgentGroupChat.Core/Services/TurnExecutor.cs`
   - No transport redesign; keep the two-block contract untouched
- `AgentGroupChat.Core/Services/PromptComposer.cs`
   - Add privileged management instructions for the privileged agent only
   - Add inactive-participants section for active agents when any permanent agents are suspended
   - Add off-scene notice for a resumed agent’s first prompt back
- `AgentGroupChat.Core/Services/ConversationRunner.cs`
   - Recompute active roster per round iteration
   - Parse and strip nested privileged actions from the privileged agent’s note
   - Queue lifecycle operations during the privileged turn
   - Apply queued operations at round end in the defined order
   - Auto-resume agents whose suspension window has expired
   - Inject durable departure notes for dismissed NPCs
   - Preserve suspended agents’ memory and clear only dismissed NPC short memory

### UI and State

- `AgentGroupChat.Hybrid/Components/Pages/Rooms.razor`
   - Add privileged actions UI block
   - Add privileged-agent selection and NPC defaults
   - Show active NPC count and permanent-agent suspension state
- `AgentGroupChat.Hybrid/State/RoomState.cs`
   - Verify room reload/save flows notify correctly after privileged configuration changes

### Logging and Inspection

- Existing log service usage in `ConversationRunner.cs`
   - Emit requested, rejected, queued, applied, and auto-resume events
- Optional follow-up UI work in logs or rooms view
   - Show suspended status and inactive NPC history more clearly if the base room editor becomes crowded

---

## Recommended Delivery Order

Implement this in four passes so the risky runtime seams are validated before the UI grows:

### Pass 1: Data and Config Plumbing

- Add all room and agent fields
- Add entity mapping and migration support
- Build and verify save/load round-trips

Exit criteria:

- Existing rooms still load
- New settings persist across restart
- Existing databases evolve without data loss

### Pass 2: Runner and Prompt Core

- Add active-roster recomputation per round
- Add privileged prompt instructions
- Add parsing and stripping of nested privileged actions
- Add round-end lifecycle queue and application

Exit criteria:

- Multi-round runs respect next-round lifecycle changes
- Suspended agents stop speaking next round
- Spawned NPCs join next round
- No privileged markup leaks into visible transcript or short memory

### Pass 3: Off-Scene Behavior Quality

- Add inactive-participants prompt section
- Add resumed-agent off-scene reminder
- Add durable dismissal notes and auto-resume behavior

Exit criteria:

- Awake agents stop addressing sleeping agents as present
- Resumed agents do not act like they directly observed missed rounds
- Auto-resume by round count works reliably

### Pass 4: Rooms UI and Operator Clarity

- Add privileged settings UI
- Add suspension state display and active NPC count
- Add validation hints around privileged-agent ordering

Exit criteria:

- A room can be fully configured without manual DB edits
- Suspended and inactive states are understandable from the editor

---

## Definition of Done

The feature is ready for MVP use when all of the following are true:

- A privileged agent can spawn an NPC and that NPC joins on the next round.
- A privileged agent can dismiss an NPC and that NPC no longer participates on the next round.
- A privileged agent can suspend a permanent agent for explicit rounds or until resumed.
- Suspended permanent agents are excluded from the active roster without losing their memory.
- Active agents receive prompt guidance that suspended participants are off-scene.
- Resumed agents receive one-turn catch-up guidance and do not hallucinate direct observation.
- The runner supports these changes during multi-round runs, not just single-round runs.
- Existing rooms and existing databases still load and save correctly after migration.
- No privileged-action markup appears in visible chat, transcript entries, or persisted short memory.

---

## What's Out of Scope for MVP

- Reviving a previously dismissed NPC with preserved memory
- NPC-specific archive or lifecycle history table
- User-issued spawn or dismiss actions
- User-issued suspension or resume actions
- Dynamic NPC personality rewrites after creation
- Mid-round roster changes
- Multiple privileged agents
- NPC-to-NPC privileged orchestration
