using LogMount.Data;
using LogMount.Models;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Services;

public interface IRequestLogImportService
{
    Task<int> SaveNewEntriesAsync(IReadOnlyList<RequestLogEntry> entries, string fileName, string batchId, DateTime uploadedAt, CancellationToken cancellationToken = default);
}

public class RequestLogImportService(LogMountDbContext dbContext) : IRequestLogImportService
{
    public async Task<int> SaveNewEntriesAsync(IReadOnlyList<RequestLogEntry> entries, string fileName, string batchId, DateTime uploadedAt, CancellationToken cancellationToken = default)
    {
        var valid = entries.Where(x => !string.IsNullOrWhiteSpace(x.PartNo)).ToList();
        if (valid.Count == 0) return 0;
        var dates = valid.Where(x => x.Date is not null).Select(x => x.Date!).Distinct().ToList();
        var existing = await dbContext.RequestLogEntries.AsNoTracking().Where(x => x.Date != null && dates.Contains(x.Date)).ToListAsync(cancellationToken);
        var keys = existing.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = valid.Where(x => keys.Add(Key(x))).ToList();
        foreach (var entry in additions) { entry.SourceFileName = fileName; entry.UploadBatchId = batchId; entry.UploadedAt = uploadedAt; }
        if (additions.Count == 0) return 0;
        await dbContext.RequestLogEntries.AddRangeAsync(additions, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return additions.Count;
    }

    private static string Key(RequestLogEntry x) => string.Join('\u001F', x.Date, x.Line, x.PartNo, x.PidOrLot, x.PQty, x.RQty, x.AmtOnRequest);
}
