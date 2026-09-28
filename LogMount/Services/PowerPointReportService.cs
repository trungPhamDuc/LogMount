using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using LogMount.Models;

namespace LogMount.Services;

public interface IPowerPointReportService
{
    FileExportResult ExportExpensivePartSummary(
        IReadOnlyList<ExpensivePartTopItem> topParts,
        IReadOnlyList<ExpensivePartSummaryItem> summaryItems,
        IReadOnlySet<string> improvedLines,
        IReadOnlySet<string> improvedRowKeys,
        bool sortByCost,
        string baseFileName);

    FileExportResult ExportImprovementReport(
        DateOnly? selectedDate,
        int windowDays,
        IReadOnlyList<PowerPointImprovementReportRow> rows,
        IReadOnlyList<string> summaryBullets);

    FileExportResult ExportSingleSlideWithChartAndTable(
        string title,
        string subtitle,
        IReadOnlyList<DailyImprovementItem> dailyItems,
        string[] tableHeaders,
        int[] columnWidths,
        List<string[]> tableRows,
        string[] comments,
        string baseFileName);
}

public class PowerPointReportService : IPowerPointReportService
{
    private const int SlideWidth = 12192000;
    private const int SlideHeight = 6858000;

    public FileExportResult ExportExpensivePartSummary(
        IReadOnlyList<ExpensivePartTopItem> topParts,
        IReadOnlyList<ExpensivePartSummaryItem> summaryItems,
        IReadOnlySet<string> improvedLines,
        IReadOnlySet<string> improvedRowKeys,
        bool sortByCost,
        string baseFileName)
    {
        var safeBaseName = SanitizeFileName(Path.GetFileNameWithoutExtension(baseFileName));
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"{safeBaseName}_bao-cao-cai-thien_{timestamp}.pptx";

        var lineItems = summaryItems
            .Where(x => !string.IsNullOrWhiteSpace(x.Line))
            .GroupBy(x => x.Line.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new LineReportItem(
                g.Key,
                g.Sum(x => x.Count),
                g.Sum(x => x.TotalCost),
                improvedLines.Contains(g.Key)))
            .Where(x => x.Count >= 6)
            .ToList();

        var lineItemsSet = lineItems.Select(x => x.Line).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in improvedLines)
        {
            if (!lineItemsSet.Contains(line))
            {
                lineItems.Add(new LineReportItem(line, 0, 0m, true));
            }
        }

        lineItems = lineItems
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Line, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var slides = new[]
        {
            BuildOverviewSlide(topParts.Take(10).ToList(), lineItems, sortByCost),
            BuildStatusTableSlide(summaryItems.Take(14).ToList(), improvedRowKeys)
        };

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(archive, "[Content_Types].xml", BuildContentTypes(slides.Length));
            AddText(archive, "_rels/.rels", RootRelationships());
            AddText(archive, "ppt/presentation.xml", BuildPresentation(slides.Length));
            AddText(archive, "ppt/_rels/presentation.xml.rels", BuildPresentationRelationships(slides.Length));
            AddText(archive, "ppt/slideMasters/slideMaster1.xml", SlideMaster());
            AddText(archive, "ppt/slideMasters/_rels/slideMaster1.xml.rels", SlideMasterRelationships());
            AddText(archive, "ppt/slideLayouts/slideLayout1.xml", SlideLayout());
            AddText(archive, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", SlideLayoutRelationships());
            AddText(archive, "ppt/theme/theme1.xml", Theme());

            for (var i = 0; i < slides.Length; i++)
            {
                AddText(archive, $"ppt/slides/slide{i + 1}.xml", slides[i]);
                AddText(archive, $"ppt/slides/_rels/slide{i + 1}.xml.rels", SlideRelationships());
            }
        }

