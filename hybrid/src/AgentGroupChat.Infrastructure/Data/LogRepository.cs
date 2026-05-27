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

    public async Task ClearAsync()
    {
        await _db.LogEntries.ExecuteDeleteAsync();
    }

    public async Task<List<LogEntry>> QueryAsync(string? category, string? source, int limit)
    {
        var query = _db.LogEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(l => l.Category == category);
        if (!string.IsNullOrWhiteSpace(source))
            query = query.Where(l => l.Source == source);
        var entities = await query.OrderByDescending(l => l.Id).Take(limit).ToListAsync();
        entities.Reverse();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task<List<string>> GetSourcesAsync()
    {
        return await _db.LogEntries
            .Select(l => l.Source)
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();
    }
}
