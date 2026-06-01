using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using AgentGroupChat.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

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
}
