using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Infrastructure.Data
{
    public sealed class DataTrackerRepository : IDataTrackerRepository
    {
        private readonly AppDbContext _db;

        public DataTrackerRepository(AppDbContext db) => _db = db;

        public async Task<List<DataTrackerConfig>> GetListAsync(string roomId)
        {
            var dataTrackers = _db.DataTrackers.Where(x => x.RoomId == roomId);
            return dataTrackers.Select(EntityMapper.ToDomain).ToList();
        }

        public async Task<List<DataTrackerConfig>> GetAgentListAsync(string roomId, string agentId)
        {
            var dataTrackers = _db.DataTrackers.Where(x => x.RoomId == roomId && x.AgentId == agentId);
            return dataTrackers.Select(EntityMapper.ToDomain).ToList();
        }

        public async Task AddAsync(DataTrackerConfig dataTracker)
        {
            _db.DataTrackers.Add(EntityMapper.ToEntity(dataTracker));
            await _db.SaveChangesAsync();
        }
        public async Task DeleteAsync(DataTrackerConfig dataTracker)
        {
            _db.DataTrackers.Remove(EntityMapper.ToEntity(dataTracker));
            await _db.SaveChangesAsync();
        }
    }
}
