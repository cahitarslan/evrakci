using System.Xml.Linq;

namespace Evrakci.Core.Templates.Udf;

/// <summary>
/// UYAP Doküman Editörü (.udf) template.
///
/// A .udf file is a zip archive whose <c>content.xml</c> holds the whole document text in one
/// <c>&lt;content&gt;</c> CDATA block; elements under <c>&lt;elements&gt;</c> (paragraph → content, tables,
/// …) point into that text with <c>startOffset</c> / <c>length</c> attributes. Because the text is
/// contiguous, a variable split over differently formatted spans is still one string. Every edit
/// shifts the offsets of all later spans; the replaced text belongs to the span where the variable
/// starts.
///
/// There is no official specification of the format; this implementation follows the structure of
/// files produced by the editor and must be checked against real UYAP samples.
/// </summary>
public sealed class UdfTemplate : ITemplateDocument
{
    const string ContentEntry = "content.xml";

    readonly ZipPackage _package;
    readonly VariableMatcher _matcher;
    readonly List<ScannedOccurrence> _occurrences = new();
    readonly List<string> _warnings = new();

    public string FileName { get; }
    public DocumentFormat Format => DocumentFormat.Udf;
    public IReadOnlyList<ScannedOccurrence> Occurrences => _occurrences;
    public IReadOnlyList<string> Warnings => _warnings;

    public UdfTemplate(string fileName, ZipPackage package, VariableMatcher matcher)
    {
        FileName = fileName;
        _package = package;
        _matcher = matcher;
        if (!package.Has(ContentEntry))
            throw new EvrakciException("Geçerli bir UDF belgesi değil (content.xml bulunamadı).");

        var model = Load(EmptyValueSource.Instance);
        int unclosed = 0;
        foreach (var p in model.Paragraphs)
        {
            unclosed += p.Plan.Unclosed;
            foreach (var o in p.Plan.Occurrences)
                _occurrences.Add(new ScannedOccurrence(o.Match.Ref, o.Occurrence, o.Match.Suffix));
        }
        if (unclosed > 0)
            _warnings.Add($"{unclosed} yerde açılıp kapanmamış değişken ayracı var; bu yerler doldurulmaz.");
        _warnings.Add("UDF desteği deneyseldir: üretilen belgeyi UYAP editöründe açıp kontrol edin.");
    }

    // ------------------------------------------------------------------ model

    sealed class Span(XElement e, int start, int length)
    {
        public XElement E { get; } = e;
        public int Start { get; set; } = start;
        public int Length { get; set; } = length;
        public int End => Start + Length;
        public int OriginalLength { get; } = length;
    }

    sealed class Para(XElement e, int start, int end, int id)
    {
        public XElement E { get; } = e;
        public int Id { get; } = id;
        public int Start { get; } = start;
        public int End { get; } = end;
        public ParagraphPlan Plan { get; set; } = null!;
    }

    sealed class Model
    {
        public required XDocument Doc { get; init; }
        public required XElement ContentElement { get; init; }
        public required string Text { get; set; }
        public List<Span> Spans { get; } = new();
        public List<Para> Paragraphs { get; } = new();
        public Dictionary<XElement, Para> ParaMap { get; } = new();
        public Dictionary<XElement, Span> SpanMap { get; } = new();
    }

