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
        query = ApplyContainsFilter(query, criteria.Line, e => e.Line);
        query = ApplyContainsFilter(query, criteria.Lane, e => e.Lane);
        query = ApplyContainsFilter(query, criteria.Table, e => e.Table);
        query = ApplyContainsFilter(query, criteria.Error, e => e.Error);
        query = ApplyContainsFilter(query, criteria.EventNo, e => e.EventNo);
        query = ApplyContainsFilter(query, criteria.ProgramName, e => e.ProgramName);
        query = ApplyContainsFilter(query, criteria.Details, e => e.Details);

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
                Lines = DistinctValues(g, e => e.Line),
                Lanes = DistinctValues(g, e => e.Lane),
                Tables = DistinctValues(g, e => e.Table)
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Error, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<ErrorLogEntry> ApplyContainsFilter(
        IEnumerable<ErrorLogEntry> query,
        string? value,
        Func<ErrorLogEntry, string?> selector)
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
}
