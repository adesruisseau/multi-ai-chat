using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface IPromptSampleRepository
{
    Task<IReadOnlyList<PromptSample>> GetAllAsync();
    Task<PromptSample?> GetAsync(string id);
    Task SaveAsync(PromptSample sample);
    Task DeleteAsync(string id);
    Task SeedBuiltInsIfEmptyAsync();
}