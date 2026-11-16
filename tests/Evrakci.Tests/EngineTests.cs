using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Evrakci.Core;
using Evrakci.Core.Cases;
using Evrakci.Core.Templates;
using Evrakci.Core.Templates.Docx;
using Evrakci.Core.Templates.Udf;
using Evrakci.Core.Text;

namespace Evrakci.Tests;

sealed class FakeValues(Dictionary<string, RenderValue> values) : IValueSource
{
    public RenderValue Get(VariableRef r) => values.TryGetValue(r.Key, out var v) ? v : RenderValue.Missing;
    public string? SuffixOverride(int occurrence) => null;
    public static RenderValue V(string s) => new(ValueState.Value, s, false);
}

public class DocxEngineTests
{
    const string Ns = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
                      "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
                      "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
                      "xmlns:v=\"urn:schemas-microsoft-com:vml\" mc:Ignorable=\"wps\"";

    static byte[] MakeDocx(string body, string? header = null, string? footnotes = null, bool macro = false)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string name, string content)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(Encoding.UTF8.GetBytes(content));
            }
            Add("[Content_Types].xml", macro
                ? "<Types><Override ContentType=\"application/vnd.ms-word.document.macroEnabled.main+xml\"/></Types>"
                : "<Types/>");
            Add("word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document {Ns}><w:body>{body}<w:sectPr/></w:body></w:document>");
            if (header is not null) Add("word/header1.xml", $"<w:hdr {Ns}>{header}</w:hdr>");
            if (footnotes is not null) Add("word/footnotes.xml", $"<w:footnotes {Ns}>{footnotes}</w:footnotes>");
        }
        return ms.ToArray();
    }

    static DocxTemplate Load(byte[] bytes) => new("t.docx", SafeZip.Read(bytes), new VariableMatcher("{{{", "}}}"));

    static string R(string text, bool bold = false) =>
        $"<w:r>{(bold ? "<w:rPr><w:b/></w:rPr>" : "")}<w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    static readonly Dictionary<string, RenderValue> Values = new()
    {
        [Tr.Key("TARAF1 ADI")] = FakeValues.V("AYŞE FİLİZ"),
        [Tr.Key("TUTAR")] = FakeValues.V("1.000,00 TL"),
        [Tr.Key("OPS")] = new(ValueState.Blank, "", true),
        [Tr.Key("NOT")] = new(ValueState.Blank, "", false),
    };

    [Fact]
    public void JoinsSplitRunsKeepsFormattingAndAdaptsSuffix()
    {
        var body = "<w:p>" + R("Sayın ") + R("{{{TA") + R("RAF1 ", bold: true) + R("adı}}}") + R("’") + R("nın", bold: true) + R(" işi.") + "</w:p>";
        var t = Load(MakeDocx(body));
        Assert.Single(t.Occurrences);
        var outBytes = t.Render(new FakeValues(Values));
        Assert.Equal("Sayın AYŞE FİLİZ’in işi.", DocxText.Paragraphs(outBytes)[0]);

        // The value takes the first run's formatting; the suffix keeps its own runs.
        var doc = XDocument.Load(new ZipArchive(new MemoryStream(outBytes)).GetEntry("word/document.xml")!.Open());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var runs = doc.Descendants(w + "r").Select(r => (Text: r.Element(w + "t")!.Value, Bold: r.Element(w + "rPr")?.Element(w + "b") is not null)).ToList();
        Assert.Contains(runs, r => r.Text == "AYŞE FİLİZ" && !r.Bold);
        Assert.Contains(runs, r => r.Text == "’in" && !r.Bold);
    }

    [Fact]
    public void HandlesTablesTextBoxesHeadersAndFootnotes()
    {
        var textbox = "<w:p><w:r><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing><wps:txbx><w:txbxContent><w:p>" +
                      R("{{{TARAF1 ADI}}}'a") + "</w:p></w:txbxContent></wps:txbx></w:drawing></mc:Choice><mc:Fallback><w:pict><v:textbox><w:txbxContent><w:p>" +
                      R("{{{TARAF1 ADI}}}'a") + "</w:p></w:txbxContent></v:textbox></w:pict></mc:Fallback></mc:AlternateContent></w:r></w:p>";
        var table = "<w:tbl><w:tr><w:tc><w:p>" + R("{{{TUTAR}}}") + "</w:p></w:tc></w:tr></w:tbl>";
        var bytes = MakeDocx(textbox + table, header: "<w:p>" + R("Dosya: {{{TARAF1 ADI}}}") + "</w:p>",
            footnotes: "<w:footnote w:id=\"1\"><w:p>" + R("Not {{{TUTAR}}}") + "</w:p></w:footnote>");
        var t = Load(bytes);
        var outBytes = t.Render(new FakeValues(Values));
        var text = DocxText.All(outBytes);
        Assert.DoesNotContain("{{{", text);
        Assert.Equal(2, text.Split("AYŞE FİLİZ'e").Length - 1);   // Choice and Fallback
        Assert.Contains("1.000,00 TL", text);
        Assert.Equal("Dosya: AYŞE FİLİZ", DocxText.Paragraphs(outBytes, "word/header1.xml")[0]);
        Assert.Equal("Not 1.000,00 TL", DocxText.Paragraphs(outBytes, "word/footnotes.xml")[0]);

        // Fallback reuses Choice's occurrence numbers, so 5 references → 4 indices.
        Assert.Equal(4, t.Occurrences.Select(o => o.Occurrence).Distinct().Count());

        var preview = t.Preview(new FakeValues(Values));
        var sections = preview.Sections.Select(s => s.Title).ToList();
        Assert.Contains("Üst bilgi", sections);
        Assert.Contains("Dipnotlar", sections);
    }

    [Fact]
    public void RemovesParagraphsOfBlankFieldsAndDropsSuffixOfBlankValues()
    {
        var body = "<w:p>" + R("Başlık") + "</w:p><w:p>" + R("Taksit {{{OPS}}}") + "</w:p><w:p>" + R("Not: {{{NOT}}}'un sonu") + "</w:p>";
        var outBytes = Load(MakeDocx(body)).Render(new FakeValues(Values));
        Assert.Equal(["Başlık", "Not: sonu"], DocxText.Paragraphs(outBytes).Select(s => s.Replace("  ", " ")).ToArray());
    }

    [Fact]
    public void RejectsMacroDocuments() =>
        Assert.Throws<EvrakciException>(() => Load(MakeDocx("<w:p/>", macro: true)));

    [Fact]
    public void CorruptFilesDoNotCrash()
    {
        Assert.Throws<EvrakciException>(() => SafeZip.Read([1, 2, 3, 4, 5]));
        var bytes = MakeDocx("<w:p><w:r><w:t>kırık");   // malformed XML
        Assert.Throws<EvrakciException>(() => Load(bytes));
    }

    [Fact]
    public void RejectsZipBombs()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using var s = zip.CreateEntry("word/document.xml", CompressionLevel.SmallestSize).Open();
            var chunk = new byte[1024 * 1024];
            for (int i = 0; i < 120; i++) s.Write(chunk);
        }
        Assert.Throws<EvrakciException>(() => SafeZip.Read(ms.ToArray()));
    }
}

