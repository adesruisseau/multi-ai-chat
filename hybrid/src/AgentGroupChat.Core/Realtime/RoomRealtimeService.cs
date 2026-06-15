using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Core.Realtime
{
    public sealed class RoomRealtimeService
    {
        private readonly IRoomRepository _roomRepository;
        private readonly IRoomInviteRepository _inviteRepository;
        private readonly IRoomLiveUpdateNotifier _liveUpdates;

        public RoomRealtimeService(IRoomRepository roomRepository, IRoomInviteRepository inviteRepository, IRoomLiveUpdateNotifier liveUpdates)
        {
            _roomRepository = roomRepository;
            _inviteRepository = inviteRepository;
            _liveUpdates = liveUpdates;
        }

        public async Task<RoomDeleteResult> DeleteRoomAsync(string roomId, string userId, CancellationToken cancellationToken = default)
        {
            var result = await _roomRepository.DeleteAsync(roomId, userId);
            if (!result.Succeeded || !string.IsNullOrWhiteSpace(result.RoomId))
            {
                return result;
            }

            await _liveUpdates.PublishAsync(
                new RoomLiveEvent("", RoomLiveEventKinds.RoomDeleted), cancellationToken);

            return result;
        }

        public async Task<RoomUpdateResult> UpdateRoomAsync(RoomConfig room, CancellationToken cancellationToken = default)
        {
            var result = await _roomRepository.SaveAsync(room);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(room.Id))
            {
                return result;
            }

            await _liveUpdates.PublishAsync(
                new RoomLiveEvent(result.RoomId, RoomLiveEventKinds.RoomUpdated), cancellationToken);

            return result;
        }
        public async Task<RoomInviteRedemptionResult> RedeemAsync(
                 Guid inviteId,
                 string userId,
                 string userName,
                 CancellationToken cancellationToken = default
                 )
        {
            var result = await _inviteRepository.RedeemAsync(inviteId, userId, userName);
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.RoomId))
                return result;

            await _liveUpdates.PublishAsync(
                new RoomLiveEvent(result.RoomId, RoomLiveEventKinds.RosterChanged), cancellationToken);

            return result;
        }
    }

    public sealed record RoomDeleteResult(bool Succeeded, string? RoomId, string? FailureReason)
    {
        public static RoomDeleteResult Success(string roomId) => new(true, null, null);
        public static RoomDeleteResult Failure(string? reason = null, string? roomId = null) => new(false, roomId, reason);
    }

    public sealed record RoomInviteRedemptionResult(bool Succeeded, string? RoomId, string? FailureReason)
    {
        public static RoomInviteRedemptionResult Success(string roomId) => new(true, roomId, null);
        public static RoomInviteRedemptionResult Failure(string? reason = null, string? roomId = null) => new(false, roomId, reason);
    }
    public sealed record RoomUpdateResult(bool Succeeded, string? RoomId, string? FailureReason)
    {
        public static RoomUpdateResult Success(string roomId) => new(true, roomId, null);
        public static RoomUpdateResult Failure(string? reason = null, string? roomId = null) => new(false, roomId, reason);
    }
}