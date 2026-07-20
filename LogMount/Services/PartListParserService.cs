using ClosedXML.Excel;

namespace LogMount.Services;

public class PartListParserService : IPartListParserService
{
    public Task<IReadOnlyList<string>> ParseAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        IReadOnlyList<string> partNames = extension switch
        {
            ".xlsx" or ".xls" => ParseExcel(stream),
            ".csv" => ParseCsv(stream),
            _ => throw new InvalidOperationException("Chỉ hỗ trợ file .xlsx, .xls hoặc .csv.")
        };

        return Task.FromResult(partNames);
    }

    private static IReadOnlyList<string> ParseExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;

        if (lastRow == 0)
        {
            throw new InvalidOperationException("File Excel không có dữ liệu.");
        }

        var partNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var row = 1; row <= lastRow; row++)
        {
            var value = worksheet.Cell(row, 1).GetFormattedString().Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (IsHeaderValue(value))
            {
                continue;
            }

            if (seen.Add(value))
            {
                partNames.Add(value);
            }
        }

        if (partNames.Count == 0)
        {
            throw new InvalidOperationException("Không tìm thấy part name trong cột A.");
        }

        return partNames;
    }

    private static IReadOnlyList<string> ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var partNames = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var value = line.Split(',')[0].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value) || IsHeaderValue(value))
            {
                continue;
            }

            if (seen.Add(value))
            {
                partNames.Add(value);
            }
        }

        if (partNames.Count == 0)
        {
            throw new InvalidOperationException("Không tìm thấy part name trong file CSV.");
        }

        return partNames;
    }

    private static bool IsHeaderValue(string value)
    {
        var normalized = value.Trim();
        return normalized.Equals("Part Name", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Parts Name", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Partname", StringComparison.OrdinalIgnoreCase);
    }
}
