using System.Text.Json;
using LogMount.Data;
using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

public class DataByDateModel : PageModel
{
    private const int PageSize = 100;
    private const int PartPageSize = 100;
    private static readonly int[] TopNOptions = [10, 20, 30];
    private static readonly JsonSerializerOptions ChartJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LogMountDbContext _dbContext;
    private readonly ILogExportService _exportService;

    public DataByDateModel(LogMountDbContext dbContext, ILogExportService exportService)
    {
        _dbContext = dbContext;
        _exportService = exportService;
    }

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? SelectedDate { get; set; }

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public string? SearchDate { get; set; }

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public int PartPageNumber { get; set; } = 1;

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public ExpensivePartFilterCriteria PartFilter { get; set; } = new();

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public LogFilterCriteria Filter { get; set; } = new();

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public int TopN { get; set; } = 10;

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public bool ShowExpensiveParts { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<string> AvailableDates { get; set; } = [];
    public IReadOnlyList<RetryLogEntry> Entries { get; set; } = [];
    public int TotalRecords { get; set; }
    public int TotalPages { get; set; }
    public IReadOnlyList<DailyRetryLogSummary> DailySummaries { get; set; } = [];
    public IReadOnlyList<DailyRetryLogSummary> DisplayDailySummaries { get; set; } = [];
    public IReadOnlyList<int> TopNChoices { get; } = TopNOptions;
    public int ExpensivePartCount { get; set; }
    public IReadOnlyList<ExpensivePartSummaryItem> ExpensivePartSummary { get; set; } = [];
    public IReadOnlyList<ExpensivePartSummaryItem> FilteredExpensivePartSummary { get; set; } = [];
    public IReadOnlyList<ExpensivePartSummaryItem> CountExpensivePartSummary { get; set; } = [];
    public IReadOnlyList<ExpensivePartSummaryItem> PagedFilteredExpensivePartSummary { get; set; } = [];
    public IReadOnlyList<ExpensivePartTopItem> TopParts { get; set; } = [];
    public int ExpensivePartTotalErrorCount { get; set; }
    public int FilteredExpensivePartErrorCount { get; set; }
    public int TotalPartPages { get; set; }
    public string ChartDataJson { get; set; } = "[]";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        DailySummaries = await ApplyRealErrorFilter(_dbContext.RetryLogEntries.AsNoTracking())
            .Where(x => !string.IsNullOrWhiteSpace(x.Date))
            .Select(x => x.Date!)
            .Distinct()
            .OrderByDescending(date => date)
            .Select(date => new DailyRetryLogSummary
            {
                Date = date
            })
            .ToListAsync(cancellationToken);

        SearchDate = SearchDate?.Trim();

        var filteredDailySummaries = string.IsNullOrWhiteSpace(SearchDate)
            ? DailySummaries
            : DailySummaries
                .Where(x => x.Date.Contains(SearchDate, StringComparison.OrdinalIgnoreCase))
                .ToList();

        AvailableDates = DailySummaries
            .Select(x => x.Date)
            .ToList();

        DisplayDailySummaries = filteredDailySummaries
            .Take(5)
            .ToList();

        SelectedDate = string.IsNullOrWhiteSpace(SelectedDate)
            ? AvailableDates.FirstOrDefault()
            : SelectedDate.Trim();

        if (string.IsNullOrWhiteSpace(SelectedDate))
        {
            TotalPages = 1;
            return;
        }

        var query = ApplyLogFilter(ApplyRealErrorFilter(_dbContext.RetryLogEntries.AsNoTracking())
            .Where(x => x.Date == SelectedDate), Filter);

        TotalRecords = await query.CountAsync(cancellationToken);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

        Entries = await query
            .OrderByDescending(x => x.UploadedAt)
            .ThenBy(x => x.Id)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        if (ShowExpensiveParts)
        {
            await LoadExpensivePartSummaryAsync(query, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        SelectedDate = SelectedDate?.Trim();
        if (string.IsNullOrWhiteSpace(SelectedDate))
        {
            ErrorMessage = "Vui lòng chọn ngày cần xóa.";
            return RedirectToPage("./DataByDate");
        }

        _dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        var totalDeleted = 0;
        int batchDeleted;
        do
        {
            batchDeleted = await _dbContext.RetryLogEntries
                .Where(x => x.Date == SelectedDate)
                .Take(5000)
                .ExecuteDeleteAsync(cancellationToken);
            totalDeleted += batchDeleted;
        } while (batchDeleted > 0);

        SuccessMessage = totalDeleted > 0
            ? $"Đã xóa {totalDeleted:N0} dòng dữ liệu retryLog ngày {SelectedDate} khỏi CSDL."
            : $"Không tìm thấy dữ liệu retryLog ngày {SelectedDate} trong CSDL.";

        return RedirectToPage("./DataByDate", new
        {
            SearchDate
        });
    }

    public async Task<IActionResult> OnGetExportAsync(
        string section,
        string format,
        bool summaryOnly,
        CancellationToken cancellationToken)
    {
        SelectedDate = SelectedDate?.Trim();
        if (string.IsNullOrWhiteSpace(SelectedDate))
        {
            return BadRequest("Vui lòng chọn ngày cần tải xuống.");
        }

        if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var exportFormat))
        {
            return BadRequest("Định dạng tải xuống không hợp lệ.");
        }

        var baseFileName = $"du-lieu-ngay-{SelectedDate.Replace('/', '-')}";
        FileExportResult exportResult;

        switch (section?.Trim().ToLowerInvariant())
        {
            case "logs":
                var logRows = await ApplyLogFilter(ApplyRealErrorFilter(_dbContext.RetryLogEntries.AsNoTracking())
                    .Where(x => x.Date == SelectedDate),
                    Filter)
                    .OrderByDescending(x => x.UploadedAt)
                    .ThenBy(x => x.Id)
                    .ToListAsync(cancellationToken);

                if (logRows.Count == 0)
                {
                    return BadRequest("Không có dữ liệu log để tải xuống.");
                }

                exportResult = _exportService.ExportLogs(logRows, exportFormat, baseFileName);
                break;

            case "expensiveparts":
                TopN = TopNOptions.Contains(TopN) ? TopN : 10;
                var expensiveRows = await BuildExpensivePartSummaryForDateAsync(SelectedDate, cancellationToken);
                var filteredRows = ExpensivePartAnalysisService.SortByCount(
                    ExpensivePartAnalysisService.Filter(expensiveRows, PartFilter),
                    PartFilter);
                var exportRows = summaryOnly
                    ? ExpensivePartAnalysisService.SortByCount(
                        ExpensivePartAnalysisService.SummarizeCounts(filteredRows),
                        PartFilter)
                    : filteredRows;

                if (exportRows.Count == 0)
                {
                    return BadRequest("Không có dữ liệu linh kiện đắt tiền để tải xuống.");
                }

                exportResult = _exportService.ExportExpensiveParts(
                    exportRows,
                    exportFormat,
                    $"{baseFileName}-linh-kien-dat-tien");
                break;

            default:
                return BadRequest("Nội dung tải xuống không hợp lệ.");
        }

        return File(exportResult.Content, exportResult.ContentType, exportResult.FileName);
    }

