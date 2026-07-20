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
}