        return new FileExportResult
        {
            Content = memoryStream.ToArray(),
            ContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            FileName = fileName
        };
    }

    public FileExportResult ExportImprovementReport(
        DateOnly? selectedDate,
        int windowDays,
        IReadOnlyList<PowerPointImprovementReportRow> rows,
        IReadOnlyList<string> summaryBullets)
    {
        var dateText = selectedDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            ?? DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var fileName = $"bao-cao-cai-thien_{dateText}_{DateTime.Now:HHmmss}.pptx";
        var slides = new[]
        {
            BuildImprovementOverviewSlide(selectedDate, windowDays, rows, summaryBullets),
            BuildImprovementTableSlide(selectedDate, rows.Take(14).ToList())
        };

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(archive, "[Content_Types].xml", BuildContentTypes(slides.Length));
            AddText(archive, "_rels/.rels", RootRelationships());
            AddText(archive, "ppt/presentation.xml", BuildPresentation(slides.Length));
            AddText(archive, "ppt/_rels/presentation.xml.rels", BuildPresentationRelationships(slides.Length));
            AddText(archive, "ppt/slideMasters/slideMaster1.xml", SlideMaster());
            AddText(archive, "ppt/slideMasters/_rels/slideMaster1.xml.rels", SlideMasterRelationships());
            AddText(archive, "ppt/slideLayouts/slideLayout1.xml", SlideLayout());
            AddText(archive, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", SlideLayoutRelationships());
            AddText(archive, "ppt/theme/theme1.xml", Theme());

            for (var i = 0; i < slides.Length; i++)
            {
                AddText(archive, $"ppt/slides/slide{i + 1}.xml", slides[i]);
                AddText(archive, $"ppt/slides/_rels/slide{i + 1}.xml.rels", SlideRelationships());
            }
        }

        return new FileExportResult
        {
            Content = memoryStream.ToArray(),
            ContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            FileName = fileName
        };
    }

    public FileExportResult ExportSingleSlideWithChartAndTable(
        string title,
        string subtitle,
        IReadOnlyList<DailyImprovementItem> dailyItems,
        string[] tableHeaders,
        int[] columnWidths,
        List<string[]> tableRows,
        string[] comments,
        string baseFileName)
    {
        var safeBaseName = SanitizeFileName(Path.GetFileNameWithoutExtension(baseFileName));
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"{safeBaseName}_{timestamp}.pptx";

        var slide = new SlideXmlBuilder();
        
        // Title & Subtitle
        slide.Text(title, 250000, 160000, 11692000, 360000, 20, "000000", bold: true);
        slide.Text(subtitle, 250000, 560000, 11692000, 260000, 11, "555555");

        // --- LEFT SIDE (X = 250,000, W = 5,600,000) ---
        // 1. Top Left: Vector Column Chart (Y = 1,000,000, H = 2,700,000)
        AddDailyColumnChart(slide, dailyItems, 250000, 1000000, 5600000, 2700000);

        // 2. Bottom Left: Daily Summary Table (Y = 3,850,000)
        AddDailySummaryTable(slide, dailyItems, 250000, 3850000, 5600000);

        // --- RIGHT SIDE (X = 6,100,000, W = 5,800,000) ---
        // 1. Top Right: Action Logs Table (Y = 1,000,000)
        var xTable = 6100000;
        var yTable = 1000000;
        const int rowHeight = 310000;

        slide.Text("NHAT KY HANH DONG CAI TIEN RETRIES", xTable, yTable, 5800000, 240000, 10, "1E3A8A", bold: true);
        yTable += 260000;

        // Draw Table Header
        AddTableRow(slide, tableHeaders, columnWidths, xTable, yTable, rowHeight, "22272E", "FFFFFF", bold: true);
        yTable += rowHeight;

        // Draw Table Rows (up to 9 rows)
        foreach (var row in tableRows.Take(9))
        {
            AddTableRow(slide, row, columnWidths, xTable, yTable, rowHeight, "F8F9FA", "111111");
            yTable += rowHeight;
        }

        // 2. Bottom Right: Comments / Remarks box (Y = 4,700,000, H = 1,900,000)
        if (comments != null && comments.Length > 0)
        {
            slide.Rect(6100000, 4700000, 5800000, 1900000, "F0F4F8", "C8D6E5");
            slide.Text("NHAN XET & EVALUATION (NHAT KY CAI TIEN)", 6220000, 4780000, 5560000, 260000, 10, "1B3A4B", bold: true);
            
            var commentsText = string.Join("\n", comments.Select(c => StripVietnameseForPpt(c)));
            slide.Text(commentsText, 6220000, 5100000, 5560000, 1400000, 8, "333333");
        }

        var slideXml = slide.Build();

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddText(archive, "[Content_Types].xml", BuildContentTypes(1));
            AddText(archive, "_rels/.rels", RootRelationships());
            AddText(archive, "ppt/presentation.xml", BuildPresentation(1));
            AddText(archive, "ppt/_rels/presentation.xml.rels", BuildPresentationRelationships(1));
            AddText(archive, "ppt/slideMasters/slideMaster1.xml", SlideMaster());
            AddText(archive, "ppt/slideMasters/_rels/slideMaster1.xml.rels", SlideMasterRelationships());
            AddText(archive, "ppt/slideLayouts/slideLayout1.xml", SlideLayout());
            AddText(archive, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", SlideLayoutRelationships());
            AddText(archive, "ppt/theme/theme1.xml", Theme());

            // Write Slide 1 XML and Relationships (standard slide, no external image required)
            AddText(archive, "ppt/slides/slide1.xml", slideXml);
            AddText(archive, "ppt/slides/_rels/slide1.xml.rels", SlideRelationships(hasImage: false));
        }

        return new FileExportResult
        {
            Content = memoryStream.ToArray(),
            ContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            FileName = fileName
        };
    }

    private static void AddDailySummaryTable(SlideXmlBuilder slide, IReadOnlyList<DailyImprovementItem> dailyItems, int x, int y, int width)
    {
        // Section Title
        slide.Text("THONG KE CHI TIET SO LAN RETRIES THEO TUNG NGAY", x, y, width, 240000, 10, "1E3A8A", bold: true);
        var tableY = y + 260000;
        const int rowHeight = 240000;

        // Column widths: Total = 5,600,000
        var widths = new[] { 300000, 850000, 950000, 650000, 750000, 2100000 };
        var headers = new[] { "#", "Ngay", "So lan retries", "Ty le %", "Xu huong", "Linh kien retries nhieu nhat trong ngay" };

        // Header Row
        var curX = x;
        for (int c = 0; c < headers.Length; c++)
        {
            slide.Rect(curX, tableY, widths[c], rowHeight, "22272E", "22272E");
            slide.Text(headers[c], curX + 5000, tableY + 20000, widths[c] - 10000, rowHeight - 40000, 7, "FFFFFF", bold: true, wrap: false, padding: 5000);
            curX += widths[c];
        }
        tableY += rowHeight;

        if (dailyItems == null || dailyItems.Count == 0) return;

        int totalRetries = dailyItems.Sum(item => item.Count);

        // Data Rows (up to 7 days)
        foreach (var item in dailyItems.Take(7))
        {
            curX = x;
            var bgColor = "F8F9FA";
            var borderColor = "E2E8F0";

            // Col 0: Rank #
            slide.Rect(curX, tableY, widths[0], rowHeight, bgColor, borderColor);
            slide.Text(item.Rank.ToString(), curX + 5000, tableY + 20000, widths[0] - 10000, rowHeight - 40000, 7, "333333", bold: true, wrap: false, padding: 5000);
            curX += widths[0];

            // Col 1: Date (Blue #1D4ED8)
            var shortDate = item.Date.Length > 10 ? item.Date[..10] : item.Date;
            slide.Rect(curX, tableY, widths[1], rowHeight, bgColor, borderColor);
            slide.Text(shortDate, curX + 5000, tableY + 20000, widths[1] - 10000, rowHeight - 40000, 7, "1D4ED8", bold: true, wrap: false, padding: 5000);
            curX += widths[1];

            // Col 2: Retry Count (Red #DC2626)
            slide.Rect(curX, tableY, widths[2], rowHeight, bgColor, borderColor);
            slide.Text(item.Count.ToString("N0", CultureInfo.InvariantCulture), curX + 5000, tableY + 20000, widths[2] - 10000, rowHeight - 40000, 7, "DC2626", bold: true, wrap: false, padding: 5000);
            curX += widths[2];

            // Col 3: Percentage (Blue #2563EB)
            slide.Rect(curX, tableY, widths[3], rowHeight, bgColor, borderColor);
            slide.Text($"{item.Percentage:0.0}%", curX + 5000, tableY + 20000, widths[3] - 10000, rowHeight - 40000, 7, "2563EB", wrap: false, padding: 5000);
            curX += widths[3];

            // Col 4: Trend Badge
            slide.Rect(curX, tableY, widths[4], rowHeight, bgColor, borderColor);
            string trendText = "-";
            string badgeColor = "6B7280"; // Gray
            if (!string.IsNullOrEmpty(item.TrendIndicator))
            {
                if (item.TrendIndicator.Contains("Giảm") || item.TrendIndicator.Contains("giam") || item.TrendIndicator.Contains("▼"))
                {
                    trendText = "v Giam";
                    badgeColor = "16A34A"; // Green
                }
                else if (item.TrendIndicator.Contains("Tăng") || item.TrendIndicator.Contains("tang") || item.TrendIndicator.Contains("▲"))
                {
                    trendText = "^ Tang";
                    badgeColor = "DC2626"; // Red
                }
            }

            // Badge shape inside cell
            slide.Rect(curX + 60000, tableY + 30000, widths[4] - 120000, rowHeight - 60000, badgeColor, badgeColor);
            slide.Text(trendText, curX + 60000, tableY + 40000, widths[4] - 120000, rowHeight - 80000, 7, "FFFFFF", bold: true, wrap: false, padding: 5000);
            curX += widths[4];

            // Col 5: Top Parts/Errors
            slide.Rect(curX, tableY, widths[5], rowHeight, bgColor, borderColor);
            var topParts = StripVietnameseForPpt(item.TopPartsOrErrors ?? "");
            if (topParts.Length > 48) topParts = topParts[..45] + "...";
            slide.Text(topParts, curX + 5000, tableY + 20000, widths[5] - 10000, rowHeight - 40000, 6, "4B5563", wrap: false, padding: 5000);

            tableY += rowHeight;
        }

        // Summary Total Row at Bottom
        curX = x;
        var totalWidth01 = widths[0] + widths[1];
        slide.Rect(curX, tableY, totalWidth01, rowHeight, "E2E8F0", "CBD5E1");
        slide.Text("Tong cong:", curX + 5000, tableY + 20000, totalWidth01 - 10000, rowHeight - 40000, 7, "111111", bold: true, wrap: false, padding: 5000);
        curX += totalWidth01;

        // Total retries (Red #DC2626)
        slide.Rect(curX, tableY, widths[2], rowHeight, "E2E8F0", "CBD5E1");
        slide.Text(totalRetries.ToString("N0", CultureInfo.InvariantCulture), curX + 5000, tableY + 20000, widths[2] - 10000, rowHeight - 40000, 7, "DC2626", bold: true, wrap: false, padding: 5000);
        curX += widths[2];

        // Total percentage (Blue #2563EB)
        slide.Rect(curX, tableY, widths[3], rowHeight, "E2E8F0", "CBD5E1");
        slide.Text("100.0%", curX + 5000, tableY + 20000, widths[3] - 10000, rowHeight - 40000, 7, "2563EB", bold: true, wrap: false, padding: 5000);
        curX += widths[3];

        // Remaining empty cells for total row
        var restWidth = widths[4] + widths[5];
        slide.Rect(curX, tableY, restWidth, rowHeight, "E2E8F0", "CBD5E1");
    }

    private static void AddDailyColumnChart(SlideXmlBuilder slide, IReadOnlyList<DailyImprovementItem> dailyItems, int x, int y, int width, int height)
    {
        // Container box
        slide.Rect(x, y, width, height, "FFFFFF", "E2E8F0");
        slide.Text("BIEU DO THEO DOI RETRIES QUA CAC NGAY (XU HUONG TANG / GIAM)", x + 100000, y + 80000, width - 200000, 260000, 10, "1E3A8A", bold: true);

        if (dailyItems == null || dailyItems.Count == 0)
        {
            slide.Text("Khong co du lieu bieu do", x + 100000, y + 1200000, width - 200000, 300000, 12, "888888");
            return;
        }

        int n = dailyItems.Count;
        int maxCount = Math.Max(1, dailyItems.Max(item => item.Count));
        
        int baselineY = y + height - 400000;
        int maxBarHeight = height - 1200000; // Leave space for title and top labels

        // X-axis baseline
        slide.Rect(x + 100000, baselineY, width - 200000, 10000, "CBD5E1", "CBD5E1");

        int totalPlotWidth = width - 200000;
        int slotWidth = totalPlotWidth / Math.Max(1, n);
        int barWidth = Math.Max(100000, Math.Min(500000, (int)(slotWidth * 0.55)));

        for (int i = 0; i < n; i++)
        {
            var item = dailyItems[i];
            int slotX = x + 100000 + i * slotWidth;
            int barX = slotX + (slotWidth - barWidth) / 2;

            double ratio = (double)item.Count / maxCount;
            int barHeight = Math.Max(30000, (int)(ratio * maxBarHeight));
            int barY = baselineY - barHeight;

            // Blue Column Bar (#2563EB)
            slide.Rect(barX, barY, barWidth, barHeight, "2563EB", "1D4ED8");

            // Count Text above bar in Blue (#1E3A8A)
            slide.Text(item.Count.ToString("N0", CultureInfo.InvariantCulture),
                barX - 100000, barY - 200000, barWidth + 200000, 180000, 8, "1E3A8A", bold: true);

            // Percentage Text above Count in Red (#DC2626)
            slide.Text($"{item.Percentage:0.0}%",
                barX - 100000, barY - 380000, barWidth + 200000, 180000, 8, "DC2626", bold: true);

            // Date Text below baseline (#475569)
            var shortDate = item.Date.Length > 10 ? item.Date[..10] : item.Date;
            slide.Text(shortDate,
                barX - 100000, baselineY + 30000, barWidth + 200000, 220000, 7, "475569");
        }
    }

    private static string BuildOverviewSlide(IReadOnlyList<ExpensivePartTopItem> topParts, IReadOnlyList<LineReportItem> lines, bool sortByCost)
    {
        var slide = new SlideXmlBuilder();
        var improved = lines.Where(x => x.IsImproved).Select(x => x.Line).Take(8).ToList();
        var needImprove = lines.Where(x => !x.IsImproved).Select(x => x.Line).Take(8).ToList();
        var maxValue = Math.Max(1m, topParts.Count == 0 ? 1m : topParts.Max(x => sortByCost ? x.TotalCost : x.TotalCount));

        slide.Text("1. Expensive Component Summary", 250000, 160000, 6200000, 360000, 24, "000000", bold: true);
        AddBarChart(slide, topParts, maxValue, sortByCost, 430000, 720000, 3300000);
        slide.Text("Tong hop linh kien dat tien", 430000, 2500000, 4200000, 280000, 13, "333333", bold: true);
        AddMiniSummaryTable(slide, topParts.Take(4).ToList(), 430000, 2820000, 6050000);

        slide.Text("- Nhung Line phai tien hanh cai thien: " + FormatLineList(needImprove),
            6600000, 980000, 4300000, 520000, 16, "000000");
        slide.Text("- Nhung Line da co bao cao cai thien: " + FormatLineList(improved),
            6600000, 1960000, 4300000, 520000, 16, "000000");
        slide.Text("✓ = da cai thien    □ = can cai thien",
            6600000, 2920000, 4300000, 320000, 14, "1F7A4D", bold: true);

        return slide.Build();
    }

    private static string BuildStatusTableSlide(IReadOnlyList<ExpensivePartSummaryItem> items, IReadOnlySet<string> improvedRowKeys)
    {
        var slide = new SlideXmlBuilder();
        slide.Text("2. Bang theo doi line can cai thien", 250000, 160000, 7600000, 360000, 24, "000000", bold: true);
        slide.Text("Bang danh dau trang thai theo nhat ky cai thien da luu trong RetryLog.",
            250000, 560000, 9000000, 260000, 12, "555555");
 
        var headers = new[] { "Parts Name", "Line", "Lane", "May", "Error Name", "So lan", "Tong tien", "Trang thai" };
        var widths = new[] { 2200000, 780000, 650000, 650000, 1780000, 780000, 1350000, 980000 };
        var x = 250000;
        var y = 980000;
        const int rowHeight = 330000;
 
        AddTableRow(slide, headers, widths, x, y, rowHeight, "22272E", "FFFFFF", bold: true);
        y += rowHeight;
 
        foreach (var item in items)
        {
            var improved = improvedRowKeys.Contains($"{item.Line}_{item.PartsName}_{item.Lane}_{item.Machine}_{item.Side}_{item.ErrorName}");
            var cells = new[]
            {
                item.PartsName,
                item.Line,
                item.Lane,
                item.Machine,
                item.ErrorName,
                item.Count.ToString("N0", CultureInfo.InvariantCulture),
                item.TotalCost.ToString("N0", CultureInfo.InvariantCulture),
                improved ? "✓ Da cai thien" : "□ Can cai thien"
            };
            AddTableRow(slide, cells, widths, x, y, rowHeight, improved ? "E9F7EF" : "FFF3F3", "111111");
            y += rowHeight;
        }

        slide.Text($"Tong cong: {items.Sum(x => x.Count):N0} lan | {items.Sum(x => x.TotalCost):N0} dong",
            250000, 6100000, 7000000, 280000, 13, "B42318", bold: true);

        return slide.Build();
    }

    private static string BuildImprovementOverviewSlide(
        DateOnly? selectedDate,
        int windowDays,
        IReadOnlyList<PowerPointImprovementReportRow> rows,
        IReadOnlyList<string> summaryBullets)
    {
        var slide = new SlideXmlBuilder();
        var dateText = selectedDate?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "-";
        var effectiveCount = rows.Sum(x => x.EffectiveActionCount);
        var waitingCount = rows.Sum(x => x.WaitingActionCount);
        var totalActions = rows.Sum(x => x.ActionCount);
        var improvedLines = rows
            .SelectMany(x => SplitLines(x.EffectiveLines))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        var allLines = rows
            .SelectMany(x => SplitLines(x.Lines))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        slide.Text("1. Improvement Report Summary", 250000, 160000, 7600000, 360000, 24, "000000", bold: true);
        slide.Text($"Ngay bao cao: {dateText} | Theo doi sau cai thien: {windowDays} ngay",
            250000, 560000, 7600000, 260000, 13, "555555");

        AddMetric(slide, "Nguoi thuc hien", rows.Count.ToString("N0", CultureInfo.InvariantCulture), 350000, 980000, "2B66D9");
        AddMetric(slide, "Hanh dong", totalActions.ToString("N0", CultureInfo.InvariantCulture), 2450000, 980000, "E85D70");
        AddMetric(slide, "Da hieu qua", effectiveCount.ToString("N0", CultureInfo.InvariantCulture), 4550000, 980000, "1F7A4D");
        AddMetric(slide, "Cho theo doi", waitingCount.ToString("N0", CultureInfo.InvariantCulture), 6650000, 980000, "F0B429");

        slide.Text("- Nhung Line phai tien hanh cai thien: " + FormatLineList(allLines),
            6600000, 2140000, 4300000, 520000, 15, "000000");
        slide.Text("- Nhung Line da co bao cao cai thien: " + FormatLineList(improvedLines),
            6600000, 2940000, 4300000, 520000, 15, "000000");
        slide.Text("✓ = da cai thien hieu qua    □ = can theo doi / can cai thien",
            6600000, 3740000, 4300000, 320000, 13, "1F7A4D", bold: true);

        slide.Text("Tong ket", 350000, 2140000, 4200000, 280000, 16, "000000", bold: true);
        var y = 2520000;
        foreach (var bullet in summaryBullets.Take(8))
        {
            slide.Text("- " + StripVietnameseForPpt(bullet), 350000, y, 5700000, 360000, 10, "222222");
            y += 380000;
        }

        return slide.Build();
    }

    private static string BuildImprovementTableSlide(DateOnly? selectedDate, IReadOnlyList<PowerPointImprovementReportRow> rows)
    {
        var slide = new SlideXmlBuilder();
        slide.Text("2. Bang theo doi cai thien theo ngay", 250000, 160000, 7800000, 360000, 24, "000000", bold: true);
        slide.Text($"Ngay: {selectedDate?.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) ?? "-"}",
            250000, 560000, 6000000, 260000, 12, "555555");

        var headers = new[] { "Ten", "Lines", "So HD", "Ngay SS", "Ket qua", "Line hieu qua", "TT" };
        var widths = new[] { 1500000, 1300000, 700000, 1000000, 3250000, 1650000, 700000 };
        var x = 250000;
        var y = 960000;
        const int rowHeight = 350000;

        AddTableRow(slide, headers, widths, x, y, rowHeight, "22272E", "FFFFFF", bold: true);
        y += rowHeight;

        foreach (var row in rows)
        {
            var effective = row.EffectiveActionCount > 0;
            AddTableRow(slide,
                [
                    row.EngineerName,
                    row.Lines,
                    row.ActionCount.ToString("N0", CultureInfo.InvariantCulture),
                    row.ComparedDaysText,
                    StripVietnameseForPpt(row.ResultText),
                    StripVietnameseForPpt(row.EffectiveLines),
                    effective ? "✓" : "□"
                ],
                widths,
                x,
                y,
                rowHeight,
                effective ? "E9F7EF" : "FFF3F3",
                "111111");
            y += rowHeight;
        }

        return slide.Build();
    }

    private static void AddMetric(SlideXmlBuilder slide, string label, string value, int x, int y, string color)
    {
        slide.Rect(x, y, 1800000, 820000, "F4F6F8", "DDE2E8");
        slide.Text(value, x + 120000, y + 90000, 1000000, 330000, 22, color, bold: true);
        slide.Text(label, x + 120000, y + 480000, 1500000, 240000, 11, "555555");
    }

    private static void AddBarChart(SlideXmlBuilder slide, IReadOnlyList<ExpensivePartTopItem> topParts, decimal maxValue, bool sortByCost, int x, int y, int width)
    {
        const int barHeight = 105000;
        const int gap = 55000;
        const int labelWidth = 820000;
        var plotWidth = width - labelWidth - 220000;

        for (var row = 0; row < topParts.Count; row++)
        {
            var item = topParts[row];
            var barY = y + row * (barHeight + gap);
            var value = sortByCost ? item.TotalCost : item.TotalCount;
            var valueText = value.ToString("N0", CultureInfo.InvariantCulture);
            var barWidth = Math.Max(70000, (int)(plotWidth * (double)(value / maxValue)));
            slide.Text(item.PartsName, x, barY - 15000, labelWidth, barHeight, 6, "666666");
            slide.Rect(x + labelWidth, barY, barWidth, barHeight, "E85D70", "E85D70");
            slide.Text(valueText, x + labelWidth + barWidth + 30000, barY - 15000, 360000, barHeight, 7, "666666");
        }
    }

    private static void AddMiniSummaryTable(SlideXmlBuilder slide, IReadOnlyList<ExpensivePartTopItem> items, int x, int y, int width)
    {
        var headers = new[] { "Parts Name", "So lan", "Tong tien", "Cai thien" };
        var widths = new[] { 2500000, 900000, 1600000, 950000 };
        const int rowHeight = 260000;

        AddTableRow(slide, headers, widths, x, y, rowHeight, "22272E", "FFFFFF", bold: true);
        y += rowHeight;

        foreach (var item in items)
        {
            AddTableRow(slide,
                [item.PartsName, item.TotalCount.ToString("N0", CultureInfo.InvariantCulture), item.TotalCost.ToString("N0", CultureInfo.InvariantCulture), "□"],
                widths, x, y, rowHeight, "F1F3F5", "111111");
            y += rowHeight;
        }
    }

    private static void AddTableRow(SlideXmlBuilder slide, IReadOnlyList<string> cells, IReadOnlyList<int> widths, int x, int y, int h, string fill, string textColor, bool bold = false)
    {
        var left = x;
        for (var i = 0; i < cells.Count; i++)
        {
            if (i >= widths.Count) break;
            slide.Rect(left, y, widths[i], h, fill, "FFFFFF");
            var cellText = StripVietnameseForPpt(cells[i] ?? string.Empty);
            slide.Text(cellText, left + 10000, y + 20000, widths[i] - 20000, h - 40000, 7, textColor, bold, wrap: false, padding: 10000);
            left += widths[i];
        }
    }

    private static string FormatLineList(IReadOnlyList<string> lines) =>
        lines.Count == 0 ? "chua co" : string.Join(", ", lines);

    private static IEnumerable<string> SplitLines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == "-")
        {
            return [];
        }

        return value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? x)
            .Where(x => !string.IsNullOrWhiteSpace(x) && x != "-");
    }

    private static string StripVietnameseForPpt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace('đ', 'd')
            .Replace('Đ', 'D');
    }

    private static void AddText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Escape(string? value) => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "RetryLog" : sanitized;
    }

    private static string BuildContentTypes(int slideCount)
    {
        var slideOverrides = string.Concat(Enumerable.Range(1, slideCount)
            .Select(i => $"""<Override PartName="/ppt/slides/slide{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>"""));

        return $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/><Override PartName="/ppt/slideMasters/slideMaster1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"/><Override PartName="/ppt/slideLayouts/slideLayout1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"/><Override PartName="/ppt/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>{slideOverrides}</Types>""";
    }

    private static string RootRelationships() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/></Relationships>""";

    private static string BuildPresentation(int slideCount)
    {
        var ids = string.Concat(Enumerable.Range(1, slideCount)
            .Select(i => $"""<p:sldId id="{255 + i}" r:id="rId{i + 1}"/>"""));
        return $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:presentation xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="rId1"/></p:sldMasterIdLst><p:sldIdLst>{ids}</p:sldIdLst><p:sldSz cx="{SlideWidth}" cy="{SlideHeight}" type="wide"/><p:notesSz cx="6858000" cy="9144000"/></p:presentation>""";
    }

    private static string BuildPresentationRelationships(int slideCount)
    {
        var sb = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" Target="slideMasters/slideMaster1.xml"/>""");
        for (var i = 1; i <= slideCount; i++)
        {
            sb.Append($"""<Relationship Id="rId{i + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide{i}.xml"/>""");
        }

        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private static string SlideMaster() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:sldMaster xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr></p:spTree></p:cSld><p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" hlink="hlink" folHlink="folHlink"/><p:sldLayoutIdLst><p:sldLayoutId id="2147483649" r:id="rId1"/></p:sldLayoutIdLst><p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>""";

    private static string SlideMasterRelationships() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="../theme/theme1.xml"/></Relationships>""";

    private static string SlideLayout() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:sldLayout xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" type="blank" preserve="1"><p:cSld name="Blank"><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr></p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>""";

    private static string SlideLayoutRelationships() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" Target="../slideMasters/slideMaster1.xml"/></Relationships>""";

    private static string SlideRelationships(bool hasImage = false) =>
        hasImage
        ? """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/image1.png"/></Relationships>"""
        : """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/></Relationships>""";

    private static string Theme() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="LogMount"><a:themeElements><a:clrScheme name="LogMount"><a:dk1><a:srgbClr val="111111"/></a:dk1><a:lt1><a:srgbClr val="FFFFFF"/></a:lt1><a:dk2><a:srgbClr val="22272E"/></a:dk2><a:lt2><a:srgbClr val="F4F6F8"/></a:lt2><a:accent1><a:srgbClr val="E85D70"/></a:accent1><a:accent2><a:srgbClr val="1F7A4D"/></a:accent2><a:accent3><a:srgbClr val="2B66D9"/></a:accent3><a:accent4><a:srgbClr val="F0B429"/></a:accent4><a:accent5><a:srgbClr val="6C757D"/></a:accent5><a:accent6><a:srgbClr val="B42318"/></a:accent6><a:hlink><a:srgbClr val="2B66D9"/></a:hlink><a:folHlink><a:srgbClr val="6C757D"/></a:folHlink></a:clrScheme><a:fontScheme name="LogMount"><a:majorFont><a:latin typeface="Arial"/></a:majorFont><a:minorFont><a:latin typeface="Arial"/></a:minorFont></a:fontScheme><a:fmtScheme name="LogMount"><a:fillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:fillStyleLst><a:lnStyleLst><a:ln w="6350"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>""";

    private sealed record LineReportItem(string Line, int Count, decimal TotalCost, bool IsImproved);

}

public sealed record PowerPointImprovementReportRow(
    string EngineerName,
    string Lines,
    int ActionCount,
    int EffectiveActionCount,
    int WaitingActionCount,
    string ComparedDaysText,
    string ResultText,
    string EffectiveLines);

internal sealed class SlideXmlBuilder
    {
        private readonly StringBuilder _content = new();
        private int _shapeId = 1;

        public void Text(string text, int x, int y, int w, int h, int fontSize, string color, bool bold = false, bool wrap = true, int padding = 10000)
        {
            var escapedText = SecurityElement.Escape(text ?? string.Empty) ?? string.Empty;
            var runs = escapedText.Split('\n').Select(line =>
                $"<a:p><a:r><a:rPr lang=\"en-US\" sz=\"{fontSize * 100}\"{(bold ? " b=\"1\"" : string.Empty)}><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill></a:rPr><a:t>{line}</a:t></a:r><a:endParaRPr lang=\"en-US\" sz=\"{fontSize * 100}\"/></a:p>");

            var wrapAttr = wrap ? "wrap=\"square\"" : "wrap=\"none\"";
            _content.Append($"""