    public Dictionary<string, string?> GetRouteValues(int pageNumber)
    {
        return new Dictionary<string, string?>
        {
            ["SelectedDate"] = SelectedDate,
            ["SearchDate"] = SearchDate,
            ["PageNumber"] = pageNumber.ToString(),
            ["PartPageNumber"] = PartPageNumber.ToString(),
            ["PartFilter.PartsName"] = PartFilter.PartsName,
            ["PartFilter.Line"] = PartFilter.Line,
            ["PartFilter.Machine"] = PartFilter.Machine,
            ["PartFilter.Shift"] = PartFilter.Shift,
            ["PartFilter.ErrorName"] = PartFilter.ErrorName,
            ["PartFilter.SortDirection"] = PartFilter.SortDirection,
            ["Filter.ErrorName"] = Filter.ErrorName,
            ["Filter.PartsName"] = Filter.PartsName,
            ["TopN"] = TopN.ToString(),
            ["ShowExpensiveParts"] = ShowExpensiveParts.ToString()
        };
    }

    public Dictionary<string, string?> GetPartRouteValues()
    {
        return new Dictionary<string, string?>
        {
            ["SelectedDate"] = SelectedDate,
            ["SearchDate"] = SearchDate,
            ["PageNumber"] = PageNumber.ToString(),
            ["PartPageNumber"] = "1",
            ["TopN"] = TopN.ToString(),
            ["ShowExpensiveParts"] = "true"
        };
    }

