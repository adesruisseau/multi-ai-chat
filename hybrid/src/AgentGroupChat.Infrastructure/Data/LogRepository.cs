using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentGroupChat.Infrastructure.Data;

public sealed class LogRepository : ILogRepository
{
    private readonly AppDbContext _db;

    public LogRepository(AppDbContext db) => _db = db;

    public async Task AppendAsync(LogEntry entry)
    {
        _db.LogEntries.Add(EntityMapper.ToEntity(entry));
        await _db.SaveChangesAsync();
    }

    public async Task ClearAsync(string roomId)
    {
        await _db.LogEntries.Where(x => x.RoomId == roomId).ExecuteDeleteAsync();
    }

    public async Task<List<LogEntry>> QueryAsync(string? category, string? source, int limit, string roomId)
    {
        var query = _db.LogEntries.Where(x => x.RoomId == roomId).AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(l => l.Category == category);
        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(l => l.Source == source);
        var entities = await query
            .OrderByDescending(l => l.Id)
            .Take(limit)
            .OrderBy(l => l.Id)
            .ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task<List<string>> GetSourcesAsync(string roomId)
    {
        return await _db.LogEntries.Where(x => x.RoomId == roomId)
            .Select(l => l.Source)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();
    }
}
