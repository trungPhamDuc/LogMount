using System.Globalization;
using LogMount.Data;
using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

[Authorize(Roles = UserRoles.StaffOrAdmin)]
public class ImprovementReportModel(LogMountDbContext dbContext) : PageModel
{
    private const int DefaultWindowDays = 3;
    private const double EffectiveReductionRate = 20.0;

    [BindProperty(SupportsGet = true)]
    public DateOnly? SelectedDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int WindowDays { get; set; } = DefaultWindowDays;

    [BindProperty(SupportsGet = true)]
    public string? EngineerName { get; set; }

    public IReadOnlyList<DateOnly> AvailableDates { get; private set; } = [];
    public IReadOnlyList<RetryImprovementActionResult> ActionResults { get; private set; } = [];
    public IReadOnlyList<RetryImprovementPersonReportRow> PersonRows { get; private set; } = [];
    public IReadOnlyList<string> SummaryBullets { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        WindowDays = WindowDays is 3 or 5 or 7 ? WindowDays : DefaultWindowDays;

        var improveDates = await dbContext.RetryImproves.AsNoTracking()
            .Select(x => x.ExecutionDate)
            .ToListAsync(cancellationToken);

        AvailableDates = improveDates
            .Select(x => DateOnly.FromDateTime(x.Date))
            .Distinct()
            .OrderByDescending(x => x)
            .ToList();

        if (AvailableDates.Count == 0)
        {
            return;
        }

        SelectedDate ??= AvailableDates[0];
        var (from, to) = GetDateRange(SelectedDate.Value);

        var improves = await ApplyEngineerFilter(dbContext.RetryImproves.AsNoTracking())
            .Where(x => x.ExecutionDate >= from && x.ExecutionDate <= to)
            .OrderBy(x => x.EngineerName)
            .ThenBy(x => x.Line)
            .ThenBy(x => x.PartsName)
            .ToListAsync(cancellationToken);

        if (improves.Count == 0)
        {
            return;
        }

        var minDate = from.AddDays(-WindowDays);
        var maxDate = from.AddDays(WindowDays);
        var logs = await LoadRetryLogsAsync(minDate, maxDate, cancellationToken);
        var logDates = logs.Select(x => x.ParsedDate).Where(x => x is not null).Select(x => x!.Value).Distinct().ToHashSet();
        var requestLogs = await LoadRequestLogsAsync(minDate, maxDate, cancellationToken);
        var requestLogDates = requestLogs.Select(x => x.ParsedDate).Where(x => x is not null).Select(x => x!.Value).Distinct().ToHashSet();

        ActionResults = improves
            .Select(item => BuildActionResult(item, logs, logDates, requestLogs, requestLogDates))
            .OrderBy(x => x.EngineerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Line, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PersonRows = ActionResults
            .GroupBy(x => x.EngineerName, StringComparer.OrdinalIgnoreCase)
            .Select(BuildPersonRow)
            .OrderByDescending(x => x.EffectiveActionCount)
            .ThenByDescending(x => x.ActionCount)
            .ThenBy(x => x.EngineerName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SummaryBullets = BuildSummaryBullets();
    }

    private IQueryable<RetryImprove> ApplyEngineerFilter(IQueryable<RetryImprove> query)
    {
        if (!string.IsNullOrWhiteSpace(EngineerName))
        {
            var engineer = EngineerName.Trim();
            query = query.Where(x => x.EngineerName.Contains(engineer));
        }

        return query;
    }

    private async Task<IReadOnlyList<RetryLogSnapshot>> LoadRetryLogsAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var fromText = from.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var toText = to.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

        var logs = await dbContext.RetryLogEntries.AsNoTracking()
            .Where(x => x.Date != null && x.Date.CompareTo(fromText) >= 0 && x.Date.CompareTo(toText) <= 0)
            .Where(x =>
                (x.ErrorNo == null || x.ErrorNo.Trim() != "0") &&
                (x.ErrorName == null || x.ErrorName.Trim().ToLower() != "vision retry"))
            .Select(x => new RetryLogSnapshot(
                x.Date,
                x.PartsName,
                x.Line,
                x.Lane,
                x.LotName,
                x.ErrorName))
            .ToListAsync(cancellationToken);

        return logs.Select(x =>
        {
            var (parsedLine, parsedSide, parsedMachine) = LotNameParser.ParseLineComponents(x.Line, x.LotName);
            return x with
            {
                ParsedDate = ParseLogDate(x.Date),
                ParsedLine = parsedLine,
                ParsedSide = parsedSide,
                ParsedMachine = parsedMachine
            };
        }).ToList();
    }

    private RetryImprovementActionResult BuildActionResult(
        RetryImprove item,
        IReadOnlyList<RetryLogSnapshot> logs,
        ISet<DateTime> logDates,
        IReadOnlyList<RequestLogSnapshot> requestLogs,
        ISet<DateTime> requestLogDates)
    {
        var executionDate = item.ExecutionDate.Date;
        var afterEnd = GetAvailableAfterEnd(executionDate, logDates);
        var compareDays = afterEnd is null ? 0 : Math.Max(1, (afterEnd.Value - executionDate).Days);

        var matchingLogs = logs.Where(log =>
            SameText(log.PartsName, item.PartsName) &&
            MatchesLine(log.Line, log.ParsedLine, item.Line) &&
            MatchesLane(log.Lane, log.LotName, item.Lane) &&
            MatchesSide(log.ParsedSide, item.Side) &&
            MatchesMachine(log.ParsedMachine, item.Machine));

        var before = matchingLogs.Count(log => log.ParsedDate == executionDate);
        var dailyResults = afterEnd is null
            ? []
            : BuildDailyResults(matchingLogs, executionDate, afterEnd.Value, before);
        var status = GetActionStatus(compareDays, dailyResults);

        if (status == RetryImprovementResultStatus.NoData)
        {
            return BuildRequestLogFallbackActionResult(item, requestLogs, requestLogDates);
        }

        var after = afterEnd is null
            ? 0
            : dailyResults.Sum(x => x.AfterCount);
        var reduced = before - after;

        return new RetryImprovementActionResult
        {
            EngineerName = string.IsNullOrWhiteSpace(item.EngineerName) ? "(Chưa rõ)" : item.EngineerName.Trim(),
            ExecutionDate = item.ExecutionDate,
            PartsName = item.PartsName ?? "-",
            Line = item.Line ?? "-",
            Lane = item.Lane ?? "-",
            Side = item.Side ?? "-",
            Machine = item.Machine ?? "-",
            ErrorName = item.ErrorName ?? "-",
            ActionTaken = item.ActionTaken,
            ComparedDays = compareDays,
            BeforeCount = before,
            AfterCount = after,
            ReducedCount = reduced,
            ReducedRate = before == 0 ? 0 : Math.Round(reduced * 100.0 / before, 1),
            ResultStatus = status,
            SourceName = "RetryLog",
            DetailUrl = BuildRetryLogImprovementUrl(item, executionDate, afterEnd ?? executionDate.AddDays(WindowDays)),
            DailyResults = dailyResults
        };
    }

    private async Task<IReadOnlyList<RequestLogSnapshot>> LoadRequestLogsAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        var fromText = from.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var toText = to.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

        var logs = await dbContext.RequestLogEntries.AsNoTracking()
            .Where(x => x.Date != null && x.Date.CompareTo(fromText) >= 0 && x.Date.CompareTo(toText) <= 0)
            .Select(x => new RequestLogSnapshot(
                x.Date,
                x.PartNo,
                x.Line,
                x.RQty))
            .ToListAsync(cancellationToken);

        return logs.Select(x => x with
        {
            ParsedDate = ParseLogDate(x.Date)
        }).ToList();
    }

