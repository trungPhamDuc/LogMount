using System.Globalization;
using System.Text.Json;
using LogMount.Data;
using LogMount.Models;
using LogMount.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

[Authorize(Roles = UserRoles.StaffOrAdmin)]
public class ImprovementReportModel(
    LogMountDbContext dbContext,
    IPowerPointReportService powerPointReportService) : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [BindProperty(SupportsGet = true)]
    public DateOnly? SelectedDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int TopN { get; set; } = 10;

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; } = "count-desc";

    public IReadOnlyList<DateOnly> AvailableDates { get; private set; } = [];
    public IReadOnlyList<ExpensivePartSummaryItem> SummaryItems { get; private set; } = [];
    public IReadOnlyList<ExpensivePartTopItem> TopParts { get; private set; } = [];
    public IReadOnlyList<ExpensivePartReportGroup> ReportGroups { get; private set; } = [];
    public IReadOnlyList<string> NeedImproveLines { get; private set; } = [];
    public IReadOnlyList<string> ImprovedLines { get; private set; } = [];
    public IReadOnlyList<RetryImprove> Improvements { get; private set; } = [];
    public string ChartDataJson { get; private set; } = "[]";

    public bool IsRowImproved(ExpensivePartSummaryItem item)
    {
        return Improvements.Any(imp => 
            string.Equals(imp.Line?.Trim(), item.Line?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(imp.PartsName) || string.Equals(imp.PartsName.Trim(), item.PartsName?.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(imp.Lane) || string.Equals(imp.Lane.Trim(), item.Lane?.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(imp.Machine) || string.Equals(imp.Machine.Trim(), item.Machine?.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(imp.Side) || 
             string.Equals(imp.Side.Trim(), item.Side?.Trim(), StringComparison.OrdinalIgnoreCase) || 
             string.Equals(imp.Side.Trim(), item.SideLabel?.Trim(), StringComparison.OrdinalIgnoreCase) || 
             (imp.Side.Trim().Equals("TOP", StringComparison.OrdinalIgnoreCase) && item.Side?.Equals("T", StringComparison.OrdinalIgnoreCase) == true) || 
             (imp.Side.Trim().Equals("BOT", StringComparison.OrdinalIgnoreCase) && item.Side?.Equals("B", StringComparison.OrdinalIgnoreCase) == true)) &&
            (string.IsNullOrEmpty(imp.ErrorName) || 
             string.Equals(imp.ErrorName.Trim(), item.ErrorName?.Trim(), StringComparison.OrdinalIgnoreCase) || 
             item.ErrorName?.Contains(imp.ErrorName.Trim(), StringComparison.OrdinalIgnoreCase) == true || 
             imp.ErrorName.Contains(item.ErrorName.Trim(), StringComparison.OrdinalIgnoreCase) == true)
        );
    }
    public int TotalCount => SummaryItems.Sum(x => x.Count);
    public decimal TotalCost => SummaryItems.Sum(x => x.TotalCost);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadReportAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetPowerPointAsync(CancellationToken cancellationToken)
    {
        await LoadReportAsync(cancellationToken);
        if (SummaryItems.Count == 0)
        {
            return BadRequest("Không có dữ liệu để tải PowerPoint.");
        }

        var improvedRowKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in ReportGroups.SelectMany(x => x.Rows))
        {
            if (IsRowImproved(item))
            {
                improvedRowKeys.Add($"{item.Line}_{item.PartsName}_{item.Lane}_{item.Machine}_{item.Side}_{item.ErrorName}");
            }
        }

        var exportResult = powerPointReportService.ExportExpensivePartSummary(
            TopParts,
            ReportGroups.SelectMany(x => x.Rows).ToList(),
            new HashSet<string>(ImprovedLines, StringComparer.OrdinalIgnoreCase),
            improvedRowKeys,
            SortBy == "cost-desc",
            $"expensive-component-{SelectedDate:yyyyMMdd}");

        return File(exportResult.Content, exportResult.ContentType, exportResult.FileName);
    }

    private async Task LoadReportAsync(CancellationToken cancellationToken)
    {
        var querySortBy = Request.Query["SortBy"].ToString();
        if (!string.IsNullOrEmpty(querySortBy))
        {
            SortBy = querySortBy;
        }
        SortBy = string.Equals(SortBy, "cost-desc", StringComparison.OrdinalIgnoreCase)
            ? "cost-desc"
            : "count-desc";

        var queryTopN = Request.Query["TopN"].ToString();
        if (!string.IsNullOrEmpty(queryTopN) && int.TryParse(queryTopN, out var valTopN))
        {
            TopN = valTopN;
        }
        TopN = TopN is 5 or 10 or 20 or 30 ? TopN : 10;

        var querySelectedDate = Request.Query["SelectedDate"].ToString();
        if (!string.IsNullOrEmpty(querySelectedDate) && DateOnly.TryParse(querySelectedDate, out var valDate))
        {
            SelectedDate = valDate;
        }

        var rawDates = await dbContext.RetryLogEntries.AsNoTracking()
            .Where(x => x.Date != null && x.Date != "")
            .Select(x => x.Date!)
            .Distinct()
            .ToListAsync(cancellationToken);

        AvailableDates = rawDates
            .Select(ParseLogDate)
            .Where(x => x is not null)
            .Select(x => DateOnly.FromDateTime(x!.Value))
            .Distinct()
            .OrderByDescending(x => x)
            .ToList();

        if (AvailableDates.Count == 0)
        {
            return;
        }

        SelectedDate ??= AvailableDates[0];
        var selectedDateText = SelectedDate.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

        var logs = await dbContext.RetryLogEntries.AsNoTracking()
            .Where(x => x.Date == selectedDateText)
            .ToListAsync(cancellationToken);

        var parts = await dbContext.ExpensiveParts.AsNoTracking()
            .Where(x => x.PartsName != null && x.PartsName != "")
            .ToListAsync(cancellationToken);

        var summary = ExpensivePartAnalysisService.Summarize(logs, parts);
        var countSummary = ExpensivePartAnalysisService.SummarizeCounts(summary);
        SummaryItems = ExpensivePartAnalysisService.SortByCount(countSummary, new ExpensivePartFilterCriteria
        {
            SortDirection = SortBy == "cost-desc" ? "cost-desc" : "desc"
        });

        TopParts = ExpensivePartAnalysisService.GetTopParts(SummaryItems, TopN, SortBy == "cost-desc");
        ChartDataJson = JsonSerializer.Serialize(TopParts.Select(x => new
        {
            x.PartsName,
            x.TotalCount,
            x.TotalCost
        }), JsonOptions);

        var selectedTopParts = TopParts
            .OrderByDescending(x => x.TotalCount >= 6 ? 1 : 0)
            .Take(4)
            .ToList();

        ReportGroups = selectedTopParts
            .Select(top => new ExpensivePartReportGroup
            {
                PartsName = top.PartsName,
                Rows = SummaryItems
                    .Where(x => string.Equals(x.PartsName, top.PartsName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => SortBy == "cost-desc" ? x.TotalCost : x.Count)
                    .ThenByDescending(x => SortBy == "cost-desc" ? x.Count : x.TotalCost)
                    .Take(8)
                    .ToList()
            })
            .Where(x => x.Rows.Count > 0)
            .ToList();

        var reportRows = ReportGroups.SelectMany(x => x.Rows).ToList();
        var lines = reportRows
            .Where(x => !string.IsNullOrWhiteSpace(x.Line))
            .GroupBy(x => x.Line.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Sum(x => x.Count) >= 6)
            .Select(g => g.Key)
            .ToList();

        var selectedDateStart = SelectedDate.Value.ToDateTime(TimeOnly.MinValue);
        var selectedDateEnd = SelectedDate.Value.ToDateTime(TimeOnly.MaxValue);
        Improvements = await dbContext.RetryImproves.AsNoTracking()
            .Where(x => x.ExecutionDate >= selectedDateStart && x.ExecutionDate <= selectedDateEnd)
            .Where(x => x.Line != null)
            .ToListAsync(cancellationToken);

        var improvedLineNames = Improvements
            .Select(x => x.Line!)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ImprovedLines = improvedLineNames
            .OrderBy(GetLineSortOrder)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        NeedImproveLines = lines
            .Where(x => !improvedLineNames.Contains(x))
            .OrderBy(GetLineSortOrder)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static DateTime? ParseLogDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var dateText = value.Trim();
        var spaceIndex = dateText.IndexOf(' ');
        if (spaceIndex > 0)
        {
            dateText = dateText[..spaceIndex];
        }

        return DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
               DateTime.TryParse(dateText, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed)
            ? parsed.Date
            : null;
    }

    public static int GetLineSortOrder(string? line)
    {
        var value = line?.Trim() ?? string.Empty;
        return value.StartsWith("L", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(value[1..], out var number)
            ? number
            : int.MaxValue;
    }
}

public class ExpensivePartReportGroup
{
    public string PartsName { get; set; } = string.Empty;
    public IReadOnlyList<ExpensivePartSummaryItem> Rows { get; set; } = [];
}
