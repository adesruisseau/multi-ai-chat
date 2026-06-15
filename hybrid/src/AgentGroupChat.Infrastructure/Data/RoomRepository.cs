using AgentGroupChat.Core;
using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Realtime;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Entities;
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

    public async Task<List<RoomConfig>> GetInvitedToRooms(string userId)
    {
        var roomIds = await _db.RoomMemberships
            .Where(x => x.UserId == userId)
            .Select(x => x.RoomId)
            .ToHashSetAsync();

        var roomEntities = await _db.Rooms.Where(r => roomIds.Contains(r.Id)).Include(r => r.Agents).Include(r => r.DataTrackers)
            .OrderBy(r => r.SortOrder).AsNoTracking().ToListAsync();

        return roomEntities.Select(EntityMapper.ToDomain).ToList();
    }


    public async Task<RoomConfig?> GetAsync(string id, string userId)
    {
        var entity = await _db.Rooms
            .Include(r => r.Agents)
            .Include(r => r.DataTrackers)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id &&
                (r.UserId == userId || _db.RoomMemberships.Any(m => m.RoomId == r.Id && m.UserId == userId)));
        return entity is null ? null : EntityMapper.ToDomain(entity);
    }

    public async Task<RoomUpdateResult> SaveAsync(RoomConfig room)
    {
        var existing = await _db.Rooms.Where(r => r.UserId == room.UserId).Include(r => r.Agents).Include(d => d.DataTrackers)
            .FirstOrDefaultAsync(r => r.Id == room.Id);

        if (existing is null)
        {
            _db.Rooms.Add(EntityMapper.ToEntity(room));
            _db.RoomMemberships.Add(new RoomMembershipEntity
            {
                RoomId = room.Id,
                UserId = room.UserId,
                Role = RoomMembershipRoles.Owner,
                JoinedAt = DateTimeOffset.UtcNow
            });

            return new RoomUpdateResult(true, room.Id, null);
        }
        else
        {
            var entity = EntityMapper.ToEntity(room);
            _db.Entry(existing).CurrentValues.SetValues(entity);

            var ownerMembership = await _db.RoomMemberships.FirstOrDefaultAsync(m => m.RoomId == room.Id && m.UserId == room.UserId);
            if (ownerMembership is null)
            {
                _db.RoomMemberships.Add(new RoomMembershipEntity
                {
                    RoomId = room.Id,
                    UserId = room.UserId,
                    Role = RoomMembershipRoles.Owner,
                    JoinedAt = DateTimeOffset.UtcNow
                });
            }
            else if (!string.Equals(ownerMembership.Role, RoomMembershipRoles.Owner, StringComparison.Ordinal))
            {
                ownerMembership.Role = RoomMembershipRoles.Owner;
            }

            var existingAgentIds = existing.Agents.Select(a => a.Id).ToHashSet();
            var incomingAgentIds = room.Agents.Select(a => a.Id).ToHashSet();

            var removedAgents = existing.Agents
                .Where(a => !incomingAgentIds.Contains(a.Id))
                .ToList();

            foreach (var removed in removedAgents)
            {
                _db.Agents.Remove(removed);
            }

            var removedParticipantUserIds = removedAgents
                .Where(a => a.IsHumanParticipant)
                .Select(a => a.UserId)
                .Where(userId => !string.IsNullOrWhiteSpace(userId))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            removedParticipantUserIds.Remove(room.UserId);

            var remainingParticipantUserIds = room.Agents
                .Where(a => a.IsHumanParticipant)
                .Select(a => a.UserId)
                .Where(userId => !string.IsNullOrWhiteSpace(userId))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            removedParticipantUserIds.ExceptWith(remainingParticipantUserIds);

            if (removedParticipantUserIds.Count > 0)
            {
                var membershipsToRemove = await _db.RoomMemberships
                    .Where(m => m.RoomId == room.Id)
                    .Where(m => removedParticipantUserIds.Contains(m.UserId))
                    .Where(m => m.Role == RoomMembershipRoles.Player)
                    .ToListAsync();

                _db.RoomMemberships.RemoveRange(membershipsToRemove);
            }

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
        return new RoomUpdateResult(true, room.Id, null);
    }

    public async Task<RoomDeleteResult> DeleteAsync(string id, string userId)
    {
        var entity = await _db.Rooms.Where(r => r.UserId == userId).Include(r => r.Agents).FirstOrDefaultAsync(r => r.Id == id);
        if (entity is not null)
        {
            var memberships = await _db.RoomMemberships.Where(m => m.RoomId == id).ToListAsync();
            if (memberships.Count > 0)
            {
                _db.RoomMemberships.RemoveRange(memberships);
            }
            _db.Rooms.Remove(entity);
            await _db.SaveChangesAsync();
            return new RoomDeleteResult(true, null, null);
        }
        return new RoomDeleteResult(false, id, "The room was not found.");
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
        _db.RoomMemberships.AddRange(seedRooms.Select(room => new RoomMembershipEntity
        {
            RoomId = room.Id,
            UserId = room.UserId,
            Role = RoomMembershipRoles.Owner,
            JoinedAt = DateTimeOffset.UtcNow
        }));
        await _db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<RoomConfig>> CreateRoomSeeds(string userId)
    {
        var aiModel = await _db.AiModels.Where(x => x.UserId == userId && x.Name == "Groq 8b Instant").FirstOrDefaultAsync();
        if (aiModel is null || String.IsNullOrWhiteSpace(aiModel.Id))
            return null;
        var prompts = await _db.PromptSamples.Where(x => x.UserId == userId && (x.Name == "Optimist" || x.Name == "Soft-Spoken Companion" || x.Name == "Summarizer Agent")).ToListAsync();
        if (prompts.Count != 3)
        {
            return null;
        }

        return [
            new RoomConfig
            {
                UserId = userId,
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
                        ColorTheme = "Ocean"
                    },
                    new AgentConfig() {
                        Name = "The Companion",
                        ModelId = aiModel.Id,
                        SystemPrompt = "",
                        IsEnabled = true,
                        MaxTokensOverride = 512,
                        CompactionBudget = 300,
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
                        ColorTheme = "Terracotta"
                    },
                },
                SharedRoomMemoryPromptSampleId = prompts.Where(x => x.Name == "Summarizer Agent").FirstOrDefault()!.Id,
                DurableMemoryPromptSampleId = prompts.Where(x => x.Name == "Summarizer Agent").FirstOrDefault()!.Id,
                NpcPromptSampleId = prompts.Where(x => x.Name == "Optimist").FirstOrDefault()!.Id
            }
            ];
    }
}