    private RetryImprovementActionResult BuildRequestLogFallbackActionResult(
        RetryImprove item,
        IReadOnlyList<RequestLogSnapshot> requestLogs,
        ISet<DateTime> requestLogDates)
    {
        var executionDate = item.ExecutionDate.Date;
        var afterEnd = GetAvailableAfterEnd(executionDate, requestLogDates);
        var compareDays = afterEnd is null ? 0 : Math.Max(1, (afterEnd.Value - executionDate).Days);

        var matchingLogs = requestLogs.Where(log =>
            SameText(log.PartNo, item.PartsName) &&
            MatchesRequestLine(log.Line, item.Line) &&
            MatchesRequestLane(log.Line, item.Lane));

        var before = SumRequestQty(matchingLogs, executionDate);
        var dailyResults = afterEnd is null
            ? []
            : BuildRequestDailyResults(matchingLogs, executionDate, afterEnd.Value, before);
        var after = afterEnd is null
            ? 0
            : dailyResults.Sum(x => x.AfterCount);
        var reduced = before - after;
        var status = GetActionStatus(compareDays, dailyResults);

        return new RetryImprovementActionResult
        {
            EngineerName = string.IsNullOrWhiteSpace(item.EngineerName) ? "(Chưa rõ)" : item.EngineerName.Trim(),
            ExecutionDate = item.ExecutionDate,
            PartsName = item.PartsName ?? "-",
            Line = item.Line ?? "-",
            Lane = item.Lane ?? "-",
            Side = item.Side ?? "-",
            Machine = item.Machine ?? "-",
            ErrorName = item.ErrorName ?? "-",
            ActionTaken = item.ActionTaken,
            ComparedDays = compareDays,
            BeforeCount = before,
            AfterCount = after,
            ReducedCount = reduced,
            ReducedRate = before == 0 ? 0 : Math.Round(reduced * 100.0 / before, 1),
            ResultStatus = status,
            SourceName = "RequestLog",
            DetailUrl = BuildRequestLogUrl(item, executionDate, afterEnd ?? executionDate.AddDays(WindowDays)),
            DailyResults = dailyResults
        };
    }

