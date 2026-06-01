# Local Human Participants And Mixed Turn Order Plan

This document scopes a local-first step between the current single-machine app and any future online shared-room product.

The goal is to support rooms where:
- there may be zero human participants
- the main local user may be observer-only or intervention-only
- one or more local humans may participate as named in-room characters
- humans and AI can appear in mixed turn order
- future online work can reuse the same vocabulary instead of replacing it

This is intentionally not an auth, networking, or realtime plan. It is a room-and-transcript modeling plan for local play on one machine.

## Core Recommendation

- Do not force the main local user to be an in-room participant.
- Separate local operator identity from room participant identity.
- Keep human participants and AI agents as different domain types.
- Keep the first human-participant edit surface intentionally small: name, `IsPlayerCharacter`, TTS voice, and appearance.
- If the UI wants one combined editing surface, call it `Cast` or `Participants`, not `Agents`.
- Treat DM privilege as a capability, not as proof that someone must be in the speaking rotation.
- Do not hardcode `Player 1 always goes first` as a global rule.
- Model turn order explicitly so rooms can support sequences like `Player 1 -> Agent 1 -> Player 2 -> Agent 2 -> DM`.

## Decisions Captured

These points are now treated as the current local-first direction:

- a human room participant should start with only four editable fields: `Name`, `IsPlayerCharacter`, `TtsVoice`, and `AppearanceSummary`
- do not add any extra human-participant fields in the first pass beyond those four
- the main local user should always be able to choose a voice even when not represented as a room participant
- if one machine hosts two humans, use explicit local seat labels such as `Primary` and `Guest`
- if a human is a real room participant, that participant's appearance should feed scene image generation
- keep human participant definitions room-local only; do not introduce reusable cross-room character profiles yet
- when the entry being edited is human, hide AI-specific agent controls such as model, system prompt, max tokens, compaction budget, NPC metadata, and similar agent-only settings
- command-driven privileged or power-user behavior should stay AI-only for now, so human participants should not appear in the privileged-agent selector
- prefer a lean first pass and adjust after real usage instead of pre-modeling every possible human-role detail

## Why This Step Is Worth Doing Before Online

This local step would clarify the product shape without dragging in auth and backend complexity yet.

It would force the app to answer the right questions:
- who is merely operating the app
- who is actually a named participant in the room
- who has DM-style control privileges
- who gets a turn slot in the speaking order
- how mixed human and AI transcript identity should be represented

Those are real design problems today, even before multiplayer login work begins.

What this step would not prove:
- account systems
- invites and room membership
- realtime sync
- reconnect handling
- backend run ownership

So this is a tactical product-model step, not an infrastructure step.

## Current Constraints In The Codebase

Today the Hybrid app still assumes one generic local human speaker in several places:

- `ConversationState` adds the local human as literal speaker `You`
- `Chat.razor` appends local human turns as `You`
- `Chat.razor` resumes rounds by scanning backward until it hits `You`
- `PromptComposer` currently builds participants from enabled agents only

That means local multi-human support is not just a UI tweak. It needs a cleaner actor model.

## Product Scenarios To Support

### 1. Zero Human Participants

Example:
- AI DM
- AI Player 1
- AI Player 2
- local user only observes, pauses, resumes, or injects the occasional DM note

This scenario is important because it means the app operator cannot be assumed to be a character.

### 2. Observer Or Intervention-Only Local User

Example:
- local user watches the room unfold
- local user can submit an out-of-band note, steering hint, or correction
- local user does not appear in the main turn rotation

This is a valid room mode and should not require character description or appearance metadata.

### 3. Single Human Participant On One Machine

Example:
- one local user plays a character
- one or more AI agents share the scene
- the human has a name, player-character toggle, voice, and optional appearance profile

This is the simplest true human-participant mode.

### 4. Two Or More Local Human Participants On One Machine

Example:
- Player 1 is local human
- Player 2 is local human
- one AI DM runs the world
- one or more AI companions or NPCs also participate

This is the smallest local form of multiplayer that still stays offline.

### 5. Human DM Outside The Rotation

Example:
- the human uses the app as a DM or referee
- the human can intervene, narrate, or steer
- the human is not a player-character and does not need to speak every round

This should be supported directly, not treated as a weird edge case.

### 6. Human Participant With DM Privileges

Example:
- the same human both plays a character and has authority to pause, continue, or inject DM-style corrections

This is why DM privilege should be separate from participation mode.

## Main User Guidance

The subtle point is important: the main local user should not always be forced to have a character description, intent description, or appearance profile.

Recommended rule:

- the main local user should always be able to choose a TTS voice
- do not require a character description or intent summary for the MVP human-participant model
- appearance or visual information should only be shown if that user is represented as a room participant
- if the main local user is observer-only or intervention-only, their profile can stay minimal

That gives the right behavior across all room shapes.

### Suggested Split

#### Local Operator Profile

This is the person using the app on the current machine.

Recommended fields:
- display name, optional
- preferred TTS voice
- maybe a local accent color later if useful for transcript readability

Not required here:
- character bio
- in-world description
- appearance summary
- turn order slot

#### Human Participant Profile