    Model Load(IValueSource values)
    {
        var doc = SafeZip.ParseXml(_package.Get(ContentEntry)!);
        var root = doc.Root ?? throw new EvrakciException("UDF belgesi boş.");
        var content = root.Element("content") ?? throw new EvrakciException("UDF belgesinde metin bölümü yok.");
        var model = new Model { Doc = doc, ContentElement = content, Text = content.Value };

        foreach (var e in root.Descendants())
        {
            if (e == content) continue;
            if (int.TryParse((string?)e.Attribute("startOffset"), out var s) &&
                int.TryParse((string?)e.Attribute("length"), out var l) && s >= 0 && l >= 0 && s + l <= model.Text.Length)
            {
                var span = new Span(e, s, l);
                model.Spans.Add(span);
                model.SpanMap[e] = span;
            }
        }

        int occurrence = 0;
        foreach (var p in root.Descendants("paragraph"))
        {
            var spans = p.Descendants().Where(model.SpanMap.ContainsKey).Select(d => model.SpanMap[d]).ToList();
            if (model.SpanMap.TryGetValue(p, out var own)) spans.Add(own);
            if (spans.Count == 0) continue;
            int start = spans.Min(x => x.Start), end = spans.Max(x => x.End);
            var para = new Para(p, start, end, model.Paragraphs.Count);
            para.Plan = Planner.Plan(model.Text[start..end], _matcher, values, ref occurrence);
            model.Paragraphs.Add(para);
            model.ParaMap[p] = para;
        }
        return model;
    }

    // ------------------------------------------------------------------ render

    public byte[] Render(IValueSource values)
    {
        var model = Load(values);
        var edits = new List<TextEdit>();
        var removed = new List<Para>();
        foreach (var p in model.Paragraphs)
        {
            if (p.Plan.Remove)
            {
                removed.Add(p);
                continue;
            }
            foreach (var e in p.Plan.Edits())
                edits.Add(e with { Start = e.Start + p.Start });
        }

        // Removed paragraphs: delete their text (including the paragraph break that ends them).
        foreach (var p in removed)
        {
            int end = p.End;
            if (end < model.Text.Length && model.Text[end] == '\n' && !(end > p.Start && model.Text[end - 1] == '\n')) end++;
            edits.Add(new TextEdit(p.Start, end - p.Start, ""));
            foreach (var d in p.E.DescendantsAndSelf()) model.SpanMap.Remove(d);
            p.E.Remove();
        }

        foreach (var edit in edits.OrderByDescending(e => e.Start))
            ApplyEdit(model, edit);
        return Save(model);
    }

    byte[] Save(Model model)
    {
        foreach (var span in model.SpanMap.Values)
        {
            if (span.E.Parent is null) continue;
            if (span.Length == 0 && span.OriginalLength > 0 && span.E.Name.LocalName == "content" &&
                span.E.Parent.Elements("content").Count() > 1)
            {
                span.E.Remove();
                continue;
            }
            span.E.SetAttributeValue("startOffset", span.Start);
            span.E.SetAttributeValue("length", span.Length);
        }
        model.ContentElement.ReplaceNodes(new XCData(model.Text));

        return _package.Write(new Dictionary<string, byte[]> { [ContentEntry] = SafeZip.SerializeXml(model.Doc) });
    }

    static void ApplyEdit(Model model, TextEdit edit)
    {
        int pos = edit.Start, oldEnd = edit.Start + edit.Length, delta = edit.Text.Length - edit.Length;
        model.Text = model.Text[..pos] + edit.Text + model.Text[oldEnd..];
        int newEnd = pos + edit.Text.Length;

        // The span containing the edit's start receives the new text; spans that started inside the
        // replaced range collapse to its end; everything after shifts by delta.
        int NewStart(int s) => s <= pos ? s : s >= oldEnd ? s + delta : newEnd;
        int NewEnd(int e) => e <= pos ? e : e >= oldEnd ? e + delta : newEnd;

        foreach (var s in model.SpanMap.Values)
        {
            int start = NewStart(s.Start), end = NewEnd(s.End);
            s.Start = start;
            s.Length = Math.Max(0, end - start);
        }
    }

    // ------------------------------------------------------------------ edit

    IReadOnlyList<SourceParagraph>? _source;

    public IReadOnlyList<SourceParagraph> SourceParagraphs => _source ??= Load(EmptyValueSource.Instance).Paragraphs
        .Select(p => new SourceParagraph(p.Id, p.Plan.Text, p.Plan.Occurrences.Select(o => o.Match).ToList()))
        .ToList();

