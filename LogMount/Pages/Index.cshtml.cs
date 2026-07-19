using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LogMount.Pages;

public class IndexModel : PageModel
{
    private const int PageSize = 50;
    private const long MaxFileSize = 50 * 1024 * 1024;

    private readonly IRetryLogParserService _parserService;
    private readonly ILogDataStore _logDataStore;
    private readonly ILogExportService _exportService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IRetryLogParserService parserService,
        ILogDataStore logDataStore,
        ILogExportService exportService,
        ILogger<IndexModel> logger)
    {
        _parserService = parserService;
        _logDataStore = logDataStore;
        _exportService = exportService;
        _logger = logger;
    }

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    [BindProperty(SupportsGet = true)]
    public LogFilterCriteria Filter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public string? UploadedFileName { get; set; }
    public int TotalRecords { get; set; }
    public int FilteredRecords { get; set; }
    public int TotalPages { get; set; }
    public IReadOnlyList<RetryLogEntry> DisplayEntries { get; set; } = [];
    public IReadOnlyList<ErrorSummaryItem> ErrorSummary { get; set; } = [];
    public IReadOnlyList<ColumnSummaryItem> ColumnSummary { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool HasData => TotalRecords > 0;
    public bool IsFiltered => Filter.HasAnyFilter;

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        if (UploadFile is null || UploadFile.Length == 0)
        {
            ErrorMessage = "Vui lòng chọn file để upload.";
            return Page();
        }

        if (UploadFile.Length > MaxFileSize)
        {
            ErrorMessage = "File quá lớn. Kích thước tối đa là 50 MB.";
            return Page();
        }

        var extension = Path.GetExtension(UploadFile.FileName).ToLowerInvariant();
        if (extension is not (".csv" or ".xlsx" or ".xls"))
        {
            ErrorMessage = "Chỉ hỗ trợ file .csv, .xlsx hoặc .xls.";
            return Page();
        }

        try
        {
            await using var stream = UploadFile.OpenReadStream();
            var entries = await _parserService.ParseAsync(stream, UploadFile.FileName, cancellationToken);

            if (entries.Count == 0)
            {
                ErrorMessage = "Không tìm thấy dữ liệu trong file.";
                return Page();
            }

            await HttpContext.Session.LoadAsync(cancellationToken);

            var dataKey = Guid.NewGuid().ToString("N");
            _logDataStore.Save(dataKey, entries, UploadFile.FileName);
            HttpContext.Session.SetString(SessionKeys.LogDataKey, dataKey);
            await HttpContext.Session.CommitAsync(cancellationToken);

            TempData["StatusMessage"] = $"Đã tải lên thành công {entries.Count:N0} dòng từ file \"{UploadFile.FileName}\".";
            return RedirectToPage(new { PageNumber = 1 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse uploaded log file.");
            ErrorMessage = $"Không thể đọc file: {ex.Message}";
            return Page();
        }
    }

    public async Task<IActionResult> OnPostClearAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);

        var dataKey = HttpContext.Session.GetString(SessionKeys.LogDataKey);
        if (!string.IsNullOrEmpty(dataKey))
        {
            _logDataStore.Clear(dataKey);
        }

        HttpContext.Session.Remove(SessionKeys.LogDataKey);
        await HttpContext.Session.CommitAsync(cancellationToken);

        TempData["StatusMessage"] = "Đã xóa dữ liệu.";
        return RedirectToPage();
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        StatusMessage = TempData["StatusMessage"] as string;

        await HttpContext.Session.LoadAsync(cancellationToken);

        var dataKey = HttpContext.Session.GetString(SessionKeys.LogDataKey);
        if (string.IsNullOrEmpty(dataKey))
        {
            return;
        }

        var session = _logDataStore.Get(dataKey);
        if (session is null)
        {
            HttpContext.Session.Remove(SessionKeys.LogDataKey);
            await HttpContext.Session.CommitAsync(cancellationToken);
            ErrorMessage = "Dữ liệu đã hết hạn. Vui lòng upload lại file.";
            return;
        }

        UploadedFileName = session.FileName;
        TotalRecords = session.Entries.Count;

        var filtered = RetryLogAnalysisService.Filter(session.Entries, Filter);

        FilteredRecords = filtered.Count;
        ErrorSummary = RetryLogAnalysisService.SummarizeErrors(filtered);
        ColumnSummary = RetryLogAnalysisService.SummarizeColumns(filtered);

        TotalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)PageSize));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

        DisplayEntries = filtered
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToList();
    }

    public async Task<IActionResult> OnGetExportAsync(string section, string format, CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);

        var dataKey = HttpContext.Session.GetString(SessionKeys.LogDataKey);
        if (string.IsNullOrEmpty(dataKey))
        {
            return NotFound("Không có dữ liệu để tải xuống.");
        }

        var session = _logDataStore.Get(dataKey);
        if (session is null)
        {
            return NotFound("Dữ liệu đã hết hạn. Vui lòng upload lại file.");
        }

        if (!Enum.TryParse<ExportSection>(section, ignoreCase: true, out var exportSection) ||
            !Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var exportFormat))
        {
            return BadRequest("Tham số export không hợp lệ.");
        }

        var filtered = RetryLogAnalysisService.Filter(session.Entries, Filter);
        FileExportResult exportResult;

        switch (exportSection)
        {
            case ExportSection.Overview:
                if (!Filter.HasAnyFilter || filtered.Count == 0)
                {
                    return BadRequest("Không có dữ liệu tổng quan để tải xuống.");
                }

                exportResult = _exportService.ExportOverview(
                    RetryLogAnalysisService.SummarizeColumns(filtered),
                    exportFormat,
                    session.FileName);
                break;

            case ExportSection.Errors:
                if (filtered.Count == 0)
                {
                    return BadRequest("Không có dữ liệu tổng hợp lỗi để tải xuống.");
                }

                exportResult = _exportService.ExportErrors(
                    RetryLogAnalysisService.SummarizeErrors(filtered),
                    exportFormat,
                    session.FileName);
                break;

            case ExportSection.Logs:
                if (filtered.Count == 0)
                {
                    return BadRequest("Không có dữ liệu log để tải xuống.");
                }

                exportResult = _exportService.ExportLogs(filtered, exportFormat, session.FileName);
                break;

            default:
                return BadRequest("Phần export không hợp lệ.");
        }

        return File(exportResult.Content, exportResult.ContentType, exportResult.FileName);
    }

    public Dictionary<string, string?> GetExportRouteValues(string section, string format)
    {
        return new Dictionary<string, string?>
        {
            ["section"] = section,
            ["format"] = format,
            ["Filter.Language"] = Filter.Language,
            ["Filter.OccurrenceTime"] = Filter.OccurrenceTime,
            ["Filter.LotName"] = Filter.LotName,
            ["Filter.ErrorNo"] = Filter.ErrorNo,
            ["Filter.ErrorName"] = Filter.ErrorName,
            ["Filter.Lane"] = Filter.Lane,
            ["Filter.Table"] = Filter.Table,
            ["Filter.PartsNo"] = Filter.PartsNo,
            ["Filter.PartsName"] = Filter.PartsName,
            ["Filter.HeadNo"] = Filter.HeadNo,
            ["Filter.NozzleType"] = Filter.NozzleType,
            ["Filter.FeederNo"] = Filter.FeederNo,
            ["Filter.FeederId"] = Filter.FeederId,
            ["Filter.CartId"] = Filter.CartId,
            ["Filter.VisErrorNo"] = Filter.VisErrorNo,
            ["Filter.ErrorVacuum"] = Filter.ErrorVacuum
        };
    }

    public Dictionary<string, string?> GetPaginationRouteValues(int pageNumber)
    {
        return new Dictionary<string, string?>
        {
            ["PageNumber"] = pageNumber.ToString(),
            ["Filter.Language"] = Filter.Language,
            ["Filter.OccurrenceTime"] = Filter.OccurrenceTime,
            ["Filter.LotName"] = Filter.LotName,
            ["Filter.ErrorNo"] = Filter.ErrorNo,
            ["Filter.ErrorName"] = Filter.ErrorName,
            ["Filter.Lane"] = Filter.Lane,
            ["Filter.Table"] = Filter.Table,
            ["Filter.PartsNo"] = Filter.PartsNo,
            ["Filter.PartsName"] = Filter.PartsName,
            ["Filter.HeadNo"] = Filter.HeadNo,
            ["Filter.NozzleType"] = Filter.NozzleType,
            ["Filter.FeederNo"] = Filter.FeederNo,
            ["Filter.FeederId"] = Filter.FeederId,
            ["Filter.CartId"] = Filter.CartId,
            ["Filter.VisErrorNo"] = Filter.VisErrorNo,
            ["Filter.ErrorVacuum"] = Filter.ErrorVacuum
        };
    }

    public static string GetErrorNameCssClass(string? errorName)
    {
        if (string.IsNullOrWhiteSpace(errorName))
        {
            return "error-name-tag error-name-empty";
        }

        var normalized = errorName.Trim().ToUpperInvariant();

        if (normalized.Contains("VISION"))
        {
            return "error-name-tag error-name-vision";
        }

        if (normalized.Contains("PICK UP"))
        {
            return "error-name-tag error-name-pickup";
        }

        if (normalized.Contains("LEAD WIDTH"))
        {
            return "error-name-tag error-name-lead";
        }

        if (normalized.Contains("PARTS DETECTION"))
        {
            return "error-name-tag error-name-parts";
        }

        return "error-name-tag error-name-default";
    }
}
