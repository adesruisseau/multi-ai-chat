using AgentGroupChat.Core;
using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Realtime;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        public async Task<RoomInviteRedemptionResult> RedeemAsync(Guid id, string userId, string username)
        {
            var inviteEntity = await _db.RoomInvites
                .Where(x => x.Id == id)
                .FirstOrDefaultAsync();
            if (inviteEntity is null)
            {
                return new RoomInviteRedemptionResult(false, null, "Redemption key was invalid.");
            }
            else if (inviteEntity.RedeemedByUserId != null || inviteEntity.RedeemedDate != null)
            {
                return new RoomInviteRedemptionResult(false, inviteEntity.RoomId.ToString(), "Key has already been redeemed.");
            }
            else if (inviteEntity.HostUserId == userId)
            {
                return new RoomInviteRedemptionResult(false, inviteEntity.RoomId.ToString(), "Hosts cannot redeem an invite to their own room.");
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
                        return new RoomInviteRedemptionResult(false, invite.RoomId, "The room was not found. Please try again with a new key.");
                    }

                    var membershipExists = await _db.RoomMemberships.AnyAsync(x => x.RoomId == updatedInviteEntity.RoomId && x.UserId == userId);
                    if (!membershipExists)
                    {
                        await _db.RoomMemberships.AddAsync(new RoomMembershipEntity
                        {
                            RoomId = updatedInviteEntity.RoomId,
                            UserId = userId,
                            Role = RoomMembershipRoles.Player,
                            JoinedAt = DateTimeOffset.UtcNow
                        });
                    }

                    var existingSeat = currRoom.Agents.FirstOrDefault(x => x.UserId == userId && x.IsHumanParticipant);
                    if (existingSeat is null)
                    {
                        var sortOrder = currRoom.Agents.Count == 0 ? 0 : currRoom.Agents.Max(x => x.SortOrder) + 1;
                        AgentConfig agentToAdd = new AgentConfig()
                        {
                            RoomId = updatedInviteEntity.RoomId,
                            Name = username,
                            SortOrder = sortOrder,
                            IsHumanParticipant = true,
                            UserId = userId
                        };
                        await _db.AddAsync(EntityMapper.ToEntity(agentToAdd));
                    }

                    await _db.SaveChangesAsync();

                    return new RoomInviteRedemptionResult(true, invite.RoomId, null);
                }
                catch (Exception)
                {
                    return new RoomInviteRedemptionResult(false, null, null);
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
            var now = DateTime.Now;
            var inviteEntities = await _db.RoomInvites
                .Where(x => x.HostUserId == userId && x.RoomId == roomId)
                .Where(x => x.RedeemedByUserId == null)
                
                .Where(x => x.RedeemedDate == null)
                .ToListAsync();
            return inviteEntities.Select(EntityMapper.ToDomain).ToList();
        }
    }
}
