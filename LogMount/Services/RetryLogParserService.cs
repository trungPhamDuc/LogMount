using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using ExcelDataReader;
using LogMount.Models;

namespace LogMount.Services;

public class RetryLogParserService : IRetryLogParserService
{
    private static readonly string[] HeaderKeywords =
    [
        "Occurrence Time", "Lot Name", "Error No.", "Error Name", "Parts Name", "Parts No."
    ];

    private static readonly Regex VersionPattern = new(@"^V\d+(\.\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static RetryLogParserService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

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
        var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

        var (headerLineIndex, headers) = FindHeaderLine(lines);
        if (headerLineIndex < 0)
        {
            throw new InvalidOperationException("Không tìm thấy dòng tiêu đề cột trong file CSV.");
        }

        var columnMap = BuildColumnMap(headers);
        var entries = new List<RetryLogEntry>();

        for (var i = headerLineIndex + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var values = SplitCsvLine(lines[i]);
            if (!TryCreateEntry(values, columnMap, out var entry))
            {
                continue;
            }

            entries.Add(entry);
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("File CSV không có dữ liệu hợp lệ.");
        }

        return entries;
    }

    private static IReadOnlyList<RetryLogEntry> ParseExcel(Stream stream)
    {
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var entries = new List<RetryLogEntry>();
        Dictionary<string, int>? columnMap = null;
        var maxColumnCount = 0;

        while (reader.Read())
        {
            var values = ReadRowValues(reader);
            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (columnMap is null)
            {
                if (!IsHeaderRow(values))
                {
                    continue;
                }

                columnMap = BuildColumnMap(values);
                maxColumnCount = Math.Max(maxColumnCount, values.Length);
                continue;
            }

            if (values.Length < maxColumnCount)
            {
                values = PadValues(values, maxColumnCount);
            }

            if (TryCreateEntry(values, columnMap, out var entry))
            {
                entries.Add(entry);
            }
        }

        if (entries.Count == 0)
        {
            throw new InvalidOperationException("File Excel không có dữ liệu hợp lệ.");
        }

        return entries;
    }

    private static (int LineIndex, IReadOnlyList<string> Headers) FindHeaderLine(string[] lines)
    {
        var scanLimit = Math.Min(lines.Length, 40);
        for (var i = 0; i < scanLimit; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var values = SplitCsvLine(lines[i]);
            if (IsHeaderRow(values))
            {
                return (i, values);
            }
        }

        return (-1, []);
    }

    private static bool TryCreateEntry(
        IReadOnlyList<string> values,
        IReadOnlyDictionary<string, int> columnMap,
        out RetryLogEntry entry)
    {
        entry = new RetryLogEntry();

        if (IsMetadataRow(values) || IsHeaderRow(values))
        {
            return false;
        }

        entry = MapRow(values, columnMap);
        return IsValidDataRow(entry);
    }

    private static string[] ReadRowValues(IExcelDataReader reader)
    {
        var values = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            values[i] = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
        }

        return values;
    }

    private static string[] PadValues(IReadOnlyList<string> values, int targetLength)
    {
        var padded = new string[targetLength];
        for (var i = 0; i < targetLength; i++)
        {
            padded[i] = i < values.Count ? values[i] : string.Empty;
        }

        return padded;
    }

    private static bool IsHeaderRow(IReadOnlyList<string> values)
    {
        var texts = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => NormalizeHeader(v.Trim()))
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();

        if (texts.Count == 0)
        {
            return false;
        }

        var matchCount = texts.Count(text =>
            HeaderKeywords.Any(keyword => text.Equals(keyword, StringComparison.OrdinalIgnoreCase)));

        return matchCount >= 2 ||
               (texts.Contains("Language", StringComparer.OrdinalIgnoreCase) &&
                texts.Contains("Lot Name", StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsMetadataRow(IReadOnlyList<string> values)
    {
        var texts = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToList();

        if (texts.Count == 0)
        {
            return true;
        }

        if (texts.All(IsMetadataValue))
        {
            return true;
        }

        return texts.Count <= 4 &&
               texts.Any(v => v.Equals("MultiLanguage", StringComparison.OrdinalIgnoreCase) ||
                              VersionPattern.IsMatch(v));
    }

    private static bool IsMetadataValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        return trimmed.Equals("MultiLanguage", StringComparison.OrdinalIgnoreCase) ||
               VersionPattern.IsMatch(trimmed);
    }

    private static bool IsValidDataRow(RetryLogEntry entry)
    {
        if (IsMetadataValue(entry.Language) ||
            IsMetadataValue(entry.OccurrenceTime) ||
            IsMetadataValue(entry.LotName) ||
            IsMetadataValue(entry.ErrorName) ||
            IsMetadataValue(entry.ErrorNo))
        {
            return false;
        }

        if (string.Equals(entry.Language, "EN", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.OccurrenceTime, "Occurrence Time", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hasLotName = !string.IsNullOrWhiteSpace(entry.LotName) &&
                         entry.LotName.StartsWith("EBR", StringComparison.OrdinalIgnoreCase);
        var hasOccurrenceTime = !string.IsNullOrWhiteSpace(entry.OccurrenceTime) &&
                                (entry.OccurrenceTime.Contains('/') || entry.OccurrenceTime.Contains('-'));
        var hasErrorNo = !string.IsNullOrWhiteSpace(entry.ErrorNo) &&
                         entry.ErrorNo.All(char.IsDigit);

        return hasLotName || (hasOccurrenceTime && hasErrorNo);
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
        var lineFromFile = GetValue("Line");

        return new RetryLogEntry
        {
            Language = GetValue("Language"),
            OccurrenceTime = occurrenceTime,
            LotName = lotName,
            Date = LotNameParser.ExtractDate(occurrenceTime),
            Line = !string.IsNullOrWhiteSpace(lineFromFile)
                ? lineFromFile
                : LotNameParser.ParseLine(lotName),
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
