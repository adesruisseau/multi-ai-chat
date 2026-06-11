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

    public async Task<AppSettings> GetAsync()
    {
        var entity = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
        return entity is null ? new AppSettings() : EntityMapper.ToDomain(entity);
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var entity = await _db.AppSettings.FirstOrDefaultAsync();
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

    public async Task<List<AiConnection>> GetConnectionsAsync()
    {
        var entities = await _db.AiConnections.OrderBy(c => c.SortOrder).AsNoTracking().ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task SaveConnectionAsync(AiConnection connection)
    {
        var existing = await _db.AiConnections.FirstOrDefaultAsync(c => c.Id == connection.Id);
        var entity = EntityMapper.ToEntity(connection);
        if (existing is null)
            _db.AiConnections.Add(entity);
        else
            _db.Entry(existing).CurrentValues.SetValues(entity);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteConnectionAsync(string id)
    {
        var entity = await _db.AiConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (entity is not null)
        {
            _db.AiConnections.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<AiModel>> GetModelsAsync()
    {
        var entities = await _db.AiModels.OrderBy(m => m.SortOrder).AsNoTracking().ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task SaveModelAsync(AiModel model)
    {
        var existing = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == model.Id);
        var entity = EntityMapper.ToEntity(model);
        if (existing is null)
            _db.AiModels.Add(entity);
        else
            _db.Entry(existing).CurrentValues.SetValues(entity);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteModelAsync(string id)
    {
        var entity = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == id);
        if (entity is not null)
        {
            _db.AiModels.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<List<ImageConnection>> GetImageConnectionsAsync()
    {
        var entities = await _db.ImageConnections.OrderBy(c => c.SortOrder).AsNoTracking().ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task SaveImageConnectionAsync(ImageConnection connection)
    {
        var existing = await _db.ImageConnections.FirstOrDefaultAsync(c => c.Id == connection.Id);
        var entity = EntityMapper.ToEntity(connection);
        if (existing is null)
            _db.ImageConnections.Add(entity);
        else
            _db.Entry(existing).CurrentValues.SetValues(entity);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteImageConnectionAsync(string id)
    {
        var entity = await _db.ImageConnections.FirstOrDefaultAsync(c => c.Id == id);
        if (entity is not null)
        {
            _db.ImageConnections.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<ImageConnection> GetImageConnectionAsync(string connectionId)
    {
        var entity = await _db.ImageConnections.Where(x => x.Id == connectionId).AsNoTracking().FirstOrDefaultAsync();
        
        if (entity is not null)
        {
            var imageConnection = EntityMapper.ToDomain(entity);
            return imageConnection;
        }
        return null;
    }

    public async Task<List<ImageModel>> GetImageModelsAsync()
    {
        var entities = await _db.ImageModels.OrderBy(m => m.SortOrder).AsNoTracking().ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }
    
    public async Task<ImageModel> GetImageModelByIdAsync(string modelId)
    {
        var entity = await _db.ImageModels.Where(x => x.Id == modelId || x.ModelId == modelId).AsNoTracking().FirstOrDefaultAsync();
        if (entity is not null)
        {
            var imageModel = EntityMapper.ToDomain(entity);
            return imageModel;
        }
        return null;
    }

    public async Task SaveImageModelAsync(ImageModel model)
    {
        var existing = await _db.ImageModels.FirstOrDefaultAsync(m => m.Id == model.Id);
        var entity = EntityMapper.ToEntity(model);
        if (existing is null)
            _db.ImageModels.Add(entity);
        else
            _db.Entry(existing).CurrentValues.SetValues(entity);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteImageModelAsync(string id)
    {
        var entity = await _db.ImageModels.FirstOrDefaultAsync(m => m.Id == id);
        if (entity is not null)
        {
            _db.ImageModels.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }

    public async Task SeedAiConnectionsIfEmptyAsync()
    {
        if (await _db.AiConnections.AnyAsync())
        {
            return;
        }

        var seedConnections = CreateConnectionSeeds().Select(EntityMapper.ToEntity).ToList();
        _db.AiConnections.AddRange(seedConnections);
        await _db.SaveChangesAsync();
    }

    public async Task SeedAiModelsIfEmptyAsync()
    {
        await SeedAiConnectionsIfEmptyAsync();
        if (await _db.AiModels.AnyAsync())
        {
            return;
        }
        var seedModels = await CreateModelSeeds();
        var seedModelEntities = seedModels.Select(EntityMapper.ToEntity).ToList();
        _db.AiModels.AddRange(seedModelEntities);
        await _db.SaveChangesAsync();
    }

    private static IReadOnlyList<AiConnection> CreateConnectionSeeds()
    {
        var now = DateTimeOffset.UtcNow;
        return
           [
                new AiConnection
                {
                    Name = "Groq",
                    Transport = LlmTransports.Groq,
                    Endpoint = "https://api.groq.com/openai/v1/chat/completions",
                    ApiKey = "YOUR_KEY_HERE",
                    SortOrder = 1
                },
                new AiConnection
                {
                    Name = "Gemini",
                    Transport = LlmTransports.Gemini,
                    Endpoint = "https://generativelanguage.googleapis.com/v1beta",
                    ApiKey = "YOUR_KEY_HERE",
                    SortOrder = 2
                }
            ];
    }

    private async Task<IReadOnlyList<AiModel>> CreateModelSeeds()
    {
        var now = DateTimeOffset.UtcNow;
        var connections = await _db.AiConnections.Where(x => x.Transport == LlmTransports.Groq || x.Transport == LlmTransports.Gemini).ToListAsync();
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
            },
            ];
        
    }
}
