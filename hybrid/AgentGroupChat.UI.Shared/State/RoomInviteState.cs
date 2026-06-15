using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Realtime;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.UI.Shared.State
{
    public sealed class RoomInviteState
    {
        private readonly IRoomInviteRepository _inviteRepo;
        private readonly IUserContext _userContext;
        private readonly RoomRealtimeService __roomRealtimeService;


        public List<RoomInvite> RoomInvites { get; private set; } = new();
        public bool IsLoaded { get; private set;  }
        public event Action? OnChange;

        public RoomInviteState(
            IRoomInviteRepository inviteRepo,
            IUserContext userContext,
            RoomRealtimeService roomRealtimeService)
        {
            _inviteRepo = inviteRepo;
            _userContext = userContext;
            __roomRealtimeService = roomRealtimeService;
        }

        public async Task LoadAsync(string roomId)
        {
            var userId = await _userContext.GetRequiredUserIdAsync();
            RoomInvites = await _inviteRepo.ListAsync(userId, roomId);
            IsLoaded = true;
            NotifyChanged();
        }

        public async Task CreateAsync(string roomId)
        {
            var userId = await _userContext.GetRequiredUserIdAsync();
            var invite = new RoomInvite
            {
                HostUserId = userId,
                RoomId = roomId,
                CreatedDate = DateTimeOffset.UtcNow,
                ExpirationDate = DateTimeOffset.UtcNow.AddDays(1)
            };
            await _inviteRepo.CreateAsync(invite);

            await LoadAsync(invite.RoomId);
        }

        public async Task<RoomInviteRedemptionResult> RedeemAsync(Guid id)
        {
            var userId = await _userContext.GetRequiredUserIdAsync();

            return await __roomRealtimeService.RedeemAsync(id, userId, _userContext.UserName ?? "New Player");
        }

        public async Task RevokeAsync(Guid id, string roomId)
        {
            var userId = await _userContext.GetRequiredUserIdAsync();

            await _inviteRepo.RevokeAsync(id, userId);
            await LoadAsync(roomId);
        }
        private void NotifyChanged() => OnChange?.Invoke();
    }
}
