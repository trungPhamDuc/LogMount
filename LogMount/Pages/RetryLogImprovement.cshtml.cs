using System.Globalization;
using System.Text.Json;
using LogMount.Data;
using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace LogMount.Pages;

public class RetryLogImprovementModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LogMountDbContext _dbContext;
    private readonly IMemoryCache _cache;

    public RetryLogImprovementModel(LogMountDbContext dbContext, IMemoryCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    [BindProperty(SupportsGet = true)]
    public RetryLogImprovementCriteria Filter { get; set; } = new();

    public IReadOnlyList<string> AvailableLines { get; set; } = [];
    public IReadOnlyList<string> AvailableLanes { get; set; } = [];
    public IReadOnlyList<string> AvailableSides { get; set; } = ["BOT", "TOP"];
    public IReadOnlyList<string> AvailableMachines { get; set; } = [];
    public IReadOnlyList<string> AvailableTables { get; set; } = [];
    public IReadOnlyList<string> AvailablePartsNames { get; set; } = [];
    public IReadOnlyList<string> AvailableErrorNames { get; set; } = [];
    public IReadOnlyList<string> AvailableErrorNos { get; set; } = [];

    // Tab 1: Daily Trend (All retries)
    public IReadOnlyList<DailyImprovementItem> DailyItems { get; set; } = [];
    public string ChartDailyJson { get; set; } = "[]";

    // Tab 2: Expensive Parts Daily Trend (LKDT theo ngày)
    public IReadOnlyList<DailyImprovementItem> ExpensiveDailyItems { get; set; } = [];
    public string ChartExpensiveDailyJson { get; set; } = "[]";
    public int FilteredExpensiveRetries { get; set; }

    // Tab 3: Improvement History filtered by user selection
    public IReadOnlyList<RetryImprove> ImprovementHistoryItems { get; set; } = [];

    public bool IsFilterApplied { get; set; }
    public int FilteredRetries { get; set; }
    public string SelectedFilterDescription { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        IsFilterApplied = Filter.HasAnyFilter;

        // INSTANT INITIAL LOAD: If no filter is applied yet, return immediately (sub-10ms response time)
        if (!IsFilterApplied)
        {
            return;
        }

        // Query dropdown options with IMemoryCache
        AvailableLines = await GetFilterOptionsAsync(e => e.Line, "lines", cancellationToken);
        AvailableLanes = await GetFilterOptionsAsync(e => e.Lane, "lanes", cancellationToken);
        AvailableTables = await GetFilterOptionsAsync(e => e.Table, "tables", cancellationToken);
        AvailablePartsNames = await GetFilterOptionsAsync(e => e.PartsName, "parts", cancellationToken);
        AvailableErrorNames = await GetFilterOptionsAsync(e => e.ErrorName, "error-names", cancellationToken);
        AvailableErrorNos = await GetFilterOptionsAsync(e => e.ErrorNo, "error-nos", cancellationToken);
        AvailableMachines = await GetMachineOptionsAsync(cancellationToken);

        // Build EF Query for RetryLogEntries
        var query = _dbContext.RetryLogEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(Filter.FromDate))
        {
            // RetryLogEntry.Date is normalized at import as yyyy/MM/dd.  Keep the
            // database column untouched here so SQL Server can use IX_RetryLogEntries_Date.
            var from = NormalizeFilterDate(Filter.FromDate);
            query = query.Where(e => e.Date != null && e.Date.CompareTo(from) >= 0);
        }

        if (!string.IsNullOrWhiteSpace(Filter.ToDate))
        {
            var to = NormalizeFilterDate(Filter.ToDate);
            query = query.Where(e => e.Date != null && e.Date.CompareTo(to) <= 0);
        }

        // Line/Lane can be stored in different formats or only derivable after
        // projection. Apply them below after reading the light row set.

        if (!string.IsNullOrWhiteSpace(Filter.Table))
        {
            var tbl = Filter.Table.Trim();
            query = query.Where(e => e.Table != null && e.Table.Contains(tbl));
        }

        if (!string.IsNullOrWhiteSpace(Filter.PartsName))
        {
            var p = Filter.PartsName.Trim();
            query = query.Where(e => e.PartsName != null && e.PartsName.Contains(p));
        }

        if (!string.IsNullOrWhiteSpace(Filter.ErrorName))
        {
            var errName = Filter.ErrorName.Trim();
            query = query.Where(e => e.ErrorName != null && e.ErrorName.Contains(errName));
        }

        if (!string.IsNullOrWhiteSpace(Filter.ErrorNo))
        {
            var errNo = Filter.ErrorNo.Trim();
            query = query.Where(e => e.ErrorNo != null && e.ErrorNo.Contains(errNo));
        }

        // 3. TAB 3: RETRY IMPROVE HISTORY FILTERED BY TOP FILTERS
        var impQuery = _dbContext.RetryImproves.AsNoTracking();

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

        if (!string.IsNullOrWhiteSpace(Filter.PartsName))
        {
            var p = Filter.PartsName.Trim();
            impQuery = impQuery.Where(x => x.PartsName != null && x.PartsName.Contains(p));
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
            var side = Filter.Side.Trim().ToUpperInvariant();
            if (side == "BOT" || side == "B")
            {
                impQuery = impQuery.Where(x => x.Side != null && (x.Side == "B" || x.Side.Contains("BOT") || x.Side.Contains("Bot") || x.Side.Contains("bot")));
            }
            else if (side == "TOP" || side == "T")
            {
                impQuery = impQuery.Where(x => x.Side != null && (x.Side == "T" || x.Side.Contains("TOP") || x.Side.Contains("Top") || x.Side.Contains("top")));
            }
            else
            {
                impQuery = impQuery.Where(x => x.Side != null && x.Side.Contains(side));
            }
        }

        if (!string.IsNullOrWhiteSpace(Filter.Machine))
        {
            var m = Filter.Machine.Trim();
            impQuery = impQuery.Where(x => x.Machine != null && x.Machine.Contains(m));
        }

        // Execute EF queries sequentially to prevent DbContext thread safety errors
        var projectedList = await query
            .Select(e => new
            {
                e.Date,
                e.Line,
                e.Lane,
                e.LotName,
                e.PartsName,
                e.ErrorName
            })
            .ToListAsync(cancellationToken);

        var expensivePartNames = await _dbContext.ExpensiveParts.AsNoTracking()
            .Where(x => x.PartsName != null && x.PartsName != "")
            .Select(x => x.PartsName!)
            .Distinct()
            .ToListAsync(cancellationToken);

        ImprovementHistoryItems = await impQuery
            .OrderByDescending(x => x.ExecutionDate)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(Filter.Side))
        {
            var side = Filter.Side.Trim().ToUpperInvariant();
            projectedList = projectedList.Where(e =>
            {
                var (_, parsedSide, _) = LotNameParser.ParseLineComponents(e.Line, e.LotName);
                var s = parsedSide.ToUpperInvariant();
                if (side == "BOT" || side == "B") return s == "B" || s.Contains("BOT");
                if (side == "TOP" || side == "T") return s == "T" || s.Contains("TOP");
                return s.Contains(side);
            }).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Filter.Line))
        {
            var line = NormalizeLineFilter(Filter.Line);
            projectedList = projectedList.Where(e =>
            {
                var (parsedLine, _, _) = LotNameParser.ParseLineComponents(e.Line, e.LotName);
                return parsedLine.Equals(line, StringComparison.OrdinalIgnoreCase) ||
                       parsedLine.Contains(line, StringComparison.OrdinalIgnoreCase) ||
                       (e.Line?.Contains(Filter.Line.Trim(), StringComparison.OrdinalIgnoreCase) == true);
            }).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Filter.Lane))
        {
            var lane = Filter.Lane.Trim();
            projectedList = projectedList.Where(e =>
                e.Lane?.Contains(lane, StringComparison.OrdinalIgnoreCase) == true ||
                e.LotName?.Contains($"LANE{lane}", StringComparison.OrdinalIgnoreCase) == true).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Filter.Machine))
        {
            var machine = Filter.Machine.Trim();
            projectedList = projectedList.Where(e =>
            {
                var (_, _, parsedMachine) = LotNameParser.ParseLineComponents(e.Line, e.LotName);
                return parsedMachine.Equals(machine, StringComparison.OrdinalIgnoreCase) ||
                       parsedMachine.Contains(machine, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        FilteredRetries = projectedList.Count;

        // 1. TAB 1: ALL RETRIES DAILY TREND (Theo ngày)
        var dailyGroups = projectedList
            .GroupBy(e => NormalizeDateString(e.Date))
            .Select(g => new
            {
                Date = g.Key,
                SortDate = ParseDateForSorting(g.Key),
                Count = g.Count(),
                TopParts = string.Join(", ", g.GroupBy(x => x.PartsName)
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
            double pct = FilteredRetries > 0 ? (dg.Count * 100.0 / FilteredRetries) : 0;
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
                TopPartsOrErrors = dg.TopParts,
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

        // 2. TAB 2: EXPENSIVE PARTS DAILY TREND (Xu hướng LKDT theo ngày)
        var expSet = new HashSet<string>(expensivePartNames, StringComparer.OrdinalIgnoreCase);

        var expensiveProjectedList = projectedList
            .Where(e => !string.IsNullOrEmpty(e.PartsName) && expSet.Contains(e.PartsName))
            .ToList();

        FilteredExpensiveRetries = expensiveProjectedList.Count;

        var expDailyGroups = expensiveProjectedList
            .GroupBy(e => NormalizeDateString(e.Date))
            .Select(g => new
            {
                Date = g.Key,
                SortDate = ParseDateForSorting(g.Key),
                Count = g.Count(),
                TopParts = string.Join(", ", g.GroupBy(x => x.PartsName)
                    .Where(x => !string.IsNullOrEmpty(x.Key))
                    .OrderByDescending(x => x.Count())
                    .Select(x => $"{x.Key} ({x.Count()})")
                    .Take(3))
            })
            .OrderBy(x => x.SortDate)
            .ToList();

        double expDailyCumulative = 0;
        var expDailyItemList = new List<DailyImprovementItem>();
        var chartExpDailyList = new List<object>();

        for (int i = 0; i < expDailyGroups.Count; i++)
        {
            var dg = expDailyGroups[i];
            double pct = FilteredExpensiveRetries > 0 ? (dg.Count * 100.0 / FilteredExpensiveRetries) : 0;
            expDailyCumulative += pct;

            string trend = "-";
            if (i > 0)
            {
                int prevCount = expDailyGroups[i - 1].Count;
                if (dg.Count > prevCount) trend = "▲ Tăng";
                else if (dg.Count < prevCount) trend = "▼ Giảm";
                else trend = "► Bằng";
            }

            expDailyItemList.Add(new DailyImprovementItem
            {
                Rank = i + 1,
                Date = dg.Date,
                Count = dg.Count,
                Percentage = Math.Round(pct, 1),
                CumulativePercentage = Math.Round(expDailyCumulative, 1),
                TopPartsOrErrors = dg.TopParts,
                TrendIndicator = trend
            });

            chartExpDailyList.Add(new
            {
                name = dg.Date,
                count = dg.Count,
                percentage = Math.Round(pct, 1),
                trend = trend
            });
        }
        ExpensiveDailyItems = expDailyItemList;
        ChartExpensiveDailyJson = JsonSerializer.Serialize(chartExpDailyList, JsonOptions);
    }

    private static string NormalizeDateString(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return "(Chưa rõ ngày)";
        var str = dateStr.Trim();
        var spaceIndex = str.IndexOf(' ');
        if (spaceIndex > 0) str = str[..spaceIndex].Trim();
        return str.Replace('/', '-');
    }

    private async Task<IReadOnlyList<string>> GetFilterOptionsAsync(
        Expression<Func<RetryLogEntry, string?>> selector,
        string name,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"retry-improvement-filter-options:{name}";
        var values = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            return await _dbContext.RetryLogEntries.AsNoTracking()
                .Select(selector)
                .Where(value => value != null && value != "")
                .Select(value => value!)
                .Distinct()
                .OrderBy(value => value)
                .Take(50)
                .ToListAsync(cancellationToken);
        });

        return values ?? [];
    }

    private static string NormalizeFilterDate(string value)
    {
        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
            : value.Trim().Replace('-', '/');
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

    private async Task<IReadOnlyList<string>> GetMachineOptionsAsync(CancellationToken cancellationToken)
    {
        var cacheKey = "retry-improvement-filter-options:machines";
        var values = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);

            var rows = await _dbContext.RetryLogEntries.AsNoTracking()
                .Select(x => new { x.Line, x.LotName })
                .Where(x => x.Line != null || x.LotName != null)
                .Take(5000)
                .ToListAsync(cancellationToken);

            return rows
                .Select(x => LotNameParser.ParseLineComponents(x.Line, x.LotName).Machine)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToList();
        });

        return values ?? [];
    }

    private static string NormalizeLineFilter(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("L", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return int.TryParse(trimmed, out _) ? $"L{trimmed}" : trimmed;
    }
}
