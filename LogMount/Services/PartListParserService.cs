using System.Globalization;
using ClosedXML.Excel;
using LogMount.Models;

namespace LogMount.Services;

public class PartListParserService : IPartListParserService
{
    public Task<IReadOnlyList<ExpensivePart>> ParseAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        IReadOnlyList<ExpensivePart> parts = extension switch
        {
            ".xlsx" or ".xls" => ParseExcel(stream),
            ".csv" => ParseCsv(stream),
            _ => throw new InvalidOperationException("Chỉ hỗ trợ file .xlsx, .xls hoặc .csv.")
        };

        return Task.FromResult(parts);
    }

    private static IReadOnlyList<ExpensivePart> ParseExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var parts = new Dictionary<string, ExpensivePart>(StringComparer.OrdinalIgnoreCase);

        for (var row = 1; row <= lastRow; row++)
        {
            var partsName = worksheet.Cell(row, 1).GetFormattedString().Trim();
            if (string.IsNullOrWhiteSpace(partsName) || IsHeaderValue(partsName))
            {
                continue;
            }

            var costText = worksheet.Cell(row, 3).GetFormattedString().Trim();
            parts[partsName] = new ExpensivePart
            {
                PartsName = partsName,
                Cost = ParseCost(costText)
            };
        }

        return EnsurePartsFound(parts.Values, "cột A");
    }

    private static IReadOnlyList<ExpensivePart> ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var parts = new Dictionary<string, ExpensivePart>(StringComparer.OrdinalIgnoreCase);

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = line.Split(',');
            var partsName = values.ElementAtOrDefault(0)?.Trim().Trim('"') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(partsName) || IsHeaderValue(partsName))
            {
                continue;
            }

            parts[partsName] = new ExpensivePart
            {
                PartsName = partsName,
                Cost = ParseCost(values.ElementAtOrDefault(2)?.Trim().Trim('"') ?? string.Empty)
            };
        }

        return EnsurePartsFound(parts.Values, "file CSV");
    }

    private static IReadOnlyList<ExpensivePart> EnsurePartsFound(
        IEnumerable<ExpensivePart> parts,
        string source)
    {
        var result = parts.ToList();
        if (result.Count == 0)
        {
            throw new InvalidOperationException($"Không tìm thấy part name trong {source}.");
        }

        return result;
    }

    private static decimal ParseCost(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var styles = NumberStyles.Number | NumberStyles.AllowCurrencySymbol;
        return decimal.TryParse(value, styles, CultureInfo.InvariantCulture, out var cost) ||
               decimal.TryParse(value, styles, CultureInfo.GetCultureInfo("vi-VN"), out cost)
            ? cost
            : 0;
    }

    private static bool IsHeaderValue(string value)
    {
        var normalized = value.Trim();
        return normalized.Equals("Part Name", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Parts Name", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Partname", StringComparison.OrdinalIgnoreCase);
    }
}
