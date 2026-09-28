using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace LogMount.Services;

public static class ImprovementLineStatusPptxService
{
    public static byte[] Create(
        IReadOnlyList<LineImprovementStatusRow> rows,
        DateOnly date,
        string needImproveNote,
        string improvedNote,
        string? screenshotPath)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var hasImage = !string.IsNullOrWhiteSpace(screenshotPath) && File.Exists(screenshotPath);
            if (hasImage)
            {
                zip.CreateEntryFromFile(screenshotPath!, "ppt/media/image1.png");
            }

            WriteEntry(zip, "[Content_Types].xml", ContentTypes(hasImage));
            WriteEntry(zip, "_rels/.rels", RelsRoot());
            WriteEntry(zip, "ppt/_rels/presentation.xml.rels", PresentationRels());
            WriteEntry(zip, "ppt/presentation.xml", PresentationXml());
            WriteEntry(zip, "ppt/slideMasters/slideMaster1.xml", SlideMasterXml());
            WriteEntry(zip, "ppt/slideMasters/_rels/slideMaster1.xml.rels", SlideMasterRels());
            WriteEntry(zip, "ppt/slideLayouts/slideLayout1.xml", SlideLayoutXml());
            WriteEntry(zip, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", SlideLayoutRels());
            WriteEntry(zip, "ppt/theme/theme1.xml", ThemeXml());
            WriteEntry(zip, "ppt/slides/_rels/slide1.xml.rels", SlideRels(hasImage));
            WriteEntry(zip, "ppt/slides/slide1.xml", SlideXml(rows, date, needImproveNote, improvedNote, hasImage));
        }

        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, string xml)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(xml);
    }

    private static string SlideXml(
        IReadOnlyList<LineImprovementStatusRow> rows,
        DateOnly date,
        string needImproveNote,
        string improvedNote,
        bool hasImage)
    {
        var shapes = new StringBuilder();
        var shapeId = 2;

        shapes.Append(TextBox(
            shapeId++,
            x: 457200, y: 182880, cx: 11277600, cy: 548640,
            fontSize: 2800,
            bold: true,
            color: "1F4E79",
            text: $"Báo cáo cải thiện Line — {date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)}"));

        if (hasImage)
        {
            shapes.Append($"""
              <p:pic>
                <p:nvPicPr>
                  <p:cNvPr id="{shapeId++}" name="Summary"/>
                  <p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr>
                  <p:nvPr/>
                </p:nvPicPr>
                <p:blipFill>
                  <a:blip r:embed="rId2"/>
                  <a:stretch><a:fillRect/></a:stretch>
                </p:blipFill>
                <p:spPr>
                  <a:xfrm>
                    <a:off x="365760" y="822960"/>
                    <a:ext cx="7315200" cy="5486400"/>
                  </a:xfrm>
                  <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                </p:spPr>
              </p:pic>
            """);
        }

        var noteX = hasImage ? 7924800L : 914400L;
        var noteCx = hasImage ? 3901440L : 10363200L;

        shapes.Append(TextBox(
            shapeId++,
            x: noteX, y: 822960, cx: noteCx, cy: 1371600,
            fontSize: 1400,
            bold: false,
            color: "C00000",
            text: needImproveNote + "\n" + improvedNote));

        var tableRows = new List<string[]>
        {
            new[] { "Line", "Cần cải thiện", "Đã cải thiện" }
        };
        tableRows.AddRange(rows.Select(row => new[]
        {
            row.DisplayLine,
            row.NeedsImprovement ? "✓" : "",
            row.IsImproved ? "✓" : ""
        }));

        if (tableRows.Count == 1)
        {
            tableRows.Add(new[] { "Không có line cần cải thiện", "", "" });
        }

        var tableY = 2286000L;
        var tableCx = noteCx;
        var rowHeight = 370840L;
        var tableCy = rowHeight * tableRows.Count;
        shapes.Append(TableShape(shapeId, noteX, tableY, tableCx, tableCy, tableRows));

        return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
  <p:cSld>
    <p:bg>
      <p:bgPr>
        <a:solidFill><a:srgbClr val="F7F9FC"/></a:solidFill>
        <a:effectLst/>
      </p:bgPr>
    </p:bg>
    <p:spTree>
      <p:nvGrpSpPr>
        <p:cNvPr id="1" name=""/>
        <p:cNvGrpSpPr/>
        <p:nvPr/>
      </p:nvGrpSpPr>
      <p:grpSpPr>
        <a:xfrm>
          <a:off x="0" y="0"/>
          <a:ext cx="0" cy="0"/>
          <a:chOff x="0" y="0"/>
          <a:chExt cx="0" cy="0"/>
        </a:xfrm>
      </p:grpSpPr>
      {{shapes}}
    </p:spTree>
  </p:cSld>
  <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
</p:sld>
""";
    }

    private static string TextBox(
        int id,
        long x,
        long y,
        long cx,
        long cy,
        int fontSize,
        bool bold,
        string color,
        string text)
    {
        var paragraphs = text.Split('\n').Select(line => $"""
          <a:p>
            <a:pPr algn="l"/>
            <a:r>
              <a:rPr lang="vi-VN" sz="{fontSize}" b="{(bold ? 1 : 0)}" dirty="0">
                <a:solidFill><a:srgbClr val="{color}"/></a:solidFill>
                <a:latin typeface="Calibri"/>
              </a:rPr>
              <a:t>{XmlEscape(line)}</a:t>
            </a:r>
          </a:p>
        """);

        return $"""
          <p:sp>
            <p:nvSpPr>
              <p:cNvPr id="{id}" name="Text {id}"/>
              <p:cNvSpPr txBox="1"/>
              <p:nvPr/>
            </p:nvSpPr>
            <p:spPr>
              <a:xfrm>
                <a:off x="{x}" y="{y}"/>
                <a:ext cx="{cx}" cy="{cy}"/>
              </a:xfrm>
              <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
              <a:noFill/>
            </p:spPr>
            <p:txBody>
              <a:bodyPr wrap="square" lIns="45720" tIns="36576" rIns="45720" bIns="36576"/>
              <a:lstStyle/>
              {string.Join(Environment.NewLine, paragraphs)}
            </p:txBody>
          </p:sp>
        """;
    }

    private static string TableShape(int id, long x, long y, long cx, long cy, List<string[]> rows)
    {
        var colCount = 3;
        var colWidth = cx / colCount;
        var rowHeight = cy / Math.Max(rows.Count, 1);
        var gridCols = string.Join("", Enumerable.Range(0, colCount).Select(_ => $"<a:gridCol w=\"{colWidth}\"/>"));
        var tableRows = new StringBuilder();

        for (var r = 0; r < rows.Count; r++)
        {
            var isHeader = r == 0;
            tableRows.Append($"<a:tr h=\"{rowHeight}\">");
            for (var c = 0; c < colCount; c++)
            {
                var fill = isHeader ? "1F4E79" : (r % 2 == 0 ? "FFFFFF" : "E9F2FB");
                var textColor = isHeader ? "FFFFFF" : (c == 0 ? "1F4E79" : "1D6F42");
                var align = c == 0 ? "l" : "ctr";
                var bold = isHeader || c > 0 ? 1 : 0;
                var value = rows[r][c];
                tableRows.Append($"""
                  <a:tc>
                    <a:txBody>
                      <a:bodyPr/>
                      <a:lstStyle/>
                      <a:p>
                        <a:pPr algn="{align}"/>
                        <a:r>
                          <a:rPr lang="vi-VN" sz="1400" b="{bold}" dirty="0">
                            <a:solidFill><a:srgbClr val="{textColor}"/></a:solidFill>
                            <a:latin typeface="Calibri"/>
                          </a:rPr>
                          <a:t>{XmlEscape(value)}</a:t>
                        </a:r>
                      </a:p>
                    </a:txBody>
                    <a:tcPr>
                      <a:lnL w="12700"><a:solidFill><a:srgbClr val="BDD7EE"/></a:solidFill></a:lnL>
                      <a:lnR w="12700"><a:solidFill><a:srgbClr val="BDD7EE"/></a:solidFill></a:lnR>
                      <a:lnT w="12700"><a:solidFill><a:srgbClr val="BDD7EE"/></a:solidFill></a:lnT>
                      <a:lnB w="12700"><a:solidFill><a:srgbClr val="BDD7EE"/></a:solidFill></a:lnB>
                      <a:solidFill><a:srgbClr val="{fill}"/></a:solidFill>
                    </a:tcPr>
                  </a:tc>
                """);
            }

            tableRows.Append("</a:tr>");
        }

        return $"""
          <p:graphicFrame>
            <p:nvGraphicFramePr>
              <p:cNvPr id="{id}" name="StatusTable"/>
              <p:cNvGraphicFramePr><a:graphicFrameLocks noGrp="1"/></p:cNvGraphicFramePr>
              <p:nvPr/>
            </p:nvGraphicFramePr>
            <p:xfrm>
              <a:off x="{x}" y="{y}"/>
              <a:ext cx="{cx}" cy="{cy}"/>
            </p:xfrm>
            <a:graphic>
              <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/table">
                <a:tbl>
                  <a:tblPr/><a:tblGrid>{gridCols}</a:tblGrid>
                  {tableRows}
                </a:tbl>
              </a:graphicData>
            </a:graphic>
          </p:graphicFrame>
        """;
    }

    private static string XmlEscape(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    private static string ContentTypes(bool hasImage) => $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  {(hasImage ? """<Default Extension="png" ContentType="image/png"/>""" : "")}
  <Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/>
  <Override PartName="/ppt/slides/slide1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>
  <Override PartName="/ppt/slideLayouts/slideLayout1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"/>
  <Override PartName="/ppt/slideMasters/slideMaster1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"/>
  <Override PartName="/ppt/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>
</Types>
""";

    private static string RelsRoot() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/>
</Relationships>
""";

    private static string PresentationRels() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" Target="slideMasters/slideMaster1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide1.xml"/>
</Relationships>
""";

    private static string PresentationXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:presentation xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
  <p:sldMasterIdLst>
    <p:sldMasterId id="2147483648" r:id="rId1"/>
  </p:sldMasterIdLst>
  <p:sldIdLst>
    <p:sldId id="256" r:id="rId2"/>
  </p:sldIdLst>
  <p:sldSz cx="12192000" cy="6858000"/>
  <p:notesSz cx="6858000" cy="9144000"/>
</p:presentation>
""";

    private static string SlideRels(bool hasImage) => $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/>
  {(hasImage ? """<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/image1.png"/>""" : "")}
