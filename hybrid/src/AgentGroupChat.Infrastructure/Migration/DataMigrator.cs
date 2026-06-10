using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Runtime.InteropServices;
using System.Xml;

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
        await EnsurePromptSamplesSeededAsync();

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

    private async Task EnsurePromptSamplesSeededAsync()
    {
        var promptSamples = new PromptSampleRepository(_db);
        await promptSamples.SeedBuiltInsIfEmptyAsync();
    }

    private async Task EvolveSchemaAsync()
    {
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await AddColumnIfMissingAsync(conn, "Rooms", "TtsEnabledOverride", "INTEGER");
            await AddColumnIfMissingAsync(conn, "Rooms", "TtsProviderOverride", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "TtsFallbackVoice", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "TtsUserVoice", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "SceneImageModelId", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "EnableSceneImageGeneration", "INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(conn, "Rooms", "UseCreativeImageGeneration", "INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(conn, "Rooms", "SceneImageStyleNotes", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "SceneImageNegativePrompt", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "MemoryModelId", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "MaxArchivedScenes", "INTEGER NOT NULL DEFAULT 40");
            await AddColumnIfMissingAsync(conn, "Rooms", "EnableSceneArchive", "INTEGER NOT NULL DEFAULT 1");
            await AddColumnIfMissingAsync(conn, "Rooms", "PauseAfterEveryReply", "INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(conn, "Rooms", "EnablePrivilegedActions", "INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(conn, "Rooms", "EnableNpcSpawning", "INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(conn, "Rooms", "PrivilegedAgentId", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "NpcModelId", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "NpcDefaultMaleVoice", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "NpcDefaultFemaleVoice", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "NpcMaxTokens", "INTEGER");
            await AddColumnIfMissingAsync(conn, "Rooms", "NpcCompactionBudget", "INTEGER NOT NULL DEFAULT 300");
            await AddColumnIfMissingAsync(conn, "Rooms", "NpcBaseInstructions", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "MaxConcurrentNpcs", "INTEGER NOT NULL DEFAULT 2");
            await AddColumnIfMissingAsync(conn, "Rooms", "UseSummarizer", "BOOLEAN DEFAULT FALSE");
            await AddColumnIfMissingAsync(conn, "Rooms", "StoreSharedRoomMemory", "BOOLEAN DEFAULT FALSE");
            await AddColumnIfMissingAsync(conn, "Rooms", "StoreDurableMemory", "BOOLEAN DEFAULT FALSE");
            await AddColumnIfMissingAsync(conn, "Rooms", "StoreLongTermArchives", "BOOLEAN DEFAULT FALSE");


            await AddColumnIfMissingAsync(conn, "Agents", "AppearanceSummary", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Agents", "IsNpc", "INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(conn, "Agents", "SpawnedByAgentId", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Agents", "IsTemporarilySuspended", "INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync(conn, "Agents", "SuspendedByAgentId", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Agents", "SuspendedUntilRound", "INTEGER");
            await AddColumnIfMissingAsync(conn, "Agents", "SuspensionReason", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Agents", "IsHumanParticipant", "BOOLEAN DEFAULT FALSE");
            await AddColumnIfMissingAsync(conn, "AiModels", "Temperature", "DECIMAL NOT NULL DEFAULT 0.7");
            await AddColumnIfMissingAsync(conn, "AiModels", "MaxTokens", "INTEGER NOT NULL DEFAULT 512");
            await AddColumnIfMissingAsync(conn, "Agents", "PromptSampleId", "INTEGER NOT NULL DEFAULT 1");
            await AddColumnIfMissingAsync(conn, "AppSettings", "KokoroUserVoice", "TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync(conn, "Rooms", "SharedRoomMemoryPromptSampleId", "INTEGER NOT NULL DEFAULT 1");
            await AddColumnIfMissingAsync(conn, "Rooms", "DurableMemoryPromptSampleId", "INTEGER NOT NULL DEFAULT 1");
            await AddColumnIfMissingAsync(conn, "Rooms", "NpcPromptSampleId", "INTEGER NOT NULL DEFAULT 1");

            await CreateTableIfMissingAsync(conn, "ImageConnections", """
                CREATE TABLE "ImageConnections" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "Name" TEXT NOT NULL DEFAULT '',
                    "Transport" TEXT NOT NULL DEFAULT 'ComfyUI',
                    "Endpoint" TEXT NOT NULL DEFAULT '',
                    "ApiKey" TEXT NOT NULL DEFAULT '',
                    "SortOrder" INTEGER NOT NULL DEFAULT 0
                )
                """);

            await CreateTableIfMissingAsync(conn, "ImageModels", """
                CREATE TABLE "ImageModels" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "Name" TEXT NOT NULL DEFAULT '',
                    "ConnectionId" TEXT NOT NULL DEFAULT '',
                    "ModelId" TEXT NOT NULL DEFAULT '',
                    "WorkflowId" TEXT NOT NULL DEFAULT '',
                    "Width" INTEGER NOT NULL DEFAULT 1024,
                    "Height" INTEGER NOT NULL DEFAULT 1024,
                    "Steps" INTEGER,
                    "GuidanceScale" REAL,
                    "NegativePrompt" TEXT NOT NULL DEFAULT '',
                    "Notes" TEXT NOT NULL DEFAULT '',
                    "SortOrder" INTEGER NOT NULL DEFAULT 0
                )
                """);

            await CreateIndexIfMissingAsync(conn, "IX_ImageConnections_Name",
                "CREATE INDEX \"IX_ImageConnections_Name\" ON \"ImageConnections\" (\"Name\")");
            await CreateIndexIfMissingAsync(conn, "IX_ImageModels_Name",
                "CREATE INDEX \"IX_ImageModels_Name\" ON \"ImageModels\" (\"Name\")");
            await CreateIndexIfMissingAsync(conn, "IX_ImageModels_ConnectionId",
                "CREATE INDEX \"IX_ImageModels_ConnectionId\" ON \"ImageModels\" (\"ConnectionId\")");

            await CreateTableIfMissingAsync(conn, "PromptSamples", """
                CREATE TABLE "PromptSamples" (
                    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "Name" TEXT NOT NULL DEFAULT '',
                    "Category" TEXT NOT NULL DEFAULT '',
                    "Description" TEXT NOT NULL DEFAULT '',
                    "PromptText" TEXT NOT NULL DEFAULT '',
                    "Tags" TEXT NOT NULL DEFAULT '',
                    "IsBuiltIn" INTEGER NOT NULL DEFAULT 0,
                    "ParentPromptSampleId" TEXT NOT NULL DEFAULT '',
                    "SourceLabel" TEXT NOT NULL DEFAULT '',
                    "SortOrder" INTEGER NOT NULL DEFAULT 0,
                    "CreatedAt" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00',
                    "UpdatedAt" TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00'
                )
                """);

            await CreateIndexIfMissingAsync(conn, "IX_PromptSamples_Name",
                "CREATE INDEX \"IX_PromptSamples_Name\" ON \"PromptSamples\" (\"Name\")");
            await CreateIndexIfMissingAsync(conn, "IX_PromptSamples_Category",
                "CREATE INDEX \"IX_PromptSamples_Category\" ON \"PromptSamples\" (\"Category\")");
            await CreateIndexIfMissingAsync(conn, "IX_PromptSamples_IsBuiltIn",
                "CREATE INDEX \"IX_PromptSamples_IsBuiltIn\" ON \"PromptSamples\" (\"IsBuiltIn\")");
            await CreateIndexIfMissingAsync(conn, "IX_PromptSamples_UpdatedAt",
                "CREATE INDEX \"IX_PromptSamples_UpdatedAt\" ON \"PromptSamples\" (\"UpdatedAt\")");

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

            await CreateTableIfMissingAsync(conn, "HumanParticipants", """
                CREATE TABLE "HumanParticipants" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "RoomId" TEXT NOT NULL DEFAULT '',
                    "Name" TEXT NOT NULL DEFAULT '',
                    "IsPlayerCharacter" INTEGER NOT NULL DEFAULT 0,
                    "AppearanceSummary" TEXT NOT NULL DEFAULT '',
                    "TtsVoice" TEXT NOT NULL DEFAULT '',
                    "AccentHex" TEXT NOT NULL DEFAULT '#4A90D9',
                    "BackgroundHex" TEXT NOT NULL DEFAULT '#DDE8F0',
                    "ParticipationMode" TEXT NOT NULL DEFAULT 'TurnParticipant',
                    "SortOrder" INTEGER NOT NULL DEFAULT 0,
                    "IsEnabled" INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY ("RoomId") REFERENCES "Rooms" ("Id") ON DELETE CASCADE
                )
                """);

            await CreateTableIfMissingAsync(conn, "DataTracking", """
                CREATE TABLE "DataTracking" (
                    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "RoomId" TEXT NOT NULL,
                    "AgentId" TEXT NULL,
                    "DataKey" TEXT NOT NULL,
                    "Value" TEXT NULL,
                    "ValueType" TEXT NOT NULL DEFAULT 'string',
                    "MinValue" REAL NULL,
                    "MaxValue" REAL NULL,
                    "Enabled" INTEGER NOT NULL DEFAULT 1,
                    "CreatedAt" TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                    FOREIGN KEY ("RoomId") REFERENCES "Rooms" ("Id") ON DELETE CASCADE,
                    FOREIGN KEY ("AgentId") REFERENCES "Agents" ("Id") ON DELETE CASCADE,
                    CHECK ("ValueType" IN ('string','int','float','decimal','bool','json')),
                    CHECK (length("DataKey") > 0),
                    CHECK ("Enabled" IN (0,1))
                );
                """);

            await CreateIndexIfMissingAsync(conn, "UX_DataTracking_Room_Agent_Key",
                """
                    CREATE UNIQUE INDEX "UX_DataTracking_Room_Agent_Key"
                    ON "DataTracking"("RoomId", "AgentId", "DataKey")
                    WHERE "AgentId" IS NOT NULL;
                """
                );
            await CreateIndexIfMissingAsync(conn, "UX_DataTracking_Room_Key_WhenNoAgent",
                """
                    CREATE UNIQUE INDEX "UX_DataTracking_Room_Key_WhenNoAgent"
                    ON "DataTracking" ("RoomId", "DataKey")
                    WHERE "AgentId" IS NULL;
                """);
            await AddColumnIfMissingAsync(conn, "DataTracking", "PromptText", "TEXT NULL");
            await AddColumnIfMissingAsync(conn, "DataTracking", "PrivilegedAgentPrompt", "TEXT NULL");
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    
    private static async Task DropTableAsync(System.Data.Common.DbConnection conn, string table)
    {
        using var drop = conn.CreateCommand();
        drop.CommandText = $"DROP TABLE \"{table}\"";
        await drop.ExecuteNonQueryAsync();
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
