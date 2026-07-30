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

    public string ExpensivePartDayChartJson { get; set; } = "[]";
    public string ExpensivePartMonthChartJson { get; set; } = "[]";
    public string DailyErrorChartJson { get; set; } = "[]";
    public string MonthlyErrorChartJson { get; set; } = "[]";
    public int DailyErrorTotal { get; set; }
    public int MonthlyErrorTotal { get; set; }
    public int DailyExpensivePartErrorTotal { get; set; }
    public int MonthlyExpensivePartErrorTotal { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        SelectedDate = NormalizeDate(SelectedDate) ?? DateTime.Today.ToString("yyyy/MM/dd");
        SelectedMonth = NormalizeMonth(SelectedMonth) ?? DateTime.Today.ToString("yyyy/MM");

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
        var dailyErrorChart = await BuildErrorChartAsync(SelectedDate, null, cancellationToken);
        var monthlyErrorChart = await BuildErrorChartAsync(null, SelectedMonth, cancellationToken);

        DailyExpensivePartErrorTotal = dailyExpensivePartChart.Sum(x => x.Value);
        MonthlyExpensivePartErrorTotal = monthlyExpensivePartChart.Sum(x => x.Value);
        DailyErrorTotal = dailyErrorChart.Sum(x => x.Value);
        MonthlyErrorTotal = monthlyErrorChart.Sum(x => x.Value);

        ExpensivePartDayChartJson = JsonSerializer.Serialize(dailyExpensivePartChart, ChartJsonOptions);
        ExpensivePartMonthChartJson = JsonSerializer.Serialize(monthlyExpensivePartChart, ChartJsonOptions);
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

        return summary
            .GroupBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new DashboardChartItem
            {
                Label = x.Key,
                Value = x.Sum(item => item.Count)
            })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
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

        return await query
            .GroupBy(x => x.ErrorName == null || x.ErrorName == string.Empty ? "(Không có tên lỗi)" : x.ErrorName)
            .Select(x => new DashboardChartItem
            {
                Label = x.Key,
                Value = x.Count()
            })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label)
            .Take(10)
            .ToListAsync(cancellationToken);
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
    public int Value { get; set; }
}
