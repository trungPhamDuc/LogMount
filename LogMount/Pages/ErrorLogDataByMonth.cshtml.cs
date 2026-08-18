using System.Text.Json;
using LogMount.Data;
using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

public class ErrorLogDataByMonthModel : PageModel
{
    private const int PageSize = 100;
    private static readonly int[] TopNOptions = [10, 20, 30];
    private static readonly JsonSerializerOptions ChartJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly LogMountDbContext _dbContext;
    private readonly ILogExportService _exportService;

    public ErrorLogDataByMonthModel(LogMountDbContext dbContext, ILogExportService exportService)
    {
        _dbContext = dbContext;
        _exportService = exportService;
    }

    [BindProperty(SupportsGet = true)]
    public string? SelectedMonth { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchMonth { get; set; }

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

    public IReadOnlyList<string> AvailableMonths { get; set; } = [];
    public IReadOnlyList<string> DisplayMonths { get; set; } = [];
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
        AvailableMonths = await LoadAvailableMonthsAsync(cancellationToken);
        SearchMonth = NormalizeMonth(SearchMonth);

        DisplayMonths = string.IsNullOrWhiteSpace(SearchMonth)
            ? AvailableMonths.Take(5).ToList()
            : AvailableMonths
                .Where(x => x.Contains(SearchMonth, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

        SelectedMonth = NormalizeMonth(SelectedMonth) ?? AvailableMonths.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(SelectedMonth))
        {
            TotalPages = 1;
            return;
        }

        TopN = TopNOptions.Contains(TopN) ? TopN : 10;

        var query = ApplyFilter(BuildMonthQuery(SelectedMonth), Filter);

        var allRows = await query
            .OrderBy(x => x.Date)
            .ThenBy(x => x.EventDate)
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
        SelectedMonth = NormalizeMonth(SelectedMonth);
        if (string.IsNullOrWhiteSpace(SelectedMonth))
        {
            ErrorMessage = "Vui lòng chọn tháng cần xóa.";
            return RedirectToPage();
        }

        _dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        var totalDeleted = 0;
        int batchDeleted;
        do
        {
            batchDeleted = await _dbContext.ErrorLogEntries
                .Where(x => x.Date != null && x.Date.StartsWith(SelectedMonth))
                .Take(5000)
                .ExecuteDeleteAsync(cancellationToken);
            totalDeleted += batchDeleted;
        } while (batchDeleted > 0);

        SuccessMessage = totalDeleted > 0
            ? $"Đã xóa {totalDeleted:N0} dòng dữ liệu ErrorLog tháng {SelectedMonth} khỏi CSDL."
            : $"Không tìm thấy dữ liệu ErrorLog tháng {SelectedMonth} trong CSDL.";

        return RedirectToPage(new { SearchMonth });
    }

    public async Task<IActionResult> OnGetExportAsync(string section, string format, CancellationToken cancellationToken)
    {
        SelectedMonth = NormalizeMonth(SelectedMonth);
        if (string.IsNullOrWhiteSpace(SelectedMonth))
        {
            return BadRequest("Vui lòng chọn tháng cần tải xuống.");
        }

        if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var exportFormat))
        {
            return BadRequest("Định dạng tải xuống không hợp lệ.");
        }

        var rows = await ApplyFilter(BuildMonthQuery(SelectedMonth), Filter)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.EventDate)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        rows = ErrorLogAnalysisService.Filter(rows, Filter).ToList();

        if (rows.Count == 0)
        {
            return BadRequest("Không có dữ liệu ErrorLog để tải xuống.");
        }

        var baseFileName = $"errorlog-thang-{SelectedMonth.Replace('/', '-')}";
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
            ["SelectedMonth"] = SelectedMonth,
            ["SearchMonth"] = SearchMonth,
            ["PageNumber"] = pageNumber.ToString(),
            ["Filter.Error"] = Filter.Error,
            ["Filter.Line"] = Filter.Line,
            ["Filter.Lane"] = Filter.Lane,
            ["Filter.Table"] = Filter.Table,
            ["Filter.Side"] = Filter.Side,
            ["Filter.Machine"] = Filter.Machine,
            ["Filter.TimeFrom"] = Filter.TimeFrom,
            ["Filter.TimeTo"] = Filter.TimeTo,
            ["Filter.DateFrom"] = Filter.DateFrom,
            ["Filter.DateTo"] = Filter.DateTo,
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

    private IQueryable<ErrorLogEntry> BuildMonthQuery(string selectedMonth)
    {
        return _dbContext.ErrorLogEntries
            .AsNoTracking()
            .Where(x => x.Date != null && x.Date.StartsWith(selectedMonth));
    }

    private async Task<IReadOnlyList<string>> LoadAvailableMonthsAsync(CancellationToken cancellationToken)
    {
        var dates = await _dbContext.ErrorLogEntries
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.Date) && x.Date!.Length >= 7)
            .Select(x => x.Date!)
            .Distinct()
            .ToListAsync(cancellationToken);

        return dates
            .Select(x => x[..7])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        if (!string.IsNullOrWhiteSpace(filter.DateFrom))
        {
            var from = filter.DateFrom.Trim().Replace('-', '/');
            query = query.Where(x => x.Date != null && string.Compare(x.Date, from) >= 0);
        }

        if (!string.IsNullOrWhiteSpace(filter.DateTo))
        {
            var to = filter.DateTo.Trim().Replace('-', '/');
            query = query.Where(x => x.Date != null && string.Compare(x.Date, to) <= 0);
        }

        return query;
    }

    private static string? NormalizeMonth(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace('-', '/');
    }
}
