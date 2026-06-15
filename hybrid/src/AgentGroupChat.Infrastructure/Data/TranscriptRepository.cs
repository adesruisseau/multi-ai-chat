using AgentGroupChat.Core.Models.Domain;
using AgentGroupChat.Core.Realtime;
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

    public async Task<ChatUpdateResult> AppendAsync(TranscriptTurn turn)
    {
        try
        {
            _db.TranscriptTurns.Add(EntityMapper.ToEntity(turn));
            await _db.SaveChangesAsync();

            return new ChatUpdateResult(true, turn.RoomId, null);
        }
        catch (Exception ex)
        {
            return new ChatUpdateResult(false, turn.RoomId, "Failed to append new message");
        }
    
    }

    public async Task<ChatUpdateResult> ClearAsync(string roomId)
    {
        try
        {
            await _db.TranscriptTurns.Where(t => t.RoomId == roomId).ExecuteDeleteAsync();
            return new ChatUpdateResult(true, roomId, null);
        }
        catch (Exception ex) 
        {
            return new ChatUpdateResult(false, roomId, "Failed to clear log");
        }
    }
}