This exists only when a named human is part of the room itself.

Recommended fields:
- `Name`
- `IsPlayerCharacter`
- `TtsVoice`
- `AppearanceSummary`

For MVP, keep it that small.

Questions such as observer-only, intervention-only, DM control, and turn placement should be handled by room roster and local-seat behavior rather than by piling more character-edit fields onto the human profile.

This means the main local user may map to:
- no participant profile at all
- one participant profile
- one of several participant profiles on a shared local machine

## Recommended Vocabulary

Use terms that can survive into online work later.

### Actor Kind

- `HumanParticipant`
- `AiAgent`

This answers what something is.

### Participation Mode

- `Observer`
- `InterventionOnly`
- `TurnParticipant`

This answers how a human participates in the room.

### Player Character Flag

- `IsPlayerCharacter = true`
- `IsPlayerCharacter = false`

For the first pass, this is enough for human participant editing.

It keeps the model lighter than introducing a large human-role enum before real usage proves it is needed.

### Control Capability

- `CanStartRun`
- `CanStopRun`
- `CanSubmitIntervention`
- `CanManageRoom`
- later, `CanApprovePrivilegedActions`

This answers what they are allowed to do in the app.

DM should live here as a capability or capability bundle, not as proof that the human must be in the roster.

For the current local-first scope, command-driven privileged actions should still remain AI-only. Human participants can exist in the room and humans can manage room membership directly through the app UI, but humans should not be exposed as privileged command actors in the room configuration dropdown.

## Do Not Overload AgentConfig For Humans

The current `AgentConfig` is clearly AI-shaped. It already carries things like:
- model id
- system prompt
- compaction budget
- spawned NPC metadata
- suspension metadata

That is a strong sign that humans should not simply be another `AgentConfig` with a toggle.

UI consequence:
- when an entry is human, hide AI-only controls such as model selection, system prompt, max tokens, compaction budget, NPC lifecycle metadata, and similar agent execution settings
- when an entry is AI, continue to show those controls normally

If the UI wants a single creation entry point, a better shape is:
- `Add Cast Member`
- choose `Human Participant` or `AI Agent`

Or keep the existing page but split it into:
- `Human Participants`
- `AI Agents`

This matters because the user suggestion of `player/Agent` is directionally right at the UX level, but not clean as a storage model.

## A Better Interpretation Of "Player / Agent"

There are still two different axes conceptually:

1. Is this a human or an AI?
2. Is this operating as a player-character participant or as a non-player controller or observer?

Examples:
- AI DM: actor kind = `AiAgent`, narrative role = `DM`
- AI player character: actor kind = `AiAgent`, narrative role = `Player`
- human player character: actor kind = `HumanParticipant`, `IsPlayerCharacter = true`
- human room participant who is not a player character: actor kind = `HumanParticipant`, `IsPlayerCharacter = false`
- human DM outside rotation: local seat with intervention rights, not necessarily a human room participant at all

That avoids conflating `player` with `human`.

## Turn Order Recommendation

Do not require `Player 1` to always go first.

That restriction would look simple at first, but it would become artificial almost immediately.

It would block or complicate valid room shapes such as:
- `Player 1 -> Agent 1 -> Player 2 -> Agent 2 -> DM`
- `Player 1 -> Agent 1 -> Agent 2 -> Player 2`
- `AI DM -> Player 1 -> AI Companion -> Player 2`
- `Player 1 -> AI DM -> Player 2`

### Recommended Rule

- any human marked `TurnParticipant` can appear anywhere in the ordered roster
- humans marked `Observer` or `InterventionOnly` do not occupy a turn slot
- AI agents continue to occupy turn slots when enabled
- the room should maintain one explicit mixed roster rather than separate human and AI sort orders
- if one machine hosts two humans, the UI should give them stable local seat labels such as `Primary` and `Guest`

### Better Modeling Option

Instead of forcing `SortOrder` to live only on `AgentConfig`, introduce an ordered room roster such as:

`TurnOrderEntry`
- `Id`
- `ActorKind`
- `ActorId`
- `SortOrder`
- `IsEnabled`

That would support mixed human and AI rotation cleanly.

## Intervention Versus Participation

These should not be the same thing.

### Intervention-Only Human

- can speak outside the main roster
- can add steering notes, DM clarifications, or one-off narrative inserts
- may have a TTS voice
- does not need a character appearance profile

### Turn Participant Human

- has a named slot in the room roster
- can speak as part of the normal round flow
- needs only name, player-character toggle, voice, and appearance for the first pass
- appearance should feed scene-image or cast-display features when present
- may also have intervention rights if the room allows it

This split is what keeps the current DM-style local usage from being forced into a player-character shape.

## Visual And TTS Guidance

### TTS

TTS voice should be broadly available.

Recommended rule:
- every human participant may choose a voice
- the main local operator may also choose a voice even when not represented as an in-room participant
- intervention-only humans should still be able to select a voice for their messages if speech playback is enabled

### Visual Or Appearance Metadata

Appearance should be conditional, not universal.

