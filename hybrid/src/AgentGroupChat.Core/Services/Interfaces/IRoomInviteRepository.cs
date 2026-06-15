using AgentGroupChat.Core.Models.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Core.Services.Interfaces
{
    public interface IRoomInviteRepository
    {
        Task CreateAsync(RoomInvite roomInvite);
        Task<bool> RedeemAsync(Guid id, string userId, string username);
        Task RevokeAsync(Guid id, string userId);
        Task<List<RoomInvite>> ListAsync(string userId, string roomId);

    }
}
