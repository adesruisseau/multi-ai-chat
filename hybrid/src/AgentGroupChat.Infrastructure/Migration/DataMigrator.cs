using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentGroupChat.Infrastructure.Migration;

public sealed class DataMigrator
{
    private readonly AppDbContext _db;
    private readonly ILegacyDataSource _legacy;

    public DataMigrator(AppDbContext db, ILegacyDataSource legacy)
    {
        _db = db;
        _legacy = legacy;
    }

    public async Task<bool> MigrateIfNeededAsync()
    {
        await _db.Database.EnsureCreatedAsync();

        if (!_legacy.HasLegacyData()) return false;
        if (await _db.Rooms.AnyAsync()) return false; // already migrated

        var settings = _legacy.LoadAppSettings();
        var connections = _legacy.LoadConnections();
        var models = _legacy.LoadModels();
        var rooms = _legacy.LoadRooms();

        // Settings
        var settingsRepo = new SettingsRepository(_db);
        await settingsRepo.SaveAsync(settings);

        foreach (var conn in connections)
            await settingsRepo.SaveConnectionAsync(conn);

        foreach (var model in models)
            await settingsRepo.SaveModelAsync(model);

        // Rooms + agents
        var roomRepo = new RoomRepository(_db);
        foreach (var room in rooms)
        {
            await roomRepo.SaveAsync(room);

            // Room memory
            var memory = _legacy.LoadRoomMemory(room.Id, room.Name);
            var memoryRepo = new MemoryRepository(_db);
            if (memory.TryGetValue("SharedRoom", out var shared) && !string.IsNullOrWhiteSpace(shared))
                await memoryRepo.SaveAsync(room.Id, null, MemoryKind.SharedRoom, shared);
            if (memory.TryGetValue("Durable", out var durable) && !string.IsNullOrWhiteSpace(durable))
                await memoryRepo.SaveAsync(room.Id, null, MemoryKind.Durable, durable);

            // Transcript
            var transcript = _legacy.LoadTranscript(room.Id, room.Name);
            var transcriptRepo = new TranscriptRepository(_db);
            foreach (var turn in transcript)
                await transcriptRepo.AppendAsync(turn);
        }

        return true;
    }
}
