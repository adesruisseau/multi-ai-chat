using AgentGroupChat.Core.Models.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Core.Services.Interfaces
{
    public interface IDataTrackerRepository
    {
        Task<List<DataTrackerConfig>> GetListAsync(string roomId);
        Task<List<DataTrackerConfig>> GetAgentListAsync(string roomId, string agentId);
        Task AddAsync(DataTrackerConfig dataTracker);
        Task DeleteAsync(DataTrackerConfig dataTracker);
    }
}
