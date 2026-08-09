using LogMount.Models;

namespace LogMount.Services;

public static class ErrorLogAnalysisService
{
    private const int MaxDistinctDisplay = 5;

    public static IReadOnlyList<ErrorLogEntry> Filter(
        IReadOnlyList<ErrorLogEntry> entries,
        ErrorLogFilterCriteria criteria)
    {
        IEnumerable<ErrorLogEntry> query = entries;

        query = ApplyContainsFilter(query, criteria.Date, e => e.Date);
        query = ApplyContainsFilter(query, criteria.EventDate, e => e.EventDate);
        query = ApplyContainsFilter(query, criteria.Line, e => ErrorLogHelper.GetEffectiveLine(e.Line, e.ProgramName));
        query = ApplyContainsFilter(query, criteria.Lane, e => e.Lane);
        query = ApplyContainsFilter(query, criteria.Table, e => e.Table);
        query = ApplyContainsFilter(query, criteria.Error, e => e.Error);
        query = ApplyContainsFilter(query, criteria.EventNo, e => e.EventNo);
        query = ApplyContainsFilter(query, criteria.ProgramName, e => e.ProgramName);
        query = ApplyContainsFilter(query, criteria.Details, e => e.Details);
        query = ApplySideFilter(query, criteria.Side);
        query = ApplyMachineFilter(query, criteria.Machine);

        return query.ToList();
    }

