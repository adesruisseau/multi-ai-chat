using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;

namespace AgentGroupChat.Hybrid.State;

public sealed class AppState
{
    private readonly ISettingsRepository _settingsRepo;
    public AppSettings Settings { get; private set; } = new();
    public List<AiConnection> Connections { get; private set; } = new();
    public List<AiModel> Models { get; private set; } = new();
    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    public AppState(ISettingsRepository settingsRepo) => _settingsRepo = settingsRepo;

    public async Task LoadAsync()
    {
        Settings = await _settingsRepo.GetAsync();
        Connections = await _settingsRepo.GetConnectionsAsync();
        Models = await _settingsRepo.GetModelsAsync();
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

    public string ResolveModelDisplayName(string modelId)
    {
        var model = Models.FirstOrDefault(m => m.Id == modelId);
        return model?.Name ?? modelId;
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
