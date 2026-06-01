# Plan: Split the Rooms Page into Settings and Agents Sub-Tabs

This feature reorganizes the Rooms page so room-level configuration and agent-level editing are no longer stacked in one long mixed editor.

The goal is not to redesign the data model. The goal is to improve information architecture and editing clarity while preserving the current room aggregate and save flow.

Right now `Rooms.razor` mixes:

- room metadata
- pacing and run behavior
- summarizer settings
- scene-image defaults
- privileged-action settings
- NPC template settings
- the entire agent list and agent editors

That is already a large editor surface, and more planned features will make it worse. Splitting the page into two sub-tabs is the cleanest near-term improvement.

---

## MVP Recommendation

When a room is selected, the right-hand editor should be divided into two sub-tabs:

1. `Room Settings`
2. `Agents`

The left-side room list stays as it is.

The important implementation rule is that this is a UI split, not a persistence split. `RoomConfig` and its `Agents` collection remain one aggregate saved through the existing `RoomState.SaveRoomAsync(room)` flow.

Why this matters:

- the current repository already saves a room plus agents together
- splitting storage would add needless complexity and partial-save edge cases
- the user's request is organizational, not relational

---

## Non-Goals for MVP

- separate routes for room settings vs. agents
- separate repositories for agent edits
- auto-save on every control change
- collaborative editing semantics
- a full visual redesign of the Rooms page

---

## Current Problem In The Repo

`Rooms.razor` currently renders one long vertical form for the selected room.

In order, it currently includes:

- room basics
- pacing/runtime fields
- summarizer controls
- scene image controls
- privileged-action and NPC defaults
- agents list and agent cards
- save and delete buttons

This causes three practical problems:

1. The user has to scroll through unrelated room-level fields to get to agents.
2. Agent editing visually competes with room infrastructure fields.
3. The page will keep expanding as new room-level systems arrive.

The scene-image controls already increased the weight of the room-level portion. More growth is expected, not hypothetical.

---

## Proposed UI Shape

Keep the outer page structure:

- left panel: room list
- right panel: selected room editor

Inside the selected room editor, add a tab set.

### Tab 1: Room Settings

This tab should contain all room-scoped fields, grouped into sections.

Suggested section order:

1. Basics
2. Turn Flow
3. Summarizer
4. Scene Images
5. Privileged Actions

#### Basics

- Name
- Topic

#### Turn Flow

- Wait for user reply
- Pause after every reply / pause at end of round
- Seconds between agents
- Max tokens
- Recent transcript turns
- User compaction budget

#### Summarizer

- Summarizer model
- Compression level
- custom summarizer numeric settings when applicable
- custom summarizer prompt override

#### Scene Images

- Enable Scene Image Generation
- style notes
- negative prompt

#### Privileged Actions

- Enable Privileged Actions
- Privileged Agent selector
- NPC model and voice defaults
- NPC token and compaction defaults
- NPC instruction template
- active NPC status and max concurrent NPCs

### Tab 2: Agents

This tab should contain:

- `Add Agent` button
- agent count and quick status summary
- all agent cards and agent-specific fields

Per-agent card content can stay close to current behavior:

- name
- role chips such as Privileged, NPC, Suspended
- model
- system prompt
- appearance summary
- enabled state
- token override
- compaction budget
- TTS voice
- voice preview
- chat color
- remove button

---

## UX Rules

### Single Save Surface

Keep one shared save model for the selected room.

Recommended behavior:

- edits in either tab modify the same selected `RoomConfig` object
- `Save Room` persists the entire aggregate
- `Delete Room` remains room-wide

This avoids subtle partial-save confusion.

### Tab Persistence

Remember the active tab while the user stays on the Rooms page.

Recommended local state:

- private field such as `_activeRoomTab`

That field does not need to persist across app restarts for MVP.

### Validation Visibility

If a room-level validation issue relates to agents, the user should still be able to see a hint from the Room Settings tab.

Examples:

- if the privileged agent is not the last enabled permanent agent, show the warning on the Room Settings tab because that is where the privileged configuration lives
- if there are zero agents, show a simple warning on the Agents tab header or empty state

---

## Recommended Implementation Shape

There are two viable implementation levels.

### Option A: Minimal UI Split In One File

Add `MudTabs` or an equivalent tab control directly inside `Rooms.razor` and keep the existing markup mostly intact, just redistributed between two tab panels.

Pros:

