using AgentGroupChat.Infrastructure.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AgentGroupChat.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppSettingsEntity> AppSettings => Set<AppSettingsEntity>();
    public DbSet<AiConnectionEntity> AiConnections => Set<AiConnectionEntity>();
    public DbSet<AiModelEntity> AiModels => Set<AiModelEntity>();
    public DbSet<ImageConnectionEntity> ImageConnections => Set<ImageConnectionEntity>();
    public DbSet<ImageModelEntity> ImageModels => Set<ImageModelEntity>();
    public DbSet<PromptSampleEntity> PromptSamples => Set<PromptSampleEntity>();
    public DbSet<RoomEntity> Rooms => Set<RoomEntity>();
    public DbSet<AgentEntity> Agents => Set<AgentEntity>();
    public DbSet<HumanParticipantEntity> HumanParticipants => Set<HumanParticipantEntity>();
    public DbSet<TranscriptTurnEntity> TranscriptTurns => Set<TranscriptTurnEntity>();
    public DbSet<MemoryBlockEntity> MemoryBlocks => Set<MemoryBlockEntity>();
    public DbSet<LogEntryEntity> LogEntries => Set<LogEntryEntity>();
    public DbSet<SceneArchiveEntity> SceneArchives => Set<SceneArchiveEntity>();
    public DbSet<DataTrackerEntity> DataTrackers => Set<DataTrackerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RoomEntity>()
            .HasMany(r => r.Agents)
            .WithOne(a => a.Room)
            .HasForeignKey(a => a.RoomId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoomEntity>()
            .HasMany(r => r.HumanParticipants)
            .WithOne(h => h.Room)
            .HasForeignKey(h => h.RoomId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RoomEntity>()
            .HasMany(r => r.DataTrackers)
            .WithOne(h => h.Room)
            .HasForeignKey(h => h.RoomId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TranscriptTurnEntity>()
            .HasIndex(t => t.RoomId);

        modelBuilder.Entity<MemoryBlockEntity>()
            .HasIndex(m => new { m.RoomId, m.AgentId, m.Kind })
            .IsUnique();

        modelBuilder.Entity<PromptSampleEntity>()
            .HasIndex(p => p.Name);

        modelBuilder.Entity<PromptSampleEntity>()
            .HasIndex(p => p.Category);

        modelBuilder.Entity<PromptSampleEntity>()
            .HasIndex(p => p.IsBuiltIn);

        modelBuilder.Entity<PromptSampleEntity>()
            .HasIndex(p => p.UpdatedAt);

        modelBuilder.Entity<ImageConnectionEntity>()
            .HasIndex(c => c.Name);

        modelBuilder.Entity<ImageModelEntity>()
            .HasIndex(m => m.Name);

        modelBuilder.Entity<ImageModelEntity>()
            .HasIndex(m => m.ConnectionId);

        modelBuilder.Entity<LogEntryEntity>()
            .HasIndex(l => l.Category);

        modelBuilder.Entity<LogEntryEntity>()
            .HasIndex(l => l.Source);

        modelBuilder.Entity<SceneArchiveEntity>()
            .HasIndex(s => s.RoomId);

        modelBuilder.Entity<SceneArchiveEntity>()
            .HasIndex(s => new { s.RoomId, s.RoundNumber });

        modelBuilder.Entity<DataTrackerEntity>()
            .HasIndex(d => new { d.RoomId, d.AgentId, d.DataKey })
            .IsUnique()
            .HasFilter("\"AgentId\" IS NOT NULL");

        modelBuilder.Entity<DataTrackerEntity>()
            .HasIndex(d => new { d.RoomId, d.DataKey })
            .IsUnique()
            .HasFilter("\"AgentId\" IS NULL");

        base.OnModelCreating(modelBuilder);
    }
}
