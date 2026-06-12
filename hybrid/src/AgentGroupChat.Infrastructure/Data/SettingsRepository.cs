using AgentGroupChat.Core;
using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using NAudio.MediaFoundation;
using System.Net;

namespace AgentGroupChat.Infrastructure.Data;

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly AppDbContext _db;

    public SettingsRepository(AppDbContext db) => _db = db;

    public async Task<AppSettings> GetAsync(string userId)
    {
        var entity = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        return entity is null ? new AppSettings { UserId = userId } : EntityMapper.ToDomain(entity);
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var entity = await _db.AppSettings.FirstOrDefaultAsync(x => x.UserId == settings.UserId);
        if (entity is null)
        {
            entity = new AppSettingsEntity();
            EntityMapper.ApplyTo(settings, entity);
            _db.AppSettings.Add(entity);
        }
        else
        {
            EntityMapper.ApplyTo(settings, entity);
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<AiConnection>> GetConnectionsAsync(string userId)
    {
        var entities = await _db.AiConnections.Where(x => x.UserId == userId).OrderBy(c => c.SortOrder).AsNoTracking().ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task SaveConnectionAsync(AiConnection connection)
    {
        var existing = await _db.AiConnections.FirstOrDefaultAsync(c => c.Id == connection.Id && c.UserId == connection.UserId);
        var entity = EntityMapper.ToEntity(connection);
        if (existing is null)
            _db.AiConnections.Add(entity);
        else
            _db.Entry(existing).CurrentValues.SetValues(entity);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteConnectionAsync(string id, string userId)
    {
        var entity = await _db.AiConnections.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (entity is not null)
        {
            _db.AiConnections.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<AiModel>> GetModelsAsync(string userId)
    {
        var entities = await _db.AiModels.Where(x => x.UserId == userId).OrderBy(m => m.SortOrder).AsNoTracking().ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task SaveModelAsync(AiModel model)
    {
        var existing = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == model.Id && m.UserId == model.UserId);
        var entity = EntityMapper.ToEntity(model);
        if (existing is null)
            _db.AiModels.Add(entity);
        else
            _db.Entry(existing).CurrentValues.SetValues(entity);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteModelAsync(string id, string userId)
    {
        var entity = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
        if (entity is not null)
        {
            _db.AiModels.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<ImageConnection>> GetImageConnectionsAsync(string userId)
    {
        var entities = await _db.ImageConnections.Where(x => x.UserId == userId).OrderBy(c => c.SortOrder).AsNoTracking().ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task SaveImageConnectionAsync(ImageConnection connection)
    {
        var existing = await _db.ImageConnections.FirstOrDefaultAsync(c => c.Id == connection.Id && c.UserId == connection.UserId);
        var entity = EntityMapper.ToEntity(connection);
        if (existing is null)
            _db.ImageConnections.Add(entity);
        else
            _db.Entry(existing).CurrentValues.SetValues(entity);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteImageConnectionAsync(string id, string userId)
    {
        var entity = await _db.ImageConnections.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (entity is not null)
        {
            _db.ImageConnections.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<ImageConnection> GetImageConnectionAsync(string connectionId, string userId)
    {
        var entity = await _db.ImageConnections.Where(x => x.Id == connectionId && x.UserId == userId).AsNoTracking().FirstOrDefaultAsync();
        
        if (entity is not null)
        {
            var imageConnection = EntityMapper.ToDomain(entity);
            return imageConnection;
        }
        return null;
    }

    public async Task<List<ImageModel>> GetImageModelsAsync(string userId)
    {
        var entities = await _db.ImageModels.Where(m => m.UserId == userId).OrderBy(m => m.SortOrder).AsNoTracking().ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }
    
    public async Task<ImageModel> GetImageModelByIdAsync(string modelId, string userId)
    {
        var entity = await _db.ImageModels.Where(x => (x.Id == modelId || x.ModelId == modelId) && x.UserId == userId).AsNoTracking().FirstOrDefaultAsync();
        if (entity is not null)
        {
            var imageModel = EntityMapper.ToDomain(entity);
            return imageModel;
        }
        return null;
    }

    public async Task SaveImageModelAsync(ImageModel model)
    {
        var existing = await _db.ImageModels.FirstOrDefaultAsync(m => m.Id == model.Id && m.UserId == model.UserId);
        var entity = EntityMapper.ToEntity(model);
        if (existing is null)
            _db.ImageModels.Add(entity);
        else
            _db.Entry(existing).CurrentValues.SetValues(entity);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteImageModelAsync(string id, string userId)
    {
        var entity = await _db.ImageModels.FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
        if (entity is not null)
        {
            _db.ImageModels.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task SeedAiConnectionsIfEmptyAsync(string userId)
    {
        if (await _db.AiConnections.Where(x => x.UserId == userId).AnyAsync())
        {
            return;
        }

        var seedConnections = CreateConnectionSeeds(userId).Select(EntityMapper.ToEntity).ToList();
        _db.AiConnections.AddRange(seedConnections);
        await _db.SaveChangesAsync();
    }

    public async Task SeedAiModelsIfEmptyAsync(string userId)
    {
        await SeedAiConnectionsIfEmptyAsync(userId);
        if (await _db.AiModels.Where(m => m.UserId == userId).AnyAsync())
        {
            return;
        }
        var seedModels = await CreateModelSeeds(userId);
        var seedModelEntities = seedModels.Select(EntityMapper.ToEntity).ToList();
        _db.AiModels.AddRange(seedModelEntities);
        await _db.SaveChangesAsync();
    }

    private static IReadOnlyList<AiConnection> CreateConnectionSeeds(string userId)
    {
        var now = DateTimeOffset.UtcNow;
        return
           [
                new AiConnection
                {
                    Name = "Groq",
                    Transport = LlmTransports.Groq,
                    Endpoint = "https://api.groq.com/openai/v1/chat/completions",
                    ApiKey = "",
                    SortOrder = 1,
                    UserId = userId
                },
                new AiConnection
                {
                    Name = "Gemini",
                    Transport = LlmTransports.Gemini,
                    Endpoint = "https://generativelanguage.googleapis.com/v1beta",
                    ApiKey = "",
                    SortOrder = 2,
                    UserId = userId
                }
            ];
    }

    private async Task<IReadOnlyList<AiModel>> CreateModelSeeds(string userId)
    {
        var now = DateTimeOffset.UtcNow;
        var connections = await _db.AiConnections
            .Where(x => x.UserId == userId)
            .Where(x => x.Transport == LlmTransports.Groq || x.Transport == LlmTransports.Gemini).ToListAsync();
        if (connections.Count() == 0)
        {
            return null;
        }
        
        return [
            new AiModel
            {
                Name = "Groq 8b Instant",
                MaxTokens = 512,
                Temperature = 0.7m,
                ModelId = "llama-3.1-8b-instant",
                Notes = "",
                SortOrder = 0,
                ConnectionId = connections.Where(x => x.Transport == LlmTransports.Groq).FirstOrDefault()!.Id,
                UserId = userId,
            },
            new AiModel
            {
                Name = "Groq 70b Versatile",
                MaxTokens = 512,
                Temperature = 0.7m,
                ModelId = "llama-3.3-70b-versatile",
                Notes = "",
                SortOrder = 0,
                ConnectionId = connections.Where(x => x.Transport == LlmTransports.Groq).FirstOrDefault()!.Id,
                UserId = userId,
            },
            new AiModel
            {
                Name = "Gemini 3.1",
                MaxTokens = 512,
                Temperature = 0.7m,
                ModelId = "gemini-3.1-flash-lite",
                Notes = "",
                SortOrder = 0,
                ConnectionId = connections.Where(x => x.Transport == LlmTransports.Gemini).FirstOrDefault()!.Id,
                UserId = userId,
            },
            new AiModel
            {
                Name = "Gemma 4 31b",
                MaxTokens = 512,
                Temperature = 0.7m,
                ModelId = "gemma-4-31b-it",
                Notes = "",
                SortOrder = 0,
                ConnectionId = connections.Where(x => x.Transport == LlmTransports.Gemini).FirstOrDefault()!.Id,
                UserId = userId,
            },
            ];
        
    }
}
