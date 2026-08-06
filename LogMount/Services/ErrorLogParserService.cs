using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using LogMount.Models;

namespace LogMount.Services;

public interface IErrorLogParserService
{
    Task<IReadOnlyList<ErrorLogEntry>> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}

public class ErrorLogParserService : IErrorLogParserService
{
    private static readonly Regex LinePattern = new(@"_L(?<line>\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LanePattern = new(@"/Lane(?<lane>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TablePattern = new(@"/Table(?<table>[A-Z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static ErrorLogParserService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<IReadOnlyList<ErrorLogEntry>> ParseAsync(
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

    private static async Task<IReadOnlyList<ErrorLogEntry>> ParseCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var (headerLineIndex, headers) = FindHeaderLine(lines.Select(SplitCsvLine));
        if (headerLineIndex < 0)
        {
            throw new InvalidOperationException("Không tìm thấy dòng tiêu đề cột trong file ErrorLog.");
        }

        var columnMap = BuildColumnMap(headers);
        var entries = new List<ErrorLogEntry>();
        for (var i = headerLineIndex + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var entry = MapRow(SplitCsvLine(lines[i]), columnMap);
            if (IsValidEntry(entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static IReadOnlyList<ErrorLogEntry> ParseExcel(Stream stream)
    {
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var entries = new List<ErrorLogEntry>();
        Dictionary<string, int>? columnMap = null;

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
                continue;
            }

            var entry = MapRow(values, columnMap);
            if (IsValidEntry(entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static (int Index, string[] Headers) FindHeaderLine(IEnumerable<string[]> rows)
    {
        var index = 0;
        foreach (var row in rows)
        {
            if (IsHeaderRow(row))
            {
                return (index, row);
            }

            index++;
        }

        return (-1, []);
    }

    private static bool IsHeaderRow(IReadOnlyList<string> values)
    {
        return values.Any(x => IsSameHeader(x, "Event Date")) &&
               values.Any(x => IsSameHeader(x, "Program Name")) &&
               values.Any(x => IsSameHeader(x, "Contents")) &&
               values.Any(x => IsSameHeader(x, "Details"));
    }

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var normalized = NormalizeHeader(headers[i]);
            if (!string.IsNullOrWhiteSpace(normalized) && !map.ContainsKey(normalized))
            {
                map[normalized] = i;
            }
        }

        return map;
    }

    private static ErrorLogEntry MapRow(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> columnMap)
    {
        var eventDate = GetValue("Event Date");
        var programName = GetValue("Program Name");
        var details = GetValue("Details");
        var table = ExtractTable(details);
        var lane = ExtractLane(details) ?? InferLaneFromTable(table);

        return new ErrorLogEntry
        {
            Date = NormalizeDate(eventDate),
            EventDate = eventDate,
            Line = ExtractLine(programName),
            Lane = lane,
            Table = table,
            Error = GetValue("Contents"),
            EventNo = GetValue("Event No.") ?? GetValue("Event No") ?? GetValue("Event Nur"),
            ProgramName = programName,
            Details = details
        };

        string? GetValue(string header)
        {
            return columnMap.TryGetValue(NormalizeHeader(header), out var index) && index < values.Count
                ? NullIfWhiteSpace(values[index])
                : null;
        }
    }

    private static bool IsValidEntry(ErrorLogEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry.EventDate) &&
               !string.IsNullOrWhiteSpace(entry.ProgramName) &&
               !string.IsNullOrWhiteSpace(entry.Error);
    }

    private static string? ExtractLine(string? programName)
    {
        if (string.IsNullOrWhiteSpace(programName))
        {
            return null;
        }

        var match = LinePattern.Match(programName);
        return match.Success ? $"Line {match.Groups["line"].Value}" : null;
    }

    private static string? ExtractLane(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return null;
        }

        var match = LanePattern.Match(details);
        return match.Success ? $"Lane {match.Groups["lane"].Value}" : null;
    }

    private static string? ExtractTable(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return null;
        }

        var match = TablePattern.Match(details);
        return match.Success ? match.Groups["table"].Value.ToUpperInvariant() : null;
    }

    private static string? InferLaneFromTable(string? table)
    {
        return table?.ToUpperInvariant() switch
        {
            "A" or "C" => "Lane 1",
            "B" or "D" => "Lane 2",
            _ => null
        };
    }

    private static string? NormalizeDate(string? eventDate)
    {
        if (string.IsNullOrWhiteSpace(eventDate))
        {
            return null;
        }

        var formats = new[]
        {
            "dd/MM/yyyy H:mm", "dd/MM/yyyy HH:mm", "d/M/yyyy H:mm", "d/M/yyyy HH:mm",
            "MM/dd/yyyy H:mm", "MM/dd/yyyy HH:mm", "M/d/yyyy H:mm", "M/d/yyyy HH:mm",
            "yyyy/MM/dd H:mm", "yyyy/MM/dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd HH:mm"
        };

        if (DateTime.TryParseExact(eventDate.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate) ||
            DateTime.TryParse(eventDate.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out exactDate) ||
            DateTime.TryParse(eventDate.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out exactDate))
        {
            return exactDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        }

        return eventDate.Trim().Length >= 10 ? eventDate.Trim()[..10].Replace('-', '/') : eventDate.Trim();
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

    private static string[] SplitCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString().Trim());
        return values.ToArray();
    }

    private static bool IsSameHeader(string value, string expected)
    {
        return string.Equals(NormalizeHeader(value), NormalizeHeader(expected), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHeader(string value)
    {
        return value.Trim().Replace(".", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Trim() == "/" ? null : value.Trim();
    }
}
