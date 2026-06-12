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

    public async Task SeedAsync()
    {
        await SeedPromptSamplesAsync();
        await SeedModelsAsync();
        await SeedRoomsAsync();
        
        
    }

    private async Task SeedRoomsAsync()
    {
        await _rooms.SeedRoom();
    }

    private async Task SeedModelsAsync()
    {
        await _settings.SeedAiModelsIfEmptyAsync();
    }

    private async Task SeedPromptSamplesAsync()
    {
        await _prompts.SeedBuiltInsIfEmptyAsync();
    }
}