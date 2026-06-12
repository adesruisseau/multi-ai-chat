using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Identity;

namespace AgentGroupChat.UI.Shared.State;

public sealed class PromptLibraryState
{
    private readonly IPromptSampleRepository _promptRepo;
    private readonly IUserContext _userContext;

    public List<PromptSample> Samples { get; private set; } = new();
    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    public PromptLibraryState(IPromptSampleRepository promptRepo, IUserContext userContext)
    {
        _promptRepo = promptRepo;
        _userContext = userContext;
    }

    public async Task LoadAsync()
    {
        Samples = (await _promptRepo.GetAllAsync(await _userContext.GetRequiredUserIdAsync())).ToList();
        IsLoaded = true;
        NotifyChanged();
    }

    public async Task SaveAsync(PromptSample sample)
    {
        sample.UserId = await _userContext.GetRequiredUserIdAsync();
        await _promptRepo.SaveAsync(sample);
        await LoadAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await _promptRepo.DeleteAsync(id, await _userContext.GetRequiredUserIdAsync());
        await LoadAsync();
    }

    private void NotifyChanged() => OnChange?.Invoke();
}