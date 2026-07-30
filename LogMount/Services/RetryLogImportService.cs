using LogMount.Data;
using LogMount.Models;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Services;

public interface IRetryLogImportService
{
    Task<int> SaveNewEntriesAsync(
        IReadOnlyList<RetryLogEntry> entries,
        string sourceFileName,
        string uploadBatchId,
        DateTime uploadedAt,
        CancellationToken cancellationToken = default);
}

public class RetryLogImportService : IRetryLogImportService
{
    private readonly LogMountDbContext _dbContext;

    public RetryLogImportService(LogMountDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> SaveNewEntriesAsync(
        IReadOnlyList<RetryLogEntry> entries,
        string sourceFileName,
        string uploadBatchId,
        DateTime uploadedAt,
        CancellationToken cancellationToken = default)
    {
        var realEntries = entries
            .Where(RetryLogAnalysisService.IsRealError)
            .ToList();

        if (realEntries.Count == 0)
        {
            return 0;
        }

        var dates = realEntries
            .Select(x => x.Date)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingEntries = dates.Count == 0
            ? new List<RetryLogEntry>()
            : await _dbContext.RetryLogEntries
                .AsNoTracking()
                .Where(x => x.Date != null && dates.Contains(x.Date))
                .ToListAsync(cancellationToken);

        var existingKeys = existingEntries
            .Select(BuildKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newEntries = new List<RetryLogEntry>();
        foreach (var entry in realEntries)
        {
            var key = BuildKey(entry);
            if (!existingKeys.Add(key))
            {
                continue;
            }

            entry.SourceFileName = sourceFileName;
            entry.UploadBatchId = uploadBatchId;
            entry.UploadedAt = uploadedAt;
            newEntries.Add(entry);
        }

        if (newEntries.Count == 0)
        {
            return 0;
        }

        await _dbContext.RetryLogEntries.AddRangeAsync(newEntries, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return newEntries.Count;
    }

    private static string BuildKey(RetryLogEntry entry)
    {
        return string.Join('\u001F',
            Normalize(entry.Date),
            Normalize(entry.Line),
            Normalize(entry.OccurrenceTime),
            Normalize(entry.LotName),
            Normalize(entry.ErrorNo),
            Normalize(entry.ErrorName),
            Normalize(entry.Lane),
            Normalize(entry.Table),
            Normalize(entry.PartsNo),
            Normalize(entry.PartsName),
            Normalize(entry.HeadNo),
            Normalize(entry.NozzleType),
            Normalize(entry.FeederNo),
            Normalize(entry.FeederId),
            Normalize(entry.CartId),
            Normalize(entry.VisErrorNo),
            Normalize(entry.ErrorVacuum));
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