    public byte[] Edit(IReadOnlyList<SourceEdit> edits)
    {
        var model = Load(EmptyValueSource.Instance);
        var absolute = new List<TextEdit>();
        foreach (var e in edits.Distinct())
        {
            if (e.Paragraph < 0 || e.Paragraph >= model.Paragraphs.Count) throw new EvrakciException("Şablon değişmiş; seçilen yer bulunamadı. Şablonu yeniden açın.");
            var p = model.Paragraphs[e.Paragraph];
            if (e.Start < 0 || e.Length <= 0 || p.Start + e.Start + e.Length > p.End)
                throw new EvrakciException("Seçilen metin değiştirilemedi.");
            absolute.Add(new TextEdit(p.Start + e.Start, e.Length, e.Text));
        }
        foreach (var edit in absolute.OrderByDescending(e => e.Start))
            ApplyEdit(model, edit);
        return Save(model);
    }

    // ------------------------------------------------------------------ rewrite (in-app editor)

    static readonly string[] RunAttributes = ["startOffset", "length", "bold", "italic", "underline"];

    public byte[] Rewrite(IReadOnlyList<EditParagraph> paragraphs, IReadOnlySet<int> shown)
    {
        var model = Load(EmptyValueSource.Instance);
        // Operations are applied from the end of the text backwards, so positions read from the
        // original model stay valid. At equal positions a change/delete runs before an insertion.
        var ops = new List<(int Pos, int Order, Action Run)>();
        var seen = new HashSet<int>();
        Para? last = null;
        var pendingNew = new List<EditParagraph>();

        void QueueInsert()
        {
            if (pendingNew.Count == 0) return;
            var items = pendingNew.ToList();
            pendingNew.Clear();
            var anchor = last ?? model.Paragraphs.FirstOrDefault()
                         ?? throw new EvrakciException("Boş bir UDF belgesine paragraf eklenemedi.");
            bool after = last is not null;
            int pos = after ? anchor.End : anchor.Start;
            ops.Add((pos, 1, () => Insert(model, anchor, after, pos, items)));
        }

        foreach (var ep in paragraphs)
        {
            if (ep.Id is int id && id >= 0 && id < model.Paragraphs.Count && seen.Add(id))
            {
                QueueInsert();
                var p = model.Paragraphs[id];
                if (!Same(model, p, ep)) ops.Add((p.Start, 0, () => Replace(model, p, ep)));
                last = p;
            }
            else pendingNew.Add(ep);
        }
        QueueInsert();
        foreach (var p in model.Paragraphs.Where(p => shown.Contains(p.Id) && !seen.Contains(p.Id)))
            ops.Add((p.Start, 0, () => Delete(model, p)));

        foreach (var op in ops.OrderByDescending(o => o.Pos).ThenBy(o => o.Order)) op.Run();
        return Save(model);
    }

    static string Body(Model model, Para p) => model.Text[p.Start..p.End].TrimEnd('\n');

    static PreviewAlignment AlignmentOf(XElement e) => (string?)e.Attribute("Alignment") switch
    {
        "1" => PreviewAlignment.Center,
        "2" => PreviewAlignment.Right,
        "3" => PreviewAlignment.Justify,
        _ => PreviewAlignment.Left,
    };

    static bool Same(Model model, Para p, EditParagraph ep)
    {
        if (AlignmentOf(p.E) != ep.Alignment) return false;
        var spans = p.E.Descendants().Where(model.SpanMap.ContainsKey).Select(d => model.SpanMap[d]).ToList();
        var body = Body(model, p);
        var original = new List<EditRun>();
        for (int i = 0; i < body.Length; i++)
        {
            int abs = p.Start + i;
            var s = spans.LastOrDefault(x => x.Start <= abs && abs < x.End);
            original.Add(new EditRun(body[i].ToString(), s is not null && Flag(s.E, "bold"), s is not null && Flag(s.E, "italic"),
                s is not null && Flag(s.E, "underline")));
        }
        return EditParagraph.Normalize(original).SequenceEqual(EditParagraph.Normalize(ep.Runs));
    }

