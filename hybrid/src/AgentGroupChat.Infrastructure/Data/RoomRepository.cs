using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentGroupChat.Infrastructure.Data;

public sealed class RoomRepository : IRoomRepository
{
    private readonly AppDbContext _db;

    public RoomRepository(AppDbContext db) => _db = db;

    public async Task<List<RoomConfig>> GetAllAsync(string userId)
    {
        var entities = await _db.Rooms.Where(r => r.UserId == userId).Include(r => r.Agents).Include(r => r.DataTrackers)
            .OrderBy(r => r.SortOrder).AsNoTracking().ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task<RoomConfig?> GetAsync(string id, string userId)
    {
        var entity = await _db.Rooms.Where(r => r.UserId == userId).Include(r => r.Agents)
            .AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return entity is null ? null : EntityMapper.ToDomain(entity);
    }

    public async Task SaveAsync(RoomConfig room)
    {
        var existing = await _db.Rooms.Where(r => r.UserId == room.UserId).Include(r => r.Agents).Include(d => d.DataTrackers)
            .FirstOrDefaultAsync(r => r.Id == room.Id);

        if (existing is null)
        {
            _db.Rooms.Add(EntityMapper.ToEntity(room));
        }
        else
        {
            var entity = EntityMapper.ToEntity(room);
            _db.Entry(existing).CurrentValues.SetValues(entity);

            var existingAgentIds = existing.Agents.Select(a => a.Id).ToHashSet();
            var incomingAgentIds = room.Agents.Select(a => a.Id).ToHashSet();

            foreach (var removed in existing.Agents.Where(a => !incomingAgentIds.Contains(a.Id)).ToList())
                _db.Agents.Remove(removed);

            foreach (var agent in room.Agents)
            {
                var existingAgent = existing.Agents.FirstOrDefault(a => a.Id == agent.Id);
                if (existingAgent is null)
                    _db.Agents.Add(EntityMapper.ToEntity(agent));
                else
                    _db.Entry(existingAgent).CurrentValues.SetValues(EntityMapper.ToEntity(agent));
            }

            var existingDataTrackers = existing.DataTrackers.Select(x => x.Id).ToHashSet();
            var incomingDataTrackers = room.DataTrackers.Select(x => x.Id).ToHashSet();

            foreach (var removed in existing.DataTrackers.Where(x => !incomingDataTrackers.Contains(x.Id)).ToList())
                _db.DataTrackers.Remove(removed);

            foreach (var tracker in room.DataTrackers)
            {
                var existingTracker = existing.DataTrackers.FirstOrDefault(x => x.Id == tracker.Id);
                if (existingTracker is null)
                {
                    _db.DataTrackers.Add(EntityMapper.ToEntity(tracker));
                }
                else
                {
                    _db.Entry(existingTracker).CurrentValues.SetValues(EntityMapper.ToEntity(tracker));
                }
            }
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id, string userId)
    {
        var entity = await _db.Rooms.Where(r => r.UserId == userId).Include(r => r.Agents).FirstOrDefaultAsync(r => r.Id == id);
        if (entity is not null)
        {
            _db.Rooms.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task SeedRoom(string userId)
    {
        if (await _db.Rooms.Where(x => x.UserId == userId).AnyAsync())
        {
            return;
        }
        var seedRooms = await CreateRoomSeeds(userId);
        var seedRoomEntities = seedRooms.Select(EntityMapper.ToEntity).ToList();
        _db.Rooms.AddRange(seedRoomEntities);
        await _db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<RoomConfig>> CreateRoomSeeds(string userId)
    {
        var aiModel = await _db.AiModels.Where(x => x.Name == "Groq 8b Instant").FirstOrDefaultAsync();
        if (aiModel is null || String.IsNullOrWhiteSpace(aiModel.Id))
            return null;
        var prompts = await _db.PromptSamples.Where(x => x.Name == "Optimist" || x.Name == "Soft-Spoken Companion" || x.Name == "Summarizer Agent").ToListAsync();
        if (prompts.Count != 3)
        {
            return null;
        }

        return [
            new RoomConfig
            {
                Name = "Sample Room",
                MaxTokens = 512,
                Topic = "Talk about anything interesting.",
                RecentTurnsWindow = 3,
                WaitForUserReply = true,
                PauseAfterEveryReply = false,
                AgentDelaySeconds = 10,
                UserCompactionBudget = 2000,
                SummarizerModelId = aiModel.Id,
                UseSummarizer = true,
                StoreSharedRoomMemory = true,
                StoreDurableMemory = false,
                StoreLongTermArchives = false,
                SummarizationLevel = "Moderate",
                SummarizerMaxTokens = 500,
                SummarizerMaxLines = 25,
                SummarizerMaxCharacters = 5600,
                SummarizerBroaderTurns = 6,
                SummarizerPromptOverride = "",
                TtsEnabledOverride = false,
                TtsProviderOverride = "",
                TtsFallbackVoice = "",
                TtsUserVoice = "",
                EnableSceneImageGeneration = false,
                UseCreativeImageGeneration = false,
                SceneImageModelId = "",
                SceneImageStyleNotes = "",
                SceneImageNegativePrompt = "",
                MemoryModelId = aiModel.Id,
                MaxArchivedScenes = 50,
                EnableSceneArchive = true,
                EnablePrivilegedActions = false,
                EnableNpcSpawning = false,
                PrivilegedAgentId = "",
                NpcModelId = aiModel.Id,
                NpcDefaultMaleVoice = "",
                NpcDefaultFemaleVoice = "",
                NpcMaxTokens = 512,
                NpcCompactionBudget = 1400,
                NpcBaseInstructions = "",
                MaxConcurrentNpcs = 2,
                SortOrder = 0,
                Agents = new() {
                    new AgentConfig() {
                        Name = "The Optimist",
                        ModelId = aiModel.Id,
                        SystemPrompt = "",
                        IsEnabled = true,
                        MaxTokensOverride = 512,
                        CompactionBudget = 300,
                        AccentHex = "#6E5AA6",
                        BackgroundHex = "#ECE6FA",
                        TtsVoice = "",
                        AppearanceSummary = "",
                        UseShortTermMemoryStorage = false,
                        UseLongTermMemoryStorage = false,
                        IsNpc = false,
                        SpawnedByAgentId = "",
                        IsTemporarilySuspended = false,
                        SuspendedByAgentId = "",
                        SuspendedUntilRound = 0,
                        SuspensionReason = "",
                        SortOrder = 0,
                        IsHumanParticipant = false,
                        PromptSampleId = prompts.Where(x => x.Name == "Optimist").First().Id,
                    },
                    new AgentConfig() {
                        Name = "The Companion",
                        ModelId = aiModel.Id,
                        SystemPrompt = "",
                        IsEnabled = true,
                        MaxTokensOverride = 512,
                        CompactionBudget = 300,
                        AccentHex = "#C56A54",
                        BackgroundHex = "#F9E5DE",
                        TtsVoice = "",
                        AppearanceSummary = "",
                        UseShortTermMemoryStorage = false,
                        UseLongTermMemoryStorage = false,
                        IsNpc = false,
                        SpawnedByAgentId = "",
                        IsTemporarilySuspended = false,
                        SuspendedByAgentId = "",
                        SuspendedUntilRound = 0,
                        SuspensionReason = "",
                        SortOrder = 0,
                        IsHumanParticipant = false,
                        PromptSampleId = prompts.Where(x => x.Name == "Soft-Spoken Companion").First().Id,
                    },
                },
                SharedRoomMemoryPromptSampleId = prompts.Where(x => x.Name == "Summarizer Agent").FirstOrDefault()!.Id,
                DurableMemoryPromptSampleId = prompts.Where(x => x.Name == "Summarizer Agent").FirstOrDefault()!.Id,
                NpcPromptSampleId = prompts.Where(x => x.Name == "Optimist").FirstOrDefault()!.Id,
                UserId = userId
            }
            ];
    }
}
