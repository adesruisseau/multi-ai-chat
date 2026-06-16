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
            var entity = EntityMapper.ToEntity(turn);
            _db.TranscriptTurns.Add(entity);
            await _db.SaveChangesAsync();

            return ChatUpdateResult.Success(EntityMapper.ToDomain(entity));
        }
        catch (Exception)
        {
            return ChatUpdateResult.Failure("Failed to append new message", turn.RoomId);
        }
    
    }

    public async Task<ChatUpdateResult> ClearAsync(string roomId)
    {
        try
        {
            await _db.TranscriptTurns.Where(t => t.RoomId == roomId).ExecuteDeleteAsync();
            return ChatUpdateResult.Success(roomId);
        }
        catch (Exception) 
        {
            return ChatUpdateResult.Failure("Failed to clear log", roomId);
        }
    }
}