    static void SetAlignment(XElement e, PreviewAlignment a) =>
        e.SetAttributeValue("Alignment", a switch { PreviewAlignment.Center => "1", PreviewAlignment.Right => "2", PreviewAlignment.Justify => "3", _ => "0" });

    /// <summary>Attributes of the paragraph's first text span other than position and bold/italic/underline (font, size …).</summary>
    static List<XAttribute> FontOf(XElement paragraph) =>
        paragraph.Descendants("content").FirstOrDefault()?.Attributes().Where(a => !RunAttributes.Contains(a.Name.LocalName))
            .Select(a => new XAttribute(a)).ToList() ?? new();

    void Replace(Model model, Para p, EditParagraph ep)
    {
        bool newline = model.Text[p.Start..p.End].EndsWith('\n');
        var font = FontOf(p.E);
        foreach (var d in p.E.Descendants().Where(model.SpanMap.ContainsKey).ToList())
        {
            model.SpanMap.Remove(d);
            d.Remove();
        }
        var text = ep.Text.Replace(VariableMatcher.Opaque.ToString(), "") + (newline ? "\n" : "");
        ApplyEdit(model, new TextEdit(p.Start, p.End - p.Start, text));
        AddRuns(model, p.E, p.Start, ep.Runs, newline, font);
        SetAlignment(p.E, ep.Alignment);
    }

    void Insert(Model model, Para anchor, bool after, int pos, List<EditParagraph> items)
    {
        var text = string.Concat(items.Select(i => i.Text.Replace(VariableMatcher.Opaque.ToString(), "") + "\n"));
        model.Text = model.Text[..pos] + text + model.Text[pos..];
        foreach (var s in model.SpanMap.Values)
            if (s.Start >= pos) s.Start += text.Length;
        var font = FontOf(anchor.E);
        XElement previous = anchor.E;
        int offset = pos;
        foreach (var (item, index) in items.Select((x, i) => (x, i)))
        {
            var e = new XElement("paragraph", anchor.E.Attributes().Where(a => a.Name.LocalName is not ("startOffset" or "length")).Select(a => new XAttribute(a)));
            AddRuns(model, e, offset, item.Runs, true, font);
            SetAlignment(e, item.Alignment);
            if (after || index > 0) previous.AddAfterSelf(e);
            else previous.AddBeforeSelf(e);
            previous = e;
            offset += item.Text.Replace(VariableMatcher.Opaque.ToString(), "").Length + 1;
        }
    }

    static void Delete(Model model, Para p)
    {
        foreach (var d in p.E.DescendantsAndSelf()) model.SpanMap.Remove(d);
        p.E.Remove();
        ApplyEdit(model, new TextEdit(p.Start, p.End - p.Start, ""));
    }

    /// <summary>Content and tab spans for the runs, written from <paramref name="start"/>; the paragraph break goes in the last span.</summary>
    static void AddRuns(Model model, XElement paragraph, int start, IReadOnlyList<EditRun> runs, bool newline, List<XAttribute> font)
    {
        int pos = start;
        XElement? lastContent = null;
        void Add(string name, int length, EditRun? style)
        {
            var e = new XElement(name, font.Select(a => new XAttribute(a)));
            if (style is { Bold: true }) e.SetAttributeValue("bold", "true");
            if (style is { Italic: true }) e.SetAttributeValue("italic", "true");
            if (style is { Underline: true }) e.SetAttributeValue("underline", "true");
            e.SetAttributeValue("startOffset", pos);
            e.SetAttributeValue("length", length);
            paragraph.Add(e);
            model.SpanMap[e] = new Span(e, pos, length);
            if (name == "content") lastContent = e;
            pos += length;
        }
        foreach (var run in EditParagraph.Normalize(runs))
        {
            var pieces = run.Text.Replace(VariableMatcher.Opaque.ToString(), "").Split('\t');
            for (int i = 0; i < pieces.Length; i++)
            {
                if (i > 0) Add("tab", 1, run);
                if (pieces[i].Length > 0) Add("content", pieces[i].Length, run);
            }
        }
        if (!newline) return;
        if (lastContent is not null && model.SpanMap[lastContent].End == pos)
        {
            var s = model.SpanMap[lastContent];
            s.Length++;
            lastContent.SetAttributeValue("length", s.Length);
        }
        else Add("content", 1, null);
    }

