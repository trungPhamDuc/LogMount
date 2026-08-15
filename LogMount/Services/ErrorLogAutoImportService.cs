using LogMount.Data;
using LogMount.Models;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Services;

public class ErrorLogAutoImportService : BackgroundService
{
    private const string PreviousDayReimportJobName = "ErrorLogPreviousDayReimport";
    private static readonly TimeSpan PreviousDayReimportTime = new(7, 30, 0);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ErrorLogAutoImportService> _logger;

    public ErrorLogAutoImportService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ErrorLogAutoImportService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("ErrorLogBatch:AutoImportEnabled", true))
        {
            return;
        }

        // Run an immediate import on startup so data is generated right away without waiting
        await ImportTodayAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            await ReimportPreviousDayIfDueAsync(now, stoppingToken);

            now = DateTime.Now;
            var nextRunTime = GetNextRunTime(
                now,
                await GetLastPreviousDayReimportDateAsync(DateOnly.FromDateTime(now), stoppingToken));
            var delay = nextRunTime - now;
            var shouldImportToday = IsScheduledImportRun(nextRunTime);

            _logger.LogInformation(
                "Next auto import error log will run at {NextRunTime}.",
                nextRunTime);

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
            }

            if (shouldImportToday)
            {
                await ImportTodayAsync(stoppingToken);
            }

            await ReimportPreviousDayIfDueAsync(DateTime.Now, stoppingToken);
        }
    }

    private static DateTime GetNextRunTime(DateTime now, DateOnly? lastPreviousDayReimportDate)
    {
        var nextThirtyMinuteRun = GetNextThirtyMinuteRunTime(now);
        var today = DateOnly.FromDateTime(now);
        var todayReimportRun = now.Date.Add(PreviousDayReimportTime);
        var nextReimportRun = lastPreviousDayReimportDate == today || todayReimportRun <= now
            ? todayReimportRun.AddDays(1)
            : todayReimportRun;

        return nextThirtyMinuteRun <= nextReimportRun
            ? nextThirtyMinuteRun
            : nextReimportRun;
    }

    private static DateTime GetNextThirtyMinuteRunTime(DateTime now)
    {
        var minuteSlot = now.Minute < 30 ? 0 : 30;
        var currentSlot = new DateTime(now.Year, now.Month, now.Day, now.Hour, minuteSlot, 0);
        var nextRun = now.Minute % 30 == 0 && now.Second == 0
            ? currentSlot
            : currentSlot.AddMinutes(30);

        if (nextRun <= now)
        {
            nextRun = nextRun.AddMinutes(30);
        }

        return nextRun;
    }

    private static bool IsScheduledImportRun(DateTime runTime)
    {
        return runTime.Minute % 30 == 0 && runTime.Second == 0;
    }

    private async Task ImportTodayAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var batchService = scope.ServiceProvider.GetRequiredService<IErrorLogBatchService>();
            var parserService = scope.ServiceProvider.GetRequiredService<IErrorLogParserService>();
            var importService = scope.ServiceProvider.GetRequiredService<IErrorLogImportService>();

            var date = DateOnly.FromDateTime(DateTime.Today);
            var outputFilePath = await batchService.RunAsync(date, cancellationToken);
            await using var stream = File.OpenRead(outputFilePath);
            var fileName = Path.GetFileName(outputFilePath);
            var entries = await parserService.ParseAsync(stream, fileName, cancellationToken);

            var savedCount = await importService.ReplaceEntriesFromFileAsync(
                entries,
                fileName,
                Guid.NewGuid().ToString("N"),
                DateTime.Now,
                cancellationToken);

            _logger.LogInformation(
                "Auto imported error log for {Date}. Parsed {ParsedCount} rows, replaced {SavedCount} rows.",
                date,
                entries.Count,
                savedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto import error log failed.");
        }
    }

    private async Task ReimportPreviousDayIfDueAsync(DateTime now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now);
        if (now.TimeOfDay < PreviousDayReimportTime)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LogMountDbContext>();
            var batchService = scope.ServiceProvider.GetRequiredService<IErrorLogBatchService>();
            var parserService = scope.ServiceProvider.GetRequiredService<IErrorLogParserService>();
            var importService = scope.ServiceProvider.GetRequiredService<IErrorLogImportService>();

            if (!await TryClaimPreviousDayReimportAsync(dbContext, today, cancellationToken))
            {
                return;
            }

            var previousDate = today.AddDays(-1);
            var outputFilePath = await batchService.RunAsync(previousDate, cancellationToken);
            await using var stream = File.OpenRead(outputFilePath);
            var fileName = Path.GetFileName(outputFilePath);
            var entries = await parserService.ParseAsync(stream, fileName, cancellationToken);

            var validEntries = entries.Where(ErrorLogAnalysisService.IsRealError).ToList();
            if (validEntries.Count == 0)
            {
                throw new InvalidOperationException("Không thay thế dữ liệu ngày trước bằng tệp không có dòng ErrorLog hợp lệ.");
            }

            var previousDateText = previousDate.ToString("yyyy/MM/dd");
            var deletedCount = await dbContext.ErrorLogEntries
                .Where(entry => entry.Date == previousDateText)
                .ExecuteDeleteAsync(cancellationToken);

            var savedCount = await importService.SaveNewEntriesAsync(
                validEntries,
                fileName,
                Guid.NewGuid().ToString("N"),
                DateTime.Now,
                cancellationToken);

            await MarkPreviousDayReimportCompletedAsync(dbContext, today, cancellationToken);

            _logger.LogInformation(
                "Reimported previous day error log for {Date}. Deleted {DeletedCount} rows, parsed {ParsedCount} rows, saved {SavedCount} rows.",
                previousDate,
                deletedCount,
                entries.Count,
                savedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Previous day error log reimport failed.");
        }
    }

    private async Task<DateOnly?> GetLastPreviousDayReimportDateAsync(DateOnly today, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LogMountDbContext>();
            var state = await dbContext.AutoImportStates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.JobName == PreviousDayReimportJobName, cancellationToken);

            if (state?.LastRunDate is null ||
                !DateOnly.TryParseExact(state.LastRunDate, "yyyy-MM-dd", out var stateDate))
            {
                return null;
            }

            return stateDate == today ? stateDate : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read previous day reimport state.");
            return null;
        }
    }

    private static async Task<bool> TryClaimPreviousDayReimportAsync(
        LogMountDbContext dbContext,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        var todayText = today.ToString("yyyy-MM-dd");
        var state = await dbContext.AutoImportStates
            .FirstOrDefaultAsync(x => x.JobName == PreviousDayReimportJobName, cancellationToken);

        if (state?.LastRunDate == todayText && state.LastCompletedAt is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        if (state is null)
        {
            dbContext.AutoImportStates.Add(new AutoImportState
            {
                JobName = PreviousDayReimportJobName,
                LastRunDate = todayText,
                LastStartedAt = DateTime.Now
            });
        }
        else
        {
            state.LastRunDate = todayText;
            state.LastStartedAt = DateTime.Now;
            state.LastCompletedAt = null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task MarkPreviousDayReimportCompletedAsync(
        LogMountDbContext dbContext,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var todayText = today.ToString("yyyy-MM-dd");
        var state = await dbContext.AutoImportStates
            .FirstOrDefaultAsync(x =>
                x.JobName == PreviousDayReimportJobName &&
                x.LastRunDate == todayText,
                cancellationToken);

        if (state is null)
        {
            return;
        }

        state.LastCompletedAt = DateTime.Now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