    private RetryImprovementPersonReportRow BuildPersonRow(IGrouping<string, RetryImprovementActionResult> group)
    {
        var rows = group.ToList();
        var effectiveRows = rows.Where(x => x.IsEffective).ToList();
        var bestLines = effectiveRows
            .Where(x => !string.IsNullOrWhiteSpace(x.Line) && x.Line != "-")
            .GroupBy(x => x.Line, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Line = g.Key,
                Reduced = g.SelectMany(x => x.DailyResults).Where(x => x.IsEffective).Sum(x => x.ReducedCount),
                Count = g.Count()
            })
            .OrderByDescending(x => x.Reduced)
            .ThenByDescending(x => x.Count)
            .ToList();

        return new RetryImprovementPersonReportRow
        {
            EngineerName = group.Key,
            ActionCount = rows.Count,
            Lines = JoinDistinct(rows.Select(x => x.Line)),
            EffectiveLines = bestLines.Count == 0
                ? "-"
                : string.Join(", ", bestLines.Select(x => $"{x.Line} (giảm {x.Reduced:N0})")),
            EffectiveActionCount = effectiveRows.Count,
            WaitingActionCount = rows.Count(x => x.ResultStatus == RetryImprovementResultStatus.Waiting),
            BeforeCount = rows.Sum(x => x.BeforeCount),
            AfterCount = rows.Sum(x => x.AfterCount),
            DetailUrl = BuildPersonDetailUrl(rows),
            ComparedDaysText = BuildComparedDaysText(rows),
            ResultText = BuildPersonResultText(rows)
        };
    }

    private IReadOnlyList<string> BuildSummaryBullets()
    {
        if (PersonRows.Count == 0)
        {
            return [];
        }

        var people = string.Join(", ", PersonRows.Select(x => x.EngineerName));
        var allLines = JoinDistinct(ActionResults.Select(x => x.Line));
        var effectiveLines = ActionResults
            .Where(x => x.IsEffective && !string.IsNullOrWhiteSpace(x.Line) && x.Line != "-")
            .GroupBy(x => x.Line, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Line = g.Key,
                Reduced = g.SelectMany(x => x.DailyResults).Where(x => x.IsEffective).Sum(x => x.ReducedCount),
                People = JoinDistinct(g.Select(x => x.EngineerName))
            })
            .OrderByDescending(x => x.Reduced)
            .ToList();
        var checkedDayResults = ActionResults
            .SelectMany(action => action.DailyResults.Select(day => new
            {
                action.EngineerName,
                action.PartsName,
                action.Line,
                action.Side,
                action.Machine,
                action.SourceName,
                Day = day
            }))
            .ToList();
        var effectiveDayResults = checkedDayResults
            .Where(x => x.Day.IsEffective)
            .OrderByDescending(x => x.Day.ReducedRate)
            .ThenByDescending(x => x.Day.ReducedCount)
            .ToList();
        var bestDay = effectiveDayResults.FirstOrDefault();
        var waiting = ActionResults.Count(x => x.ResultStatus == RetryImprovementResultStatus.Waiting);
        var noData = ActionResults.Count(x => x.ResultStatus == RetryImprovementResultStatus.NoData);
        var notEffective = ActionResults.Count(x => x.ResultStatus == RetryImprovementResultStatus.NotEffective);

        var bullets = new List<string>
        {
            $"Có {PersonRows.Count:N0} người đã làm cải thiện RetryLog: {people}.",
            $"Các line đã thực hiện: {allLines}.",
            effectiveLines.Count == 0
                ? $"Chưa có line nào giảm hơn {EffectiveReductionRate:N0}% trong dữ liệu RetryLog sau ngày cải thiện."
                : "Line có hiệu quả: " + string.Join("; ", effectiveLines.Select(x => $"{x.Line} giảm {x.Reduced:N0} lần rơi hiệu quả ({x.People})")) + "."
        };

        if (effectiveDayResults.Count > 0)
        {
            bullets.Add("Ngày có hiệu quả: " + string.Join("; ", effectiveDayResults
                .Take(8)
                .Select(x => $"{x.EngineerName} - {BuildTargetText(x.PartsName, x.Line, x.Side, x.Machine)} [{x.SourceName}] ngày {x.Day.Date:yyyy/MM/dd} giảm {x.Day.ReducedRate:N1}% ({x.Day.BeforeCount:N0} → {x.Day.AfterCount:N0})")) + ".");
        }

        if (bestDay is not null)
        {
            bullets.Add($"Hiệu quả cao nhất: {bestDay.EngineerName} - {BuildTargetText(bestDay.PartsName, bestDay.Line, bestDay.Side, bestDay.Machine)} [{bestDay.SourceName}] ngày {bestDay.Day.Date:yyyy/MM/dd}, giảm {bestDay.Day.ReducedRate:N1}% ({bestDay.Day.BeforeCount:N0} → {bestDay.Day.AfterCount:N0}).");
        }

        if (notEffective > 0)
        {
            var notEffectiveDetails = ActionResults
                .Where(x => x.ResultStatus == RetryImprovementResultStatus.NotEffective)
                .Select(x => $"{x.EngineerName} - {BuildTargetText(x.PartsName, x.Line, x.Side, x.Machine)} [{x.SourceName}]: " +
                             string.Join(", ", x.DailyResults.Select(day => $"{day.Date:yyyy/MM/dd} {BuildDayChangeText(day)} ({day.BeforeCount:N0} → {day.AfterCount:N0})")))
                .Take(5)
                .ToList();
            bullets.Add($"Có {notEffective:N0} hành động chưa đạt mức giảm hơn {EffectiveReductionRate:N0}%: " + string.Join("; ", notEffectiveDetails) + ".");
        }

        if (noData > 0)
        {
            var noDataDetails = ActionResults
                .Where(x => x.ResultStatus == RetryImprovementResultStatus.NoData)
                .Select(x => $"{x.EngineerName} - {BuildTargetText(x.PartsName, x.Line, x.Side, x.Machine)}")
                .Take(5)
                .ToList();
            bullets.Add($"Có {noData:N0} hành động không có dữ liệu RetryLog và RequestLog để so sánh: " + string.Join("; ", noDataDetails) + ".");
        }

        if (waiting > 0)
        {
            bullets.Add($"Có {waiting:N0} hành động đang chờ theo dõi vì chưa có dữ liệu ngày tiếp theo để so sánh.");
        }

        return bullets;
    }

    private DateTime? GetAvailableAfterEnd(DateTime executionDate, ISet<DateTime> logDates)
    {
        var maxDate = executionDate.AddDays(WindowDays);
        return logDates
            .Where(x => x > executionDate && x <= maxDate)
            .OrderByDescending(x => x)
            .FirstOrDefault() is var date && date != default
            ? date
            : null;
    }

    private string BuildRetryLogImprovementUrl(RetryImprove item, DateTime from, DateTime to)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["Filter.FromDate"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Filter.ToDate"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Filter.PartsName"] = item.PartsName,
            ["Filter.Line"] = item.Line,
            ["Filter.Lane"] = item.Lane,
            ["Filter.Side"] = item.Side,
            ["Filter.Machine"] = item.Machine,
            ["Filter.TopN"] = "20"
        };

        return "/RetryLogImprovement?" + string.Join("&", parameters
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));
    }

    private string BuildRequestLogUrl(RetryImprove item, DateTime from, DateTime to)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["SelectedMonth"] = from.ToString("yyyy/MM", CultureInfo.InvariantCulture),
            ["DateFrom"] = DateOnly.FromDateTime(from).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["DateTo"] = DateOnly.FromDateTime(to).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["PartNo"] = item.PartsName,
            ["Line"] = BuildRequestLineFilter(item.Line, item.Lane),
            ["TopN"] = "0",
            ["SortBy"] = "rqty"
        };

        return "/RequestLog/DataByMonth?" + string.Join("&", parameters
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"));
    }

    private static string BuildPersonDetailUrl(IReadOnlyList<RetryImprovementActionResult> rows)
    {
        var first = rows.FirstOrDefault();
        return first?.DetailUrl ?? "/RetryLogImprovement";
    }

    private static string BuildComparedDaysText(IReadOnlyList<RetryImprovementActionResult> rows)
    {
        var days = rows.Where(x => x.ComparedDays > 0).Select(x => x.ComparedDays).Distinct().OrderBy(x => x).ToList();
        var hasWaiting = rows.Any(x => x.ComparedDays == 0);
        var text = days.Count == 0 ? string.Empty : string.Join(", ", days.Select(x => $"{x} ngày"));
        if (hasWaiting)
        {
            text = string.IsNullOrWhiteSpace(text) ? "Chờ theo dõi" : text + ", chờ theo dõi";
        }

        return string.IsNullOrWhiteSpace(text) ? "-" : text;
    }

    private static string BuildPersonResultText(IReadOnlyList<RetryImprovementActionResult> rows)
    {
        if (rows.All(x => x.ResultStatus == RetryImprovementResultStatus.Waiting))
        {
            return "Chờ theo dõi: chưa có dữ liệu ngày tiếp theo để so sánh.";
        }

        if (rows.All(x => x.ResultStatus == RetryImprovementResultStatus.NoData))
        {
            return "Không có dữ liệu RetryLog và RequestLog để so sánh.";
        }

        var details = rows
            .SelectMany(x => x.DailyResults.Select(day => new
            {
                x.PartsName,
                x.Line,
                x.Side,
                x.Machine,
                x.SourceName,
                Day = day
            }))
            .OrderBy(x => x.Day.DayNumber)
            .ThenBy(x => x.Line, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var target = BuildTargetText(x.PartsName, x.Line, x.Side, x.Machine);
                return $"[{x.SourceName}] ngày {x.Day.DayNumber} {BuildDayChangeText(x.Day)} ({x.Day.BeforeCount:N0} → {x.Day.AfterCount:N0}) {target}";
            })
            .ToList();

        if (details.Count == 0)
        {
            return "Chờ theo dõi: chưa có dữ liệu ngày tiếp theo để so sánh.";
        }

        return string.Join("; ", details) + ".";
    }

    private static IReadOnlyList<RetryImprovementDailyResult> BuildDailyResults(
        IEnumerable<RetryLogSnapshot> matchingLogs,
        DateTime executionDate,
        DateTime afterEnd,
        int beforeCount)
    {
        var results = new List<RetryImprovementDailyResult>();
        var days = Math.Max(1, (afterEnd.Date - executionDate.Date).Days);

        for (var day = 1; day <= days; day++)
        {
            var date = executionDate.AddDays(day);
            var afterCount = matchingLogs.Count(log => log.ParsedDate == date.Date);
            var reducedRate = CalculateReductionRate(beforeCount, afterCount);
            var status = reducedRate > EffectiveReductionRate
                ? RetryImprovementResultStatus.Effective
                : RetryImprovementResultStatus.NotEffective;

            results.Add(new RetryImprovementDailyResult
            {
                DayNumber = day,
                Date = date,
                BeforeCount = beforeCount,
                AfterCount = afterCount,
                ReducedRate = reducedRate,
                ResultStatus = status
            });
        }

        return results;
    }

    private static IReadOnlyList<RetryImprovementDailyResult> BuildRequestDailyResults(
        IEnumerable<RequestLogSnapshot> matchingLogs,
        DateTime executionDate,
        DateTime afterEnd,
        int beforeCount)
    {
        var results = new List<RetryImprovementDailyResult>();
        var days = Math.Max(1, (afterEnd.Date - executionDate.Date).Days);

        for (var day = 1; day <= days; day++)
        {
            var date = executionDate.AddDays(day);
            var afterCount = SumRequestQty(matchingLogs, date);
            var reducedRate = CalculateReductionRate(beforeCount, afterCount);
            var status = reducedRate > EffectiveReductionRate
                ? RetryImprovementResultStatus.Effective
                : RetryImprovementResultStatus.NotEffective;

            results.Add(new RetryImprovementDailyResult
            {
                DayNumber = day,
                Date = date,
                BeforeCount = beforeCount,
                AfterCount = afterCount,
                ReducedRate = reducedRate,
                ResultStatus = status
            });
        }

        return results;
    }

    private static RetryImprovementResultStatus GetActionStatus(
        int compareDays,
        IReadOnlyList<RetryImprovementDailyResult> dailyResults)
    {
        if (compareDays == 0)
        {
            return RetryImprovementResultStatus.Waiting;
        }

        if (dailyResults.All(x => x.BeforeCount == 0 && x.AfterCount == 0))
        {
            return RetryImprovementResultStatus.NoData;
        }

        return dailyResults.Any(x => x.ResultStatus == RetryImprovementResultStatus.Effective)
            ? RetryImprovementResultStatus.Effective
            : RetryImprovementResultStatus.NotEffective;
    }

    private static double CalculateReductionRate(int beforeCount, int afterCount)
    {
        if (beforeCount <= 0)
        {
            return afterCount == 0 ? 0 : -100;
        }

        return Math.Round((beforeCount - afterCount) * 100.0 / beforeCount, 1);
    }

    private static int SumRequestQty(IEnumerable<RequestLogSnapshot> matchingLogs, DateTime date)
    {
        return (int)Math.Round(matchingLogs
            .Where(log => log.ParsedDate == date.Date)
            .Sum(log => log.RQty), MidpointRounding.AwayFromZero);
    }

    private static string BuildTargetText(params string?[] values)
    {
        return string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v) && v != "-"));
    }

    private static string BuildDayChangeText(RetryImprovementDailyResult day)
    {
        if (day.AfterCount < day.BeforeCount)
        {
            return $"giảm {day.ReducedRate:N1}%";
        }

        if (day.AfterCount > day.BeforeCount)
        {
            return $"tăng {Math.Abs(day.ReducedRate):N1}%";
        }

        return "bằng 0.0%";
    }

    private static bool SameText(string? source, string? expected)
    {
        return string.IsNullOrWhiteSpace(expected) ||
               expected == "-" ||
               string.Equals(source?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLine(string? rawLine, string? parsedLine, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || expected == "-")
        {
            return true;
        }

        var normalized = NormalizeLine(expected);
        return SameText(parsedLine, normalized) ||
               SameText(rawLine, expected) ||
               (!string.IsNullOrWhiteSpace(rawLine) && rawLine.Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesRequestLine(string? requestLine, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || expected == "-")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return false;
        }

        var normalized = NormalizeLine(expected);
        var requestLineNumber = ParseRequestLineNumber(requestLine);
        return SameText(requestLineNumber, normalized) ||
               requestLine.Contains(expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesRequestLane(string? requestLine, string? expectedLane)
    {
        if (string.IsNullOrWhiteSpace(expectedLane) || expectedLane == "-")
        {
            return true;
        }

        return string.Equals(ParseRequestLane(requestLine), expectedLane.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLane(string? lane, string? lotName, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || expected == "-")
        {
            return true;
        }

        var value = expected.Trim();
        return SameText(lane, value) ||
               SameText(lane, $"LANE{value}") ||
               (!string.IsNullOrWhiteSpace(lane) && lane.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
               (!string.IsNullOrWhiteSpace(lotName) && lotName.Contains($"LANE{value}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesMachine(string? parsedMachine, string? expected)
    {
        return SameText(parsedMachine, expected);
    }

    private static bool MatchesSide(string? parsedSide, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || expected == "-")
        {
            return true;
        }

        var side = expected.Trim().ToUpperInvariant();
        var parsed = parsedSide?.Trim().ToUpperInvariant() ?? string.Empty;

        if (side is "BOT" or "B")
        {
            return parsed is "B" or "BOT";
        }

        if (side is "TOP" or "T")
        {
            return parsed is "T" or "TOP";
        }

        return parsed.Contains(side, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLine(string line)
    {
        var value = line.Trim();
        if (value.Equals("LTE", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("LLTE", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("L0", StringComparison.OrdinalIgnoreCase))
        {
            return "LLTE";
        }

        return value.StartsWith("L", StringComparison.OrdinalIgnoreCase)
            ? value.ToUpperInvariant()
            : $"L{value}";
    }

    private static string? BuildRequestLineFilter(string? line, string? lane)
    {
        if (string.IsNullOrWhiteSpace(line) || line == "-")
        {
            return null;
        }

        var trimmedLine = line.Trim();
        if (trimmedLine.Contains('.'))
        {
            return trimmedLine;
        }

        var normalized = NormalizeLine(trimmedLine);
        if (normalized.Equals("LLTE", StringComparison.OrdinalIgnoreCase))
        {
            return "LTE";
        }

        if (!string.IsNullOrWhiteSpace(lane) && lane != "-")
        {
            return $"{normalized.TrimStart('L')}.{lane.Trim()}";
        }

        return null;
    }

    private static string ParseRequestLineNumber(string? requestLine)
    {
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return string.Empty;
        }

        var value = requestLine.Trim();
        if (value.Contains("LTE", StringComparison.OrdinalIgnoreCase))
        {
            return "LLTE";
        }

        var separatorIndex = value.IndexOf('.');
        var firstPart = separatorIndex >= 0 ? value[..separatorIndex] : value;
        return string.IsNullOrWhiteSpace(firstPart) ? string.Empty : NormalizeLine(firstPart);
    }

    private static string ParseRequestLane(string? requestLine)
    {
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return string.Empty;
        }

        var value = requestLine.Trim();
        var separatorIndex = value.IndexOf('.');
        return separatorIndex >= 0 && separatorIndex + 1 < value.Length
            ? value[(separatorIndex + 1)..].Trim()
            : string.Empty;
    }

    private static DateTime? ParseLogDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var dateText = value.Trim();
        var spaceIndex = dateText.IndexOf(' ');
        if (spaceIndex > 0)
        {
            dateText = dateText[..spaceIndex];
        }

        return DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
               DateTime.TryParse(dateText, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed)
            ? parsed.Date
            : null;
    }

    private static bool IsBetween(DateTime? date, DateTime from, DateTime to)
    {
        return date is not null && date.Value >= from.Date && date.Value <= to.Date;
    }

    private static (DateTime From, DateTime To) GetDateRange(DateOnly value)
    {
        var start = value.ToDateTime(TimeOnly.MinValue);
        return (start, start.AddDays(1).AddTicks(-1));
    }

    private static string JoinDistinct(IEnumerable<string?> values)
    {
        var result = values
            .Where(x => !string.IsNullOrWhiteSpace(x) && x != "-")
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return result.Count == 0 ? "-" : string.Join(", ", result);
    }

    private sealed record RetryLogSnapshot(
        string? Date,
        string? PartsName,
        string? Line,
        string? Lane,
        string? LotName,
        string? ErrorName)
    {
        public DateTime? ParsedDate { get; init; }
        public string ParsedLine { get; init; } = string.Empty;
        public string ParsedSide { get; init; } = string.Empty;
        public string ParsedMachine { get; init; } = string.Empty;
    }

    private sealed record RequestLogSnapshot(
        string? Date,
        string? PartNo,
        string? Line,
        decimal RQty)
    {
        public DateTime? ParsedDate { get; init; }
    }
}

public enum RetryImprovementResultStatus
{
    Effective,
    NotEffective,
    Waiting,
    NoData
}

public class RetryImprovementActionResult
{
    public string EngineerName { get; set; } = string.Empty;
    public DateTime ExecutionDate { get; set; }
    public string PartsName { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string Lane { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
    public string ErrorName { get; set; } = string.Empty;
    public string ActionTaken { get; set; } = string.Empty;
    public int ComparedDays { get; set; }
    public int BeforeCount { get; set; }
    public int AfterCount { get; set; }
    public int ReducedCount { get; set; }
    public double ReducedRate { get; set; }
    public RetryImprovementResultStatus ResultStatus { get; set; }
    public string SourceName { get; set; } = "RetryLog";
    public string DetailUrl { get; set; } = string.Empty;
    public IReadOnlyList<RetryImprovementDailyResult> DailyResults { get; set; } = [];
    public bool IsEffective => ResultStatus == RetryImprovementResultStatus.Effective;
}

public class RetryImprovementDailyResult
{
    public int DayNumber { get; set; }
    public DateTime Date { get; set; }
    public int BeforeCount { get; set; }
    public int AfterCount { get; set; }
    public int ReducedCount => BeforeCount - AfterCount;
    public double ReducedRate { get; set; }
    public RetryImprovementResultStatus ResultStatus { get; set; }
    public bool IsEffective => ResultStatus == RetryImprovementResultStatus.Effective;
    public string ResultText => AfterCount < BeforeCount
        ? "giảm"
        : AfterCount > BeforeCount
            ? "tăng"
            : "bằng";
}

public class RetryImprovementPersonReportRow
{
    public string EngineerName { get; set; } = string.Empty;
    public string Lines { get; set; } = string.Empty;
    public int ActionCount { get; set; }
    public int EffectiveActionCount { get; set; }
    public int WaitingActionCount { get; set; }
    public int BeforeCount { get; set; }
    public int AfterCount { get; set; }
    public string EffectiveLines { get; set; } = string.Empty;
    public string ComparedDaysText { get; set; } = string.Empty;
    public string ResultText { get; set; } = string.Empty;
    public string DetailUrl { get; set; } = string.Empty;
}
