using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface ISettingsRepository
{
    Task<AppSettings> GetAsync();
    Task SaveAsync(AppSettings settings);
    Task<List<AiConnection>> GetConnectionsAsync();
    Task SaveConnectionAsync(AiConnection connection);
    Task DeleteConnectionAsync(string id);
    Task<List<AiModel>> GetModelsAsync();
    Task SaveModelAsync(AiModel model);
    Task DeleteModelAsync(string id);
}
