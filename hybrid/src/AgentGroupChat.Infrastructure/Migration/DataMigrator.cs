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
        await EvolveSchemaAsync();

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

    private async Task EvolveSchemaAsync()
    {
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await AddColumnIfMissingAsync(conn, "Rooms", "MemoryModelId", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "MaxArchivedScenes", "INTEGER NOT NULL DEFAULT 40");
            await AddColumnIfMissingAsync(conn, "Rooms", "EnableSceneArchive", "INTEGER NOT NULL DEFAULT 1");
            await AddColumnIfMissingAsync(conn, "Rooms", "PauseAfterEveryReply", "INTEGER NOT NULL DEFAULT 0");

            await CreateTableIfMissingAsync(conn, "SceneArchives", """
                CREATE TABLE "SceneArchives" (
                    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "RoomId" TEXT NOT NULL DEFAULT '',
                    "RoundNumber" INTEGER NOT NULL DEFAULT 0,
                    "Label" TEXT NOT NULL DEFAULT '',
                    "KeyEntities" TEXT NOT NULL DEFAULT '',
                    "SharedRoomSnapshot" TEXT NOT NULL DEFAULT '',
                    "DurableSnapshot" TEXT NOT NULL DEFAULT '',
                    "IsMajor" INTEGER NOT NULL DEFAULT 0,
                    "CreatedAt" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00'
                )
                """);

            await CreateIndexIfMissingAsync(conn, "IX_SceneArchives_RoomId",
                "CREATE INDEX \"IX_SceneArchives_RoomId\" ON \"SceneArchives\" (\"RoomId\")");
            await CreateIndexIfMissingAsync(conn, "IX_SceneArchives_RoomId_RoundNumber",
                "CREATE INDEX \"IX_SceneArchives_RoomId_RoundNumber\" ON \"SceneArchives\" (\"RoomId\", \"RoundNumber\")");
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private static async Task AddColumnIfMissingAsync(
        System.Data.Common.DbConnection conn, string table, string column, string definition)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        reader.Close();

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}";
        await alter.ExecuteNonQueryAsync();
    }

    private static async Task CreateTableIfMissingAsync(
        System.Data.Common.DbConnection conn, string table, string createSql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
        var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
        if (exists) return;

        using var create = conn.CreateCommand();
        create.CommandText = createSql;
        await create.ExecuteNonQueryAsync();
    }

    private static async Task CreateIndexIfMissingAsync(
        System.Data.Common.DbConnection conn, string indexName, string createSql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{indexName}'";
        var exists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
        if (exists) return;

        using var create = conn.CreateCommand();
        create.CommandText = createSql;
        await create.ExecuteNonQueryAsync();
    }
}
