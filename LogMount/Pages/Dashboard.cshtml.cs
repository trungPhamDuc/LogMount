using System.Text.Json;
using LogMount.Data;
using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

public class DashboardModel : PageModel
{
    private static readonly int[] TopNOptions = [0, 10, 20, 30];

    private static readonly JsonSerializerOptions ChartJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LogMountDbContext _dbContext;

    public DashboardModel(LogMountDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? SelectedDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SelectedMonth { get; set; }

    [BindProperty(SupportsGet = true)]
    public int TopN { get; set; } = 10;

    public IReadOnlyList<int> TopNChoices { get; } = TopNOptions;
    public string ExpensivePartDayChartJson { get; set; } = "[]";
    public string ExpensivePartMonthChartJson { get; set; } = "[]";
    public string ExpensivePartDayCostChartJson { get; set; } = "[]";
    public string ExpensivePartMonthCostChartJson { get; set; } = "[]";
    public string DailyErrorChartJson { get; set; } = "[]";
    public string MonthlyErrorChartJson { get; set; } = "[]";
    public int DailyErrorTotal { get; set; }
    public int MonthlyErrorTotal { get; set; }
    public int DailyExpensivePartErrorTotal { get; set; }
    public int MonthlyExpensivePartErrorTotal { get; set; }
    public decimal DailyExpensivePartCostTotal { get; set; }
    public decimal MonthlyExpensivePartCostTotal { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        SelectedDate = NormalizeDate(SelectedDate) ?? DateTime.Today.ToString("yyyy/MM/dd");
        SelectedMonth = NormalizeMonth(SelectedMonth) ?? DateTime.Today.ToString("yyyy/MM");
        TopN = TopNOptions.Contains(TopN) ? TopN : 10;

        if (!await HasDataForDateAsync(SelectedDate, cancellationToken))
        {
            SelectedDate = await GetLatestDateAsync(cancellationToken) ?? SelectedDate;
        }

        if (!await HasDataForMonthAsync(SelectedMonth, cancellationToken))
        {
            SelectedMonth = await GetLatestMonthAsync(cancellationToken) ?? SelectedMonth;
        }

        var dailyExpensivePartChart = await BuildExpensivePartChartAsync(SelectedDate, null, cancellationToken);
        var monthlyExpensivePartChart = await BuildExpensivePartChartAsync(null, SelectedMonth, cancellationToken);
        var dailyExpensivePartCostChart = await BuildExpensivePartCostChartAsync(SelectedDate, null, cancellationToken);
        var monthlyExpensivePartCostChart = await BuildExpensivePartCostChartAsync(null, SelectedMonth, cancellationToken);
        var dailyErrorChart = await BuildErrorChartAsync(SelectedDate, null, cancellationToken);
        var monthlyErrorChart = await BuildErrorChartAsync(null, SelectedMonth, cancellationToken);

        DailyExpensivePartErrorTotal = await GetExpensivePartErrorTotalAsync(SelectedDate, null, cancellationToken);
        MonthlyExpensivePartErrorTotal = await GetExpensivePartErrorTotalAsync(null, SelectedMonth, cancellationToken);
        DailyErrorTotal = await GetErrorTotalAsync(SelectedDate, null, cancellationToken);
        MonthlyErrorTotal = await GetErrorTotalAsync(null, SelectedMonth, cancellationToken);
        DailyExpensivePartCostTotal = await GetExpensivePartCostTotalAsync(SelectedDate, null, cancellationToken);
        MonthlyExpensivePartCostTotal = await GetExpensivePartCostTotalAsync(null, SelectedMonth, cancellationToken);

        ExpensivePartDayChartJson = JsonSerializer.Serialize(dailyExpensivePartChart, ChartJsonOptions);
        ExpensivePartMonthChartJson = JsonSerializer.Serialize(monthlyExpensivePartChart, ChartJsonOptions);
        ExpensivePartDayCostChartJson = JsonSerializer.Serialize(dailyExpensivePartCostChart, ChartJsonOptions);
        ExpensivePartMonthCostChartJson = JsonSerializer.Serialize(monthlyExpensivePartCostChart, ChartJsonOptions);
        DailyErrorChartJson = JsonSerializer.Serialize(dailyErrorChart, ChartJsonOptions);
        MonthlyErrorChartJson = JsonSerializer.Serialize(monthlyErrorChart, ChartJsonOptions);
    }

    private async Task<IReadOnlyList<DashboardChartItem>> BuildExpensivePartChartAsync(
        string? selectedDate,
        string? selectedMonth,
        CancellationToken cancellationToken)
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

        var query = BuildRealErrorQuery()
            .Where(x => x.PartsName != null && expensivePartNames.Contains(x.PartsName));

        if (!string.IsNullOrWhiteSpace(selectedDate))
        {
            query = query.Where(x => x.Date == selectedDate);
        }

        if (!string.IsNullOrWhiteSpace(selectedMonth))
        {
            query = query.Where(x => x.Date != null && x.Date.StartsWith(selectedMonth));
        }

        var entries = await query.ToListAsync(cancellationToken);

        var summary = ExpensivePartAnalysisService.Summarize(entries, expensiveParts);

        var items = summary
            .GroupBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new DashboardChartItem
            {
                Label = x.Key,
                Value = x.Sum(item => item.Count)
            })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ApplyTopFilter(items, TopN);
    }

    private async Task<IReadOnlyList<DashboardChartItem>> BuildExpensivePartCostChartAsync(
        string? selectedDate,
        string? selectedMonth,
        CancellationToken cancellationToken)
    {
        var summary = await BuildExpensivePartSummaryAsync(selectedDate, selectedMonth, cancellationToken);

        var items = summary
            .GroupBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new DashboardChartItem
            {
                Label = x.Key,
                Value = x.Sum(item => item.TotalCost)
            });

        items = items
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase);

        return ApplyTopFilter(items.ToList(), TopN);
    }

    private async Task<IReadOnlyList<ExpensivePartSummaryItem>> BuildExpensivePartSummaryAsync(
        string? selectedDate,
        string? selectedMonth,
        CancellationToken cancellationToken)
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

        var query = BuildRealErrorQuery()
            .Where(x => x.PartsName != null && expensivePartNames.Contains(x.PartsName));

        if (!string.IsNullOrWhiteSpace(selectedDate))
        {
            query = query.Where(x => x.Date == selectedDate);
        }

        if (!string.IsNullOrWhiteSpace(selectedMonth))
        {
            query = query.Where(x => x.Date != null && x.Date.StartsWith(selectedMonth));
        }

        var entries = await query.ToListAsync(cancellationToken);

        return ExpensivePartAnalysisService.Summarize(entries, expensiveParts);
    }

    private async Task<IReadOnlyList<DashboardChartItem>> BuildErrorChartAsync(
        string? selectedDate,
        string? selectedMonth,
        CancellationToken cancellationToken)
    {
        var query = BuildRealErrorQuery();

        if (!string.IsNullOrWhiteSpace(selectedDate))
        {
            query = query.Where(x => x.Date == selectedDate);
        }

        if (!string.IsNullOrWhiteSpace(selectedMonth))
        {
            query = query.Where(x => x.Date != null && x.Date.StartsWith(selectedMonth));
        }

        var items = await query
            .GroupBy(x => x.ErrorName == null || x.ErrorName == string.Empty ? "(Không có tên lỗi)" : x.ErrorName)
            .Select(x => new DashboardChartItem
            {
                Label = x.Key,
                Value = x.Count()
            })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label)
            .ToListAsync(cancellationToken);

        return ApplyTopFilter(items, TopN);
    }

    private static IReadOnlyList<DashboardChartItem> ApplyTopFilter(
        IReadOnlyList<DashboardChartItem> items,
        int topN)
    {
        return topN <= 0
            ? items
            : items.Take(topN).ToList();
    }

    private async Task<int> GetErrorTotalAsync(
        string? selectedDate,
        string? selectedMonth,
        CancellationToken cancellationToken)
    {
        return await ApplyDateMonthFilter(BuildRealErrorQuery(), selectedDate, selectedMonth)
            .CountAsync(cancellationToken);
    }

    private async Task<int> GetExpensivePartErrorTotalAsync(
        string? selectedDate,
        string? selectedMonth,
        CancellationToken cancellationToken)
    {
        var summary = await BuildExpensivePartSummaryAsync(selectedDate, selectedMonth, cancellationToken);
        return summary.Sum(x => x.Count);
    }

    private async Task<decimal> GetExpensivePartCostTotalAsync(
        string? selectedDate,
        string? selectedMonth,
        CancellationToken cancellationToken)
    {
        var summary = await BuildExpensivePartSummaryAsync(selectedDate, selectedMonth, cancellationToken);
        return summary.Sum(x => x.TotalCost);
    }

    private static IQueryable<RetryLogEntry> ApplyDateMonthFilter(
        IQueryable<RetryLogEntry> query,
        string? selectedDate,
        string? selectedMonth)
    {
        if (!string.IsNullOrWhiteSpace(selectedDate))
        {
            query = query.Where(x => x.Date == selectedDate);
        }

        if (!string.IsNullOrWhiteSpace(selectedMonth))
        {
            query = query.Where(x => x.Date != null && x.Date.StartsWith(selectedMonth));
        }

        return query;
    }

    private Task<bool> HasDataForDateAsync(string date, CancellationToken cancellationToken)
    {
        return BuildRealErrorQuery().AnyAsync(x => x.Date == date, cancellationToken);
    }

    private Task<bool> HasDataForMonthAsync(string month, CancellationToken cancellationToken)
    {
        return BuildRealErrorQuery().AnyAsync(x => x.Date != null && x.Date.StartsWith(month), cancellationToken);
    }

    private Task<string?> GetLatestDateAsync(CancellationToken cancellationToken)
    {
        return BuildRealErrorQuery()
            .Where(x => !string.IsNullOrWhiteSpace(x.Date))
            .Select(x => x.Date)
            .OrderByDescending(x => x)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<string?> GetLatestMonthAsync(CancellationToken cancellationToken)
    {
        var latestDate = await GetLatestDateAsync(cancellationToken);
        return latestDate?.Length >= 7 ? latestDate[..7] : null;
    }

    private IQueryable<RetryLogEntry> BuildRealErrorQuery()
    {
        return _dbContext.RetryLogEntries
            .AsNoTracking()
            .Where(x =>
                (x.ErrorNo == null || x.ErrorNo.Trim() != "0") &&
                (x.ErrorName == null || x.ErrorName.Trim().ToLower() != "vision retry"));
    }

    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Replace('-', '/');
    }

    private static string? NormalizeMonth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Replace('-', '/');
    }
}

public class DashboardChartItem
{
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
}