    public Dictionary<string, string?> GetPartPaginationRouteValues(int pageNumber)
    {
        return new Dictionary<string, string?>
        {
            ["SelectedDate"] = SelectedDate,
            ["SearchDate"] = SearchDate,
            ["PageNumber"] = PageNumber.ToString(),
            ["PartPageNumber"] = pageNumber.ToString(),
            ["PartFilter.PartsName"] = PartFilter.PartsName,
            ["PartFilter.Line"] = PartFilter.Line,
            ["PartFilter.Machine"] = PartFilter.Machine,
            ["PartFilter.Shift"] = PartFilter.Shift,
            ["PartFilter.ErrorName"] = PartFilter.ErrorName,
            ["PartFilter.SortDirection"] = PartFilter.SortDirection,
            ["Filter.ErrorName"] = Filter.ErrorName,
            ["Filter.PartsName"] = Filter.PartsName,
            ["TopN"] = TopN.ToString(),
            ["ShowExpensiveParts"] = "true"
        };
    }

    public Dictionary<string, string?> GetExportRouteValues(string section, string format, bool summaryOnly = false)
    {
        return new Dictionary<string, string?>
        {
            ["section"] = section,
            ["format"] = format,
            ["summaryOnly"] = summaryOnly.ToString(),
            ["SelectedDate"] = SelectedDate,
            ["SearchDate"] = SearchDate,
            ["PageNumber"] = PageNumber.ToString(),
            ["PartPageNumber"] = PartPageNumber.ToString(),
            ["PartFilter.PartsName"] = PartFilter.PartsName,
            ["PartFilter.Line"] = PartFilter.Line,
            ["PartFilter.Machine"] = PartFilter.Machine,
            ["PartFilter.Shift"] = PartFilter.Shift,
            ["PartFilter.ErrorName"] = PartFilter.ErrorName,
            ["PartFilter.SortDirection"] = PartFilter.SortDirection,
            ["Filter.ErrorName"] = Filter.ErrorName,
            ["Filter.PartsName"] = Filter.PartsName,
            ["TopN"] = TopN.ToString(),
            ["ShowExpensiveParts"] = ShowExpensiveParts.ToString()
        };
    }

