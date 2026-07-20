using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LogMount.Pages;

public class ExpensivePartsModel : PageModel
{
    private readonly ILogDataStore _logDataStore;
    private readonly IPartDataStore _partDataStore;
    private readonly ILogExportService _exportService;

    public ExpensivePartsModel(
        ILogDataStore logDataStore,
        IPartDataStore partDataStore,
        ILogExportService exportService)
    {
        _logDataStore = logDataStore;
        _partDataStore = partDataStore;
        _exportService = exportService;
    }

    public IReadOnlyList<ExpensivePartSummaryItem> Summary { get; set; } = [];
    public string? LogFileName { get; set; }
    public string? PartFileName { get; set; }
    public int LogRecordCount { get; set; }
    public int PartCount { get; set; }
    public int TotalErrorCount { get; set; }
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var loadResult = await LoadSummaryAsync(cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(string format, CancellationToken cancellationToken)
    {
        var loadResult = await LoadSummaryAsync(cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (Summary.Count == 0)
        {
            return BadRequest("Không có dữ liệu để tải xuống.");
        }

        if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var exportFormat))
        {
            return BadRequest("Định dạng export không hợp lệ.");
        }

        var exportResult = _exportService.ExportExpensiveParts(
            Summary,
            exportFormat,
            PartFileName ?? "part-lkdt");

        return File(exportResult.Content, exportResult.ContentType, exportResult.FileName);
    }

    private async Task<IActionResult?> LoadSummaryAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);

        var logDataKey = HttpContext.Session.GetString(SessionKeys.LogDataKey);
        var partDataKey = HttpContext.Session.GetString(SessionKeys.PartDataKey);

        if (string.IsNullOrEmpty(logDataKey))
        {
            TempData["ErrorMessage"] = "Chưa có dữ liệu retry log. Vui lòng upload file log trước.";
            return RedirectToPage("/Index");
        }

        if (string.IsNullOrEmpty(partDataKey))
        {
            TempData["ErrorMessage"] = "Chưa có file part lkdt. Vui lòng upload file linh kiện đắt tiền trước.";
            return RedirectToPage("/Index");
        }

        var logSession = _logDataStore.Get(logDataKey);
        var partSession = _partDataStore.Get(partDataKey);

        if (logSession is null)
        {
            HttpContext.Session.Remove(SessionKeys.LogDataKey);
            await HttpContext.Session.CommitAsync(cancellationToken);
            TempData["ErrorMessage"] = "Dữ liệu log đã hết hạn. Vui lòng upload lại.";
            return RedirectToPage("/Index");
        }

        if (partSession is null)
        {
            HttpContext.Session.Remove(SessionKeys.PartDataKey);
            await HttpContext.Session.CommitAsync(cancellationToken);
            TempData["ErrorMessage"] = "Dữ liệu part lkdt đã hết hạn. Vui lòng upload lại.";
            return RedirectToPage("/Index");
        }

        LogFileName = logSession.FileName;
        PartFileName = partSession.FileName;
        LogRecordCount = logSession.Entries.Count;
        PartCount = partSession.PartNames.Count;
        Summary = ExpensivePartAnalysisService.Summarize(logSession.Entries, partSession.PartNames);
        TotalErrorCount = Summary.Sum(x => x.Count);

        return null;
    }
}
