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
    Task<List<ImageConnection>> GetImageConnectionsAsync();
    Task SaveImageConnectionAsync(ImageConnection connection);
    Task DeleteImageConnectionAsync(string id);
    Task<List<ImageModel>> GetImageModelsAsync();
    Task<ImageModel> GetImageModelByIdAsync(string modelId);
    Task<ImageConnection> GetImageConnectionAsync(string connectionId);
    Task SaveImageModelAsync(ImageModel model);
    Task DeleteImageModelAsync(string id);

    Task SeedAiModelsIfEmptyAsync();
}