    private async Task LoadExpensivePartSummaryAsync(
        IQueryable<RetryLogEntry> selectedDateQuery,
        CancellationToken cancellationToken)
    {
        TopN = TopNOptions.Contains(TopN) ? TopN : 10;

        var expensiveParts = await _dbContext.ExpensiveParts
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.PartsName))
            .OrderBy(x => x.UploadedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var expensivePartNames = expensiveParts
            .Where(x => !string.IsNullOrWhiteSpace(x.PartsName))
            .Select(x => x.PartsName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        ExpensivePartCount = expensivePartNames.Count;

        if (ExpensivePartCount == 0)
        {
            return;
        }

        var entries = await selectedDateQuery
            .Where(entry => entry.PartsName != null &&
                            expensivePartNames.Contains(entry.PartsName) &&
                            (entry.ErrorNo == null || entry.ErrorNo.Trim() != "0") &&
                            (entry.ErrorName == null || entry.ErrorName.Trim().ToLower() != "vision retry"))
            .ToListAsync(cancellationToken);

        ExpensivePartSummary = ExpensivePartAnalysisService.Summarize(entries, expensiveParts);
        ExpensivePartTotalErrorCount = ExpensivePartSummary.Sum(x => x.Count);

        var filtered = ExpensivePartAnalysisService.Filter(ExpensivePartSummary, PartFilter);
        FilteredExpensivePartSummary = ExpensivePartAnalysisService.SortByCount(filtered, PartFilter);
        FilteredExpensivePartErrorCount = FilteredExpensivePartSummary.Sum(x => x.Count);
        CountExpensivePartSummary = ExpensivePartAnalysisService.SortByCount(
            ExpensivePartAnalysisService.SummarizeCounts(FilteredExpensivePartSummary),
            PartFilter);
        TotalPartPages = Math.Max(1, (int)Math.Ceiling(FilteredExpensivePartSummary.Count / (double)PartPageSize));
        PartPageNumber = Math.Clamp(PartPageNumber, 1, TotalPartPages);
        PagedFilteredExpensivePartSummary = FilteredExpensivePartSummary
            .Skip((PartPageNumber - 1) * PartPageSize)
            .Take(PartPageSize)
            .ToList();

        TopParts = ExpensivePartAnalysisService.GetTopParts(filtered, TopN, PartFilter.IsCostSort);

        ChartDataJson = JsonSerializer.Serialize(TopParts.Select(x => new
        {
            x.PartsName,
            x.TotalCount
        }), ChartJsonOptions);
    }

    private async Task<IReadOnlyList<ExpensivePartSummaryItem>> BuildExpensivePartSummaryForDateAsync(
        string selectedDate,
        CancellationToken cancellationToken)
    {
        var expensiveParts = await _dbContext.ExpensiveParts
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.PartsName))
            .OrderBy(x => x.UploadedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var expensivePartNames = expensiveParts
            .Where(x => !string.IsNullOrWhiteSpace(x.PartsName))
            .Select(x => x.PartsName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (expensivePartNames.Count == 0)
        {
            return [];
        }

        var entries = await ApplyRealErrorFilter(_dbContext.RetryLogEntries.AsNoTracking())
            .Where(entry => entry.Date == selectedDate &&
                            entry.PartsName != null &&
                            expensivePartNames.Contains(entry.PartsName))
            .ToListAsync(cancellationToken);

        return ExpensivePartAnalysisService.Summarize(entries, expensiveParts);
    }

    private static IQueryable<RetryLogEntry> ApplyRealErrorFilter(IQueryable<RetryLogEntry> query)
    {
        return query.Where(x =>
            (x.ErrorNo == null || x.ErrorNo.Trim() != "0") &&
            (x.ErrorName == null || x.ErrorName.Trim().ToLower() != "vision retry"));
    }

    private static IQueryable<RetryLogEntry> ApplyLogFilter(
        IQueryable<RetryLogEntry> query,
        LogFilterCriteria criteria)
    {
        if (!string.IsNullOrWhiteSpace(criteria.ErrorName))
        {
            var errorName = criteria.ErrorName.Trim();
            query = query.Where(x => x.ErrorName != null && x.ErrorName.Contains(errorName));
        }

        if (!string.IsNullOrWhiteSpace(criteria.PartsName))
        {
            var partsName = criteria.PartsName.Trim();
            query = query.Where(x => x.PartsName != null && x.PartsName.Contains(partsName));
        }

        return query;
    }
}

public class DailyRetryLogSummary
{
    public string Date { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public int UploadedFileCount { get; set; }
    public DateTime? LastUploadedAt { get; set; }
}
