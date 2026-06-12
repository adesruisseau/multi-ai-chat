using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;

namespace AgentGroupChat.UI.Shared.State;

public sealed class AppState
{
    private readonly ISettingsRepository _settingsRepo;
    public AppSettings Settings { get; private set; } = new();
    public List<AiConnection> Connections { get; private set; } = new();
    public List<AiModel> Models { get; private set; } = new();
    public List<ImageConnection> ImageConnections { get; private set; } = new();
    public List<ImageModel> ImageModels { get; private set; } = new();
    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    public AppState(ISettingsRepository settingsRepo) => _settingsRepo = settingsRepo;

    public async Task LoadAsync()
    {
        Settings = await _settingsRepo.GetAsync();
        Connections = await _settingsRepo.GetConnectionsAsync();
        Models = await _settingsRepo.GetModelsAsync();
        ImageConnections = await _settingsRepo.GetImageConnectionsAsync();
        ImageModels = await _settingsRepo.GetImageModelsAsync();
        IsLoaded = true;
        NotifyChanged();
    }

    public async Task SaveSettingsAsync()
    {
        await _settingsRepo.SaveAsync(Settings);
        NotifyChanged();
    }

    public async Task SaveConnectionAsync(AiConnection connection)
    {
        await _settingsRepo.SaveConnectionAsync(connection);
        Connections = await _settingsRepo.GetConnectionsAsync();
        NotifyChanged();
    }

    public async Task DeleteConnectionAsync(string id)
    {
        await _settingsRepo.DeleteConnectionAsync(id);
        Connections = await _settingsRepo.GetConnectionsAsync();
        NotifyChanged();
    }

    public async Task SaveModelAsync(AiModel model)
    {
        await _settingsRepo.SaveModelAsync(model);
        Models = await _settingsRepo.GetModelsAsync();
        NotifyChanged();
    }

    public async Task DeleteModelAsync(string id)
    {
        await _settingsRepo.DeleteModelAsync(id);
        Models = await _settingsRepo.GetModelsAsync();
        NotifyChanged();
    }

    public async Task SaveImageConnectionAsync(ImageConnection connection)
    {
        await _settingsRepo.SaveImageConnectionAsync(connection);
        ImageConnections = await _settingsRepo.GetImageConnectionsAsync();
        NotifyChanged();
    }

    public async Task DeleteImageConnectionAsync(string id)
    {
        await _settingsRepo.DeleteImageConnectionAsync(id);
        ImageConnections = await _settingsRepo.GetImageConnectionsAsync();
        NotifyChanged();
    }

    public async Task SaveImageModelAsync(ImageModel model)
    {
        await _settingsRepo.SaveImageModelAsync(model);
        ImageModels = await _settingsRepo.GetImageModelsAsync();
        NotifyChanged();
    }

    public async Task DeleteImageModelAsync(string id)
    {
        await _settingsRepo.DeleteImageModelAsync(id);
        ImageModels = await _settingsRepo.GetImageModelsAsync();
        NotifyChanged();
    }

    public string ResolveModelDisplayName(string modelId)
    {
        var model = Models.FirstOrDefault(m => m.Id == modelId);
        return model?.Name ?? modelId;
    }

    public string ResolveImageModelDisplayName(string modelId)
    {
        var model = ImageModels.FirstOrDefault(m => m.Id == modelId);
        return model?.Name ?? modelId;
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
