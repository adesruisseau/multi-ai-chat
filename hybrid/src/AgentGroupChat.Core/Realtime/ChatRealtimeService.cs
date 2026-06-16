using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;

namespace AgentGroupChat.Core.Realtime
{
    public sealed class ChatRealtimeService
    {
        private readonly ITranscriptRepository _transcriptRepository;
        private readonly IChatLiveUpdateNotifier _liveUpdates;

        public ChatRealtimeService(ITranscriptRepository transcriptRepository, IChatLiveUpdateNotifier liveUpdates)
        {
            _transcriptRepository = transcriptRepository;
            _liveUpdates = liveUpdates;
        }

        public async Task<ChatUpdateResult> AppendAsync(TranscriptTurn turn, CancellationToken cancellationToken = default)
        {
            var result = await _transcriptRepository.AppendAsync(turn);
            if (!result.Succeeded || result.Turn is null || string.IsNullOrWhiteSpace(result.RoomId))
            {
                return result;
            }

            await _liveUpdates.PublishAsync(
                new ChatLiveEvent(result.RoomId, ChatLiveEventKinds.NewMessage, result.Turn),
                cancellationToken);

            return result;
        }

        public async Task<ChatUpdateResult> ClearAsync(string roomId, CancellationToken cancellationToken = default)
        {
            var result = await _transcriptRepository.ClearAsync(roomId);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.RoomId))
            {
                return result;
            }

            await _liveUpdates.PublishAsync(
                new ChatLiveEvent(result.RoomId, ChatLiveEventKinds.ChatCleared),
                cancellationToken);

            return result;
        }
    }

    public sealed record ChatUpdateResult(bool Succeeded, string? RoomId, TranscriptTurn? Turn, string? FailureReason)
    {
        public static ChatUpdateResult Success(TranscriptTurn turn) => new(true, turn.RoomId, turn, null);

        public static ChatUpdateResult Success(string roomId) => new(true, roomId, null, null);

        public static ChatUpdateResult Failure(string? reason = null, string? roomId = null) =>
            new(false, roomId, null, reason);
    }
}
