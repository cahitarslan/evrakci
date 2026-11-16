using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Evrakci.Core.Templates.Udf;

/// <summary>
/// Converts a Word document into a UYAP Doküman Editörü (.udf) file: paragraphs with alignment,
/// indents and line breaks, bold / italic / underline text, font and size, tabs and borderless or
/// bordered tables. Pictures, headers, footers and footnotes are not carried over.
///
/// The UDF layout (format_id 1.8: one CDATA text, &lt;paragraph&gt; / &lt;content&gt; / &lt;tab&gt; /
/// &lt;table&gt; elements pointing into it with startOffset / length) follows files written by the
/// UYAP editor as documented by the open-source UDF-Toolkit project.
/// </summary>
public static class DocxToUdf
{
    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    sealed class Writer
    {
        public readonly StringBuilder Text = new();
        public readonly StringBuilder Xml = new();
        public string Font = "Times New Roman";
        public double Size = 12;
    }

    public static byte[] Convert(byte[] docx)
    {
        var package = SafeZip.Read(docx);
        var document = SafeZip.ParseXml(package.Get("word/document.xml") ?? throw new EvrakciException("Geçerli bir Word belgesi değil."));
        var w = new Writer();
        if (package.Get("word/styles.xml") is { } stylesBytes)
        {
            var defaults = SafeZip.ParseXml(stylesBytes).Root?.Element(W + "docDefaults")?.Element(W + "rPrDefault")?.Element(W + "rPr");
            if ((string?)defaults?.Element(W + "rFonts")?.Attribute(W + "ascii") is { Length: > 0 } f) w.Font = f;
            if (HalfPoints(defaults) is { } sz) w.Size = sz;
        }
        var body = document.Root?.Element(W + "body") ?? throw new EvrakciException("Word belgesinin gövdesi boş.");
        Blocks(body, w);
        if (w.Text.Length == 0) Paragraph(new XElement(W + "p"), w);

        var margins = body.Element(W + "sectPr")?.Element(W + "pgMar");
        double M(string name, double fallback) => double.TryParse((string?)margins?.Attribute(W + name), NumberStyles.Any, Inv, out var v) ? v / 20 : fallback;
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\n<template format_id=\"1.8\">\n");
        xml.Append("<content><![CDATA[").Append(w.Text.ToString().Replace("]]>", "]]]]><![CDATA[>")).Append("]]></content>\n");
        xml.Append("<properties><pageFormat mediaSizeName=\"1\"")
           .Append($" leftMargin=\"{N(M("left", 70.875))}\" rightMargin=\"{N(M("right", 70.875))}\"")
           .Append($" topMargin=\"{N(M("top", 56.7))}\" bottomMargin=\"{N(M("bottom", 56.7))}\"")
           .Append(" paperOrientation=\"1\" headerFOffset=\"20.0\" footerFOffset=\"20.0\" /></properties>\n");
        xml.Append("<elements resolver=\"hvl-default\">\n").Append(w.Xml).Append("</elements>\n");
        xml.Append("<styles><style name=\"default\" description=\"Geçerli\" family=\"Dialog\" size=\"12\" bold=\"false\" italic=\"false\" foreground=\"-13421773\" FONT_ATTRIBUTE_KEY=\"javax.swing.plaf.FontUIResource[family=Dialog,name=Dialog,style=plain,size=12]\" />")
           .Append($"<style name=\"hvl-default\" family=\"{Esc(w.Font)}\" size=\"{I(w.Size)}\" description=\"Gövde\" /></styles>\n</template>\n");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var s = zip.CreateEntry("content.xml", CompressionLevel.Optimal).Open();
            s.Write(new UTF8Encoding(false).GetBytes(xml.ToString()));
        }
        return ms.ToArray();
    }

    static void Blocks(XElement container, Writer w)
    {
        foreach (var e in container.Elements())
        {
            if (e.Name == W + "p") Paragraph(e, w);
            else if (e.Name == W + "tbl") Table(e, w);
            else if (e.Name == W + "sdt") Blocks(e.Element(W + "sdtContent") ?? e, w);
            else if (e.Name == W + "customXml") Blocks(e, w);
        }
    }

    static void Table(XElement tbl, Writer w)
    {
        var widths = tbl.Element(W + "tblGrid")?.Elements(W + "gridCol")
            .Select(g => double.TryParse((string?)g.Attribute(W + "w"), NumberStyles.Any, Inv, out var v) ? v / 20 : 100).ToList() ?? new();
        var rows = tbl.Elements(W + "tr").ToList();
        int columns = Math.Max(1, rows.Count == 0 ? widths.Count : rows.Max(r => r.Elements(W + "tc").Count()));
        while (widths.Count < columns) widths.Add(100);
        var borders = tbl.Element(W + "tblPr")?.Element(W + "tblBorders");
        bool none = borders is not null && borders.Elements().All(b => (string?)b.Attribute(W + "val") is "nil" or "none");
        // The editor reads column widths and font sizes as whole numbers; "100.0" stops it opening the file.
        w.Xml.Append($"<table tableName=\"Sabit\" columnCount=\"{columns}\" columnSpans=\"{string.Join(",", widths.Take(columns).Select(I))}\" border=\"{(none ? "borderNone" : "borderCell")}\">");
        int r = 0;
        foreach (var tr in rows)
        {
            w.Xml.Append($"<row rowName=\"row{++r}\" rowType=\"dataRow\">");
            foreach (var tc in tr.Elements(W + "tc"))
            {
                w.Xml.Append("<cell>");
                Blocks(tc, w);
                w.Xml.Append("</cell>");
            }
            w.Xml.Append("</row>");
        }
        w.Xml.Append("</table>\n");
    }

    sealed record Piece(string Text, bool Bold, bool Italic, bool Underline, string? Font, double? Size);

    static void Paragraph(XElement p, Writer w)
    {
        var pPr = p.Element(W + "pPr");
        // A line break inside a Word paragraph becomes a new UDF paragraph with the same settings.
        var lines = new List<List<Piece>> { new() };
        foreach (var r in p.Descendants(W + "r").Where(r => r.Ancestors(W + "p").First() == p))
        {
            if (r.Ancestors().Any(a => a.Name == W + "del" || a.Name.LocalName == "Fallback")) continue;
            var rPr = r.Element(W + "rPr");
            bool b = On(rPr, "b"), i = On(rPr, "i");
            bool u = rPr?.Element(W + "u") is { } ue && (string?)ue.Attribute(W + "val") != "none";
            var font = (string?)rPr?.Element(W + "rFonts")?.Attribute(W + "ascii");
            var size = HalfPoints(rPr);
            foreach (var c in r.Elements())
            {
                if (c.Name == W + "t") { if (Clean(c.Value) is { Length: > 0 } t) lines[^1].Add(new Piece(t, b, i, u, font, size)); }
                else if (c.Name == W + "tab") lines[^1].Add(new Piece("\t", b, i, u, font, size));
                else if (c.Name == W + "br" || c.Name == W + "cr") lines.Add(new());
                else if (c.Name == W + "noBreakHyphen") lines[^1].Add(new Piece("-", b, i, u, font, size));
            }
        }

        var align = (string?)pPr?.Element(W + "jc")?.Attribute(W + "val") switch
        {
            "center" => "1", "right" or "end" => "2", "both" or "distribute" => "3", _ => "0",
        };
        var ind = pPr?.Element(W + "ind");
        double Twips(string name) => double.TryParse((string?)ind?.Attribute(W + name), NumberStyles.Any, Inv, out var v) ? v / 20 : 0;
        double left = Twips("left") + Twips("start");
        double first = Twips("firstLine") - Twips("hanging");
        var tabs = pPr?.Element(W + "tabs")?.Elements(W + "tab")
            .Where(t => (string?)t.Attribute(W + "val") is not ("clear" or "bar"))
            .Select(t => double.TryParse((string?)t.Attribute(W + "pos"), NumberStyles.Any, Inv, out var v) ? v / 20 : 0)
            .Where(v => v > 0).ToList() ?? new();
        // Word measures tab stops from the margin; the UYAP editor (Swing) from the paragraph's left indent, so a
        // stop at 170 with a 180 indent would land at 350. A stop left of the indent ("Label <tab> : value" with a
        // hanging indent) cannot be written as a negative stop, so the indent moves back to just before it; the
        // first line still starts where it did and wrapped lines stay under the value.
        double firstStart = left + first;
        double tabBase = Math.Min(left, tabs.Where(t => t > firstStart).Select(t => Math.Max(firstStart, t - 1)).DefaultIfEmpty(left).Min());
        tabs = tabs.Where(t => t > tabBase).Select(t => t - tabBase).ToList();
        left = tabBase;
        first = firstStart - tabBase;
        var attrs = $"Alignment=\"{align}\" LeftIndent=\"{N(left)}\" RightIndent=\"0.0\"" +
                    (first != 0 ? $" FirstLineIndent=\"{N(first)}\"" : "") +
                    (tabs.Count > 0 ? $" TabSet=\"{string.Join(",", tabs.Select(t => N(t) + ":0:0"))}\"" : "") +
                    (pPr?.Element(W + "numPr") is not null ? " Bulleted=\"true\" BulletType=\"BULLET_TYPE_ELLIPSE\"" : "");

        foreach (var line in lines)
        {
            w.Xml.Append($"<paragraph {attrs}>");
            Piece? last = null;
            foreach (var piece in Merge(line))
            {
                last = piece;
                if (piece.Text == "\t")
                {
                    w.Xml.Append($"<tab {Style(piece, w)} startOffset=\"{w.Text.Length}\" length=\"1\" />");
                    w.Text.Append('\t');
                    continue;
                }
                w.Xml.Append($"<content {Style(piece, w)} startOffset=\"{w.Text.Length}\" length=\"{piece.Text.Length}\" />");
                w.Text.Append(piece.Text);
            }
            // The paragraph break belongs to its own one-character span.
            var end = last ?? new Piece("", false, false, false, null, null);
            w.Xml.Append($"<content {Style(end with { Bold = false, Italic = false, Underline = false }, w)} startOffset=\"{w.Text.Length}\" length=\"1\" />");
            w.Text.Append('\n');
            w.Xml.Append("</paragraph>\n");
        }
    }

    /// <summary>Joins neighbouring text of equal formatting; tabs stay single pieces.</summary>
    static IEnumerable<Piece> Merge(List<Piece> pieces)
    {
        Piece? current = null;
        foreach (var p in pieces)
        {
            if (p.Text == "\t")
            {
                if (current is not null) { yield return current; current = null; }
                yield return p;
                continue;
            }
            if (current is not null && current with { Text = "" } == p with { Text = "" }) current = current with { Text = current.Text + p.Text };
            else
            {
                if (current is not null) yield return current;
                current = p;
            }
        }
        if (current is not null) yield return current;
    }

    static string Style(Piece p, Writer w)
    {
        var sb = new StringBuilder($"family=\"{Esc(p.Font ?? w.Font)}\" size=\"{I(p.Size ?? w.Size)}\"");
        if (p.Bold) sb.Append(" bold=\"true\"");
        if (p.Italic) sb.Append(" italic=\"true\"");
        if (p.Underline) sb.Append(" underline=\"true\"");
        return sb.ToString();
    }

    static bool On(XElement? rPr, string name)
    {
        var e = rPr?.Element(W + name);
        if (e is null) return false;
        var v = (string?)e.Attribute(W + "val");
        return v is null || !(v is "0" or "false" or "off");
    }

    static double? HalfPoints(XElement? rPr) =>
        double.TryParse((string?)rPr?.Element(W + "sz")?.Attribute(W + "val"), NumberStyles.Any, Inv, out var v) ? v / 2 : null;

    static string N(double v) => v.ToString("0.0###", Inv);

    /// <summary>Whole number (font sizes, column widths).</summary>
    static string I(double v) => Math.Max(1, (int)Math.Round(v, MidpointRounding.AwayFromZero)).ToString(Inv);

    /// <summary>Drops characters XML 1.0 does not allow (Word keeps some control characters in text).</summary>
    static string Clean(string s) =>
        s.Any(c => c < 0x20 && c != '\t') ? new string(s.Where(c => c >= 0x20 || c == '\t').ToArray()) : s;

    static string Esc(string s) => SecurityElement.Escape(s) ?? "";
}
