using System.Text.Json;
using LogMount.Data;
using LogMount.Models;
using LogMount.Services;
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

    public DataByDateModel(LogMountDbContext dbContext)
    {
        _dbContext = dbContext;
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
    public int TopN { get; set; } = 10;

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)]
    public bool ShowExpensiveParts { get; set; }

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
        DailySummaries = await _dbContext.RetryLogEntries
            .AsNoTracking()
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

        var query = _dbContext.RetryLogEntries
            .AsNoTracking()
            .Where(x => x.Date == SelectedDate);

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
            ["TopN"] = TopN.ToString(),
            ["ShowExpensiveParts"] = "true"
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
                            (entry.ErrorName == null || entry.ErrorName != "Vision Retry"))
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
}

public class DailyRetryLogSummary
{
    public string Date { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public int UploadedFileCount { get; set; }
    public DateTime? LastUploadedAt { get; set; }
}
