using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services.Interfaces;

public interface IPromptSampleRepository
{
    Task<IReadOnlyList<PromptSample>> GetAllAsync(string userId);
    Task<PromptSample?> GetAsync(int id, string userId);
    Task SaveAsync(PromptSample sample);
    Task DeleteAsync(int id, string userId);
    Task SeedBuiltInsIfEmptyAsync(string userId);
}