Recommended rule:
- only show or require appearance for named in-room participants
- do not require appearance for observer-only or intervention-only operator profiles
- human appearance summaries should feed scene image generation when that human is a real room participant
- use human appearance summaries only where the UI or image pipeline actually benefits from them

This keeps the app from asking unnecessary character questions when the user is operating more like a director than a cast member.

## Recommended UI Shape

The cleanest local-first IA is probably not to keep this under `Add Agent` forever.

A better direction is:

### Rooms Page

Use a `Cast` area with two sections:
- `Human Participants`
- `AI Agents`

Each human participant card can show:
- name
- `Is Player Character`
- voice
- appearance
- whether they are in the turn roster

Do not show human participants any AI-only controls such as:
- model
- system prompt
- max tokens
- compaction budget
- NPC lifecycle settings
- privileged command settings

If two humans share one machine, the UI can also show stable seat chips such as `Primary` and `Guest` so speaker selection and future online carry-forward stay explicit.

Each AI agent card can continue to show the existing agent fields, plus optionally a lightweight narrative role label such as `Player` or `DM`.

### Privileged Agent Selector

When the room asks which actor is allowed to use privileged or power-user commands:
- list only eligible AI agents in that dropdown
- do not list human participants there
- keep human add or remove behavior as direct room editing by the local operator rather than as a command path
- reserve command-driven privileged behavior for DM, narrator, or other eligible AI agents only

### Chat Page

When the next turn belongs to a human participant:
- the UI should identify whose turn it is
- the local operator can select or confirm the active human if needed
- the message input should submit under that human's name instead of under `You`

When a human is intervention-only:
- the UI should allow an out-of-turn insert with a clear channel label such as `DM Note` or `Observer Note`
- do not render intervention-only input as a normal in-scene character speaker unless that human is also acting as a real room participant in the roster

## Prompt And Memory Implications

If humans become first-class room participants, agent prompts should stop treating participants as AI-only.

The agent-facing prompt context should eventually be able to distinguish:
- active AI agents
- active human participants in the roster
- intervention-only humans
- off-scene participants

Important recommendation:
- do not create human `system prompts`
- use compact participant descriptors instead, such as name and whether the human is a player character
- if a human participant has an appearance summary, let image generation consume it the same way it consumes AI participant appearance summaries

That keeps humans represented in context without pretending they are autonomous AI actors.

## Suggested Local Data Shape

This is not implementation yet, but it is the clean direction.

### LocalOperatorProfile

Suggested responsibilities:
- device-level display identity
- preferred voice
- local-only preferences

If one machine hosts more than one human, treat those as explicit local seats rather than as one shared operator blob.

Suggested seat labels for MVP:
- `Primary`
- `Guest`

### HumanParticipantConfig

Suggested fields:
- `Id`
- `RoomId`
- `Name`
- `IsPlayerCharacter`
- `AppearanceSummary`
- `TtsVoice`

That keeps the first implementation small and aligned with the current requirement signal.

This config is room-local only for now. Do not introduce a reusable cross-room character profile in the MVP.

### AiAgentConfig

Current `AgentConfig` is already close to this, though a lightweight narrative-role label may still be useful.

### TurnOrderEntry

Preferred long-term shape for mixed play:
- `Id`
- `RoomId`
- `ActorKind`
- `ActorId`
- `SortOrder`
- `IsEnabled`

This would keep rotation independent from whether the actor is human or AI.

## Delivery Recommendation

If this local-first work is pursued, the clean order is:

1. Introduce room-level human participants as a concept distinct from agents.
2. Replace the hardcoded local-human speaker `You` with explicit speaker identity.
3. Teach chat and transcript state to handle mixed human and AI speakers.
4. Introduce a mixed ordered roster instead of AI-only sort order.
5. Add observer and intervention-only modes plus explicit local seat labels such as `Primary` and `Guest`.
6. Only after that, layer online identity, membership, and realtime on top.

## What To Avoid

- forcing the main local user to become a character in every room
- using `AgentConfig` as the storage model for humans
- making `player` mean `human`
- hardcoding `Player 1 always goes first`
- making DM privilege equivalent to always being in the speaking roster
- requiring appearance metadata for observer or intervention-only usage

## Deferred For Later

Explicitly defer these until the first local human-participant pass has real usage behind it:

- expanding human participant fields beyond `Name`, `IsPlayerCharacter`, `TtsVoice`, and `AppearanceSummary`
- reusable cross-room character profiles
- exposing any command-driven privileged behavior to humans

## Summary

The right local step is not `make the main user a participant by default`.

The right local step is:
- keep the app operator optional
- add first-class human participants
- separate participation mode from control privilege
- allow mixed human and AI turn order
- keep TTS broadly available
- keep the first human-participant model intentionally small
- keep human participant definitions room-local only for now
- hide agent-only settings when the edited entry is human
- render intervention-only human input as a labeled channel such as `DM Note` rather than as an in-scene character turn
- keep privileged command actors AI-only and exclude humans from the privileged-agent selector
- make appearance conditional on actual room participation and feed it into image generation when present

That gives a clean bridge from today's single-machine AI rooms to a future online shared-room model without forcing every room into the shape of `one human player plus agents`.