public class UdfEngineTests
{
    static byte[] MakeUdf(params (string Text, bool Bold)[][] paragraphs)
    {
        var text = new StringBuilder();
        var elements = new StringBuilder();
        foreach (var para in paragraphs)
        {
            elements.Append("<paragraph Alignment=\"3\">");
            foreach (var (t, bold) in para)
            {
                elements.Append($"<content {(bold ? "bold=\"true\" " : "")}startOffset=\"{text.Length}\" length=\"{t.Length}\" />");
                text.Append(t);
            }
            elements.Append("</paragraph>");
        }
        var xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\" ?><template format_id=\"1.8\"><content><![CDATA[{text}]]></content>" +
                  $"<properties><pageFormat mediaSizeName=\"1\"/></properties><elements resolver=\"hvl-default\">{elements}</elements>" +
                  "<styles><style name=\"hvl-default\" family=\"Times New Roman\" size=\"12\"/></styles></template>";
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using var s = zip.CreateEntry("content.xml").Open();
            s.Write(Encoding.UTF8.GetBytes(xml));
        }
        return ms.ToArray();
    }

    static (string Text, List<(int Start, int Length, bool Bold)> Spans) Read(byte[] udf)
    {
        using var zip = new ZipArchive(new MemoryStream(udf));
        var doc = XDocument.Load(zip.GetEntry("content.xml")!.Open());
        var text = doc.Root!.Element("content")!.Value;
        var spans = doc.Root.Element("elements")!.Descendants("content")
            .Select(c => ((int)c.Attribute("startOffset")!, (int)c.Attribute("length")!, (string?)c.Attribute("bold") == "true")).ToList();
        return (text, spans);
    }

    [Fact]
    public void ReplacesAcrossSpansAndShiftsOffsets()
    {
        var udf = MakeUdf(
            [("Sayın {{{A", false), ("D}}}'n", true), ("ın borcu {{{TUTAR}}}'dır.\n", false)],
            [("Silinecek {{{OPS}}} satır.\n", false)],
            [("Son satır.\n", false)]);
        var t = new UdfTemplate("t.udf", SafeZip.Read(udf), new VariableMatcher("{{{", "}}}"));
        Assert.Equal(3, t.Occurrences.Count);

        var values = new FakeValues(new()
        {
            [Tr.Key("AD")] = FakeValues.V("AHMET"),
            [Tr.Key("TUTAR")] = FakeValues.V("1.000,00 TL"),
            [Tr.Key("OPS")] = new(ValueState.Blank, "", true),
        });
        var (text, spans) = Read(t.Render(values));
        Assert.Equal("Sayın AHMET'in borcu 1.000,00 TL'dir.\nSon satır.\n", text);

        // Spans stay contiguous and cover the whole text.
        int pos = 0;
        foreach (var (s, l, _) in spans)
        {
            Assert.Equal(pos, s);
            pos += l;
        }
        Assert.Equal(text.Length, pos);
        // The value went into the span where the variable started (not bold); the suffix kept the bold span.
        Assert.Equal("Sayın AHMET", text.Substring(spans[0].Start, spans[0].Length));
        Assert.Equal((true, "'in"), (spans[1].Bold, text.Substring(spans[1].Start, spans[1].Length)));

        var preview = t.Preview(values);
        var paras = preview.Sections.SelectMany(s => s.Blocks).OfType<PreviewParagraph>().ToList();
        Assert.Equal(3, paras.Count);
        Assert.True(paras[1].Removed);
    }
}