- smallest diff
- fastest implementation
- minimal new components

Cons:

- `Rooms.razor` remains large
- future growth will still be awkward

### Option B: UI Split Plus Subcomponents

Extract the right-side editor into subcomponents such as:

- `RoomSettingsEditor.razor`
- `RoomAgentsEditor.razor`

Then let `Rooms.razor` own only:

- room list
- selected room selection
- tab state
- shared save/delete actions
- a few cross-cutting helpers

Pros:

- cleaner file boundaries
- easier future expansion
- easier isolated testing and future review

Cons:

- slightly more upfront work

Recommended choice:

- Option B is the better long-term fit because `Rooms.razor` is already large and will keep growing.
- If the team wants the smallest first step, land Option A first and extract after behavior is stable.

---

## File-By-File Impact

### Minimum viable implementation

- update `AgentGroupChat.Hybrid/Components/Pages/Rooms.razor`

### Recommended cleaner implementation

- keep `AgentGroupChat.Hybrid/Components/Pages/Rooms.razor` as the shell
- add `AgentGroupChat.Hybrid/Components/Rooms/RoomSettingsEditor.razor`
- add `AgentGroupChat.Hybrid/Components/Rooms/RoomAgentsEditor.razor`

Shared responsibilities that should remain in the shell page for MVP:

- selected room handling
- `AddRoom`
- `SaveRoom`
- `DeleteRoom`
- room-level voice refresh for Kokoro voices if the child components need them

Responsibilities that can move into the child editors:

- room-level field rendering
- agent card rendering
- `AddAgent`
- `RemoveAgent`
- per-agent voice preview button wiring
- color selection UI

---

## State And Persistence Considerations

This feature should not change the current save contract:

- `RoomState.SelectedRoom` stays the single editing object
- `RoomState.SaveRoomAsync(room)` persists room plus agent list together
- `IRoomRepository` does not need a new partial-save method

This is important because the current infrastructure already treats rooms and agents as one aggregate, and the user request does not justify breaking that model.

---

## Suggested Tab-Level Enhancements

These are optional but align well with the split.

### Tab Labels With Counts

- `Room Settings`
- `Agents (3)`

This gives quick context without adding complexity.

### Empty State For Agents Tab

If a room has no agents yet, show:

- a short prompt explaining what agents are for
- a single `Add Agent` button

### Sticky Save Bar

If the page continues to grow, consider a sticky bottom action bar for:

- Save Room
- Delete Room

That can be done later without changing the core split.

---

## Interaction With Other Planned Features

This split helps future features rather than colliding with them.

### Prompt Library

If a prompt library page lands later, the Agents tab is the correct place for small `Apply From Library` and `Save Prompt to Library` actions.

### Online Shared Rooms

If room roles and online collaboration appear later, the Room Settings tab is the obvious future home for access and membership settings, while Agents remains focused on AI participants.

### More Room-Level Systems

Future systems such as retrieval tuning, model overrides, or image-model defaults will expand the Room Settings tab without making the Agents experience worse.

---

## Delivery Order

1. Introduce the tab state in the selected-room editor.
2. Move existing room-scoped sections into `Room Settings`.
3. Move agent list and cards into `Agents`.
4. Keep shared save/delete actions outside the tabs or in a common footer.
5. If desired, extract subcomponents after the split is behaviorally stable.

---

## Validation Plan

1. Select a room and verify both tabs show the expected data with no missing fields.
2. Edit room-level settings, switch tabs, and verify unsaved changes remain visible.
3. Add or remove agents from the Agents tab and verify room-level settings remain intact.
4. Save the room and reload it; verify both room settings and agents persisted correctly.
5. Verify privileged-agent warnings still show correctly after the split.
6. Verify Kokoro voice refresh and agent voice preview still work from the Agents tab.
7. Verify deleting a room still works regardless of the currently selected tab.

---

## Decisions

- Split the Rooms editor into `Room Settings` and `Agents` sub-tabs.
- Keep rooms and agents as one persisted aggregate.
- Do not create partial-save infrastructure for this feature.
- Prefer extracting subcomponents if the team is willing to do slightly more than the minimum.
- Treat this as an information-architecture improvement, not a domain-model change.

---

## Future Extensions

- a third `Advanced` tab if room-level systems become too dense
- tab badges for warnings
- prompt-library actions embedded into agent cards
- drag-and-drop agent ordering
- room summary card at the top of the editor

None of those are required to get the current split value.