using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;

namespace AgentGroupChat.UI.Shared.State;

public sealed class PromptLibraryState
{
    private readonly IPromptSampleRepository _promptRepo;

    public List<PromptSample> Samples { get; private set; } = new();
    public bool IsLoaded { get; private set; }

    public event Action? OnChange;

    public PromptLibraryState(IPromptSampleRepository promptRepo) => _promptRepo = promptRepo;

    public async Task LoadAsync()
    {
        Samples = (await _promptRepo.GetAllAsync()).ToList();
        IsLoaded = true;
        NotifyChanged();
    }

    public async Task SaveAsync(PromptSample sample)
    {
        await _promptRepo.SaveAsync(sample);
        await LoadAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await _promptRepo.DeleteAsync(id);
        await LoadAsync();
    }

    private void NotifyChanged() => OnChange?.Invoke();
}