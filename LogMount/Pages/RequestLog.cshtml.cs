using LogMount.Data;
using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

public class RequestLogModel(LogMountDbContext dbContext, IRequestLogParserService parser, IRequestLogImportService importer, ILogger<RequestLogModel> logger) : PageModel
{
    [BindProperty] public IReadOnlyList<IFormFile> UploadFiles { get; set; } = [];
    [BindProperty(SupportsGet = true)] public string? Date { get; set; }
    [BindProperty(SupportsGet = true)] public string? Line { get; set; }
    [BindProperty(SupportsGet = true)] public string? PartNo { get; set; }
    [BindProperty(SupportsGet = true)] public string SortBy { get; set; } = "rqty";
    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public IReadOnlyList<string> Dates { get; private set; } = [];
    public IReadOnlyList<string> Lines { get; private set; } = [];
    public IReadOnlyList<RequestLogSummary> Summary { get; private set; } = [];
    public int TotalRows { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Dates = await dbContext.RequestLogEntries.AsNoTracking().Where(x => x.Date != null).Select(x => x.Date!).Distinct().OrderByDescending(x => x).ToListAsync(cancellationToken);
        Lines = await dbContext.RequestLogEntries.AsNoTracking().Where(x => x.Line != null).Select(x => x.Line!).Distinct().OrderBy(x => x).ToListAsync(cancellationToken);
        var query = dbContext.RequestLogEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Date)) query = query.Where(x => x.Date == Date.Trim());
        if (!string.IsNullOrWhiteSpace(Line)) query = query.Where(x => x.Line == Line.Trim());
        if (!string.IsNullOrWhiteSpace(PartNo)) query = query.Where(x => x.PartNo != null && x.PartNo.Contains(PartNo.Trim()));
        TotalRows = await query.CountAsync(cancellationToken);
        Summary = [];
    }

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        if (UploadFiles.Count == 0) { ErrorMessage = "Vui lòng chọn ít nhất một file RequestLog."; return RedirectToPage(); }
        try
        {
            var read = 0; var saved = 0; var batchId = Guid.NewGuid().ToString("N");
            foreach (var file in UploadFiles.Where(x => x.Length > 0))
            {
                await using var stream = file.OpenReadStream();
                var entries = await parser.ParseAsync(stream, file.FileName, cancellationToken);
                read += entries.Count;
                saved += await importer.SaveNewEntriesAsync(entries, file.FileName, batchId, DateTime.Now, cancellationToken);
            }
            StatusMessage = $"Đã tải RequestLog: đọc {read:N0} dòng, lưu mới {saved:N0} dòng.";
        }
        catch (Exception ex) { logger.LogError(ex, "Failed to upload RequestLog files."); ErrorMessage = $"Không thể tải RequestLog: {ex.Message}"; }
        return RedirectToPage();
    }
}

public sealed record RequestLogSummary(string Date, string ModelSuffix, string Chassis, string Board, string PartAssy, string PartNo, string Line, decimal PQty, decimal RQty, decimal Amount, decimal TotalAmount, decimal DropRate);
