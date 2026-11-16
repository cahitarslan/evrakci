using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Evrakci.Core.Cases;
using Evrakci.Core.Output;
using Evrakci.Core.Sets;
using Evrakci.Core.Templates;
using Evrakci.Core.Templates.Docx;
using Evrakci.Core.Templates.Udf;
using Evrakci.Core.Text;

namespace Evrakci.Tests;

public class RewriteAndUdfTests
{
    static readonly VariableMatcher M = new("{{{", "}}}");

    static ITemplateDocument Docx(byte[] b) => TemplateLoader.Load(Save(b, ".docx"), M);
    static ITemplateDocument Udf(byte[] b) => TemplateLoader.Load(Save(b, ".udf"), M);

    static string Save(byte[] bytes, string ext)
    {
        var p = Path.Combine(TestPaths.TempDir(), "t" + ext);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    /// <summary>The editor's view of a template: every paragraph as it is, with its formatting.</summary>
    static List<EditParagraph> AsEdited(ITemplateDocument t, out HashSet<int> shown)
    {
        var preview = t.Preview(EmptyValueSource.Instance);
        var list = new List<EditParagraph>();
        void Walk(IEnumerable<PreviewBlock> blocks)
        {
            foreach (var b in blocks)
            {
                if (b is PreviewParagraph p)
                {
                    var src = t.SourceParagraphs.First(x => x.Id == p.Id).Text.TrimEnd('\n');
                    list.Add(new EditParagraph(p.Id, [new EditRun(src)], p.Alignment));
                }
                else if (b is PreviewTable tb) foreach (var row in tb.Rows) foreach (var cell in row) Walk(cell);
            }
        }
        foreach (var s in preview.Sections) Walk(s.Blocks);
        shown = list.Select(p => p.Id!.Value).ToHashSet();
        return list;
    }

    static byte[] Sample() => new DocxBuilder()
        .Title("BAŞLIK")
        .Para("Birinci paragraf {{{AD}}}’ın metni.")
        .Field("Etiket", "{{{TUTAR}}}")
        .Para("Üçüncü paragraf.")
        .Signatures(["TARAF", "{{{AD}}}"])
        .Build();

    [Fact]
    public void DocxRewriteChangesOnlyEditedParagraphs()
    {
        var t = Docx(Sample());
        var edited = AsEdited(t, out var shown);
        // Unchanged edit list (titles are bold, so give them their real formatting via Same check): plain text differs in bold only for the title.
        int title = edited.FindIndex(p => p.Text == "BAŞLIK");
        edited[title] = edited[title] with { Runs = [new EditRun("BAŞLIK", Bold: true)] };
        int sig = edited.FindIndex(p => p.Text == "TARAF");
        edited[sig] = edited[sig] with { Runs = [new EditRun("TARAF", Bold: true)] };

        int i = edited.FindIndex(p => p.Text.StartsWith("Birinci"));
        edited[i] = edited[i] with { Runs = [new EditRun("Değişti: "), new EditRun("{{{AD}}}", Bold: true), new EditRun(" son.")], Alignment = PreviewAlignment.Center };
        edited.Insert(i + 1, new EditParagraph(null, [new EditRun("Yeni paragraf {{{YENİ}}}")], PreviewAlignment.Left));
        edited.RemoveAll(p => p.Text == "Üçüncü paragraf.");

        var bytes = t.Rewrite(edited, shown);
        var text = DocxText.Paragraphs(bytes);
        Assert.Contains("Değişti: {{{AD}}} son.", text);
        Assert.Equal(text.IndexOf("Değişti: {{{AD}}} son.") + 1, text.IndexOf("Yeni paragraf {{{YENİ}}}"));
        Assert.DoesNotContain("Üçüncü paragraf.", text);
        Assert.Contains("Etiket\t: {{{TUTAR}}}", text);

        var again = Docx(bytes);
        Assert.Contains(again.Occurrences, o => o.Ref.Name == "YENİ");
        var doc = XDocument.Load(new ZipArchive(new MemoryStream(bytes)).GetEntry("word/document.xml")!.Open());
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var p = doc.Descendants(w + "p").First(x => string.Concat(x.Descendants(w + "t").Select(e => e.Value)).StartsWith("Değişti"));
        Assert.Equal("center", (string?)p.Element(w + "pPr")!.Element(w + "jc")!.Attribute(w + "val"));
        Assert.True(p.Elements(w + "r").Any(r => r.Element(w + "rPr")?.Element(w + "b") is not null && r.Value == "{{{AD}}}"), p.ToString());

        // An edit list identical to the template leaves it unchanged.
        var same = AsEdited(again, out var shown2);
        foreach (var k in new[] { "BAŞLIK", "TARAF" })
        {
            int j = same.FindIndex(x => x.Text == k);
            same[j] = same[j] with { Runs = [new EditRun(k, Bold: true)] };
        }
        int d = same.FindIndex(x => x.Text.StartsWith("Değişti"));
        same[d] = same[d] with { Runs = [new EditRun("Değişti: "), new EditRun("{{{AD}}}", Bold: true), new EditRun(" son.")] };
        Assert.Equal(DocxText.All(bytes), DocxText.All(again.Rewrite(same, shown2)));
    }

    [Fact]
    public void ConvertsWordToUdfThatReadsBackWithTheSameVariables()
    {
        var docx = Sample();
        var udf = DocxToUdf.Convert(docx);
        var t = Udf(udf);
        Assert.Equal(Docx(docx).Occurrences.Select(o => o.Ref.Key), t.Occurrences.Select(o => o.Ref.Key));

        var values = new Dictionary<string, RenderValue> { [Tr.Key("AD")] = FakeValues.V("AYŞE"), [Tr.Key("TUTAR")] = FakeValues.V("10,00 TL") };
        var rendered = t.Render(new FakeValues(values));
        var xml = XDocument.Load(new ZipArchive(new MemoryStream(rendered)).GetEntry("content.xml")!.Open());
        var text = xml.Root!.Element("content")!.Value;
        Assert.Contains("Birinci paragraf AYŞE’nin metni.\n", text);
        Assert.Contains("Etiket\t: 10,00 TL\n", text);
        // Every span stays inside the text.
        foreach (var e in xml.Root.Descendants().Where(e => e.Attribute("startOffset") is not null))
            Assert.True((int)e.Attribute("startOffset")! + (int)e.Attribute("length")! <= text.Length);
        Assert.NotNull(xml.Root.Descendants("table").Single().Attribute("columnSpans"));
        Assert.Contains(xml.Root.Descendants("content"), c => (string?)c.Attribute("bold") == "true");
    }

    /// <summary>The UYAP editor measures tab stops from the left indent, Word from the margin: the ":" of a
    /// "Label <tab> : value" line (hanging indent) must stay where Word puts it.</summary>
    [Fact]
    public void UdfTabStopsLandWhereWordPutsThem()
    {
        var docx = Sample();
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var word = XDocument.Load(new ZipArchive(new MemoryStream(docx)).GetEntry("word/document.xml")!.Open())
            .Descendants(w + "p").First(p => p.Descendants(w + "tab").Any(t => t.Parent!.Name == w + "r"));
        var wordStop = (double)word.Descendants(w + "tabs").Single().Element(w + "tab")!.Attribute(w + "pos")! / 20;
        var ind = word.Element(w + "pPr")!.Element(w + "ind")!;
        var wordFirstLine = ((double?)ind.Attribute(w + "left") ?? 0) / 20 - ((double?)ind.Attribute(w + "hanging") ?? 0) / 20;

        var xml = XDocument.Load(new ZipArchive(new MemoryStream(DocxToUdf.Convert(docx))).GetEntry("content.xml")!.Open());
        var p = xml.Descendants("paragraph").First(e => e.Element("tab") is not null);
        double left = (double)p.Attribute("LeftIndent")!, first = (double?)p.Attribute("FirstLineIndent") ?? 0;
        var stops = ((string)p.Attribute("TabSet")!).Split(',').Select(s => double.Parse(s.Split(':')[0], CultureInfo.InvariantCulture)).ToList();
        Assert.All(stops, s => Assert.True(s >= 0));
        Assert.Equal(wordStop, left + stops[0], 3);
        Assert.Equal(wordFirstLine, left + first, 3);
    }

    [Fact]
    public void UdfRewriteEditsInsertsAndDeletes()
    {
        var t = Udf(DocxToUdf.Convert(Sample()));
        var edited = AsEdited(t, out var shown);
        int i = edited.FindIndex(p => p.Text.StartsWith("Birinci"));
        edited[i] = edited[i] with { Runs = [new EditRun("Kısa "), new EditRun("{{{AD}}}", Italic: true)] };
        edited.Insert(i + 1, new EditParagraph(null, [new EditRun("Araya giren {{{YENİ}}}")], PreviewAlignment.Right));
        edited.RemoveAll(p => p.Text == "Üçüncü paragraf.");
        var bytes = t.Rewrite(edited, shown);

        var again = Udf(bytes);
        var texts = again.SourceParagraphs.Select(p => p.Text.TrimEnd('\n')).ToList();
        Assert.Contains("Kısa {{{AD}}}", texts);
        Assert.Equal(texts.IndexOf("Kısa {{{AD}}}") + 1, texts.IndexOf("Araya giren {{{YENİ}}}"));
        Assert.DoesNotContain("Üçüncü paragraf.", texts);
        Assert.Contains("Etiket\t: {{{TUTAR}}}", texts);
        Assert.Contains(again.Occurrences, o => o.Ref.Name == "YENİ");
    }

    [Fact]
    public void GeneratorProducesWordUdfOrBoth()
    {
        var set = TemplateSet.Open(Path.Combine(TestPaths.BuiltInSets, "Kira"));
        var inputs = Resolver.NewCase(set);
        foreach (var f in set.Fields.Where(f => !f.IsComputed)) { inputs[f.Key].Blank = true; }
        var res = Resolver.Resolve(set, inputs);
        var docs = new HashSet<string> { set.Documents[2].Entry.Template };
        var dir = TestPaths.TempDir();
        Assert.Single(Generator.Plan(set, res, dir, docs, OutputFormats.Word), p => p.Path.EndsWith(".docx"));
        Assert.Single(Generator.Plan(set, res, dir, docs, OutputFormats.Udf), p => p.Path.EndsWith(".udf"));
        var both = Generator.Plan(set, res, dir, docs, OutputFormats.Both);
        Assert.Equal(2, both.Count);
        if (res.IsValid)
        {
            Generator.Write(both, res, new SuffixOverrides(), false);
            Assert.NotNull(Udf(File.ReadAllBytes(both.Single(p => p.Format == DocumentFormat.Udf).Path)));
        }
    }

    [Theory, MemberData(nameof(BuiltInSetTests.Sets), MemberType = typeof(BuiltInSetTests))]
    public void EveryBuiltInTemplateConvertsToUdfWithItsVariables(string folder)
    {
        var set = TemplateSet.Open(Path.Combine(TestPaths.BuiltInSets, folder));
        foreach (var d in set.Documents)
        {
            var udf = Udf(DocxToUdf.Convert(File.ReadAllBytes(d.Path)));
            Assert.Equal(d.Template!.Occurrences.Select(o => o.Ref.Key).Order(), udf.Occurrences.Select(o => o.Ref.Key).Order());
        }
    }

    /// <summary>The UYAP editor refuses files whose font sizes or column widths are not whole numbers.</summary>
    [Theory, MemberData(nameof(BuiltInSetTests.Sets), MemberType = typeof(BuiltInSetTests))]
    public void UdfOutputUsesWholeNumberSizesAndValidOffsets(string folder)
    {
        var set = TemplateSet.Open(Path.Combine(TestPaths.BuiltInSets, folder));
        foreach (var d in set.Documents)
        {
            var bytes = DocxToUdf.Convert(File.ReadAllBytes(d.Path));
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
            Assert.Equal(["content.xml"], zip.Entries.Select(e => e.FullName));
            using var s = zip.Entries[0].Open();
            var xml = System.Xml.Linq.XDocument.Load(s);
            var text = xml.Root!.Element("content")!.Value;
            foreach (var e in xml.Descendants())
            {
                if (e.Attribute("size") is { } size) Assert.Matches(@"^\d+$", size.Value);
                if (e.Attribute("columnSpans") is { } spans) Assert.Matches(@"^\d+(,\d+)*$", spans.Value);
                if (e.Attribute("startOffset") is { } start)
                {
                    int st = int.Parse(start.Value), len = int.Parse(e.Attribute("length")!.Value);
                    Assert.True(len > 0 && st + len <= text.Length, $"{d.Entry.Template}: {e}");
                }
            }
        }
    }
}
