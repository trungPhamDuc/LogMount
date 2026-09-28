using System.Globalization;
using LogMount.Data;
using LogMount.Models;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Services;

public interface IRequestLogImportService
{
    Task<int> SaveNewEntriesAsync(IReadOnlyList<RequestLogEntry> entries, string fileName, string batchId, DateTime uploadedAt, CancellationToken cancellationToken = default);
    Task<int> ReplaceEntriesFromFileAsync(IReadOnlyList<RequestLogEntry> entries, string fileName, string batchId, DateTime uploadedAt, CancellationToken cancellationToken = default);
    Task<int> CleanupDuplicatesAsync(CancellationToken cancellationToken = default);
}

public class RequestLogImportService(LogMountDbContext dbContext) : IRequestLogImportService
{
    public async Task<int> SaveNewEntriesAsync(IReadOnlyList<RequestLogEntry> entries, string fileName, string batchId, DateTime uploadedAt, CancellationToken cancellationToken = default)
    {
        var valid = entries.Where(x => !string.IsNullOrWhiteSpace(x.PartNo)).ToList();
        if (valid.Count == 0) return 0;

        // Clean up any existing duplicates in the DB first
        await CleanupDuplicatesAsync(cancellationToken);

        // Normalize entry dates
        foreach (var entry in valid)
        {
            entry.Date = RequestLogParserService.NormalizeDate(entry.Date) ?? entry.Date;
        }

        var targetDates = valid
            .Where(x => !string.IsNullOrWhiteSpace(x.Date))
            .Select(x => x.Date!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = await dbContext.RequestLogEntries.AsNoTracking()
            .Where(x => x.Date != null)
            .ToListAsync(cancellationToken);

        var relevantExisting = targetDates.Count == 0
            ? []
            : existing.Where(x => targetDates.Contains(RequestLogParserService.NormalizeDate(x.Date) ?? "")).ToList();

        var existingKeys = relevantExisting.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var additions = new List<RequestLogEntry>();
        foreach (var entry in valid)
        {
            var key = Key(entry);
            if (!existingKeys.Add(key))
            {
                continue;
            }

            entry.SourceFileName = fileName;
            entry.UploadBatchId = batchId;
            entry.UploadedAt = uploadedAt;
            additions.Add(entry);
        }

        if (additions.Count == 0) return 0;

        await dbContext.RequestLogEntries.AddRangeAsync(additions, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return additions.Count;
    }

    public async Task<int> ReplaceEntriesFromFileAsync(IReadOnlyList<RequestLogEntry> entries, string fileName, string batchId, DateTime uploadedAt, CancellationToken cancellationToken = default)
    {
        var valid = entries.Where(x => !string.IsNullOrWhiteSpace(x.PartNo)).ToList();
        if (valid.Count == 0)
        {
            throw new InvalidOperationException("Không thể thay thế dữ liệu RequestLog bằng tệp không có dòng hợp lệ.");
        }

        await dbContext.RequestLogEntries
            .Where(entry => entry.SourceFileName == fileName)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var entry in valid)
        {
            entry.Date = RequestLogParserService.NormalizeDate(entry.Date) ?? entry.Date;
            entry.SourceFileName = fileName;
            entry.UploadBatchId = batchId;
            entry.UploadedAt = uploadedAt;
        }

        await dbContext.RequestLogEntries.AddRangeAsync(valid, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return valid.Count;
    }

    public async Task<int> CleanupDuplicatesAsync(CancellationToken cancellationToken = default)
    {
        var allEntries = await dbContext.RequestLogEntries.ToListAsync(cancellationToken);
        if (allEntries.Count == 0) return 0;

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicatesToRemove = new List<RequestLogEntry>();

        foreach (var entry in allEntries)
        {
            var key = Key(entry);
            if (!seenKeys.Add(key))
            {
                duplicatesToRemove.Add(entry);
            }
        }

        if (duplicatesToRemove.Count > 0)
        {
            dbContext.RequestLogEntries.RemoveRange(duplicatesToRemove);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return duplicatesToRemove.Count;
    }

    private static string Key(RequestLogEntry x) => string.Join('\u001F',
        (RequestLogParserService.NormalizeDate(x.Date) ?? "").Trim(),
        (x.Shift ?? "").Trim(),
        (x.Line ?? "").Trim(),
        (x.Process ?? "").Trim(),
        (x.ModelSuffix ?? "").Trim(),
        (x.Chassis ?? "").Trim(),
        (x.Board ?? "").Trim(),
        (x.PartAssy ?? "").Trim(),
        (x.WorkOrder ?? "").Trim(),
        (x.PartNo ?? "").Trim(),
        (x.PidOrLot ?? "").Trim(),
        (x.Unit ?? "").Trim(),
        x.PQty.ToString("0.######", CultureInfo.InvariantCulture),
        x.RQty.ToString("0.######", CultureInfo.InvariantCulture),
        x.AmtOnRequest.ToString("0.######", CultureInfo.InvariantCulture),
        (x.Department ?? "").Trim());
}
