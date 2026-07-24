using System.Text.Json;
using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LogMount.Pages;

public class ExpensivePartsModel : PageModel
{
    private static readonly int[] TopNOptions = [10, 20, 30];
    private static readonly JsonSerializerOptions ChartJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

    [BindProperty(SupportsGet = true)]
    public ExpensivePartFilterCriteria Filter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int TopN { get; set; } = 10;

    public IReadOnlyList<ExpensivePartSummaryItem> Summary { get; set; } = [];
    public IReadOnlyList<ExpensivePartSummaryItem> FilteredSummary { get; set; } = [];
    public IReadOnlyList<ExpensivePartSummaryItem> CountSummary { get; set; } = [];
    public IReadOnlyList<ExpensivePartTopItem> TopParts { get; set; } = [];
    public IReadOnlyList<int> TopNChoices { get; } = TopNOptions;
    public string? LogFileName { get; set; }
    public string? PartFileName { get; set; }
    public int LogRecordCount { get; set; }
    public int PartCount { get; set; }
    public int TotalErrorCount { get; set; }
    public int FilteredErrorCount { get; set; }
    public bool IsFiltered => Filter.HasAnyFilter;
    public string ChartDataJson { get; set; } = "[]";
    public string DetailDataJson { get; set; } = "[]";
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

    public async Task<IActionResult> OnGetExportAsync(string format, bool summaryOnly, CancellationToken cancellationToken)
    {
        var loadResult = await LoadSummaryAsync(cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        var exportItems = summaryOnly ? CountSummary : FilteredSummary;
        if (exportItems.Count == 0)
        {
            return BadRequest("Không có dữ liệu để tải xuống.");
        }

        if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var exportFormat))
        {
            return BadRequest("Định dạng export không hợp lệ.");
        }

        var exportResult = _exportService.ExportExpensiveParts(
            exportItems,
            exportFormat,
            PartFileName ?? "part-lkdt");

        return File(exportResult.Content, exportResult.ContentType, exportResult.FileName);
    }

    public Dictionary<string, string?> GetExportRouteValues(string format, bool summaryOnly = false)
    {
        return new Dictionary<string, string?>
        {
            ["format"] = format,
            ["Filter.PartsName"] = Filter.PartsName,
            ["Filter.Line"] = Filter.Line,
            ["Filter.Machine"] = Filter.Machine,
            ["Filter.Shift"] = Filter.Shift,
            ["Filter.ErrorName"] = Filter.ErrorName,
            ["Filter.SortDirection"] = Filter.SortDirection,
            ["TopN"] = TopN.ToString(),
            ["summaryOnly"] = summaryOnly.ToString()
        };
    }

    private async Task<IActionResult?> LoadSummaryAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);

        var logDataKey = HttpContext.Session.GetString(SessionKeys.LogDataKey);
        var partDataKey = HttpContext.Session.GetString(SessionKeys.PartDataKey);

        if (string.IsNullOrEmpty(logDataKey))
        {
            TempData["ErrorMessage"] = "Chưa có dữ liệu retryLog. Vui lòng tải tệp log trước.";
            return RedirectToPage("/Index");
        }

        if (string.IsNullOrEmpty(partDataKey))
        {
            TempData["ErrorMessage"] = "Chưa có tệp part lkdt. Vui lòng tải tệp linh kiện đắt tiền trước.";
            return RedirectToPage("/Index");
        }

        var logSession = _logDataStore.Get(logDataKey);
        var partSession = _partDataStore.Get(partDataKey);

        if (logSession is null)
        {
            HttpContext.Session.Remove(SessionKeys.LogDataKey);
            await HttpContext.Session.CommitAsync(cancellationToken);
            TempData["ErrorMessage"] = "Dữ liệu log đã hết hạn. Vui lòng tải lên lại.";
            return RedirectToPage("/Index");
        }

        if (partSession is null)
        {
            HttpContext.Session.Remove(SessionKeys.PartDataKey);
            await HttpContext.Session.CommitAsync(cancellationToken);
            TempData["ErrorMessage"] = "Dữ liệu part lkdt đã hết hạn. Vui lòng tải lên lại.";
            return RedirectToPage("/Index");
        }

        LogFileName = logSession.FileName;
        PartFileName = partSession.FileName;
        LogRecordCount = logSession.Entries.Count;
        PartCount = partSession.Parts.Count;
        Summary = ExpensivePartAnalysisService.Summarize(logSession.Entries, partSession.Parts);
        TotalErrorCount = Summary.Sum(x => x.Count);

        TopN = TopNOptions.Contains(TopN) ? TopN : 10;

        var filtered = ExpensivePartAnalysisService.Filter(Summary, Filter);
        FilteredSummary = ExpensivePartAnalysisService.SortByCount(filtered, Filter);
        FilteredErrorCount = FilteredSummary.Sum(x => x.Count);
        CountSummary = ExpensivePartAnalysisService.SortByCount(
            ExpensivePartAnalysisService.SummarizeCounts(FilteredSummary),
            Filter);
        TopParts = ExpensivePartAnalysisService.GetTopParts(filtered, TopN, Filter.IsCostSort);

        ChartDataJson = JsonSerializer.Serialize(TopParts.Select(x => new
        {
            x.PartsName,
            x.TotalCount
        }), ChartJsonOptions);

        return null;
    }
}
