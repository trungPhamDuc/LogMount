using LogMount.Data;
using LogMount.Models;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Services;

public interface IErrorLogImportService
{
    Task<int> SaveNewEntriesAsync(
        IReadOnlyList<ErrorLogEntry> entries,
        string sourceFileName,
        string uploadBatchId,
        DateTime uploadedAt,
        CancellationToken cancellationToken = default);

    Task<int> ReplaceEntriesFromFileAsync(
        IReadOnlyList<ErrorLogEntry> entries,
        string sourceFileName,
        string uploadBatchId,
        DateTime uploadedAt,
        CancellationToken cancellationToken = default);
}

public class ErrorLogImportService : IErrorLogImportService
{
    private readonly LogMountDbContext _dbContext;

    public ErrorLogImportService(LogMountDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> SaveNewEntriesAsync(
        IReadOnlyList<ErrorLogEntry> entries,
        string sourceFileName,
        string uploadBatchId,
        DateTime uploadedAt,
        CancellationToken cancellationToken = default)
    {
        var validEntries = entries
            .Where(IsValidEntry)
            .ToList();

        if (validEntries.Count == 0)
        {
            return 0;
        }

        var dates = validEntries
            .Select(x => x.Date)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingEntries = dates.Count == 0
            ? new List<ErrorLogEntry>()
            : await _dbContext.ErrorLogEntries
                .AsNoTracking()
                .Where(x => x.Date != null && dates.Contains(x.Date))
                .ToListAsync(cancellationToken);

        var existingKeys = existingEntries
            .Select(BuildKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newEntries = new List<ErrorLogEntry>();
        foreach (var entry in validEntries)
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

        await _dbContext.ErrorLogEntries.AddRangeAsync(newEntries, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return newEntries.Count;
    }

    public async Task<int> ReplaceEntriesFromFileAsync(
        IReadOnlyList<ErrorLogEntry> entries,
        string sourceFileName,
        string uploadBatchId,
        DateTime uploadedAt,
        CancellationToken cancellationToken = default)
    {
        var validEntries = entries
            .Where(IsValidEntry)
            .ToList();

        await _dbContext.ErrorLogEntries
            .Where(entry => entry.SourceFileName == sourceFileName)
            .ExecuteDeleteAsync(cancellationToken);

        if (validEntries.Count == 0)
        {
            return 0;
        }

        foreach (var entry in validEntries)
        {
            entry.SourceFileName = sourceFileName;
            entry.UploadBatchId = uploadBatchId;
            entry.UploadedAt = uploadedAt;
        }

        await _dbContext.ErrorLogEntries.AddRangeAsync(validEntries, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return validEntries.Count;
    }

    private static bool IsValidEntry(ErrorLogEntry entry)
    {
        if (string.Equals(entry.EventDate, "Event Date", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.ProgramName, "Program Name", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Error, "Contents", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(entry.EventDate) &&
               !string.IsNullOrWhiteSpace(entry.Error);
    }

    private static string BuildKey(ErrorLogEntry entry)
    {
        return string.Join('\u001F',
            Normalize(entry.EventDate),
            Normalize(entry.Line),
            Normalize(entry.Lane),
            Normalize(entry.Table),
            Normalize(entry.Error),
            Normalize(entry.EventNo),
            Normalize(entry.ProgramName),
            Normalize(entry.Details));
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;
}
