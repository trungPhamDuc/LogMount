using System.Text.Json;
using LogMount.Data;
using LogMount.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

public class RequestLogDataByDateModel(LogMountDbContext dbContext) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? SelectedDate { get; set; }
    [BindProperty(SupportsGet = true)] public string? Line { get; set; }
    [BindProperty(SupportsGet = true)] public string? PartNo { get; set; }
    [BindProperty(SupportsGet = true)] public string SortBy { get; set; } = "rqty";
    [BindProperty(SupportsGet = true)] public int TopN { get; set; } = 10;
    public IReadOnlyList<string> AvailableDates { get; private set; } = [];
    public IReadOnlyList<string> AvailableLines { get; private set; } = [];
    public IReadOnlyList<RequestLogEntry> Entries { get; private set; } = [];
    public string ChartDataJson { get; private set; } = "[]";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        AvailableDates = await dbContext.RequestLogEntries.AsNoTracking().Where(x => x.Date != null).Select(x => x.Date!).Distinct().OrderByDescending(x => x).ToListAsync(cancellationToken);
        SelectedDate = string.IsNullOrWhiteSpace(SelectedDate) ? AvailableDates.FirstOrDefault() : SelectedDate.Trim();
        if (string.IsNullOrWhiteSpace(SelectedDate)) return;
        var query = dbContext.RequestLogEntries.AsNoTracking().Where(x => x.Date == SelectedDate);
        AvailableLines = await query.Where(x => x.Line != null).Select(x => x.Line!).Distinct().OrderBy(x => x).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(Line)) query = query.Where(x => x.Line == Line.Trim());
        if (!string.IsNullOrWhiteSpace(PartNo)) query = query.Where(x => x.PartNo != null && x.PartNo.Contains(PartNo.Trim()));
        Entries = await query.OrderBy(x => x.Line).ThenBy(x => x.PartNo).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        TopN = TopN is 0 or 10 or 20 or 30 ? TopN : 10;
        var ordered = SortBy.ToLowerInvariant() switch
        {
            "rate" => Entries.OrderByDescending(DropRate).ThenByDescending(x => x.RQty),
            "amount" => Entries.OrderByDescending(TotalAmount).ThenByDescending(x => x.RQty),
            _ => Entries.OrderByDescending(x => x.RQty).ThenByDescending(DropRate)
        };
        var chart = TopN == 0 ? ordered : ordered.Take(TopN);
        Entries = chart.ToList();
        ChartDataJson = JsonSerializer.Serialize(chart.Select(x => new { label = $"{x.PartNo} ({x.Line})", requestQty = x.RQty, dropRate = DropRate(x) }), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
    public static decimal TotalAmount(RequestLogEntry item) => Math.Ceiling(item.AmtOnRequest * item.RQty);
    private static decimal DropRate(RequestLogEntry item) => item.PQty == 0 ? 0 : item.RQty / item.PQty * 100m;
}