public class CaseFileTests
{
    static CaseData Sample() => new()
    {
        SetName = "Deneme",
        SavedAt = DateTimeOffset.Now,
        Values = { ["TARAF1 TCKN"] = new CaseValue { Value = "12345678950" }, ["TAKSİT3 TARİHİ"] = new CaseValue { Blank = true } },
        SuffixOverrides = new() { ["Anlaşma Belgesi.docx"] = new() { ["12"] = "ya" } },
    };

    [Fact]
    public void PlainRoundTrip()
    {
        var path = Path.Combine(TestPaths.TempDir(), "v.evrak");
        CaseFile.Save(path, Sample(), null);
        Assert.False(CaseFile.IsEncrypted(path));
        var back = CaseFile.Load(path, null);
        Assert.Equal("12345678950", back.Values["TARAF1 TCKN"].Value);
        Assert.True(back.Values["TAKSİT3 TARİHİ"].Blank);
        Assert.Equal("ya", back.SuffixOverrides!["Anlaşma Belgesi.docx"]["12"]);
        Assert.Contains("\"formatVersion\": 1", File.ReadAllText(path));
    }

    [Fact]
    public void EncryptedRoundTripAndWrongPassword()
    {
        var path = Path.Combine(TestPaths.TempDir(), "v.evrak");
        CaseFile.Save(path, Sample(), "gizli şifre");
        Assert.True(CaseFile.IsEncrypted(path));
        Assert.DoesNotContain("12345678950", File.ReadAllText(path));
        Assert.Equal("12345678950", CaseFile.Load(path, "gizli şifre").Values["TARAF1 TCKN"].Value);
        var ex = Assert.Throws<EvrakciException>(() => CaseFile.Load(path, "yanlış"));
        Assert.Contains("Şifre yanlış", ex.Message);
        Assert.Throws<EvrakciException>(() => CaseFile.Load(path, null));
    }

    [Fact]
    public void RejectsNewerVersions()
    {
        var path = Path.Combine(TestPaths.TempDir(), "v.evrak");
        File.WriteAllText(path, "{\"formatVersion\": 99, \"values\": {}}");
        Assert.Throws<EvrakciException>(() => CaseFile.Load(path, null));
    }
}
