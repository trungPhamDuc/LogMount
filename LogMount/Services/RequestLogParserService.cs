using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using LogMount.Models;

namespace LogMount.Services;

public interface IRequestLogParserService
{
    Task<IReadOnlyList<RequestLogEntry>> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}

public class RequestLogParserService : IRequestLogParserService
{
    static RequestLogParserService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<IReadOnlyList<RequestLogEntry>> ParseAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".csv" => await ParseCsvAsync(stream, cancellationToken),
            ".xlsx" or ".xls" => ParseExcel(stream),
            _ => throw new InvalidOperationException("Chỉ hỗ trợ file .csv, .xlsx hoặc .xls.")
        };
    }

    private static async Task<IReadOnlyList<RequestLogEntry>> ParseCsvAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var lines = (await reader.ReadToEndAsync(cancellationToken)).Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        return ParseRows(lines.Select(SplitCsvLine));
    }

    private static IReadOnlyList<RequestLogEntry> ParseExcel(Stream stream)
    {
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var entries = new List<RequestLogEntry>();
        var foundRequestSheet = false;

        do
        {
            if (!IsRequestSheet(reader.Name))
            {
                continue;
            }

            var rows = new List<string[]>();
            while (reader.Read())
            {
                var values = new string[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++) values[i] = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
                rows.Add(values);
            }

            var sheetEntries = ParseRows(rows, throwIfHeaderMissing: false);
            if (sheetEntries.Count > 0)
            {
                foundRequestSheet = true;
                entries.AddRange(sheetEntries);
            }
        } while (reader.NextResult());

        if (!foundRequestSheet)
        {
            throw new InvalidOperationException("Không tìm thấy dữ liệu hợp lệ trong các sheet T3, T4, ... (Sheet1 được bỏ qua).");
        }

        return entries;
    }

    private static IReadOnlyList<RequestLogEntry> ParseRows(IEnumerable<string[]> rows, bool throwIfHeaderMissing = true)
    {
        Dictionary<string, int>? columns = null;
        var entries = new List<RequestLogEntry>();
        foreach (var row in rows)
        {
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (IsHeader(row)) { columns = BuildMap(row); continue; }
            if (columns is null) continue;
            var entry = Map(row, columns);
            if (!string.IsNullOrWhiteSpace(entry.PartNo)) entries.Add(entry);
        }
        if (columns is null && throwIfHeaderMissing) throw new InvalidOperationException("Không tìm thấy dòng tiêu đề gồm DATE, LINE và P/N trong file RequestLog.");
        return entries;
    }

    private static bool IsRequestSheet(string? sheetName) =>
        !string.IsNullOrWhiteSpace(sheetName) &&
        Regex.IsMatch(sheetName.Trim(), @"^T([3-9]|[1-9]\d+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static bool IsHeader(IReadOnlyList<string> row) =>
        row.Any(x => HeaderMatches(x, "DATE")) &&
        row.Any(x => HeaderMatches(x, "LINE")) &&
        row.Any(x => HeaderMatches(x, "P/N"));

    private static Dictionary<string, int> BuildMap(IReadOnlyList<string> headers)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var key = Normalize(headers[i]);
            if (!string.IsNullOrWhiteSpace(key) && !result.ContainsKey(key)) result[key] = i;
        }
        return result;
    }

    private static RequestLogEntry Map(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> columns)
    {
        string? Get(params string[] headers)
        {
            foreach (var header in headers)
                if (columns.TryGetValue(Normalize(header), out var index) && index < row.Count && !string.IsNullOrWhiteSpace(row[index])) return row[index].Trim();

            var expected = headers.Select(Normalize).ToArray();
            foreach (var column in columns)
                if (expected.Any(value => column.Key.StartsWith(value, StringComparison.OrdinalIgnoreCase)) &&
                    column.Value < row.Count && !string.IsNullOrWhiteSpace(row[column.Value])) return row[column.Value].Trim();
            return null;
        }
        return new RequestLogEntry
        {
            Date = NormalizeDate(Get("DATE")), Shift = Get("SHIFT"), Line = Get("LINE"), Process = Get("PROCESS"),
            ModelSuffix = Get("MODEL.SUFFIX", "MODEL SUFFIX"), Chassis = Get("CHASSIS"), Board = Get("BOARD"),
            PartAssy = Get("PART Ass'y", "PART ASSY"), WorkOrder = Get("W/O", "WO"), PartNo = Get("P/N", "PN"),
            PidOrLot = Get("PID or Lot", "PID/LOT"), Unit = Get("Unit", "Đ.V"),
            PQty = ParseDecimal(Get("P.Qty", "P Qty")), RQty = ParseDecimal(Get("R.Q'ty", "R.Qty", "R Qty")),
            AmtOnRequest = ParseDecimal(Get("Amt on request (VND)", "Amt on request", "Amount on request")),
            Remarks = Get("Remarks (Ghi chú)", "Remarks"), Department = Get("BO PHAN", "BỘ PHẬN"),
            StatusRemarks = Get("Remarks (Trạng thái)", "Status Remarks")
        };
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var cleaned = value.Replace(",", string.Empty).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result) ? result : 0;
    }

    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date) ||
               DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            ? date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : value.Trim();
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static bool HeaderMatches(string value, string expected) => Normalize(value).StartsWith(Normalize(expected), StringComparison.OrdinalIgnoreCase);

    private static string[] SplitCsvLine(string line)
    {
        var values = new List<string>(); var current = new StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else quoted = !quoted; }
            else if (line[i] == ',' && !quoted) { values.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(line[i]);
        }
        values.Add(current.ToString().Trim()); return values.ToArray();
    }
}