<p:sp><p:nvSpPr><p:cNvPr id="{NextId()}" name="TextBox"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="{x}" y="{y}"/><a:ext cx="{w}" cy="{h}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></p:spPr><p:txBody><a:bodyPr lIns="{padding}" rIns="{padding}" tIns="{padding}" bIns="{padding}" {wrapAttr} rtlCol="0"/><a:lstStyle/>{string.Concat(runs)}</p:txBody></p:sp>
""");
        }

        public void Rect(int x, int y, int w, int h, string fill, string line)
        {
            _content.Append($"""
<p:sp><p:nvSpPr><p:cNvPr id="{NextId()}" name="Rectangle"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="{x}" y="{y}"/><a:ext cx="{w}" cy="{h}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val="{fill}"/></a:solidFill><a:ln w="6350"><a:solidFill><a:srgbClr val="{line}"/></a:solidFill></a:ln></p:spPr></p:sp>
""");
        }

        public void Picture(string relId, int x, int y, int w, int h)
        {
            _content.Append($"""
<p:pic><p:nvPicPr><p:cNvPr id="{NextId()}" name="Picture"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr><p:spPr><a:xfrm><a:off x="{x}" y="{y}"/><a:ext cx="{w}" cy="{h}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr><p:blipFill><a:blip r:embed="{relId}"/><a:stretch><a:fillRect/></a:stretch></p:blipFill></p:pic>
""");
        }

        public string Build() =>
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill><a:effectLst/></p:bgPr></p:bg><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>{_content}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>""";

        private int NextId() => ++_shapeId;
    }
