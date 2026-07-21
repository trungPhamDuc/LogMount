using LogMount.Models;

namespace LogMount.Services;

public static class ExpensivePartAnalysisService
{
    public static IReadOnlyList<ExpensivePartSummaryItem> Summarize(
        IReadOnlyList<RetryLogEntry> logEntries,
        IReadOnlyList<string> expensivePartNames)
    {
        if (logEntries.Count == 0 || expensivePartNames.Count == 0)
        {
            return [];
        }

        var partSet = new HashSet<string>(expensivePartNames, StringComparer.OrdinalIgnoreCase);

        return logEntries
            .Where(e => !string.IsNullOrWhiteSpace(e.PartsName) && partSet.Contains(e.PartsName))
            .Select(e =>
            {
                var (lineNumber, side, machine) = LotNameParser.ParseLineComponents(e.Line, e.LotName);
                return new
                {
                    Entry = e,
                    LineNumber = lineNumber,
                    Side = side,
                    Machine = machine
                };
            })
            .GroupBy(x => new
            {
                x.Entry.PartsName,
                x.LineNumber,
                x.Side,
                x.Machine,
                x.Entry.ErrorNo,
                x.Entry.ErrorName
            })
            .Select(g => new ExpensivePartSummaryItem
            {
                PartsName = g.Key.PartsName,
                Line = g.Key.LineNumber,
                Side = g.Key.Side,
                SideLabel = LotNameParser.GetSideLabel(g.Key.Side),
                Machine = g.Key.Machine,
                ErrorNo = g.Key.ErrorNo,
                ErrorName = string.IsNullOrWhiteSpace(g.Key.ErrorName) ? "(Không có tên lỗi)" : g.Key.ErrorName,
                Count = g.Count()
            })
            .OrderBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Line, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Side, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Machine, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.ErrorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<ExpensivePartSummaryItem> Filter(
        IReadOnlyList<ExpensivePartSummaryItem> items,
        ExpensivePartFilterCriteria criteria)
    {
        IEnumerable<ExpensivePartSummaryItem> query = items;

        query = ApplyContainsFilter(query, criteria.PartsName, x => x.PartsName);
        query = ApplyContainsFilter(query, criteria.Line, x => x.Line);
        query = ApplyContainsFilter(query, criteria.Machine, x => x.Machine);
        query = ApplyContainsFilter(query, criteria.ErrorName, x => x.ErrorName);

        return query.ToList();
    }

    public static IReadOnlyList<ExpensivePartSummaryItem> SortByCount(
        IReadOnlyList<ExpensivePartSummaryItem> items,
        bool descending)
    {
        return descending
            ? items.OrderByDescending(x => x.Count)
                .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ErrorName, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : items.OrderBy(x => x.Count)
                .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ErrorName, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    public static IReadOnlyList<ExpensivePartTopItem> GetTopParts(
        IReadOnlyList<ExpensivePartSummaryItem> items,
        int topN)
    {
        if (items.Count == 0 || topN <= 0)
        {
            return [];
        }

        var topItems = items
            .GroupBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ExpensivePartTopItem
            {
                PartsName = g.Key,
                TotalCount = g.Sum(x => x.Count),
                ErrorGroupCount = g.Count()
            })
            .OrderByDescending(x => x.TotalCount)
            .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
            .Take(topN)
            .ToList();

        for (var i = 0; i < topItems.Count; i++)
        {
            topItems[i].Rank = i + 1;
        }

        return topItems;
    }

    private static IEnumerable<ExpensivePartSummaryItem> ApplyContainsFilter(
        IEnumerable<ExpensivePartSummaryItem> query,
        string? value,
        Func<ExpensivePartSummaryItem, string> selector)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return query;
        }

        var trimmed = value.Trim();
        return query.Where(x => selector(x).Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
