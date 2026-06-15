**Important -- Markdown documentation may be outdated**

I'm having issues with some of the turn logic in my app.

I've made a large rewrite into blazor server with asp.net identity layered over.
Major changes
1. Users now own table rows (e.g. 1 user owns a 'room' through their user id)
2. Finished foreign key mapping from accessory tables into rooms
3. Login system through asp.net identity
4. invite to room, redeem invite, revoke invite functionality
5. slight shift to chat room logic.

Here's the low-
- In a chat-room, the room host has the highest privilege
- In a chat-room, there may be one or more 'other' players
- In a chat-room, there may be one or more 'AI agents'

- A new chat has a few 'start' conditions (e.g. first message sent):
'Room.WaitForUserReply' is checked
    Resolution: 'The Room Host must begin the chat before any players or AI agents can respond'

    Caveats:
        - Room.WaitForUserReply (true) & Room.PauseAfterEveryReply (true)
            -- The room host must manually start every AI chat message (or send a message for themselves), unless that message comes from another player

        - Room.WaitForUserReply (true) && Room.PauseAfterEveryReply (false)
            -- The room host must manually intervene once at the beginning of the round (e.g. before any other AI agent/user can reply)
            -- This may cause a 'pain point' due to the fact that the room host MAY OR MAY _NOT_ be an actual player instead of an AI agent.
                --- so, if the room only has 1 other player, and either of the above scenarios are true, how do we let the other real users chat (or do we just block them until the room host frees them up to message.)
                --- perhaps, if this setting is checked, we force the 'room host' to be a room player (e.g. agent.IsHumanParticipant == true) and they get added to List<Agents> ??

        - Room.WaitForUserReply (false)
            -- The room host does NOT need to manually start every AI chat message, any real player in the room can kick off a round.
                --- if the real player is first/next.. then they must send a message
                --- otherwise, the message box is disabled and all the guest members can do is click the 'run' button, allowing the ai transmissions to begin

    


Now, another complication that has been introduced is AiModel objects present in AppState.
- the room host has no issues running a room because their own models are accessible in AppState.
- room guests have privilege issues running a room because their own models are not referenced by the agents, and they cannot access another players models (currently.. I think _this_ needs to change.)

Furthermore, I am speculating about moving even more logic out of the Chat.razor page, to the back-end service to try and keep our business logic out of the .razor pages more effectively and prevent object state drift. I am speculating moving AiModels out of AppState (or at least changing some of the related logic.)

Relevant files:
Chat.razor, AppState.cs, ConversationRunner.cs, Room & Agent class files
        


