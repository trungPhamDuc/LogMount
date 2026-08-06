using System.Text.Json;
using LogMount.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

public class ErrorLogDashboardModel : PageModel
{
    private static readonly int[] TopNOptions = [0, 10, 20, 30];
    private static readonly JsonSerializerOptions ChartJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LogMountDbContext _dbContext;

    public ErrorLogDashboardModel(LogMountDbContext dbContext)
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
    public string DailyErrorChartJson { get; set; } = "[]";
    public string MonthlyErrorChartJson { get; set; } = "[]";
    public int DailyErrorTotal { get; set; }
    public int MonthlyErrorTotal { get; set; }

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

        var dailyChart = await BuildErrorChartAsync(SelectedDate, null, cancellationToken);
        var monthlyChart = await BuildErrorChartAsync(null, SelectedMonth, cancellationToken);

        DailyErrorTotal = await GetErrorTotalAsync(SelectedDate, null, cancellationToken);
        MonthlyErrorTotal = await GetErrorTotalAsync(null, SelectedMonth, cancellationToken);

        DailyErrorChartJson = JsonSerializer.Serialize(dailyChart, ChartJsonOptions);
        MonthlyErrorChartJson = JsonSerializer.Serialize(monthlyChart, ChartJsonOptions);
    }

    private async Task<IReadOnlyList<ErrorLogChartItem>> BuildErrorChartAsync(
        string? selectedDate,
        string? selectedMonth,
        CancellationToken cancellationToken)
    {
        var query = ApplyDateMonthFilter(_dbContext.ErrorLogEntries.AsNoTracking(), selectedDate, selectedMonth);

        var items = await query
            .GroupBy(x => x.Error == null || x.Error == string.Empty ? "(Không có lỗi)" : x.Error)
            .Select(x => new ErrorLogChartItem
            {
                Label = x.Key,
                Value = x.Count()
            })
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Label)
            .ToListAsync(cancellationToken);

        return TopN <= 0 ? items : items.Take(TopN).ToList();
    }

    private async Task<int> GetErrorTotalAsync(
        string? selectedDate,
        string? selectedMonth,
        CancellationToken cancellationToken)
    {
        return await ApplyDateMonthFilter(_dbContext.ErrorLogEntries.AsNoTracking(), selectedDate, selectedMonth)
            .CountAsync(cancellationToken);
    }

    private static IQueryable<Models.ErrorLogEntry> ApplyDateMonthFilter(
        IQueryable<Models.ErrorLogEntry> query,
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
        return _dbContext.ErrorLogEntries.AsNoTracking().AnyAsync(x => x.Date == date, cancellationToken);
    }

    private Task<bool> HasDataForMonthAsync(string month, CancellationToken cancellationToken)
    {
        return _dbContext.ErrorLogEntries.AsNoTracking().AnyAsync(x => x.Date != null && x.Date.StartsWith(month), cancellationToken);
    }

    private Task<string?> GetLatestDateAsync(CancellationToken cancellationToken)
    {
        return _dbContext.ErrorLogEntries
            .AsNoTracking()
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

    private static string? NormalizeDate(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace('-', '/');
    }

    private static string? NormalizeMonth(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace('-', '/');
    }
}

public class ErrorLogChartItem
{
    public string Label { get; set; } = string.Empty;
    public int Value { get; set; }
}
