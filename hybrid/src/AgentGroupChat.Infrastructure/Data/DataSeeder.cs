using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Data;
using AgentGroupChat.Infrastructure.Seeding;

public class DataSeeder : IDataSeeder
{
    private readonly AppDbContext _db;
    private readonly IRoomRepository _rooms;
    private readonly ISettingsRepository _settings;
    private readonly IPromptSampleRepository _prompts;

    public DataSeeder(
        AppDbContext db,
        IRoomRepository rooms,
        ISettingsRepository settings,
        IPromptSampleRepository prompts)
    {
        _db = db;
        _rooms = rooms;
        _settings = settings;
        _prompts = prompts;
    }

    public async Task SeedAsync(string userId)
    {
        await SeedPromptSamplesAsync(userId);
        await SeedModelsAsync(userId);
        await SeedRoomsAsync(userId);
        
        
    }

    private async Task SeedRoomsAsync(string userId)
    {
        await _rooms.SeedRoom(userId);
    }

    private async Task SeedModelsAsync(string userId)
    {
        await _settings.SeedAiModelsIfEmptyAsync(userId);
    }

    private async Task SeedPromptSamplesAsync(string userId)
    {
        await _prompts.SeedBuiltInsIfEmptyAsync(userId);
    }
}