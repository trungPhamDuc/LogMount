using LogMount.Models;

namespace LogMount.Services;

public static class ExpensivePartAnalysisService
{
    public static IReadOnlyList<ExpensivePartSummaryItem> Summarize(
        IReadOnlyList<RetryLogEntry> logEntries,
        IReadOnlyList<ExpensivePart> expensiveParts)
    {
        if (logEntries.Count == 0 || expensiveParts.Count == 0)
        {
            return [];
        }

        var costsByPartName = expensiveParts
            .Where(part => !string.IsNullOrWhiteSpace(part.PartsName))
            .GroupBy(part => part.PartsName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
            group => group.Key,
            group => group.Last().Cost ?? 0m,
            StringComparer.OrdinalIgnoreCase);

        return logEntries
            .Where(e => !string.IsNullOrWhiteSpace(e.PartsName) &&
                        costsByPartName.ContainsKey(e.PartsName!) &&
                        !IsVisionRetry(e.ErrorName))
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
                Cost = costsByPartName[x.Entry.PartsName!],
                x.LineNumber,
                x.Side,
                x.Machine,
                x.Entry.Lane,
                x.Entry.FeederNo,
                x.Entry.ErrorNo,
                x.Entry.ErrorName
            })
            .Select(g => new ExpensivePartSummaryItem
            {
                PartsName = g.Key.PartsName ?? string.Empty,
                Cost = g.Key.Cost,
                Line = g.Key.LineNumber,
                Side = g.Key.Side,
                SideLabel = LotNameParser.GetSideLabel(g.Key.Side),
                Machine = g.Key.Machine,
                Lane = g.Key.Lane ?? string.Empty,
                FeederNo = g.Key.FeederNo ?? string.Empty,
                ErrorNo = g.Key.ErrorNo ?? string.Empty,
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
        ExpensivePartFilterCriteria criteria)
    {
        if (criteria.IsCostSort)
        {
            return criteria.IsCostDescending
                ? items.OrderByDescending(x => x.TotalCost)
                    .ThenByDescending(x => x.Count)
                    .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : items.OrderBy(x => x.TotalCost)
                    .ThenByDescending(x => x.Count)
                    .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        if (criteria.IsCountSort)
        {
            return criteria.IsDescending
                ? items.OrderByDescending(x => x.Count)
                    .ThenBy(x => GetLineSortOrder(x.Line))
                    .ThenBy(x => GetSideSortOrder(x.Side))
                    .ThenBy(x => x.Machine, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : items.OrderBy(x => x.Count)
                    .ThenBy(x => GetLineSortOrder(x.Line))
                    .ThenBy(x => GetSideSortOrder(x.Side))
                    .ThenBy(x => x.Machine, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        var orderedItems = items
            .OrderBy(x => GetLineSortOrder(x.Line))
            .ThenBy(x => x.Line, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => GetSideSortOrder(x.Side))
            .ThenBy(x => x.Machine, StringComparer.OrdinalIgnoreCase);

        return orderedItems
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ErrorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<ExpensivePartTopItem> GetTopParts(
        IReadOnlyList<ExpensivePartSummaryItem> items,
        int topN,
        bool sortByCost)
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
                Cost = g.First().Cost,
                TotalCount = g.Sum(x => x.Count),
                TotalCost = g.Sum(x => x.TotalCost),
                ErrorGroupCount = g.Count()
            })
            .ToList();

        var sortedTopItems = sortByCost
            ? topItems.OrderByDescending(x => x.TotalCost)
                .ThenByDescending(x => x.TotalCount)
                .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
                .Take(topN)
                .ToList()
            : topItems.OrderByDescending(x => x.TotalCount)
                .ThenBy(x => x.PartsName, StringComparer.OrdinalIgnoreCase)
                .Take(topN)
                .ToList();

        for (var i = 0; i < sortedTopItems.Count; i++)
        {
            sortedTopItems[i].Rank = i + 1;
        }

        return sortedTopItems;
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

    private static bool IsVisionRetry(string? errorName) =>
        string.Equals(errorName?.Trim(), "Vision Retry", StringComparison.OrdinalIgnoreCase);

    private static int GetLineSortOrder(string? line)
    {
        var value = line?.Trim() ?? string.Empty;
        return value.StartsWith("L", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(value[1..], out var lineNumber)
            ? lineNumber
            : int.MaxValue;
    }

    private static int GetSideSortOrder(string? side) =>
        side?.Trim().Equals("B", StringComparison.OrdinalIgnoreCase) == true ? 0 :
        side?.Trim().Equals("T", StringComparison.OrdinalIgnoreCase) == true ? 1 : 2;
}
