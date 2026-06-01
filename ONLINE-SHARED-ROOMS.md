## Online Shared Rooms Architecture Plan

This document captures the recommended path for turning Agent Group Chat into an online shared-room product where two human users can play together with AI agents in the same live room.

If the goal is to validate local human participants and mixed human or AI turn order before auth and realtime work, see `LOCAL-HUMAN-PARTICIPANTS.md` first. That document scopes the offline stepping stone this online plan should build on rather than replace.

The core recommendation is to treat online play as a server-authoritative system. The current hybrid app has strong domain seams already, but room execution, memory refresh, privileged actions, and transcript state all currently assume one local machine and one effective human user. For online play, those behaviors must happen once on the backend and be streamed to connected clients.

## Product Goal

Support a shared room where:
- two authenticated humans can join the same room
- one human can hold DM-style privileges
- AI agents can participate alongside human users
- transcript, room memory, durable memory, logs, and archived scenes are shared
- both users see the same room state in realtime

## Design Principle

Prefer the smallest hosted experience that proves multiplayer play.

That means the first online proof of concept should be:
- text-first
- server-run
- role-based
- invite-based
- capability-gated

It should not try to preserve every local desktop feature on day one.

## First Online MVP

Included:
- user accounts and authentication
- room ownership and room membership
- invite links or room tokens
- shared transcript for multiple humans
- server-side agent execution
- shared room memory and durable memory
- server-side scene archive retrieval and logging
- a human DM or owner role
- one active run per room
- realtime updates to all connected clients

Excluded:
- peer-to-peer execution
- required local Ollama support
- required local Piper support
- required local Kokoro support
- shared audio streaming
- scene image generation
- simultaneous collaborative room editing during active runs
- public lobby or matchmaking
- offline parity

## Why Server-Authoritative Is The Right Shape

The current local architecture centers execution in:
- `ConversationRunner`
- `TurnExecutor`
- `PromptComposer`
- `MemorySummarizer`
- `SceneRetrievalService`
- `LlmClient`

Those services are already a good domain core, but they currently run inside one client process against local SQLite and local app state.

For two humans to share the same room safely:
- transcript writes must happen once
- memory refresh must happen once
- privileged actions must be validated once
- archived scene retrieval must be selected once
- run ownership must be locked once

If two clients each try to run the room, the system will diverge or double-apply side effects. That is why the room runtime must move behind backend APIs and realtime event distribution.

## Current Code Seams That Matter

Primary orchestration and persistence anchors:
- `hybrid/src/AgentGroupChat.Core/Services/ConversationRunner.cs`
- `hybrid/src/AgentGroupChat.Core/Services/TurnExecutor.cs`
- `hybrid/src/AgentGroupChat.Core/Services/PromptComposer.cs`
- `hybrid/src/AgentGroupChat.Core/Services/MemorySummarizer.cs`
- `hybrid/src/AgentGroupChat.Core/Services/SceneRetrievalService.cs`
- `hybrid/src/AgentGroupChat.Core/Services/LlmClient.cs`
- `hybrid/src/AgentGroupChat.Core/Models/Domain/RoomConfig.cs`
- `hybrid/src/AgentGroupChat.Core/Models/Domain/AgentConfig.cs`
- `hybrid/src/AgentGroupChat.Infrastructure/Entities/Entities.cs`
- `hybrid/src/AgentGroupChat.Infrastructure/Data/AppDbContext.cs`
- `hybrid/src/AgentGroupChat.Hybrid/Components/Pages/Chat.razor`
- `hybrid/src/AgentGroupChat.Hybrid/State/RoomState.cs`
- `hybrid/src/AgentGroupChat.Hybrid/State/ConversationState.cs`

These files define the behaviors that an online system must either preserve or relocate.

## Recommended System Shape

### 1. Client Layer

Possible clients:
- the current MAUI desktop client
- a future web client

Client responsibilities:
- authenticate the user
- fetch room state
- render transcript, status, memory views, logs, and room metadata
- send user turns and control commands
- subscribe to room events

Client responsibilities should not include:
- owning room execution
- refreshing memory
- deciding scene retrieval
- applying privileged actions directly

### 2. Backend API Layer

The backend should expose contracts for:
- authentication
- current user profile
- room create, read, update, delete
- invite create and redeem
- membership list and role changes
- transcript fetch and append
- run start and stop
- DM-only room actions
- memory inspection
- scene archive inspection
- log inspection

### 3. Realtime Layer

The system needs a room-scoped realtime channel.

Required event categories:
- user joined or left
- room presence updated
- run started or stopped
- agent thinking started
- agent turn completed
- user turn appended
- memory updated
- scene archive updated
- NPC spawned, suspended, resumed, or dismissed
- errors or model failures

The underlying transport can be WebSockets or another bidirectional realtime channel. The design should not depend on one specific framework.

### 4. Runtime Layer

The backend runtime should own:
- room execution locks
- prompt construction
- LLM calls
- turn sequencing
- post-round memory refresh
- archive retrieval
- privileged action validation and application
- audit emission

This layer is effectively the online form of today's `ConversationRunner` and related services.

### 5. Persistence Layer

The backend should use a durable relational store for:
- users
- rooms
- room memberships
- room invites
- room run locks
- agents
- transcript turns
- memory blocks
- scene archives
- logs
- provider or model configuration references

Blob storage is optional until shared assets such as generated images or hosted audio become part of the product.

### 6. Capability Layer

The backend should advertise per-room or per-deployment capabilities so unsupported features degrade cleanly.

Examples:
- hosted text models available
- local-only text models unavailable
- server TTS unavailable
- client-local TTS allowed
- scene image generation unavailable
- scene archive enabled