    public static IReadOnlyList<ErrorLogSummaryItem> SummarizeErrors(IReadOnlyList<ErrorLogEntry> entries)
    {
        return entries
            .GroupBy(e => new { e.Error, e.EventNo })
            .Select(g => new ErrorLogSummaryItem
            {
                Error = string.IsNullOrWhiteSpace(g.Key.Error) ? "(Không có lỗi)" : g.Key.Error,
                EventNo = g.Key.EventNo ?? string.Empty,
                Count = g.Count(),
                Dates = DistinctValues(g, e => e.Date),
                Lines = DistinctValues(g, e => ErrorLogHelper.GetEffectiveLine(e.Line, e.ProgramName)),
                Lanes = DistinctValues(g, e => e.Lane),
                Tables = DistinctValues(g, e => e.Table)
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Error, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<ErrorLogDetailSummaryItem> SummarizeByLocation(IReadOnlyList<ErrorLogEntry> entries)
    {
        return entries
            .Select(e => new
            {
                Entry = e,
                Line = ErrorLogHelper.GetEffectiveLine(e.Line, e.ProgramName) ?? string.Empty,
                Side = ErrorLogHelper.ParseSide(e.ProgramName),
                Machine = ErrorLogHelper.ParseMachine(e.ProgramName)
            })
            .GroupBy(x => new
            {
                Error = string.IsNullOrWhiteSpace(x.Entry.Error) ? "(Không có lỗi)" : x.Entry.Error,
                x.Entry.EventNo,
                x.Line,
                x.Entry.Lane,
                x.Side,
                x.Machine,
                x.Entry.Table
            })
            .Select(g => new ErrorLogDetailSummaryItem
            {
                Error = g.Key.Error,
                EventNo = g.Key.EventNo ?? string.Empty,
                Line = g.Key.Line,
                Lane = g.Key.Lane ?? string.Empty,
                Side = g.Key.Side,
                SideLabel = ErrorLogHelper.GetSideLabel(g.Key.Side),
                Machine = g.Key.Machine,
                Table = g.Key.Table ?? string.Empty,
                Count = g.Count()
            })
            .ToList();
    }

    public static IReadOnlyList<ErrorLogDetailSummaryItem> FilterDetailSummary(
        IReadOnlyList<ErrorLogDetailSummaryItem> items,
        ErrorLogFilterCriteria criteria)
    {
        IEnumerable<ErrorLogDetailSummaryItem> query = items;

        query = ApplyContainsFilter(query, criteria.Error, x => x.Error);
        query = ApplyContainsFilter(query, criteria.Line, x => x.Line);
        query = ApplyContainsFilter(query, criteria.Lane, x => x.Lane);
        query = ApplyContainsFilter(query, criteria.Table, x => x.Table);
        query = ApplyContainsFilter(query, criteria.EventNo, x => x.EventNo);
        query = ApplyContainsFilter(query, criteria.Side, x => x.Side);
        query = ApplyContainsFilter(query, criteria.Machine, x => x.Machine);

        return query.ToList();
    }

    public static IReadOnlyList<ErrorLogDetailSummaryItem> SortDetailSummary(
        IReadOnlyList<ErrorLogDetailSummaryItem> items,
        ErrorLogFilterCriteria criteria)
    {
        if (criteria.IsCountSort)
        {
            return criteria.IsDescending
                ? items.OrderByDescending(x => x.Count)
                    .ThenBy(x => GetLineSortOrder(x.Line))
                    .ThenBy(x => x.Lane, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => GetSideSortOrder(x.Side))
                    .ThenBy(x => x.Machine, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Error, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : items.OrderBy(x => x.Count)
                    .ThenBy(x => GetLineSortOrder(x.Line))
                    .ThenBy(x => x.Lane, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => GetSideSortOrder(x.Side))
                    .ThenBy(x => x.Machine, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Error, StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        return items
            .OrderBy(x => GetLineSortOrder(x.Line))
            .ThenBy(x => x.Line, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Lane, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => GetSideSortOrder(x.Side))
            .ThenBy(x => x.Machine, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.Error, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<ErrorLogTopItem> GetTopErrors(
        IReadOnlyList<ErrorLogDetailSummaryItem> items,
        int topN)
    {
        if (items.Count == 0 || topN <= 0)
        {
            return [];
        }

        var topItems = items
            .GroupBy(x => x.Error, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ErrorLogTopItem
            {
                Error = g.Key,
                TotalCount = g.Sum(x => x.Count),
                LocationGroupCount = g.Count()
            })
            .OrderByDescending(x => x.TotalCount)
            .ThenBy(x => x.Error, StringComparer.OrdinalIgnoreCase)
            .Take(topN)
            .ToList();

        for (var i = 0; i < topItems.Count; i++)
        {
            topItems[i].Rank = i + 1;
        }

        return topItems;
    }

    private static IEnumerable<ErrorLogEntry> ApplySideFilter(
        IEnumerable<ErrorLogEntry> query,
        string? side)
    {
        if (string.IsNullOrWhiteSpace(side))
        {
            return query;
        }

        var trimmed = side.Trim();
        return query.Where(e =>
            ErrorLogHelper.ParseSide(e.ProgramName).Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
            ErrorLogHelper.GetSideLabel(ErrorLogHelper.ParseSide(e.ProgramName))
                .Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<ErrorLogEntry> ApplyMachineFilter(
        IEnumerable<ErrorLogEntry> query,
        string? machine)
    {
        if (string.IsNullOrWhiteSpace(machine))
        {
            return query;
        }

        var trimmed = machine.Trim();
        return query.Where(e =>
            ErrorLogHelper.ParseMachine(e.ProgramName).Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<T> ApplyContainsFilter<T>(
        IEnumerable<T> query,
        string? value,
        Func<T, string?> selector)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return query;
        }

        var trimmed = value.Trim();
        return query.Where(e => selector(e)?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string DistinctValues(IEnumerable<ErrorLogEntry> entries, Func<ErrorLogEntry, string?> selector)
    {
        var values = entries
            .Select(selector)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (values.Count == 0)
        {
            return "-";
        }

        if (values.Count <= MaxDistinctDisplay)
        {
            return string.Join(", ", values);
        }

        return string.Join(", ", values.Take(MaxDistinctDisplay)) + $" (+{values.Count - MaxDistinctDisplay})";
    }

    private static int GetLineSortOrder(string? line)
    {
        var value = line?.Trim() ?? string.Empty;
        if (value.Contains("LTE", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var lineNumber) ? lineNumber : int.MaxValue;
    }

    private static int GetSideSortOrder(string? side) =>
        side?.Trim().Equals("B", StringComparison.OrdinalIgnoreCase) == true ? 0 :
        side?.Trim().Equals("T", StringComparison.OrdinalIgnoreCase) == true ? 1 : 2;
}