</Relationships>
""";

    private static string SlideLayoutRels() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" Target="../slideMasters/slideMaster1.xml"/>
</Relationships>
""";

    private static string SlideMasterRels() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="../theme/theme1.xml"/>
</Relationships>
""";

    private static string SlideLayoutXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sldLayout xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" type="blank" preserve="1">
  <p:cSld name="Blank">
    <p:spTree>
      <p:nvGrpSpPr>
        <p:cNvPr id="1" name=""/>
        <p:cNvGrpSpPr/>
        <p:nvPr/>
      </p:nvGrpSpPr>
      <p:grpSpPr>
        <a:xfrm>
          <a:off x="0" y="0"/>
          <a:ext cx="0" cy="0"/>
          <a:chOff x="0" y="0"/>
          <a:chExt cx="0" cy="0"/>
        </a:xfrm>
      </p:grpSpPr>
    </p:spTree>
  </p:cSld>
  <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
</p:sldLayout>
""";

    private static string SlideMasterXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sldMaster xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
  <p:cSld>
    <p:bg>
      <p:bgPr>
        <a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill>
        <a:effectLst/>
      </p:bgPr>
    </p:bg>
    <p:spTree>
      <p:nvGrpSpPr>
        <p:cNvPr id="1" name=""/>
        <p:cNvGrpSpPr/>
        <p:nvPr/>
      </p:nvGrpSpPr>
      <p:grpSpPr>
        <a:xfrm>
          <a:off x="0" y="0"/>
          <a:ext cx="0" cy="0"/>
          <a:chOff x="0" y="0"/>
          <a:chExt cx="0" cy="0"/>
        </a:xfrm>
      </p:grpSpPr>
    </p:spTree>
  </p:cSld>
  <p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" hlink="hlink" folHlink="folHlink"/>
  <p:sldLayoutIdLst>
    <p:sldLayoutId id="2147483649" r:id="rId1"/>
  </p:sldLayoutIdLst>
