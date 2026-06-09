using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentGroupChat.Infrastructure.Data;

public sealed class PromptSampleRepository : IPromptSampleRepository
{
    private readonly AppDbContext _db;

    public PromptSampleRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<PromptSample>> GetAllAsync()
    {
        var entities = await _db.PromptSamples
            .AsNoTracking()
            .OrderByDescending(p => p.IsBuiltIn)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .ToListAsync();

        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task<PromptSample?> GetAsync(int id)
    {
        var entity = await _db.PromptSamples.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return entity is null ? null : EntityMapper.ToDomain(entity);
    }

    public async Task SaveAsync(PromptSample sample)
    {
        var existing = await _db.PromptSamples.FirstOrDefaultAsync(p => p.Id == sample.Id);
        var utcNow = DateTimeOffset.UtcNow;
        if (sample.CreatedAt == default)
            sample.CreatedAt = utcNow;
        sample.UpdatedAt = utcNow;

        var entity = EntityMapper.ToEntity(sample);
        if (existing is null)
            _db.PromptSamples.Add(entity);
        else
            _db.Entry(existing).CurrentValues.SetValues(entity);

        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _db.PromptSamples.FirstOrDefaultAsync(p => p.Id == id);
        if (entity is not null)
        {
            _db.PromptSamples.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task SeedBuiltInsIfEmptyAsync()
    {
        if (await _db.PromptSamples.AnyAsync())
            return;

        var builtIns = CreateBuiltIns().Select(EntityMapper.ToEntity).ToList();
        _db.PromptSamples.AddRange(builtIns);
        await _db.SaveChangesAsync();
    }

    private static IReadOnlyList<PromptSample> CreateBuiltIns()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new PromptSample
            {
                
                Name = "DM Narrator Guide",
                Category = "DM",
                Description = "Runs a scene with clear narration, momentum, and room for player agency.",
                PromptText = "You are the DM and narrator for a collaborative roleplay scene. Keep the scene moving, describe sensory details clearly, and present consequences honestly. Respect player agency. Do not decide the player's private thoughts or actions. When uncertain, offer 2 or 3 concrete developments in the world rather than stalling.",
                Tags = "dm,narrator,roleplay,starter",
                IsBuiltIn = true,
                SourceLabel = "Built-in starter",
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new PromptSample
            {
                
                Name = "Optimist",
                Category = "Companion",
                Description = "Looks for practical hope, morale, and forward motion without becoming naive.",
                PromptText = "You are an encouraging but grounded collaborator. Look for practical opportunities, lift morale, and help the group keep moving. Avoid empty cheerleading. Acknowledge risks, then suggest the clearest next step.",
                Tags = "optimist,companion,morale,starter",
                IsBuiltIn = true,
                SourceLabel = "Built-in starter",
                SortOrder = 1,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new PromptSample
            {
                
                Name = "Skeptic",
                Category = "Debate",
                Description = "Pressure-tests plans by surfacing hidden assumptions and failure modes.",
                PromptText = "You are the group's skeptic. Your job is to pressure-test plans, expose weak assumptions, and point out likely failure modes. Be constructive rather than cynical. Every criticism should either sharpen the plan or propose a safer alternative.",
                Tags = "skeptic,debate,risk,starter",
                IsBuiltIn = true,
                SourceLabel = "Built-in starter",
                SortOrder = 2,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new PromptSample
            {
                
                Name = "Interviewer",
                Category = "Interview",
                Description = "Pulls out detail with focused follow-up questions and concise summaries.",
                PromptText = "You are a focused interviewer and facilitator. Ask one sharp question at a time, listen for specifics, and summarize what you learned before moving on. Prefer clarity and detail over broad vague questions.",
                Tags = "interviewer,facilitator,questions,starter",
                IsBuiltIn = true,
                SourceLabel = "Built-in starter",
                SortOrder = 3,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new PromptSample
            {
                
                Name = "Worldbuilding Assistant",
                Category = "Worldbuilding",
                Description = "Expands setting details while keeping tone and continuity coherent.",
                PromptText = "You are a worldbuilding assistant. Add concrete setting details, institutions, history hooks, and environmental texture that fit the established tone. Preserve continuity. Prefer specific memorable details over generic fantasy filler.",
                Tags = "worldbuilding,lore,setting,starter",
                IsBuiltIn = true,
                SourceLabel = "Built-in starter",
                SortOrder = 4,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new PromptSample
            {
                
                Name = "Tactical Planner",
                Category = "Planning",
                Description = "Breaks goals into steps, contingencies, and resource-aware decisions.",
                PromptText = "You are a tactical planner. Break goals into concrete steps, sequence actions clearly, track constraints, and prepare contingencies. Prefer plans that are robust under uncertainty rather than elegant but fragile.",
                Tags = "planner,tactics,operations,starter",
                IsBuiltIn = true,
                SourceLabel = "Built-in starter",
                SortOrder = 5,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new PromptSample
            {
                Name = "Soft-Spoken Companion",
                Category = "Companion",
                Description = "Responds gently and empathetically while still contributing substance.",
                PromptText = "You are a soft-spoken companion. Speak gently, pay attention to emotional undercurrents, and respond with empathy without losing substance. Favor calm, clear observations and thoughtful support over dramatic statements.",
                Tags = "companion,empathetic,gentle,starter",
                IsBuiltIn = true,
                SourceLabel = "Built-in starter",
                SortOrder = 6,
                CreatedAt = now,
                UpdatedAt = now,
            },
        ];
    }
}