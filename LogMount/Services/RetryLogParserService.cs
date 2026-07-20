using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using LogMount.Models;

namespace LogMount.Services;

public class RetryLogParserService : IRetryLogParserService
{
    public async Task<IReadOnlyList<RetryLogEntry>> ParseAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension switch
        {
            ".csv" => await ParseCsvAsync(stream, cancellationToken),
            ".xlsx" or ".xls" => ParseExcel(stream),
            _ => throw new InvalidOperationException("Chỉ hỗ trợ file .csv, .xlsx hoặc .xls.")
        };
    }

    private static async Task<IReadOnlyList<RetryLogEntry>> ParseCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 3)
        {
            throw new InvalidOperationException("File CSV không có dữ liệu hợp lệ.");
        }

        var headerLine = lines[1];
        var headers = SplitCsvLine(headerLine);
        var columnMap = BuildColumnMap(headers);

        var entries = new List<RetryLogEntry>();
        for (var i = 2; i < lines.Length; i++)
        {
            var values = SplitCsvLine(lines[i]);
            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            entries.Add(MapRow(values, columnMap));
        }

        return entries;
    }

    private static IReadOnlyList<RetryLogEntry> ParseExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;

        if (lastRow < 3)
        {
            throw new InvalidOperationException("File Excel không có dữ liệu hợp lệ.");
        }

        var headerRow = worksheet.Row(2);
        var headers = headerRow.CellsUsed().Select(c => c.GetString().Trim()).ToList();
        var columnMap = BuildColumnMap(headers);

        var entries = new List<RetryLogEntry>();
        for (var row = 3; row <= lastRow; row++)
        {
            var values = new List<string>();
            for (var col = 1; col <= headers.Count; col++)
            {
                values.Add(worksheet.Cell(row, col).GetFormattedString().Trim());
            }

            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            entries.Add(MapRow(values, columnMap));
        }

        return entries;
    }

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var header = NormalizeHeader(headers[i]);
            if (!string.IsNullOrEmpty(header))
            {
                map[header] = i;
            }
        }

        return map;
    }

    private static string NormalizeHeader(string header)
    {
        var normalized = header.Trim();
        if (normalized.Equals("EN", StringComparison.OrdinalIgnoreCase))
        {
            return "Language";
        }

        return normalized;
    }

    private static RetryLogEntry MapRow(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> columnMap)
    {
        string GetValue(string columnName)
        {
            if (columnMap.TryGetValue(columnName, out var index) && index < values.Count)
            {
                return values[index].Trim();
            }

            return string.Empty;
        }

        var lotName = GetValue("Lot Name");
        var occurrenceTime = GetValue("Occurrence Time");

        return new RetryLogEntry
        {
            Language = GetValue("Language"),
            OccurrenceTime = occurrenceTime,
            LotName = lotName,
            Date = LotNameParser.ExtractDate(occurrenceTime),
            Line = LotNameParser.ParseLine(lotName),
            ErrorNo = GetValue("Error No."),
            ErrorName = GetValue("Error Name"),
            Lane = GetValue("Lane"),
            Table = GetValue("Table"),
            PartsNo = GetValue("Parts No."),
            PartsName = GetValue("Parts Name"),
            HeadNo = GetValue("Head No."),
            NozzleType = GetValue("Nozzle Type"),
            FeederNo = GetValue("Feeder No."),
            FeederId = GetValue("Feeder ID"),
            CartId = GetValue("Cart ID"),
            VisErrorNo = GetValue("Vis Error No."),
            ErrorVacuum = GetValue("Error Vacuum")
        };
    }

    private static List<string> SplitCsvLine(string line)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            BadDataFound = null,
            MissingFieldFound = null
        };

        using var csvReader = new StringReader(line);
        using var csv = new CsvReader(csvReader, config);
        if (csv.Read())
        {
            var record = new List<string>();
            for (var i = 0; i < csv.Parser.Count; i++)
            {
                record.Add(csv.GetField(i) ?? string.Empty);
            }

            return record;
        }

        return [line];
    }
}
