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
        bool sortByCost,
        string baseFileName);

    FileExportResult ExportImprovementReport(
        DateOnly? selectedDate,
        int windowDays,
        IReadOnlyList<PowerPointImprovementReportRow> rows,
        IReadOnlyList<string> summaryBullets);
}

public class PowerPointReportService : IPowerPointReportService
{
    private const int SlideWidth = 12192000;
    private const int SlideHeight = 6858000;

    public FileExportResult ExportExpensivePartSummary(
        IReadOnlyList<ExpensivePartTopItem> topParts,
        IReadOnlyList<ExpensivePartSummaryItem> summaryItems,
        IReadOnlySet<string> improvedLines,
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
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Line, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var slides = new[]
        {
            BuildOverviewSlide(topParts.Take(10).ToList(), lineItems, sortByCost),
            BuildStatusTableSlide(summaryItems.Take(14).ToList(), improvedLines)
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

    private static string BuildStatusTableSlide(IReadOnlyList<ExpensivePartSummaryItem> items, IReadOnlySet<string> improvedLines)
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
            var improved = improvedLines.Contains(item.Line);
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
            slide.Rect(left, y, widths[i], h, fill, "FFFFFF");
            slide.Text(cells[i], left + 45000, y + 45000, widths[i] - 90000, h - 90000, 8, textColor, bold);
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

        return $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/><Override PartName="/ppt/slideMasters/slideMaster1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"/><Override PartName="/ppt/slideLayouts/slideLayout1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"/><Override PartName="/ppt/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>{slideOverrides}</Types>""";
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

    private static string SlideRelationships() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/></Relationships>""";

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

        public void Text(string text, int x, int y, int w, int h, int fontSize, string color, bool bold = false)
        {
            var escapedText = SecurityElement.Escape(text ?? string.Empty) ?? string.Empty;
            var runs = escapedText.Split('\n').Select(line =>
                $"<a:p><a:r><a:rPr lang=\"en-US\" sz=\"{fontSize * 100}\"{(bold ? " b=\"1\"" : string.Empty)}><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill></a:rPr><a:t>{line}</a:t></a:r><a:endParaRPr lang=\"en-US\" sz=\"{fontSize * 100}\"/></a:p>");

            _content.Append($"""
<p:sp><p:nvSpPr><p:cNvPr id="{NextId()}" name="TextBox"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="{x}" y="{y}"/><a:ext cx="{w}" cy="{h}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></p:spPr><p:txBody><a:bodyPr wrap="square" rtlCol="0"/><a:lstStyle/>{string.Concat(runs)}</p:txBody></p:sp>
""");
        }

        public void Rect(int x, int y, int w, int h, string fill, string line)
        {
            _content.Append($"""
<p:sp><p:nvSpPr><p:cNvPr id="{NextId()}" name="Rectangle"/><p:cNvSpPr/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="{x}" y="{y}"/><a:ext cx="{w}" cy="{h}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:solidFill><a:srgbClr val="{fill}"/></a:solidFill><a:ln w="6350"><a:solidFill><a:srgbClr val="{line}"/></a:solidFill></a:ln></p:spPr></p:sp>
""");
        }

        public string Build() =>
            $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill><a:effectLst/></p:bgPr></p:bg><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>{_content}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>""";

        private int NextId() => ++_shapeId;
    }
