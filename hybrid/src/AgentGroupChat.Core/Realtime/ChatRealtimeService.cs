using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

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

        public async Task<ChatUpdateResult> UpdateAsync(TranscriptTurn turn, CancellationToken cancellationToken = default)
        {
            var result = await _transcriptRepository.AppendAsync(turn);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.RoomId))
            {
                return result;
            }

            await _liveUpdates.PublishAsync(new ChatLiveEvent(result.RoomId, ChatLiveEventKinds.NewMessage), cancellationToken);

            return result;
        }

        public async Task<ChatUpdateResult> ClearAsync(string roomId, CancellationToken cancellationToken = default)
        {
            var result = await _transcriptRepository.ClearAsync(roomId);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.RoomId))
            {
                return result;
            }
            await _liveUpdates.PublishAsync(new ChatLiveEvent(result.RoomId, ChatLiveEventKinds.ChatCleared), cancellationToken);

            return result;
        }
    }

    public sealed record ChatUpdateResult(bool Succeeded, string? RoomId, string? FailureReason)
    {
        public static ChatUpdateResult Success(string roomId) => new(true, roomId, null);
        public static ChatUpdateResult Failure(string? reason = null, string? roomId = null) => new(false, roomId, reason);
    }
}
