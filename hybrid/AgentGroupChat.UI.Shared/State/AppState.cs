using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Identity;

namespace AgentGroupChat.UI.Shared.State;

public sealed class AppState
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly IUserContext _userContext;
    public AppSettings Settings { get; private set; } = new();
    public List<AiConnection> Connections { get; private set; } = new();
    public List<AiModel> Models { get; private set; } = new();
    public List<ImageConnection> ImageConnections { get; private set; } = new();
    public List<ImageModel> ImageModels { get; private set; } = new();
    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    public AppState(ISettingsRepository settingsRepo, IUserContext userContext)
    {
        _settingsRepo = settingsRepo;
        _userContext = userContext;
    }

    public async Task LoadAsync()
    {
        var userId = await _userContext.GetRequiredUserIdAsync();

        Settings = await _settingsRepo.GetAsync(userId);
        Settings.UserId = userId;
        Connections = await _settingsRepo.GetConnectionsAsync(userId);
        Models = await _settingsRepo.GetModelsAsync(userId);
        ImageConnections = await _settingsRepo.GetImageConnectionsAsync(userId);
        ImageModels = await _settingsRepo.GetImageModelsAsync(userId);
        IsLoaded = true;
        NotifyChanged();
    }

    public async Task SaveSettingsAsync()
    {
        Settings.UserId = await _userContext.GetRequiredUserIdAsync();
        await _settingsRepo.SaveAsync(Settings);
        NotifyChanged();
    }

    public async Task SaveConnectionAsync(AiConnection connection)
    {
        var userId = await _userContext.GetRequiredUserIdAsync();
        connection.UserId = userId;
        await _settingsRepo.SaveConnectionAsync(connection);
        Connections = await _settingsRepo.GetConnectionsAsync(userId);
        NotifyChanged();
    }

    public async Task DeleteConnectionAsync(string id)
    {
        var userId = await _userContext.GetRequiredUserIdAsync();
        await _settingsRepo.DeleteConnectionAsync(id, userId);
        Connections = await _settingsRepo.GetConnectionsAsync(userId);
        NotifyChanged();
    }

    public async Task SaveModelAsync(AiModel model)
    {
        var userId = await _userContext.GetRequiredUserIdAsync();
        model.UserId = userId;
        await _settingsRepo.SaveModelAsync(model);
        Models = await _settingsRepo.GetModelsAsync(userId);
        NotifyChanged();
    }

    public async Task DeleteModelAsync(string id)
    {
        var userId = await _userContext.GetRequiredUserIdAsync();
        await _settingsRepo.DeleteModelAsync(id, userId);
        Models = await _settingsRepo.GetModelsAsync(userId);
        NotifyChanged();
    }

    public async Task SaveImageConnectionAsync(ImageConnection connection)
    {
        var userId = await _userContext.GetRequiredUserIdAsync();
        connection.UserId = userId;
        await _settingsRepo.SaveImageConnectionAsync(connection);
        ImageConnections = await _settingsRepo.GetImageConnectionsAsync(userId);
        NotifyChanged();
    }

    public async Task DeleteImageConnectionAsync(string id)
    {
        var userId = await _userContext.GetRequiredUserIdAsync();
        await _settingsRepo.DeleteImageConnectionAsync(id, userId);
        ImageConnections = await _settingsRepo.GetImageConnectionsAsync(userId);
        NotifyChanged();
    }

    public async Task SaveImageModelAsync(ImageModel model)
    {
        var userId = await _userContext.GetRequiredUserIdAsync();
        model.UserId = userId;
        await _settingsRepo.SaveImageModelAsync(model);
        ImageModels = await _settingsRepo.GetImageModelsAsync(userId);
        NotifyChanged();
    }

    public async Task DeleteImageModelAsync(string id)
    {
        var userId = await _userContext.GetRequiredUserIdAsync();
        await _settingsRepo.DeleteImageModelAsync(id, userId);
        ImageModels = await _settingsRepo.GetImageModelsAsync(userId);
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
