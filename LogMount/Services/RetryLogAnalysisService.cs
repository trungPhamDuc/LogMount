using LogMount.Models;

namespace LogMount.Services;

public static class RetryLogAnalysisService
{
    private const int MaxDistinctDisplay = 5;

    public static IReadOnlyList<RetryLogEntry> Filter(
        IReadOnlyList<RetryLogEntry> entries,
        LogFilterCriteria criteria)
    {
        IEnumerable<RetryLogEntry> query = entries;

        query = ApplyContainsFilter(query, criteria.Date, e => e.Date);
        query = ApplyContainsFilter(query, criteria.Line, e => e.Line);
        query = ApplyContainsFilter(query, criteria.Language, e => e.Language);
        query = ApplyContainsFilter(query, criteria.OccurrenceTime, e => e.OccurrenceTime);
        query = ApplyContainsFilter(query, criteria.LotName, e => e.LotName);
        query = ApplyContainsFilter(query, criteria.ErrorNo, e => e.ErrorNo);
        query = ApplyContainsFilter(query, criteria.ErrorName, e => e.ErrorName);
        query = ApplyContainsFilter(query, criteria.Lane, e => e.Lane);
        query = ApplyContainsFilter(query, criteria.Table, e => e.Table);
        query = ApplyContainsFilter(query, criteria.PartsNo, e => e.PartsNo);
        query = ApplyContainsFilter(query, criteria.PartsName, e => e.PartsName);
        query = ApplyContainsFilter(query, criteria.HeadNo, e => e.HeadNo);
        query = ApplyContainsFilter(query, criteria.NozzleType, e => e.NozzleType);
        query = ApplyContainsFilter(query, criteria.FeederNo, e => e.FeederNo);
        query = ApplyContainsFilter(query, criteria.FeederId, e => e.FeederId);
        query = ApplyContainsFilter(query, criteria.CartId, e => e.CartId);
        query = ApplyContainsFilter(query, criteria.VisErrorNo, e => e.VisErrorNo);
        query = ApplyContainsFilter(query, criteria.ErrorVacuum, e => e.ErrorVacuum);

        return query.ToList();
    }

    public static IReadOnlyList<ErrorSummaryItem> SummarizeErrors(IReadOnlyList<RetryLogEntry> entries)
    {
        return entries
            .GroupBy(e => new { e.ErrorName, e.ErrorNo })
            .Select(g => new ErrorSummaryItem
            {
                ErrorName = string.IsNullOrWhiteSpace(g.Key.ErrorName) ? "(Không có tên lỗi)" : g.Key.ErrorName,
                ErrorNo = g.Key.ErrorNo ?? string.Empty,
                Count = g.Count(),
                Dates = DistinctValues(g, e => e.Date),
                Lines = DistinctValues(g, e => e.Line),
                OccurrenceTimes = DistinctValues(g, e => e.OccurrenceTime),
                LotNames = DistinctValues(g, e => e.LotName),
                Lanes = DistinctValues(g, e => e.Lane),
                Tables = DistinctValues(g, e => e.Table),
                PartsNumbers = DistinctValues(g, e => e.PartsNo),
                PartsNames = DistinctValues(g, e => e.PartsName),
                HeadNumbers = DistinctValues(g, e => e.HeadNo),
                NozzleTypes = DistinctValues(g, e => e.NozzleType),
                FeederNumbers = DistinctValues(g, e => e.FeederNo),
                FeederIds = DistinctValues(g, e => e.FeederId),
                CartIds = DistinctValues(g, e => e.CartId),
                VisErrorNumbers = DistinctValues(g, e => e.VisErrorNo),
                ErrorVacuums = DistinctValues(g, e => e.ErrorVacuum)
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.ErrorName)
            .ToList();
    }

    public static IReadOnlyList<ColumnSummaryItem> SummarizeColumns(IReadOnlyList<RetryLogEntry> entries)
    {
        return
        [
            BuildColumnSummary("Date", entries, e => e.Date),
            BuildColumnSummary("Line", entries, e => e.Line),
            BuildColumnSummary("Language", entries, e => e.Language),
            BuildColumnSummary("Occurrence Time", entries, e => e.OccurrenceTime),
            BuildColumnSummary("Lot Name", entries, e => e.LotName),
            BuildColumnSummary("Error No.", entries, e => e.ErrorNo),
            BuildColumnSummary("Error Name", entries, e => e.ErrorName),
            BuildColumnSummary("Lane", entries, e => e.Lane),
            BuildColumnSummary("Table", entries, e => e.Table),
            BuildColumnSummary("Parts No.", entries, e => e.PartsNo),
            BuildColumnSummary("Parts Name", entries, e => e.PartsName),
            BuildColumnSummary("Head No.", entries, e => e.HeadNo),
            BuildColumnSummary("Nozzle Type", entries, e => e.NozzleType),
            BuildColumnSummary("Feeder No.", entries, e => e.FeederNo),
            BuildColumnSummary("Feeder ID", entries, e => e.FeederId),
            BuildColumnSummary("Cart ID", entries, e => e.CartId),
            BuildColumnSummary("Vis Error No.", entries, e => e.VisErrorNo),
            BuildColumnSummary("Error Vacuum", entries, e => e.ErrorVacuum)
        ];
    }

    private static IEnumerable<RetryLogEntry> ApplyContainsFilter(
        IEnumerable<RetryLogEntry> query,
        string? value,
        Func<RetryLogEntry, string?> selector)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return query;
        }

        var trimmed = value.Trim();
        return query.Where(e => selector(e)?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static ColumnSummaryItem BuildColumnSummary(
        string columnName,
        IReadOnlyList<RetryLogEntry> entries,
        Func<RetryLogEntry, string?> selector)
    {
        var values = entries
            .Select(selector)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ColumnSummaryItem
        {
            ColumnName = columnName,
            DistinctCount = values.Count,
            Values = FormatDistinctValues(values)
        };
    }

    private static string DistinctValues(IEnumerable<RetryLogEntry> entries, Func<RetryLogEntry, string?> selector)
    {
        var values = entries
            .Select(selector)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return FormatDistinctValues(values);
    }

    private static string FormatDistinctValues(IReadOnlyList<string> values)
    {
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
