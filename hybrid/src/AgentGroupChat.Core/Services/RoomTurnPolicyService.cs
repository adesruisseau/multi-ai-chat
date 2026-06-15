using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services;

public sealed class RoomTurnPolicy
{
    public string InputLabel { get; init; } = "Message";
    public string WaitingMessage { get; init; } = string.Empty;
    public string? NextSpeakerName { get; init; }
    public string? ExpectedHumanParticipantId { get; init; }
    public string? ExpectedHumanUserId { get; init; }
    public bool RequiresHumanMessage { get; init; }
    public bool CanSendMessage { get; init; }
    public bool CanContinueRun { get; init; }
}

public sealed class RoomTurnPolicyService
{
    public RoomTurnPolicy Evaluate(RoomConfig room, IReadOnlyList<TranscriptTurn> sessionTurns, string currentUserId)
    {
        var nextParticipant = GetNextParticipant(room, sessionTurns);
        if (nextParticipant is null)
        {
            return new RoomTurnPolicy
            {
                InputLabel = "Message",
                WaitingMessage = "No enabled participants are available.",
            };
        }

        if (nextParticipant.IsHumanParticipant)
        {
            var isExpectedUser = string.Equals(nextParticipant.UserId, currentUserId, StringComparison.Ordinal);
            return new RoomTurnPolicy
            {
                InputLabel = isExpectedUser ? $"{nextParticipant.Name}'s Message" : $"Waiting for {nextParticipant.Name}",
                WaitingMessage = isExpectedUser
                    ? $"It is {nextParticipant.Name}'s turn to reply."
                    : $"Waiting for {nextParticipant.Name} to reply.",
                NextSpeakerName = nextParticipant.Name,
                ExpectedHumanParticipantId = nextParticipant.Id,
                ExpectedHumanUserId = nextParticipant.UserId,
                RequiresHumanMessage = true,
                CanSendMessage = isExpectedUser,
                CanContinueRun = false,
            };
        }

        var isHost = string.Equals(room.UserId, currentUserId, StringComparison.Ordinal);
        var canContinueAi = !room.WaitForUserReply || isHost;
        return new RoomTurnPolicy
        {
            InputLabel = isHost ? "Narrator Message (optional)" : nextParticipant.Name,
            WaitingMessage = isHost
                ? "You can optionally add a narrator message or continue the AI turn."
                : canContinueAi
                    ? $"{nextParticipant.Name} will reply next."
                    : "Waiting for the room host to continue the AI turn.",
            NextSpeakerName = nextParticipant.Name,
            CanSendMessage = isHost,
            CanContinueRun = canContinueAi,
        };
    }

    public static AgentConfig? GetNextParticipant(RoomConfig room, IReadOnlyList<TranscriptTurn> sessionTurns)
    {
        var orderedParticipants = GetOrderedParticipants(room);
        if (orderedParticipants.Count == 0)
            return null;

        if (sessionTurns.Count == 0)
            return orderedParticipants[0];

        for (var index = sessionTurns.Count - 1; index >= 0; index--)
        {
            var lastSpeaker = sessionTurns[index].Speaker;
            var lastParticipantIndex = orderedParticipants.FindIndex(participant =>
                participant.Name.Equals(lastSpeaker, StringComparison.OrdinalIgnoreCase));
            if (lastParticipantIndex < 0)
                continue;

            var nextIndex = lastParticipantIndex + 1;
            return nextIndex < orderedParticipants.Count
                ? orderedParticipants[nextIndex]
                : orderedParticipants[0];
        }

        return orderedParticipants[0];
    }

    public static AgentConfig? GetNextRunnableAgent(RoomConfig room, IReadOnlyList<TranscriptTurn> sessionTurns)
    {
        var orderedParticipants = GetOrderedParticipants(room);
        if (orderedParticipants.Count == 0)
            return null;

        var nextParticipant = GetNextParticipant(room, sessionTurns);
        if (nextParticipant is null)
            return null;

        var startIndex = orderedParticipants.FindIndex(participant => participant.Id == nextParticipant.Id);
        if (startIndex < 0)
            return null;

        for (var offset = 0; offset < orderedParticipants.Count; offset++)
        {
            var index = (startIndex + offset) % orderedParticipants.Count;
            var participant = orderedParticipants[index];
            if (!participant.IsHumanParticipant)
                return participant;
        }

        return null;
    }

    public static List<AgentConfig> GetOrderedParticipants(RoomConfig room) =>
        room.Agents
            .Where(agent => agent.IsEnabled && !agent.IsTemporarilySuspended)
            .OrderBy(agent => agent.SortOrder)
            .ToList();
}