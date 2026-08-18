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
    public DbSet<ErrorLogEntry> ErrorLogEntries => Set<ErrorLogEntry>();
    public DbSet<ExpensivePart> ExpensiveParts => Set<ExpensivePart>();
    public DbSet<AutoImportState> AutoImportStates => Set<AutoImportState>();
    public DbSet<RetryImprove> RetryImproves => Set<RetryImprove>();
    public DbSet<ErrorImprove> ErrorImproves => Set<ErrorImprove>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<RequestLogEntry> RequestLogEntries => Set<RequestLogEntry>();

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
            entity.Property(x => x.SourceFileName).HasMaxLength(255);
            entity.Property(x => x.UploadBatchId).HasMaxLength(50);

            entity.HasIndex(x => x.Date);
            entity.HasIndex(x => new { x.Date, x.UploadedAt, x.Id })
                .IsDescending(false, true, false);
            entity.HasIndex(x => x.PartsName);
            entity.HasIndex(x => x.ErrorNo);
            entity.HasIndex(x => x.Line);
            entity.HasIndex(x => x.UploadedAt);
        });

        modelBuilder.Entity<AutoImportState>(entity =>
        {
            entity.HasKey(x => x.JobName);
            entity.Property(x => x.JobName).HasMaxLength(100);
            entity.Property(x => x.LastRunDate).HasMaxLength(10);
        });

        modelBuilder.Entity<ExpensivePart>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PartsName).HasMaxLength(255);
            entity.Property(x => x.Cost).HasColumnType("decimal(18,2)");
            entity.Property(x => x.SourceFileName).HasMaxLength(255);

            entity.HasIndex(x => x.PartsName);
            entity.HasIndex(x => x.UploadedAt);
        });

        modelBuilder.Entity<ErrorLogEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Date).HasMaxLength(50);
            entity.Property(x => x.EventDate).HasMaxLength(50);
            entity.Property(x => x.Line).HasMaxLength(50);
            entity.Property(x => x.Lane).HasMaxLength(50);
            entity.Property(x => x.Table).HasMaxLength(50);
            entity.Property(x => x.Error).HasMaxLength(255);
            entity.Property(x => x.EventNo).HasMaxLength(50);
            entity.Property(x => x.ProgramName).HasMaxLength(500);
            entity.Property(x => x.Details).HasMaxLength(1000);
            entity.Property(x => x.SourceFileName).HasMaxLength(255);
            entity.Property(x => x.UploadBatchId).HasMaxLength(50);

            entity.HasIndex(x => x.Date);
            entity.HasIndex(x => new { x.Date, x.UploadedAt, x.Id })
                .IsDescending(false, true, false);
            entity.HasIndex(x => x.Error);
            entity.HasIndex(x => x.Line);
            entity.HasIndex(x => x.UploadedAt);
        });

        modelBuilder.Entity<RequestLogEntry>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Date).HasMaxLength(50);
            entity.Property(x => x.Shift).HasMaxLength(20);
            entity.Property(x => x.Line).HasMaxLength(50);
            entity.Property(x => x.Process).HasMaxLength(100);
            entity.Property(x => x.ModelSuffix).HasMaxLength(255);
            entity.Property(x => x.Chassis).HasMaxLength(255);
            entity.Property(x => x.Board).HasMaxLength(100);
            entity.Property(x => x.PartAssy).HasMaxLength(255);
            entity.Property(x => x.WorkOrder).HasMaxLength(255);
            entity.Property(x => x.PartNo).HasMaxLength(255);
            entity.Property(x => x.PidOrLot).HasMaxLength(255);
            entity.Property(x => x.Unit).HasMaxLength(50);
            entity.Property(x => x.PQty).HasColumnType("decimal(18, 3)");
            entity.Property(x => x.RQty).HasColumnType("decimal(18, 3)");
            entity.Property(x => x.AmtOnRequest).HasColumnType("decimal(18, 2)");
            entity.Property(x => x.Remarks).HasMaxLength(1000);
            entity.Property(x => x.Department).HasMaxLength(255);
            entity.Property(x => x.StatusRemarks).HasMaxLength(1000);
            entity.Property(x => x.SourceFileName).HasMaxLength(255);
            entity.Property(x => x.UploadBatchId).HasMaxLength(50);

            entity.HasIndex(x => x.Date);
            entity.HasIndex(x => x.Line);
            entity.HasIndex(x => x.PartNo);
            entity.HasIndex(x => x.RQty);
            entity.HasIndex(x => x.UploadedAt);
        });

        modelBuilder.Entity<RetryImprove>(entity =>
        {
            entity.ToTable("RetryImprove");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PartsName).HasMaxLength(255);
            entity.Property(x => x.Line).HasMaxLength(50);
            entity.Property(x => x.Lane).HasMaxLength(50);
            entity.Property(x => x.Side).HasMaxLength(50);
            entity.Property(x => x.Machine).HasMaxLength(50);
            entity.Property(x => x.ErrorName).HasMaxLength(255);
            entity.Property(x => x.EngineerName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ActionTaken).HasMaxLength(2000).IsRequired();
            entity.HasIndex(x => x.PartsName);
            entity.HasIndex(x => x.ExecutionDate);
        });

        modelBuilder.Entity<ErrorImprove>(entity =>
        {
            entity.ToTable("ErrorImprove");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Error).HasMaxLength(255);
            entity.Property(x => x.Line).HasMaxLength(50);
            entity.Property(x => x.Lane).HasMaxLength(50);
            entity.Property(x => x.Side).HasMaxLength(50);
            entity.Property(x => x.Machine).HasMaxLength(50);
            entity.Property(x => x.EngineerName).HasMaxLength(255).IsRequired();
            entity.Property(x => x.ActionTaken).HasMaxLength(2000).IsRequired();
            entity.HasIndex(x => x.Error);
            entity.HasIndex(x => x.ExecutionDate);
        });

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Username).IsUnique();
            entity.HasIndex(x => x.EmployeeId);
        });
    }
}
