using System.Globalization;
using System.Text.Json;
using LogMount.Data;
using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

public class ErrorLogImprovementModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LogMountDbContext _dbContext;

    public ErrorLogImprovementModel(LogMountDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public ErrorLogImprovementCriteria Filter { get; set; } = new();

    public IReadOnlyList<string> AvailableErrors { get; set; } = [];
    public IReadOnlyList<string> AvailableLines { get; set; } = [];
    public IReadOnlyList<string> AvailableLanes { get; set; } = [];
    public IReadOnlyList<string> AvailableSides { get; set; } = ["BOT", "TOP"];
    public IReadOnlyList<string> AvailableMachines { get; set; } = [];
    public IReadOnlyList<string> AvailableTables { get; set; } = [];

    public IReadOnlyList<ImprovementSummaryItem> SummaryItems { get; set; } = [];
    public IReadOnlyList<DailyImprovementItem> DailyItems { get; set; } = [];
    public IReadOnlyList<ErrorImprove> ImprovementHistoryItems { get; set; } = [];

    public bool IsFilterApplied { get; set; }
    public int TotalErrors { get; set; }
    public int FilteredErrors { get; set; }
    public string SelectedFilterDescription { get; set; } = string.Empty;
    public string ChartErrorsJson { get; set; } = "[]";
    public string ChartDailyJson { get; set; } = "[]";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IsFilterApplied = Filter.HasAnyFilter;

        // INSTANT INITIAL LOAD: If no filter is applied yet, return immediately (sub-10ms response time)
        if (!IsFilterApplied)
        {
            return;
        }

        // Query dropdown options only when user applies filters
        AvailableErrors = await _dbContext.ErrorLogEntries.AsNoTracking()
            .Where(e => e.Error != null && e.Error != "")
            .Select(e => e.Error!)
            .Distinct()
            .OrderBy(v => v)
            .Take(50)
            .ToListAsync(cancellationToken);

        AvailableLines = await _dbContext.ErrorLogEntries.AsNoTracking()
            .Where(e => e.Line != null && e.Line != "")
            .Select(e => e.Line!)
            .Distinct()
            .OrderBy(v => v)
            .Take(50)
            .ToListAsync(cancellationToken);

        AvailableLanes = await _dbContext.ErrorLogEntries.AsNoTracking()
            .Where(e => e.Lane != null && e.Lane != "")
            .Select(e => e.Lane!)
            .Distinct()
            .OrderBy(v => v)
            .Take(50)
            .ToListAsync(cancellationToken);

        AvailableTables = await _dbContext.ErrorLogEntries.AsNoTracking()
            .Where(e => e.Table != null && e.Table != "")
            .Select(e => e.Table!)
            .Distinct()
            .OrderBy(v => v)
            .Take(50)
            .ToListAsync(cancellationToken);

        // Build EF Query directly with lightweight projection
        var query = _dbContext.ErrorLogEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(Filter.FromDate))
        {
            var from = Filter.FromDate.Trim().Replace('/', '-');
            query = query.Where(e => (e.Date != null && e.Date.Replace("/", "-").CompareTo(from) >= 0) ||
                                     (e.EventDate != null && e.EventDate.Replace("/", "-").CompareTo(from) >= 0));
        }

        if (!string.IsNullOrWhiteSpace(Filter.ToDate))
        {
            var to = Filter.ToDate.Trim().Replace('/', '-');
            query = query.Where(e => (e.Date != null && e.Date.Replace("/", "-").CompareTo(to) <= 0) ||
                                     (e.EventDate != null && e.EventDate.Replace("/", "-").CompareTo(to) <= 0));
        }

        if (!string.IsNullOrWhiteSpace(Filter.Error))
        {
            var err = Filter.Error.Trim();
            query = query.Where(e => (e.Error != null && e.Error.Contains(err)) ||
                                     (e.EventNo != null && e.EventNo.Contains(err)));
        }

        if (!string.IsNullOrWhiteSpace(Filter.Line))
        {
            var l = Filter.Line.Trim();
            query = query.Where(e => (e.Line != null && e.Line.Contains(l)) ||
                                     (e.ProgramName != null && e.ProgramName.Contains(l)));
        }

        if (!string.IsNullOrWhiteSpace(Filter.Lane))
        {
            var lane = Filter.Lane.Trim();
            query = query.Where(e => e.Lane != null && e.Lane.Contains(lane));
        }

        if (!string.IsNullOrWhiteSpace(Filter.Table))
        {
            var tbl = Filter.Table.Trim();
            query = query.Where(e => e.Table != null && e.Table.Contains(tbl));
        }

        var projectedList = await query
            .Select(e => new
            {
                Date = e.Date ?? e.EventDate,
                e.ProgramName,
                e.Error
            })
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(Filter.Side))
        {
            var side = Filter.Side.Trim().ToUpperInvariant();
            projectedList = projectedList.Where(e =>
            {
                var s = ErrorLogHelper.ParseSide(e.ProgramName).ToUpperInvariant();
                if (side == "BOT" || side == "B") return s == "B" || s.Contains("BOT");
                if (side == "TOP" || side == "T") return s == "T" || s.Contains("TOP");
                return s.Contains(side);
            }).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Filter.Machine))
        {
            var machine = Filter.Machine.Trim();
            projectedList = projectedList.Where(e =>
                ErrorLogHelper.ParseMachine(e.ProgramName).Equals(machine, StringComparison.OrdinalIgnoreCase) ||
                ErrorLogHelper.ParseMachine(e.ProgramName).Contains(machine, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        FilteredErrors = projectedList.Count;

        // 1. Group BY ERROR (Error Breakdown)
        var errorGroups = projectedList
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Error) ? "(Chưa rõ lỗi)" : e.Error.Trim())
            .Select(g => new
            {
                Name = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        int topN = Filter.TopN > 0 ? Filter.TopN : 20;
        var errorGroupsLimited = Filter.TopN > 0 ? errorGroups.Take(topN).ToList() : errorGroups;

        double cumulative = 0;
        var summaryItemList = new List<ImprovementSummaryItem>();
        var chartErrorsList = new List<object>();

        for (int i = 0; i < errorGroupsLimited.Count; i++)
        {
            var eg = errorGroupsLimited[i];
            double pct = FilteredErrors > 0 ? (eg.Count * 100.0 / FilteredErrors) : 0;
            cumulative += pct;

            summaryItemList.Add(new ImprovementSummaryItem
            {
                Rank = i + 1,
                Name = eg.Name,
                Count = eg.Count,
                Percentage = Math.Round(pct, 1),
                CumulativePercentage = Math.Round(cumulative, 1)
            });

            chartErrorsList.Add(new
            {
                name = eg.Name,
                count = eg.Count,
                percentage = Math.Round(pct, 1)
            });
        }
        SummaryItems = summaryItemList;
        ChartErrorsJson = JsonSerializer.Serialize(chartErrorsList, JsonOptions);

        // 2. Group BY DATE (Daily Trend)
        var dailyGroups = projectedList
            .GroupBy(e => NormalizeDateString(e.Date))
            .Select(g => new
            {
                Date = g.Key,
                SortDate = ParseDateForSorting(g.Key),
                Count = g.Count(),
                TopErrors = string.Join(", ", g.GroupBy(x => x.Error)
                    .Where(x => !string.IsNullOrEmpty(x.Key))
                    .OrderByDescending(x => x.Count())
                    .Select(x => $"{x.Key} ({x.Count()})")
                    .Take(3))
            })
            .OrderBy(x => x.SortDate)
            .ToList();

        double dailyCumulative = 0;
        var dailyItemList = new List<DailyImprovementItem>();
        var chartDailyList = new List<object>();

        for (int i = 0; i < dailyGroups.Count; i++)
        {
            var dg = dailyGroups[i];
            double pct = FilteredErrors > 0 ? (dg.Count * 100.0 / FilteredErrors) : 0;
            dailyCumulative += pct;

            string trend = "-";
            if (i > 0)
            {
                int prevCount = dailyGroups[i - 1].Count;
                if (dg.Count > prevCount) trend = "▲ Tăng";
                else if (dg.Count < prevCount) trend = "▼ Giảm";
                else trend = "► Bằng";
            }

            dailyItemList.Add(new DailyImprovementItem
            {
                Rank = i + 1,
                Date = dg.Date,
                Count = dg.Count,
                Percentage = Math.Round(pct, 1),
                CumulativePercentage = Math.Round(dailyCumulative, 1),
                TopPartsOrErrors = dg.TopErrors,
                TrendIndicator = trend
            });

            chartDailyList.Add(new
            {
                name = dg.Date,
                count = dg.Count,
                percentage = Math.Round(pct, 1),
                trend = trend
            });
        }
        DailyItems = dailyItemList;
        ChartDailyJson = JsonSerializer.Serialize(chartDailyList, JsonOptions);

        // 3. TAB 3: ERROR IMPROVE HISTORY FILTERED BY TOP FILTERS
        var impQuery = _dbContext.ErrorImproves.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(Filter.FromDate))
        {
            if (DateTime.TryParse(Filter.FromDate, out var fromDt))
            {
                impQuery = impQuery.Where(x => x.ExecutionDate >= fromDt.Date);
            }
        }

        if (!string.IsNullOrWhiteSpace(Filter.ToDate))
        {
            if (DateTime.TryParse(Filter.ToDate, out var toDt))
            {
                impQuery = impQuery.Where(x => x.ExecutionDate <= toDt.Date);
            }
        }

        if (!string.IsNullOrWhiteSpace(Filter.Error))
        {
            var err = Filter.Error.Trim();
            impQuery = impQuery.Where(x => x.Error != null && x.Error.Contains(err));
        }

        if (!string.IsNullOrWhiteSpace(Filter.Line))
        {
            var l = Filter.Line.Trim();
            impQuery = impQuery.Where(x => x.Line != null && x.Line.Contains(l));
        }

        if (!string.IsNullOrWhiteSpace(Filter.Lane))
        {
            var lane = Filter.Lane.Trim();
            impQuery = impQuery.Where(x => x.Lane != null && x.Lane.Contains(lane));
        }

        if (!string.IsNullOrWhiteSpace(Filter.Side))
        {
            var side = Filter.Side.Trim();
            impQuery = impQuery.Where(x => x.Side != null && x.Side.Contains(side));
        }

        if (!string.IsNullOrWhiteSpace(Filter.Machine))
        {
            var m = Filter.Machine.Trim();
            impQuery = impQuery.Where(x => x.Machine != null && x.Machine.Contains(m));
        }

        ImprovementHistoryItems = await impQuery
            .OrderByDescending(x => x.ExecutionDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    private static string NormalizeDateString(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return "(Chưa rõ ngày)";
        var str = dateStr.Trim();
        var spaceIndex = str.IndexOf(' ');
        if (spaceIndex > 0) str = str[..spaceIndex].Trim();
        return str.Replace('/', '-');
    }

    private static DateTime ParseDateForSorting(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return DateTime.MinValue;
        var norm = NormalizeDateString(dateStr);
        if (DateTime.TryParse(norm, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(norm, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
        {
            return dt.Date;
        }
        return DateTime.MinValue;
    }
}
