using System.IO.Compression;
using System.Security;
using System.Text;

namespace Evrakci.Core.Templates.Docx;

public enum DocxAlign { Left, Center, Right, Justify }

/// <summary>
/// Writes simple Word documents (A4, Times New Roman): headings, paragraphs, "label : value" rows and
/// a signature row. Used to build the ready-made templates and new blank documents. In paragraph
/// text, <c>**…**</c> marks bold.
/// </summary>
public sealed class DocxBuilder
{
    const int LabelWidth = 3400;   // twips (6 cm) – where the ": value" column starts

    readonly StringBuilder _body = new();

    public DocxBuilder Title(string text, int sizeHalfPoints = 26) =>
        Raw(Paragraph(Runs(text, bold: true, size: sizeHalfPoints), DocxAlign.Center, after: 60));

    public DocxBuilder Heading(string text) => Raw(Paragraph(Runs(text, bold: true), DocxAlign.Left, before: 160, after: 60));

    public DocxBuilder Para(string text, DocxAlign align = DocxAlign.Justify, bool indent = false, int after = 120) =>
        Raw(Paragraph(Runs(text), align, after: after, firstLine: indent ? 709 : 0));

    public DocxBuilder Empty() => Raw(Paragraph("", DocxAlign.Left, after: 0));

    /// <summary>"Label : value" row; wrapped value lines stay in the value column.</summary>
    public DocxBuilder Field(string label, string value, bool boldLabel = false)
    {
        var runs = Runs(label, bold: boldLabel) + "<w:r><w:tab/><w:t xml:space=\"preserve\">: </w:t></w:r>" + Runs(value);
        var pPr = $"<w:pPr><w:tabs><w:tab w:val=\"left\" w:pos=\"{LabelWidth}\"/></w:tabs>" +
                  $"<w:spacing w:before=\"0\" w:after=\"40\"/><w:ind w:left=\"{LabelWidth + 200}\" w:hanging=\"{LabelWidth + 200}\"/></w:pPr>";
        return Raw($"<w:p>{pPr}{runs}</w:p>");
    }

    /// <summary>Numbered item ("1. text") with a hanging indent.</summary>
    public DocxBuilder Item(string number, string text)
    {
        var pPr = "<w:pPr><w:tabs><w:tab w:val=\"left\" w:pos=\"425\"/></w:tabs><w:spacing w:after=\"80\"/>" +
                  "<w:ind w:left=\"425\" w:hanging=\"425\"/><w:jc w:val=\"both\"/></w:pPr>";
        return Raw($"<w:p>{pPr}{Runs(number)}<w:r><w:tab/></w:r>{Runs(text)}</w:p>");
    }

    /// <summary>A borderless table with one column per signer; each cell is a list of centred lines.</summary>
    public DocxBuilder Signatures(params string[][] columns)
    {
        if (columns.Length == 0) return this;
        int width = 9070 / columns.Length;
        var sb = new StringBuilder();
        sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:jc w:val=\"center\"/>")
          .Append("<w:tblBorders><w:top w:val=\"nil\"/><w:left w:val=\"nil\"/><w:bottom w:val=\"nil\"/><w:right w:val=\"nil\"/>")
          .Append("<w:insideH w:val=\"nil\"/><w:insideV w:val=\"nil\"/></w:tblBorders><w:tblLayout w:type=\"fixed\"/></w:tblPr><w:tblGrid>");
        foreach (var _ in columns) sb.Append($"<w:gridCol w:w=\"{width}\"/>");
        sb.Append("</w:tblGrid><w:tr>");
        foreach (var col in columns)
        {
            sb.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{width}\" w:type=\"dxa\"/></w:tcPr>");
            for (int i = 0; i < col.Length; i++)
                sb.Append(Paragraph(Runs(col[i], bold: i == 0), DocxAlign.Center, after: 0));
            // Room for the signature.
            sb.Append(Paragraph("", DocxAlign.Center, after: 0)).Append(Paragraph("", DocxAlign.Center, after: 0));
            sb.Append("</w:tc>");
        }
        sb.Append("</w:tr></w:tbl>");
        return Raw(sb.ToString());
    }

    public DocxBuilder Raw(string xml)
    {
        _body.Append(xml);
        return this;
    }

    // ------------------------------------------------------------------ xml helpers

    static string Paragraph(string runs, DocxAlign align, int before = 0, int after = 120, int firstLine = 0)
    {
        var jc = align switch { DocxAlign.Center => "center", DocxAlign.Right => "right", DocxAlign.Justify => "both", _ => "left" };
        var ind = firstLine > 0 ? $"<w:ind w:firstLine=\"{firstLine}\"/>" : "";
        return $"<w:p><w:pPr><w:spacing w:before=\"{before}\" w:after=\"{after}\"/>{ind}<w:jc w:val=\"{jc}\"/></w:pPr>{runs}</w:p>";
    }

    /// <summary>Text with **bold** parts and line breaks (\n) as runs.</summary>
    static string Runs(string text, bool bold = false, int size = 0)
    {
        var sb = new StringBuilder();
        var parts = text.Split("**");
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            bool b = bold ^ (i % 2 == 1);
            var rPr = (b ? "<w:b/>" : "") + (size > 0 ? $"<w:sz w:val=\"{size}\"/><w:szCs w:val=\"{size}\"/>" : "");
            var lines = parts[i].Split('\n');
            sb.Append("<w:r>");
            if (rPr.Length > 0) sb.Append("<w:rPr>").Append(rPr).Append("</w:rPr>");
            for (int l = 0; l < lines.Length; l++)
            {
                if (l > 0) sb.Append("<w:br/>");
                if (lines[l].Length > 0) sb.Append("<w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(lines[l])).Append("</w:t>");
            }
            sb.Append("</w:r>");
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ package

    public byte[] Build()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                s.Write(new UTF8Encoding(false).GetBytes(content));
            }
            Add("[Content_Types].xml", ContentTypes);
            Add("_rels/.rels", Rels);
            Add("word/_rels/document.xml.rels", DocumentRels);
            Add("word/styles.xml", Styles);
            Add("word/settings.xml", Settings);
            Add("word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<w:document xmlns:w=\"{W}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>" +
                _body +
                "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
                "<w:pgMar w:top=\"1134\" w:right=\"1134\" w:bottom=\"1134\" w:left=\"1418\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/>" +
                "</w:sectPr></w:body></w:document>");
        }
        return ms.ToArray();
    }

    const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    const string ContentTypes =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
        "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
        "<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>" +
        "</Types>";

    const string Rels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
        "</Relationships>";

    const string DocumentRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>" +
        "</Relationships>";

    const string Settings =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:settings xmlns:w=\"{W}\"><w:defaultTabStop w:val=\"709\"/><w:characterSpacingControl w:val=\"doNotCompress\"/>" +
        "<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat>" +
        "</w:settings>";

    const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:styles xmlns:w=\"{W}\"><w:docDefaults><w:rPrDefault><w:rPr>" +
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\" w:eastAsia=\"Times New Roman\" w:cs=\"Times New Roman\"/>" +
        "<w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/><w:lang w:val=\"tr-TR\" w:eastAsia=\"tr-TR\" w:bidi=\"ar-SA\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"120\" w:line=\"264\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>" +
        "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:name w:val=\"Normal Table\"/>" +
        "<w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/>" +
        "<w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>" +
        "</w:styles>";
}
