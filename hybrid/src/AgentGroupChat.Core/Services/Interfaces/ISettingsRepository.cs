using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface ISettingsRepository
{
    Task<AppSettings> GetAsync(string userId);
    Task SaveAsync(AppSettings settings);
    Task<List<AiConnection>> GetConnectionsAsync(string userId);
    Task SaveConnectionAsync(AiConnection connection);
    Task DeleteConnectionAsync(string id, string userId);
    Task<List<AiModel>> GetModelsAsync(string userId, string? roomId = null);
    Task SaveModelAsync(AiModel model);
    Task DeleteModelAsync(string id, string userId);
    Task<List<ImageConnection>> GetImageConnectionsAsync(string userId);
    Task SaveImageConnectionAsync(ImageConnection connection);
    Task DeleteImageConnectionAsync(string id, string userId);
    Task<List<ImageModel>> GetImageModelsAsync(string userId);
    Task<ImageModel> GetImageModelByIdAsync(string modelId, string userId);
    Task<ImageConnection> GetImageConnectionAsync(string connectionId, string userId);
    Task SaveImageModelAsync(ImageModel model);
    Task DeleteImageModelAsync(string id, string userId);

    Task SeedAiModelsIfEmptyAsync(string userId);
}
