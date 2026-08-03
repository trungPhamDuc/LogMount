using System.Text.Json;
using LogMount.Data;
using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

public class DataByMonthModel : PageModel
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

    public DataByMonthModel(LogMountDbContext dbContext, ILogExportService exportService)
    {
        _dbContext = dbContext;
        _exportService = exportService;
    }

    [BindProperty(SupportsGet = true)]
    public string? SelectedMonth { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchMonth { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PartPageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public ExpensivePartFilterCriteria PartFilter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public LogFilterCriteria Filter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int TopN { get; set; } = 10;

    [BindProperty(SupportsGet = true)]
    public bool ShowExpensiveParts { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<string> AvailableMonths { get; set; } = [];
    public IReadOnlyList<string> DisplayMonths { get; set; } = [];
    public IReadOnlyList<RetryLogEntry> Entries { get; set; } = [];
    public int TotalRecords { get; set; }
    public int TotalPages { get; set; }
    public IReadOnlyList<int> TopNChoices { get; } = TopNOptions;
    public int ExpensivePartCount { get; set; }
    public IReadOnlyList<ExpensivePartSummaryItem> ExpensivePartSummary { get; set; } = [];
    public IReadOnlyList<ExpensivePartSummaryItem> FilteredExpensivePartSummary { get; set; } = [];
    public IReadOnlyList<ExpensivePartSummaryItem> CountExpensivePartSummary { get; set; } = [];
    public IReadOnlyList<ExpensivePartSummaryItem> PagedFilteredExpensivePartSummary { get; set; } = [];
    public IReadOnlyList<ExpensivePartTopItem> TopParts { get; set; } = [];
    public int FilteredExpensivePartErrorCount { get; set; }
    public int TotalPartPages { get; set; }
    public string ChartDataJson { get; set; } = "[]";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        AvailableMonths = await LoadAvailableMonthsAsync(cancellationToken);
        SearchMonth = NormalizeMonth(SearchMonth);

        DisplayMonths = string.IsNullOrWhiteSpace(SearchMonth)
            ? AvailableMonths.Take(5).ToList()
            : AvailableMonths
                .Where(x => x.Contains(SearchMonth, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

        SelectedMonth = NormalizeMonth(SelectedMonth) ?? AvailableMonths.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(SelectedMonth))
        {
            TotalPages = 1;
            return;
        }

        var query = ApplyLogFilter(BuildMonthQuery(SelectedMonth), Filter);

        TotalRecords = await query.CountAsync(cancellationToken);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

        Entries = await query
            .OrderBy(x => x.Date)
            .ThenBy(x => x.OccurrenceTime)
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
        SelectedMonth = NormalizeMonth(SelectedMonth);
        if (string.IsNullOrWhiteSpace(SelectedMonth))
        {
            ErrorMessage = "Vui lòng chọn tháng cần xóa.";
            return RedirectToPage("./DataByMonth");
        }

        var deletedCount = await _dbContext.RetryLogEntries
            .Where(x => x.Date != null && x.Date.StartsWith(SelectedMonth))
            .ExecuteDeleteAsync(cancellationToken);

        SuccessMessage = deletedCount > 0
            ? $"Đã xóa dữ liệu retryLog tháng {SelectedMonth} khỏi CSDL."
            : $"Không tìm thấy dữ liệu retryLog tháng {SelectedMonth} trong CSDL.";

        return RedirectToPage("./DataByMonth", new
        {
            SearchMonth
        });
    }

    public async Task<IActionResult> OnGetExportAsync(
        string section,
        string format,
        bool summaryOnly,
        CancellationToken cancellationToken)
    {
        SelectedMonth = NormalizeMonth(SelectedMonth);
        if (string.IsNullOrWhiteSpace(SelectedMonth))
        {
            return BadRequest("Vui lòng chọn tháng cần tải xuống.");
        }

        if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var exportFormat))
        {
            return BadRequest("Định dạng tải xuống không hợp lệ.");
        }

        var baseFileName = $"du-lieu-thang-{SelectedMonth.Replace('/', '-')}";
        FileExportResult exportResult;

        switch (section?.Trim().ToLowerInvariant())
        {
            case "logs":
                var logRows = await ApplyLogFilter(BuildMonthQuery(SelectedMonth), Filter)
                    .OrderBy(x => x.Date)
                    .ThenBy(x => x.OccurrenceTime)
                    .ThenBy(x => x.Id)
                    .ToListAsync(cancellationToken);

                if (logRows.Count == 0)
                {
                    return BadRequest("Không có dữ liệu log để tải xuống.");
                }

                exportResult = _exportService.ExportLogs(logRows, exportFormat, baseFileName);
                break;

            case "expensiveparts":
                var expensiveRows = await BuildExpensivePartSummaryForMonthAsync(SelectedMonth, cancellationToken);
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
            ["SelectedMonth"] = SelectedMonth,
            ["SearchMonth"] = SearchMonth,
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
            ["SelectedMonth"] = SelectedMonth,
            ["SearchMonth"] = SearchMonth,
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
            ["SelectedMonth"] = SelectedMonth,
            ["SearchMonth"] = SearchMonth,
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
            ["SelectedMonth"] = SelectedMonth,
            ["SearchMonth"] = SearchMonth,
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
        IQueryable<RetryLogEntry> selectedMonthQuery,
        CancellationToken cancellationToken)
    {
        TopN = TopNOptions.Contains(TopN) ? TopN : 10;

        ExpensivePartSummary = await BuildExpensivePartSummaryForMonthAsync(
            SelectedMonth ?? string.Empty,
            cancellationToken,
            selectedMonthQuery);

        ExpensivePartCount = ExpensivePartSummary
            .Select(x => x.PartsName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (ExpensivePartCount == 0)
        {
            return;
        }

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

    private async Task<IReadOnlyList<ExpensivePartSummaryItem>> BuildExpensivePartSummaryForMonthAsync(
        string selectedMonth,
        CancellationToken cancellationToken,
        IQueryable<RetryLogEntry>? selectedMonthQuery = null)
    {
        var expensiveParts = await _dbContext.ExpensiveParts
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.PartsName))
            .OrderBy(x => x.UploadedAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var expensivePartNames = expensiveParts
            .Select(x => x.PartsName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (expensivePartNames.Count == 0)
        {
            return [];
        }

        selectedMonthQuery ??= BuildMonthQuery(selectedMonth);

        var entries = await selectedMonthQuery
            .Where(entry => entry.PartsName != null &&
                            expensivePartNames.Contains(entry.PartsName))
            .ToListAsync(cancellationToken);

        return ExpensivePartAnalysisService.Summarize(entries, expensiveParts);
    }

    private IQueryable<RetryLogEntry> BuildMonthQuery(string selectedMonth)
    {
        return _dbContext.RetryLogEntries
            .AsNoTracking()
            .Where(x => x.Date != null && x.Date.StartsWith(selectedMonth))
            .Where(x =>
                (x.ErrorNo == null || x.ErrorNo.Trim() != "0") &&
                (x.ErrorName == null || x.ErrorName.Trim().ToLower() != "vision retry"));
    }

    private async Task<IReadOnlyList<string>> LoadAvailableMonthsAsync(CancellationToken cancellationToken)
    {
        var dates = await BuildRealErrorQuery()
            .Where(x => !string.IsNullOrWhiteSpace(x.Date) && x.Date!.Length >= 7)
            .Select(x => x.Date!)
            .Distinct()
            .ToListAsync(cancellationToken);

        return dates
            .Select(x => x[..7])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeMonth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Replace('-', '/');
    }

    private IQueryable<RetryLogEntry> BuildRealErrorQuery()
    {
        return _dbContext.RetryLogEntries
            .AsNoTracking()
            .Where(x =>
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
