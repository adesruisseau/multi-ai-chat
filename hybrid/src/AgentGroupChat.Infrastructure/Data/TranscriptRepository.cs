using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AgentGroupChat.Infrastructure.Data;

public sealed class TranscriptRepository : ITranscriptRepository
{
    private readonly AppDbContext _db;

    public TranscriptRepository(AppDbContext db) => _db = db;

    public async Task<List<TranscriptTurn>> GetAsync(string roomId)
    {
        var entities = await _db.TranscriptTurns
            .Where(t => t.RoomId == roomId)
            .OrderBy(t => t.Id)
            .AsNoTracking()
            .ToListAsync();
        return entities.Select(EntityMapper.ToDomain).ToList();
    }

    public async Task AppendAsync(TranscriptTurn turn)
    {
        _db.TranscriptTurns.Add(EntityMapper.ToEntity(turn));
        await _db.SaveChangesAsync();
    }

    public async Task ClearAsync(string roomId)
    {
        await _db.TranscriptTurns.Where(t => t.RoomId == roomId).ExecuteDeleteAsync();
    }
}
