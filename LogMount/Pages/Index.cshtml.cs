using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LogMount.Pages;

internal static class IndexUploadLimits
{
    public const long MaxFileSize = 200L * 1024 * 1024;
    public const long MaxTotalUploadSize = 500L * 1024 * 1024;
}

[RequestSizeLimit(IndexUploadLimits.MaxTotalUploadSize)]
[RequestFormLimits(MultipartBodyLengthLimit = IndexUploadLimits.MaxTotalUploadSize, ValueLengthLimit = int.MaxValue)]
public class IndexModel : PageModel
{
    private const int PageSize = 50;
    private const long MaxFileSize = IndexUploadLimits.MaxFileSize;
    private const long MaxTotalUploadSize = IndexUploadLimits.MaxTotalUploadSize;

    private readonly IRetryLogParserService _parserService;
    private readonly IPartListParserService _partListParserService;
    private readonly ILogDataStore _logDataStore;
    private readonly IPartDataStore _partDataStore;
    private readonly ILogExportService _exportService;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IRetryLogParserService parserService,
        IPartListParserService partListParserService,
        ILogDataStore logDataStore,
        IPartDataStore partDataStore,
        ILogExportService exportService,
        ILogger<IndexModel> logger)
    {
        _parserService = parserService;
        _partListParserService = partListParserService;
        _logDataStore = logDataStore;
        _partDataStore = partDataStore;
        _exportService = exportService;
        _logger = logger;
    }

    [BindProperty]
    public List<IFormFile> UploadFiles { get; set; } = [];

    [BindProperty]
    public IFormFile? PartListFile { get; set; }

    [BindProperty(SupportsGet = true)]
    public LogFilterCriteria Filter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public string? UploadedFileName { get; set; }
    public int UploadedFileCount { get; set; }
    public string? UploadedPartFileName { get; set; }
    public int UploadedPartCount { get; set; }
    public int TotalRecords { get; set; }
    public int FilteredRecords { get; set; }
    public int TotalPages { get; set; }
    public IReadOnlyList<RetryLogEntry> DisplayEntries { get; set; } = [];
    public IReadOnlyList<ErrorSummaryItem> ErrorSummary { get; set; } = [];
    public IReadOnlyList<ColumnSummaryItem> ColumnSummary { get; set; } = [];
    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public bool HasData => TotalRecords > 0;
    public bool HasPartData => UploadedPartCount > 0;
    public bool CanSummarizeExpensiveParts => HasData && HasPartData;
    public bool IsFiltered => Filter.HasAnyFilter;

    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancellationToken)
    {
        var files = UploadFiles.Where(f => f.Length > 0).ToList();
        if (files.Count == 0)
        {
            ErrorMessage = "Vui lòng chọn ít nhất một file để upload.";
            return Page();
        }

        var totalSize = files.Sum(f => f.Length);
        if (totalSize > MaxTotalUploadSize)
        {
            ErrorMessage = $"Tổng dung lượng file vượt quá giới hạn {MaxTotalUploadSize / (1024 * 1024)} MB.";
            return Page();
        }

        var allEntries = new List<RetryLogEntry>();
        var fileNames = new List<string>();
        var parseErrors = new List<string>();

        foreach (var file in files)
        {
            if (file.Length > MaxFileSize)
            {
                parseErrors.Add($"\"{file.FileName}\": file quá lớn (tối đa {MaxFileSize / (1024 * 1024)} MB/file).");
                continue;
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".csv" or ".xlsx" or ".xls"))
            {
                parseErrors.Add($"\"{file.FileName}\": chỉ hỗ trợ .csv, .xlsx hoặc .xls.");
                continue;
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var entries = await _parserService.ParseAsync(stream, file.FileName, cancellationToken);

                if (entries.Count == 0)
                {
                    parseErrors.Add($"\"{file.FileName}\": không có dữ liệu.");
                    continue;
                }

                allEntries.AddRange(entries);
                fileNames.Add(file.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse uploaded log file {FileName}.", file.FileName);
                parseErrors.Add($"\"{file.FileName}\": {ex.Message}");
            }
        }

        if (allEntries.Count == 0)
        {
            ErrorMessage = parseErrors.Count > 0
                ? string.Join(" ", parseErrors)
                : "Không tìm thấy dữ liệu trong các file.";
            return Page();
        }

        try
        {
            await HttpContext.Session.LoadAsync(cancellationToken);

            var dataKey = Guid.NewGuid().ToString("N");
            _logDataStore.Save(dataKey, allEntries, fileNames);
            HttpContext.Session.SetString(SessionKeys.LogDataKey, dataKey);
            await HttpContext.Session.CommitAsync(cancellationToken);

            var statusMessage = $"Đã tải lên thành công {allEntries.Count:N0} dòng từ {fileNames.Count} file.";
            if (parseErrors.Count > 0)
            {
                statusMessage += $" ({parseErrors.Count} file lỗi: {string.Join("; ", parseErrors)})";
            }

            TempData["StatusMessage"] = statusMessage;
            return RedirectToPage(new { PageNumber = 1 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save uploaded log data.");
            ErrorMessage = $"Không thể lưu dữ liệu: {ex.Message}";
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

    public async Task<IActionResult> OnPostUploadPartAsync(CancellationToken cancellationToken)
    {
        if (PartListFile is null || PartListFile.Length == 0)
        {
            TempData["ErrorMessage"] = "Vui lòng chọn file part lkdt để upload.";
            return RedirectToPage();
        }

        if (PartListFile.Length > MaxFileSize)
        {
            TempData["ErrorMessage"] = "File part lkdt quá lớn. Kích thước tối đa là 50 MB.";
            return RedirectToPage();
        }

        var extension = Path.GetExtension(PartListFile.FileName).ToLowerInvariant();
        if (extension is not (".csv" or ".xlsx" or ".xls"))
        {
            TempData["ErrorMessage"] = "File part lkdt chỉ hỗ trợ .csv, .xlsx hoặc .xls.";
            return RedirectToPage();
        }

        try
        {
            await using var stream = PartListFile.OpenReadStream();
            var partNames = await _partListParserService.ParseAsync(stream, PartListFile.FileName, cancellationToken);

            await HttpContext.Session.LoadAsync(cancellationToken);

            var partDataKey = Guid.NewGuid().ToString("N");
            _partDataStore.Save(partDataKey, partNames, PartListFile.FileName);
            HttpContext.Session.SetString(SessionKeys.PartDataKey, partDataKey);
            await HttpContext.Session.CommitAsync(cancellationToken);

            TempData["StatusMessage"] = $"Đã tải lên thành công {partNames.Count:N0} linh kiện đắt tiền từ file \"{PartListFile.FileName}\".";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse uploaded part list file.");
            TempData["ErrorMessage"] = $"Không thể đọc file part lkdt: {ex.Message}";
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostClearPartAsync(CancellationToken cancellationToken)
    {
        await HttpContext.Session.LoadAsync(cancellationToken);

        var partDataKey = HttpContext.Session.GetString(SessionKeys.PartDataKey);
        if (!string.IsNullOrEmpty(partDataKey))
        {
            _partDataStore.Clear(partDataKey);
        }

        HttpContext.Session.Remove(SessionKeys.PartDataKey);
        await HttpContext.Session.CommitAsync(cancellationToken);

        TempData["StatusMessage"] = "Đã xóa file part lkdt.";
        return RedirectToPage();
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        StatusMessage = TempData["StatusMessage"] as string;
        ErrorMessage = TempData["ErrorMessage"] as string;

        await HttpContext.Session.LoadAsync(cancellationToken);

        await LoadPartSessionAsync(cancellationToken);

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
        UploadedFileCount = session.FileNames.Count > 0 ? session.FileNames.Count : 1;
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

    private async Task LoadPartSessionAsync(CancellationToken cancellationToken)
    {
        var partDataKey = HttpContext.Session.GetString(SessionKeys.PartDataKey);
        if (string.IsNullOrEmpty(partDataKey))
        {
            return;
        }

        var partSession = _partDataStore.Get(partDataKey);
        if (partSession is null)
        {
            HttpContext.Session.Remove(SessionKeys.PartDataKey);
            await HttpContext.Session.CommitAsync(cancellationToken);
            return;
        }

        UploadedPartFileName = partSession.FileName;
        UploadedPartCount = partSession.PartNames.Count;
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
            ["Filter.Date"] = Filter.Date,
            ["Filter.Line"] = Filter.Line,
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
            ["Filter.Date"] = Filter.Date,
            ["Filter.Line"] = Filter.Line,
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
