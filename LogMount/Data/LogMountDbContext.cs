using LogMount.Models;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Data;

public class LogMountDbContext : DbContext
{
    public LogMountDbContext(DbContextOptions<LogMountDbContext> options)
        : base(options)
    {
    }

    public DbSet<RetryLogEntry> RetryLogEntries => Set<RetryLogEntry>();
    public DbSet<ExpensivePart> ExpensiveParts => Set<ExpensivePart>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RetryLogEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Date).HasMaxLength(50);
            entity.Property(x => x.Line).HasMaxLength(50);
            entity.Property(x => x.Language).HasMaxLength(50);
            entity.Property(x => x.OccurrenceTime).HasMaxLength(50);
            entity.Property(x => x.LotName).HasMaxLength(255);
            entity.Property(x => x.ErrorNo).HasMaxLength(50);
            entity.Property(x => x.ErrorName).HasMaxLength(255);
            entity.Property(x => x.Lane).HasMaxLength(50);
            entity.Property(x => x.Table).HasMaxLength(50);
            entity.Property(x => x.PartsNo).HasMaxLength(255);
            entity.Property(x => x.PartsName).HasMaxLength(255);
            entity.Property(x => x.HeadNo).HasMaxLength(50);
            entity.Property(x => x.NozzleType).HasMaxLength(100);
            entity.Property(x => x.FeederNo).HasMaxLength(50);
            entity.Property(x => x.FeederId).HasMaxLength(100);
            entity.Property(x => x.CartId).HasMaxLength(100);
            entity.Property(x => x.VisErrorNo).HasMaxLength(100);
            entity.Property(x => x.ErrorVacuum).HasMaxLength(100);

            entity.HasIndex(x => x.PartsName);
            entity.HasIndex(x => x.ErrorNo);
            entity.HasIndex(x => x.Line);
        });

        modelBuilder.Entity<ExpensivePart>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PartsName).HasMaxLength(255);
            entity.Property(x => x.Cost).HasColumnType("decimal(18,2)");

            entity.HasIndex(x => x.PartsName).IsUnique();
        });
    }
}
