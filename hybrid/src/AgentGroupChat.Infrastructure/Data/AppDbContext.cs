using AgentGroupChat.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentGroupChat.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppSettingsEntity> AppSettings => Set<AppSettingsEntity>();
    public DbSet<AiConnectionEntity> AiConnections => Set<AiConnectionEntity>();
    public DbSet<AiModelEntity> AiModels => Set<AiModelEntity>();
    public DbSet<RoomEntity> Rooms => Set<RoomEntity>();
    public DbSet<AgentEntity> Agents => Set<AgentEntity>();
    public DbSet<TranscriptTurnEntity> TranscriptTurns => Set<TranscriptTurnEntity>();
    public DbSet<MemoryBlockEntity> MemoryBlocks => Set<MemoryBlockEntity>();
    public DbSet<LogEntryEntity> LogEntries => Set<LogEntryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RoomEntity>()
            .HasMany(r => r.Agents)
            .WithOne(a => a.Room)
            .HasForeignKey(a => a.RoomId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TranscriptTurnEntity>()
            .HasIndex(t => t.RoomId);

        modelBuilder.Entity<MemoryBlockEntity>()
            .HasIndex(m => new { m.RoomId, m.AgentId, m.Kind })
            .IsUnique();

        modelBuilder.Entity<LogEntryEntity>()
            .HasIndex(l => l.Category);

        modelBuilder.Entity<LogEntryEntity>()
            .HasIndex(l => l.Source);
    }
}
