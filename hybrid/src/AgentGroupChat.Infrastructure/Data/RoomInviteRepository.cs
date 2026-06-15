using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Infrastructure.Data
{
    public sealed class RoomInviteRepository : IRoomInviteRepository
    {
        private readonly AppDbContext _db;

        public RoomInviteRepository(AppDbContext db) => _db = db;
        public async Task CreateAsync(RoomInvite roomInvite)
        {
            _db.RoomInvites.Add(EntityMapper.ToEntity(roomInvite));
            await _db.SaveChangesAsync();
        }
        public async Task<bool> RedeemAsync(Guid id, string userId, string username)
        {
            var inviteEntity = await _db.RoomInvites
                .Where(x => x.RedeemedByUserId == null)
                .Where(x => x.RedeemedDate == null)
                .Where(x => x.Id == id)
                .Where(x => x.HostUserId != userId)
                .FirstOrDefaultAsync();
            if (inviteEntity is null)
            {
                return false;
            }
            else
            {
                try
                {
                    var invite = EntityMapper.ToDomain(inviteEntity);
                    invite.RedeemedByUserId = userId;
                    invite.RedeemedDate = DateTimeOffset.Now;

                    var updatedInviteEntity = EntityMapper.ToEntity(invite);
                    _db.Entry(inviteEntity).CurrentValues.SetValues(updatedInviteEntity);
                    var currRoom = await _db.Rooms.Include(x => x.Agents).Where(x => x.Id == updatedInviteEntity.RoomId).FirstOrDefaultAsync();
                    
                    if (currRoom is null)
                    {
                        return false;
                    }
                    int sortOrder = currRoom.Agents.Select(x => x.SortOrder).Max() + 1;
                    AgentConfig agentToAdd = new AgentConfig()
                    {
                        RoomId = updatedInviteEntity.RoomId,
                        Name = username,
                        SortOrder = sortOrder,
                        IsHumanParticipant = true,
                        UserId = userId
                    };
                    await _db.AddAsync(EntityMapper.ToEntity(agentToAdd));

                    await _db.SaveChangesAsync();

                    return true;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
            
        }
        public async Task RevokeAsync(Guid id, string userId)
        {
            var inviteEntity = await _db.RoomInvites.Where(x => x.Id == id && x.HostUserId == userId).FirstAsync();
            if (inviteEntity is null)
            {
                return;
            }
            var invite = EntityMapper.ToDomain(inviteEntity);
            invite.ExpirationDate = invite.CreatedDate.AddDays(-1);
            invite.RedeemedDate = invite.CreatedDate.AddDays(-1);
            invite.RedeemedByUserId = invite.HostUserId;

            var updatedInviteEntity = EntityMapper.ToEntity(invite);
            _db.Entry(inviteEntity).CurrentValues.SetValues(updatedInviteEntity);
            await _db.SaveChangesAsync();
        }

        public async Task<List<RoomInvite>> ListAsync(string userId, string roomId)
        {
            var inviteEntities = await _db.RoomInvites.Where(x => x.HostUserId == userId && x.RoomId == roomId).ToListAsync();
            return inviteEntities.Select(EntityMapper.ToDomain).ToList();
        }
    }
}
