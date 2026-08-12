using System.Text.Json;
using LogMount.Data;
using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

public class ErrorLogDataByDateModel : PageModel
{
    private const int PageSize = 100;
    private static readonly int[] TopNOptions = [10, 20, 30];
    private static readonly JsonSerializerOptions ChartJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LogMountDbContext _dbContext;
    private readonly ILogExportService _exportService;

    public ErrorLogDataByDateModel(LogMountDbContext dbContext, ILogExportService exportService)
    {
        _dbContext = dbContext;
        _exportService = exportService;
    }

    [BindProperty(SupportsGet = true)]
    public string? SelectedDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public ErrorLogFilterCriteria Filter { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int TopN { get; set; } = 10;

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public IReadOnlyList<string> AvailableDates { get; set; } = [];
    public IReadOnlyList<string> DisplayDates { get; set; } = [];
    public IReadOnlyList<ErrorLogEntry> Entries { get; set; } = [];
    public IReadOnlyList<ErrorLogSummaryItem> Summary { get; set; } = [];
    public IReadOnlyList<ErrorLogDetailSummaryItem> DetailSummary { get; set; } = [];
    public IReadOnlyList<ErrorLogTopItem> TopErrors { get; set; } = [];
    public IReadOnlyList<int> TopNChoices { get; } = TopNOptions;
    public int TotalRecords { get; set; }
    public int TotalPages { get; set; }
    public int TotalErrorCount { get; set; }
    public int FilteredErrorCount { get; set; }
    public string ChartDataJson { get; set; } = "[]";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        AvailableDates = await _dbContext.ErrorLogEntries
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.Date))
            .Select(x => x.Date!)
            .Distinct()
            .OrderByDescending(x => x)
            .ToListAsync(cancellationToken);

        SearchDate = SearchDate?.Trim();
        DisplayDates = string.IsNullOrWhiteSpace(SearchDate)
            ? AvailableDates.Take(5).ToList()
            : AvailableDates
                .Where(x => x.Contains(SearchDate, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

        SelectedDate = string.IsNullOrWhiteSpace(SelectedDate)
            ? AvailableDates.FirstOrDefault()
            : SelectedDate.Trim();

        if (string.IsNullOrWhiteSpace(SelectedDate))
        {
            TotalPages = 1;
            return;
        }

        TopN = TopNOptions.Contains(TopN) ? TopN : 10;

        var query = ApplyFilter(_dbContext.ErrorLogEntries.AsNoTracking()
            .Where(x => x.Date == SelectedDate), Filter);

        var allRows = await query
            .OrderByDescending(x => x.EventDate)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        allRows = ErrorLogAnalysisService.Filter(allRows, Filter).ToList();

        Summary = ErrorLogAnalysisService.SummarizeErrors(allRows);
        TotalRecords = allRows.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);
        Entries = allRows
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        var detailSummary = ErrorLogAnalysisService.SummarizeByLocation(allRows);
        TotalErrorCount = detailSummary.Sum(x => x.Count);
        DetailSummary = ErrorLogAnalysisService.SortDetailSummary(
            ErrorLogAnalysisService.FilterDetailSummary(detailSummary, Filter),
            Filter);
        FilteredErrorCount = DetailSummary.Sum(x => x.Count);
        TopErrors = ErrorLogAnalysisService.GetTopErrors(DetailSummary, TopN);
        ChartDataJson = JsonSerializer.Serialize(TopErrors.Select(x => new
        {
            x.Error,
            x.TotalCount
        }), ChartJsonOptions);
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        SelectedDate = SelectedDate?.Trim();
        if (string.IsNullOrWhiteSpace(SelectedDate))
        {
            ErrorMessage = "Vui lòng chọn ngày cần xóa.";
            return RedirectToPage();
        }

        _dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        var totalDeleted = 0;
        int batchDeleted;
        do
        {
            batchDeleted = await _dbContext.ErrorLogEntries
                .Where(x => x.Date == SelectedDate)
                .Take(5000)
                .ExecuteDeleteAsync(cancellationToken);
            totalDeleted += batchDeleted;
        } while (batchDeleted > 0);

        SuccessMessage = totalDeleted > 0
            ? $"Đã xóa {totalDeleted:N0} dòng dữ liệu ErrorLog ngày {SelectedDate} khỏi CSDL."
            : $"Không tìm thấy dữ liệu ErrorLog ngày {SelectedDate} trong CSDL.";

        return RedirectToPage(new { SearchDate });
    }

    public async Task<IActionResult> OnGetExportAsync(string section, string format, CancellationToken cancellationToken)
    {
        SelectedDate = SelectedDate?.Trim();
        if (string.IsNullOrWhiteSpace(SelectedDate))
        {
            return BadRequest("Vui lòng chọn ngày cần tải xuống.");
        }

        if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var exportFormat))
        {
            return BadRequest("Định dạng tải xuống không hợp lệ.");
        }

        var rows = await ApplyFilter(_dbContext.ErrorLogEntries.AsNoTracking()
                .Where(x => x.Date == SelectedDate), Filter)
            .OrderByDescending(x => x.EventDate)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        rows = ErrorLogAnalysisService.Filter(rows, Filter).ToList();

        if (rows.Count == 0)
        {
            return BadRequest("Không có dữ liệu ErrorLog để tải xuống.");
        }

        var baseFileName = $"errorlog-ngay-{SelectedDate.Replace('/', '-')}";
        var exportResult = section?.Trim().ToLowerInvariant() switch
        {
            "summary" => _exportService.ExportErrorLogSummary(ErrorLogAnalysisService.SummarizeErrors(rows), exportFormat, baseFileName),
            "detailsummary" => _exportService.ExportErrorLogDetailSummary(
                ErrorLogAnalysisService.SortDetailSummary(
                    ErrorLogAnalysisService.FilterDetailSummary(
                        ErrorLogAnalysisService.SummarizeByLocation(rows),
                        Filter),
                    Filter),
                exportFormat,
                baseFileName),
            _ => _exportService.ExportErrorLogs(rows, exportFormat, baseFileName)
        };

        return File(exportResult.Content, exportResult.ContentType, exportResult.FileName);
    }

    public Dictionary<string, string?> GetRouteValues(int pageNumber)
    {
        return new Dictionary<string, string?>
        {
            ["SelectedDate"] = SelectedDate,
            ["SearchDate"] = SearchDate,
            ["PageNumber"] = pageNumber.ToString(),
            ["Filter.Error"] = Filter.Error,
            ["Filter.Line"] = Filter.Line,
            ["Filter.Lane"] = Filter.Lane,
            ["Filter.Table"] = Filter.Table,
            ["Filter.Side"] = Filter.Side,
            ["Filter.Machine"] = Filter.Machine,
            ["Filter.TimeFrom"] = Filter.TimeFrom,
            ["Filter.TimeTo"] = Filter.TimeTo,
            ["Filter.SortDirection"] = Filter.SortDirection,
            ["TopN"] = TopN.ToString()
        };
    }

    public static string GetErrorNameCssClass(string? errorName) => ErrorLogHelper.GetErrorNameCssClass(errorName);

    public Dictionary<string, string?> GetExportRouteValues(string section, string format)
    {
        var values = GetRouteValues(PageNumber);
        values["section"] = section;
        values["format"] = format;
        return values;
    }

    private static IQueryable<ErrorLogEntry> ApplyFilter(IQueryable<ErrorLogEntry> query, ErrorLogFilterCriteria filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Error))
        {
            var value = filter.Error.Trim();
            query = query.Where(x => x.Error != null && x.Error.Contains(value));
        }

        if (!string.IsNullOrWhiteSpace(filter.Line))
        {
            var value = filter.Line.Trim();
            query = query.Where(x => x.Line != null && x.Line.Contains(value));
        }

        if (!string.IsNullOrWhiteSpace(filter.Lane))
        {
            var value = filter.Lane.Trim();
            query = query.Where(x => x.Lane != null && x.Lane.Contains(value));
        }

        if (!string.IsNullOrWhiteSpace(filter.Table))
        {
            var value = filter.Table.Trim();
            query = query.Where(x => x.Table != null && x.Table.Contains(value));
        }

        return query;
    }
}