This is what keeps the design system agnostic and prevents the hosted product from pretending local desktop features exist everywhere.

## Multi-User Domain Model

### New Concepts

Recommended new records:
- `User`
- `AuthSession`
- `RoomMembership`
- `RoomInvite`
- `RoomRunLock`
- `RoomPresence`
- `AuditEvent`

### Existing Concepts To Extend

#### Room

Add concepts such as:
- owner user id
- visibility or access mode
- created by user id
- updated by user id
- online capability flags or references

#### Transcript Turn

The current model treats the human speaker as the literal string `You`.

For multiplayer this must be replaced or extended with:
- user id
- user display name
- actor type such as human or agent
- room id
- created at
- optional client request id for deduplication

#### Log Entry

Logs should become room-scoped and optionally user-scoped.

Recommended additions:
- room id
- user id when applicable
- action type
- correlation id or run id

## Room Roles

Recommended first-pass human roles:
- `Owner`
- `DM`
- `Player`
- optional `Observer`

Behavior recommendation:
- `Owner` can manage room membership, settings, and room lifecycle
- `DM` can start or stop runs and perform privileged control actions
- `Player` can join and submit turns
- `Observer` can view only if that role is introduced later

Important distinction:
- the human DM role is separate from the current privileged agent concept
- the privileged agent can remain an AI participant with special XML-driven actions
- the human DM should be able to approve, reject, or override those actions in future iterations if needed

## Turn And Run Rules

The safest MVP rules are:
- exactly one active run per room
- exactly one backend authority for the room runtime
- human turns are append-only
- human turns submitted while a run is active are queued for the next valid boundary
- room-setting edits are blocked or tightly constrained while a run is active

Why this matters:
- avoids double execution
- avoids conflicting memory refreshes
- avoids clients racing to become the room authority
- keeps failure and reconnect behavior tractable

## Invite And Access Model

Recommended access flow:
1. user A creates a room
2. user A creates an invite link or token
3. user B redeems the invite
4. redemption creates a room membership with a chosen role
5. user B can now join the shared room and receive realtime updates

Use expiring invite tokens or links instead of exposing raw room identifiers.

## Realtime Room Behavior

A shared room should feel live even when agents are the ones speaking.

Recommended event flow:
1. both users join the room and subscribe to room events
2. one authorized user starts a run
3. backend acquires room lock
4. backend emits `run_started`
5. backend emits `agent_thinking_started` and `turn_appended` as work progresses
6. backend emits memory and archive updates when the round closes
7. backend emits `run_completed` or `run_failed`
8. clients reconcile their local view from the event stream and on reconnect from current room snapshots

## Hosted Feature Strategy

The online product should be explicit about which desktop-era features survive hosting.

### Safe To Support Early

- hosted text model providers
- room memory and durable memory
- scene retrieval
- logs and audit trail
- human and AI mixed transcript

### Optional Later

- hosted Kokoro or other server-side TTS
- cloud image generation
- richer collaborative room editing
- push notifications

### Do Not Make Baseline Requirements

- local Ollama on every participant machine
- local Piper installation on every participant machine
- local Kokoro server on every participant machine
- Windows-only voice APIs

## Security And Operations Requirements

Minimum online safeguards:
- authenticated users
- room-level authorization
- secure provider secret storage
- room run locking
- idempotent start-run commands
- audit trail for membership, invites, and DM actions
- rate limiting for run commands and invite redemption
- reconnect-safe event handling
- observability for model failures, stuck runs, and lock leakage

The exact infrastructure vendor is not the decision. The decision is that these behaviors must exist.

## MVP Delivery Order

### Phase 1: Shared Text Rooms Without Agents

Deliver:
- auth
- room creation
- invites and memberships
- realtime shared transcript
- presence

Success condition:
- two humans can join one room and see each other's turns immediately

### Phase 2: Server-Run Agents

Deliver:
- move conversation runtime to backend
- broadcast agent turns to both clients
- persist transcript and memory once

Success condition:
- both users see the same agent behavior and room state without divergence

### Phase 3: DM Roles And Privileged Control

Deliver:
- Owner and DM roles
- manual run controls
- privileged and NPC workflows in shared rooms

Success condition:
- one user can operate as DM while both players see the same evolving scene

### Phase 4: Capability-Gated Enhancements

Deliver:
- selective hosted TTS
- selective cloud image generation
- richer moderation or inspection tooling

Success condition:
- optional advanced features layer on without changing the core shared-room contract

## Main Risks

Primary risks to watch:
- trying to preserve too many local-only features in the first hosted version
- letting multiple clients mutate room execution state directly
- underestimating run-locking and reconnect behavior
- mixing human DM authority with AI privileged actions without a clear rule model
- shipping realtime transcript sync before deciding who actually owns memory refresh and run sequencing

## Success Criteria For The Two-Player Scenario

The online architecture is good enough when this works cleanly:
1. two users log in
2. one user invites the other into a room
3. both load the same room and see the same transcript state
4. one authorized user starts a run
5. the backend runs agents once and updates memory once
6. both users receive the same live updates in the same order
7. one user disconnects and reconnects without corrupting room state
8. a model failure logs once and surfaces consistently to both users

## Recommendation Summary

If the goal is to someday host a DnD-style room with two players, one or more agents, and a privileged DM, the right path is:
- server-authoritative execution
- authenticated room membership
- invite-based sharing
- realtime room events
- one active run per room
- text-first MVP
- capability-gated optional features for everything local-only

That gets the project to a real online proof of concept without pretending the hosted product must immediately support every local desktop runtime integration.