</p:sldMaster>
""";

    private static string ThemeXml() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Office Theme">
  <a:themeElements>
    <a:clrScheme name="Office">
      <a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1>
      <a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1>
      <a:dk2><a:srgbClr val="1F497D"/></a:dk2>
      <a:lt2><a:srgbClr val="EEECE1"/></a:lt2>
      <a:accent1><a:srgbClr val="4F81BD"/></a:accent1>
      <a:accent2><a:srgbClr val="C0504D"/></a:accent2>
      <a:accent3><a:srgbClr val="9BBB59"/></a:accent3>
      <a:accent4><a:srgbClr val="8064A2"/></a:accent4>
      <a:accent5><a:srgbClr val="4BACC6"/></a:accent5>
      <a:accent6><a:srgbClr val="F79646"/></a:accent6>
      <a:hlink><a:srgbClr val="0000FF"/></a:hlink>
      <a:folHlink><a:srgbClr val="800080"/></a:folHlink>
    </a:clrScheme>
    <a:fontScheme name="Office">
      <a:majorFont>
        <a:latin typeface="Calibri"/>
        <a:ea typeface=""/>
        <a:cs typeface=""/>
      </a:majorFont>
      <a:minorFont>
        <a:latin typeface="Calibri"/>
        <a:ea typeface=""/>
        <a:cs typeface=""/>
      </a:minorFont>
    </a:fontScheme>
    <a:fmtScheme name="Office">
      <a:fillStyleLst>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
      </a:fillStyleLst>
      <a:lnStyleLst>
        <a:ln w="9525"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
        <a:ln w="25400"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
        <a:ln w="38100"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:prstDash val="solid"/></a:ln>
      </a:lnStyleLst>
      <a:effectStyleLst>
        <a:effectStyle><a:effectLst/></a:effectStyle>
        <a:effectStyle><a:effectLst/></a:effectStyle>
        <a:effectStyle><a:effectLst/></a:effectStyle>
      </a:effectStyleLst>
      <a:bgFillStyleLst>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
        <a:solidFill><a:schemeClr val="phClr"/></a:solidFill>
      </a:bgFillStyleLst>
    </a:fmtScheme>
  </a:themeElements>
</a:theme>
""";
}

public class LineImprovementStatusRow
{
    public string ProductionLine { get; set; } = string.Empty;
    public string DisplayLine { get; set; } = string.Empty;
    public string NoteLine { get; set; } = string.Empty;
    public bool NeedsImprovement { get; set; }
    public bool IsImproved { get; set; }
    public int RetryCount { get; set; }
}
