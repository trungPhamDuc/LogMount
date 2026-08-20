using System.Text.Json;
using LogMount.Data;
using LogMount.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LogMount.Pages;

public class RequestLogDataByMonthModel(LogMountDbContext dbContext) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? SelectedMonth { get; set; }
    [BindProperty(SupportsGet = true)] public string? Line { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? DateFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? DateTo { get; set; }
    [BindProperty(SupportsGet = true)] public string? PartNo { get; set; }
    [BindProperty(SupportsGet = true)] public int TopN { get; set; } = 0;
    [BindProperty(SupportsGet = true)] public string SortBy { get; set; } = "rqty";

    public IReadOnlyList<string> AvailableMonths { get; private set; } = [];
    public IReadOnlyList<string> AvailableLines { get; private set; } = [];
    public IReadOnlyList<RequestLogSummary> Summary { get; private set; } = [];
    public string ChartDataJson { get; private set; } = "[]";
    public int TotalRows { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var dates = await dbContext.RequestLogEntries.AsNoTracking().Where(x => x.Date != null).Select(x => x.Date!).Distinct().ToListAsync(cancellationToken);
        AvailableMonths = dates.Where(x => x.Length >= 7).Select(x => x[..7]).Distinct().OrderByDescending(x => x).ToList();
        SelectedMonth = string.IsNullOrWhiteSpace(SelectedMonth) ? AvailableMonths.FirstOrDefault() : SelectedMonth.Trim();
        if (string.IsNullOrWhiteSpace(SelectedMonth)) return;

        var query = dbContext.RequestLogEntries.AsNoTracking().Where(x => x.Date != null && x.Date.StartsWith(SelectedMonth));
        AvailableLines = await query.Where(x => x.Line != null).Select(x => x.Line!).Distinct().OrderBy(x => x).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(Line)) query = query.Where(x => x.Line == Line.Trim());
        if (!string.IsNullOrWhiteSpace(PartNo)) query = query.Where(x => x.PartNo != null && x.PartNo.Contains(PartNo.Trim()));
        var rows = await query.ToListAsync(cancellationToken);
        if (DateFrom is not null) rows = rows.Where(x => string.CompareOrdinal(x.Date, DateFrom.Value.ToString("yyyy/MM/dd")) >= 0).ToList();
        if (DateTo is not null) rows = rows.Where(x => string.CompareOrdinal(x.Date, DateTo.Value.ToString("yyyy/MM/dd")) <= 0).ToList();
        TotalRows = rows.Count;
        // Không gộp: mỗi dòng CSDL tương ứng một dòng Excel.
        TopN = TopN is 0 or 10 or 20 or 30 ? TopN : 0;
        var items = rows.Select(x => new RequestLogSummary(x.Date ?? "-", x.ModelSuffix ?? "-", x.Chassis ?? "-", x.Board ?? "-", x.PartAssy ?? "-", x.PartNo ?? "-", x.Line ?? "-", x.PQty, x.RQty, x.RQty == 0 ? 0 : x.AmtOnRequest / x.RQty, x.AmtOnRequest, x.PQty == 0 ? 0 : x.RQty / x.PQty * 100m));
        items = SortBy.ToLowerInvariant() switch
        {
            "rate" => items.OrderByDescending(x => x.DropRate).ThenByDescending(x => x.RQty),
            "amount" => items.OrderByDescending(x => x.TotalAmount).ThenByDescending(x => x.RQty),
            _ => items.OrderByDescending(x => x.RQty).ThenByDescending(x => x.DropRate)
        };
        Summary = (TopN == 0 ? items : items.Take(TopN)).ToList();
        // Dùng cùng dữ liệu đang hiển thị trong bảng để biểu đồ luôn khớp với
        // các bộ lọc LINE, P/N, Top và ưu tiên đã chọn.
        var chartRows = Summary.GroupBy(x => new { x.PartNo, x.Line })
            .Select(g =>
            {
                var production = g.Sum(x => x.PQty);
                var request = g.Sum(x => x.RQty);
                return new
                {
                    label = $"{g.Key.PartNo} ({g.Key.Line})",
                    requestQty = request,
                    totalAmount = g.Sum(x => x.TotalAmount),
                    dropRate = production == 0 ? 0 : request / production * 100m
                };
            });

        chartRows = SortBy.Equals("rate", StringComparison.OrdinalIgnoreCase)
            ? chartRows.OrderByDescending(x => x.dropRate).ThenByDescending(x => x.requestQty)
            : SortBy.Equals("amount", StringComparison.OrdinalIgnoreCase)
                ? chartRows.OrderByDescending(x => x.totalAmount).ThenByDescending(x => x.requestQty)
                : chartRows.OrderByDescending(x => x.requestQty).ThenByDescending(x => x.dropRate);
        ChartDataJson = JsonSerializer.Serialize(chartRows, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