    // ------------------------------------------------------------------ preview

    public PreviewDocument Preview(IValueSource values)
    {
        var model = Load(values);
        var doc = new PreviewDocument { Title = FileName };
        var root = model.Doc.Root!;
        foreach (var top in root.Elements())
        {
            var name = top.Name.LocalName;
            if (name is "content" or "properties" or "styles") continue;
            var section = new PreviewSection
            {
                Title = name switch { "header" => "Üst bilgi", "footer" => "Alt bilgi", _ => null },
            };
            Walk(top, section.Blocks, model);
            if (section.Blocks.Count > 0) doc.Sections.Add(section);
        }
        return doc;
    }

    static void Walk(XElement container, List<PreviewBlock> blocks, Model model)
    {
        foreach (var child in container.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "paragraph":
                    if (model.ParaMap.TryGetValue(child, out var para)) blocks.Add(ToParagraph(para, model));
                    break;
                case "table":
                    var table = new PreviewTable();
                    foreach (var row in child.Elements("row"))
                    {
                        var cells = new List<List<PreviewBlock>>();
                        foreach (var cell in row.Elements("cell"))
                        {
                            var cellBlocks = new List<PreviewBlock>();
                            Walk(cell, cellBlocks, model);
                            cells.Add(cellBlocks);
                        }
                        table.Rows.Add(cells);
                    }
                    blocks.Add(table);
                    break;
                default:
                    Walk(child, blocks, model);
                    break;
            }
        }
    }

    static PreviewParagraph ToParagraph(Para para, Model model)
    {
        var spans = para.E.Descendants().Where(model.SpanMap.ContainsKey).Select(d => model.SpanMap[d]).ToList();
        SegmentBuilder.CharFormat FormatAt(int i)
        {
            int abs = para.Start + i;
            var s = spans.LastOrDefault(x => x.Start <= abs && abs < x.End);
            if (s is null) return default;
            return new SegmentBuilder.CharFormat(Flag(s.E, "bold"), Flag(s.E, "italic"), Flag(s.E, "underline"));
        }
        var align = (string?)para.E.Attribute("Alignment");
        var p = new PreviewParagraph
        {
            Alignment = align switch
            {
                "1" => PreviewAlignment.Center,
                "2" => PreviewAlignment.Right,
                "3" => PreviewAlignment.Justify,
                _ => PreviewAlignment.Left,
            },
            Removed = para.Plan.Remove,
            Bullet = para.E.Attribute("Bulleted")?.Value == "true" || para.E.Attribute("Numbered")?.Value == "true",
            Id = para.Id,
        };
        SegmentBuilder.Build(p, para.Plan, FormatAt);
        // Paragraph text ends with the line break; drop it from the last literal.
        if (p.Segments.Count > 0 && p.Segments[^1] is { Kind: SegmentKind.Literal } last && last.Text.EndsWith('\n'))
        {
            p.Segments[^1] = new PreviewSegment
            {
                Text = last.Text.TrimEnd('\n'), Kind = SegmentKind.Literal,
                Bold = last.Bold, Italic = last.Italic, Underline = last.Underline,
                SourceStart = last.SourceStart, SourceLength = last.Text.TrimEnd('\n').Length,
            };
        }
        return p;
    }

    static bool Flag(XElement e, string name) =>
        string.Equals((string?)e.Attribute(name), "true", StringComparison.OrdinalIgnoreCase);
}
