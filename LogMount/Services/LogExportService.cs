using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using LogMount.Models;

namespace LogMount.Services;

public interface ILogExportService
{
    FileExportResult ExportOverview(IReadOnlyList<ColumnSummaryItem> items, ExportFormat format, string baseFileName);
    FileExportResult ExportErrors(IReadOnlyList<ErrorSummaryItem> items, ExportFormat format, string baseFileName);
    FileExportResult ExportLogs(IReadOnlyList<RetryLogEntry> items, ExportFormat format, string baseFileName);
    FileExportResult ExportExpensiveParts(IReadOnlyList<ExpensivePartSummaryItem> items, ExportFormat format, string baseFileName);
}

public class LogExportService : ILogExportService
{
    private static readonly string[] LogHeaders =
    [
        "Date", "Line", "Occurrence Time", "Error No.", "Error Name",
        "Lane", "Table", "Parts No.", "Parts Name", "Head No.", "Nozzle Type",
        "Feeder No.", "Feeder ID", "Cart ID", "Vis Error No.", "Error Vacuum"
    ];

    private static readonly string[] ErrorHeaders =
    [
        "Error No.", "Error Name", "Số lần", "Date", "Line", "Occurrence Time", "Lot Name", "Lane", "Table",
        "Parts No.", "Parts Name", "Head No.", "Nozzle Type", "Feeder No.", "Feeder ID",
        "Cart ID", "Vis Error No.", "Error Vacuum"
    ];

    private static readonly string[] OverviewHeaders = ["Cột", "Giá trị", "Số giá trị"];

    private static readonly string[] ExpensivePartHeaders =
    [
        "Parts Name", "Giá 1 con", "Tổng giá tiền", "Line", "Mặt", "Máy", "Lane", "Feeder No.", "Error No.", "Error Name", "Số lần"
    ];

    public FileExportResult ExportOverview(IReadOnlyList<ColumnSummaryItem> items, ExportFormat format, string baseFileName)
    {
        var rows = items.Select(item => new string[]
        {
            item.ColumnName,
            item.Values,
            item.DistinctCount.ToString(CultureInfo.InvariantCulture)
        });

        return Export("tong-quan", OverviewHeaders, rows, format, baseFileName);
    }

    public FileExportResult ExportErrors(IReadOnlyList<ErrorSummaryItem> items, ExportFormat format, string baseFileName)
    {
        var rows = items.Select(item => new string[]
        {
            item.ErrorNo,
            item.ErrorName,
            item.Count.ToString(CultureInfo.InvariantCulture),
            item.Dates,
            item.Lines,
            item.OccurrenceTimes,
            item.LotNames,
            item.Lanes,
            item.Tables,
            item.PartsNumbers,
            item.PartsNames,
            item.HeadNumbers,
            item.NozzleTypes,
            item.FeederNumbers,
            item.FeederIds,
            item.CartIds,
            item.VisErrorNumbers,
            item.ErrorVacuums
        });

        return Export("tong-hop-loi", ErrorHeaders, rows, format, baseFileName);
    }

    public FileExportResult ExportLogs(IReadOnlyList<RetryLogEntry> items, ExportFormat format, string baseFileName)
    {
        var rows = items.Select(item => new string[]
        {
            item.Date,
            item.Line,
            item.OccurrenceTime,
            item.ErrorNo,
            item.ErrorName,
            item.Lane,
            item.Table,
            item.PartsNo,
            item.PartsName,
            item.HeadNo,
            item.NozzleType,
            item.FeederNo,
            item.FeederId,
            item.CartId,
            item.VisErrorNo,
            item.ErrorVacuum
        });

        return Export("du-lieu-log", LogHeaders, rows, format, baseFileName);
    }

    public FileExportResult ExportExpensiveParts(IReadOnlyList<ExpensivePartSummaryItem> items, ExportFormat format, string baseFileName)
    {
        var rows = items.Select(item => new string[]
            {
            item.PartsName,
            item.Cost.ToString("F2", CultureInfo.InvariantCulture),
            item.TotalCost.ToString("F2", CultureInfo.InvariantCulture),
            item.Line,
                item.SideLabel,
                item.Machine,
                item.Lane,
                item.FeederNo,
                item.ErrorNo,
                item.ErrorName,
                item.Count.ToString(CultureInfo.InvariantCulture)
            })
            .Append([
                "Tổng cộng (đã lọc)", string.Empty,
                items.Sum(item => item.TotalCost).ToString("F2", CultureInfo.InvariantCulture),
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty,
                items.Sum(item => item.Count).ToString(CultureInfo.InvariantCulture)
            ]);

        var webHeaders = new[]
        {
            "Parts Name", "Line", "Mặt", "Máy", "Lane", "Feeder", "Error No.", "Error Name", "Tổng giá tiền", "Số lần"
        };

        var webRows = items.Select(item => new string[]
            {
                item.PartsName,
                item.Line,
                $"{item.SideLabel} ({item.Side})",
                item.Machine,
                item.Lane,
                item.FeederNo,
                item.ErrorNo,
                item.ErrorName,
                item.TotalCost.ToString("F2", CultureInfo.InvariantCulture),
                item.Count.ToString(CultureInfo.InvariantCulture)
            })
            .Append([
                "Tổng cộng (đã lọc)",
                string.Empty, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty,
                items.Sum(item => item.TotalCost).ToString("F2", CultureInfo.InvariantCulture),
                items.Sum(item => item.Count).ToString(CultureInfo.InvariantCulture)
            ]);

        return Export("linh-kien-dat-tien", webHeaders, webRows, format, baseFileName);
    }

    private static FileExportResult Export(
        string sectionName,
        IReadOnlyList<string> headers,
        IEnumerable<string[]> rows,
        ExportFormat format,
        string baseFileName)
    {
        var safeBaseName = SanitizeFileName(Path.GetFileNameWithoutExtension(baseFileName));
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var rowList = rows.ToList();

        return format switch
        {
            ExportFormat.Csv => CreateCsv($"{safeBaseName}_{sectionName}_{timestamp}.csv", headers, rowList),
            ExportFormat.Xlsx => CreateXlsx($"{safeBaseName}_{sectionName}_{timestamp}.xlsx", headers, rowList),
            _ => throw new InvalidOperationException("Định dạng export không hợp lệ.")
        };
    }

    private static FileExportResult CreateCsv(string fileName, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream, new UTF8Encoding(true), leaveOpen: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        };

        using (var csv = new CsvWriter(writer, config))
        {
            foreach (var header in headers)
            {
                csv.WriteField(header);
            }

            csv.NextRecord();

            foreach (var row in rows)
            {
                foreach (var value in row)
                {
                    csv.WriteField(value);
                }

                csv.NextRecord();
            }
        }

        writer.Flush();
        return new FileExportResult
        {
            Content = memoryStream.ToArray(),
            ContentType = "text/csv",
            FileName = fileName
        };
    }

    private static FileExportResult CreateXlsx(string fileName, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Export");

        for (var col = 0; col < headers.Count; col++)
        {
            worksheet.Cell(1, col + 1).Value = headers[col];
            worksheet.Cell(1, col + 1).Style.Font.Bold = true;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            for (var col = 0; col < row.Length; col++)
            {
                worksheet.Cell(rowIndex + 2, col + 1).Value = row[col];
            }
        }

        worksheet.Columns().AdjustToContents();
        worksheet.SheetView.FreezeRows(1);

        using var memoryStream = new MemoryStream();
        workbook.SaveAs(memoryStream);

        return new FileExportResult
        {
            Content = memoryStream.ToArray(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileName = fileName
        };
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "RetryLog" : sanitized;
    }
}
