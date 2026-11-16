using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Evrakci.Core.Templates.Docx;

/// <summary>
/// Word (.docx) template. Variables are searched on the concatenated text of each paragraph's own
/// &lt;w:t&gt; nodes, so values split across runs by Word are found; replacement text takes the
/// formatting of the run in which the variable starts. Body, tables, text boxes (DrawingML and VML),
/// headers, footers, footnotes and endnotes are processed.
/// </summary>
public sealed partial class DocxTemplate : ITemplateDocument
{
    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly XNamespace MC = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    static readonly XName WP = W + "p", WT = W + "t", WR = W + "r", WRPr = W + "rPr", WPPr = W + "pPr";

    readonly ZipPackage _package;
    readonly VariableMatcher _matcher;
    readonly List<string> _parts;
    readonly List<ScannedOccurrence> _occurrences = new();
    readonly List<string> _warnings = new();

    public string FileName { get; }
    public DocumentFormat Format => DocumentFormat.Docx;
    public IReadOnlyList<ScannedOccurrence> Occurrences => _occurrences;
    public IReadOnlyList<string> Warnings => _warnings;

    [GeneratedRegex(@"^word/(document|header\d*|footer\d*|footnotes|endnotes)\.xml$", RegexOptions.IgnoreCase)]
    private static partial Regex TextPart();

    public DocxTemplate(string fileName, ZipPackage package, VariableMatcher matcher)
    {
        FileName = fileName;
        _package = package;
        _matcher = matcher;

        var contentTypes = package.Get("[Content_Types].xml");
        if (contentTypes is null || !package.Has("word/document.xml"))
            throw new EvrakciException("Geçerli bir Word belgesi değil.");
        var ct = Encoding.UTF8.GetString(contentTypes);
        if (ct.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) ||
            package.Entries.Any(e => e.Name.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)))
            throw new EvrakciException("Makro içeren belgeler güvenlik nedeniyle kabul edilmez.");

        _parts = package.Entries.Select(e => e.Name).Where(n => TextPart().IsMatch(n))
            .OrderBy(PartOrder).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        int unclosed = 0;
        foreach (var part in Process(EmptyValueSource.Instance))
            foreach (var info in part.Paragraphs)
            {
                unclosed += info.Plan.Unclosed;
                foreach (var o in info.Plan.Occurrences)
                    _occurrences.Add(new ScannedOccurrence(o.Match.Ref, o.Occurrence, o.Match.Suffix));
            }
        if (unclosed > 0)
            _warnings.Add($"{unclosed} yerde açılıp kapanmamış değişken ayracı var; bu yerler doldurulmaz.");
    }

    static int PartOrder(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.EndsWith("document.xml")) return 0;
        if (n.Contains("header")) return 1;
        if (n.Contains("footer")) return 2;
        if (n.Contains("footnotes")) return 3;
        return 4;
    }

    // ------------------------------------------------------------------ model

    sealed class ParaInfo(XElement p, int id)
    {
        public XElement P { get; } = p;
        public int Id { get; } = id;
        public List<XElement> Nodes { get; } = new();
        public ParagraphPlan Plan { get; set; } = null!;
        public int[] Offsets { get; private set; } = [];

        public string BuildText()
        {
            var sb = new StringBuilder();
            Offsets = new int[Nodes.Count];
            for (int i = 0; i < Nodes.Count; i++)
            {
                Offsets[i] = sb.Length;
                var n = Nodes[i];
                if (n.Name == WT) sb.Append(n.Value);
                else if (n.Name == W + "tab") sb.Append('\t');
                else if (n.Name == W + "br" || n.Name == W + "cr") sb.Append('\n');
                else sb.Append(VariableMatcher.Opaque);
            }
            return sb.ToString();
        }

        int NodeAt(int pos)
        {
            for (int i = Nodes.Count - 1; i >= 0; i--)
                if (Offsets[i] <= pos) return i;
            return 0;
        }

        public SegmentBuilder.CharFormat FormatAt(int pos)
        {
            if (Nodes.Count == 0) return default;
            var rPr = Nodes[NodeAt(pos)].Parent?.Element(WRPr);
            return new SegmentBuilder.CharFormat(On(rPr, "b"), On(rPr, "i"), Underlined(rPr));
        }

        static bool On(XElement? rPr, string name)
        {
            var e = rPr?.Element(W + name);
            if (e is null) return false;
            var v = (string?)e.Attribute(W + "val");
            return v is null || !(v is "0" or "false" or "off");
        }

        static bool Underlined(XElement? rPr)
        {
            var e = rPr?.Element(W + "u");
            return e is not null && (string?)e.Attribute(W + "val") != "none";
        }

        /// <summary>False when the range does not start and end inside text nodes (tab, break, field).</summary>
        public bool Apply(TextEdit edit)
        {
            if (Nodes.Count == 0 || edit.Length <= 0) return false;
            int si = NodeAt(edit.Start), ei = NodeAt(edit.Start + edit.Length - 1);
            if (Nodes[si].Name != WT || Nodes[ei].Name != WT) return false;
            int so = edit.Start - Offsets[si], eo = edit.Start + edit.Length - Offsets[ei];
            for (int k = si; k <= ei; k++)
            {
                var n = Nodes[k];
                if (n.Name != WT) continue;
                var s = n.Value;
                string updated;
                if (k == si && k == ei) updated = s[..so] + edit.Text + s[eo..];
                else if (k == si) updated = s[..so] + edit.Text;
                else if (k == ei) updated = s[eo..];
                else updated = "";
                n.Value = updated;
                n.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            }
            return true;
        }
    }

    sealed record PartModel(string Name, XDocument Doc, List<ParaInfo> Paragraphs, Dictionary<XElement, ParaInfo> Map);

    List<PartModel> Process(IValueSource values)
    {
        var result = new List<PartModel>();
        int occurrence = 0, id = 0;
        foreach (var name in _parts)
        {
            var doc = SafeZip.ParseXml(_package.Get(name)!);
            var list = new List<ParaInfo>();
            if (doc.Root is not null) Visit(doc.Root, null, list, values, ref occurrence, ref id);
            result.Add(new PartModel(name, doc, list, list.ToDictionary(p => p.P)));
        }
        return result;
    }

    void Visit(XElement e, ParaInfo? current, List<ParaInfo> list, IValueSource values, ref int occurrence, ref int id)
    {
        var name = e.Name;
        if (name == WP)
        {
            var info = new ParaInfo(e, id++);
            foreach (var child in e.Elements()) Visit(child, info, list, values, ref occurrence, ref id);
            info.Plan = Planner.Plan(info.BuildText(), _matcher, values, ref occurrence);
            list.Add(info);
            return;
        }
        if (name == WT || name == W + "tab" || name == W + "br" || name == W + "cr" ||
            name == W + "noBreakHyphen" || name == W + "softHyphen" || name == W + "sym" || name == W + "ptab")
        {
            current?.Nodes.Add(e);
            return;
        }
        if (name == W + "delText" || name == W + "instrText") return;
        if (name == MC + "AlternateContent")
        {
            // Choice and Fallback hold the same content; give the Fallback copy the same occurrence
            // numbers so a suffix correction applies to both.
            // Paragraph ids are shared the same way, so a template edit reaches both copies.
            int start = occurrence, choiceEnd = occurrence, startId = id, choiceEndId = id;
            foreach (var branch in e.Elements())
            {
                if (branch.Name == MC + "Fallback") { occurrence = start; id = startId; }
                foreach (var child in branch.Elements()) Visit(child, current, list, values, ref occurrence, ref id);
                if (branch.Name != MC + "Fallback") { choiceEnd = Math.Max(choiceEnd, occurrence); choiceEndId = Math.Max(choiceEndId, id); }
            }
            occurrence = Math.Max(occurrence, choiceEnd);
            id = Math.Max(id, choiceEndId);
            return;
        }
        foreach (var child in e.Elements()) Visit(child, current, list, values, ref occurrence, ref id);
    }

    // ------------------------------------------------------------------ render

    public byte[] Render(IValueSource values)
    {
        var replaced = new Dictionary<string, byte[]>();
        foreach (var part in Process(values))
        {
            bool changed = false;
            foreach (var info in part.Paragraphs)
                foreach (var edit in info.Plan.Edits())
                    changed |= info.Apply(edit);
            foreach (var info in part.Paragraphs.Where(i => i.Plan.Remove))
            {
                RemoveParagraph(info.P);
                changed = true;
            }
            if (changed) replaced[part.Name] = SafeZip.SerializeXml(part.Doc);
        }
        return _package.Write(replaced);
    }

    static void RemoveParagraph(XElement p)
    {
        if (p.Parent is null) return;   // already removed with an outer paragraph
        var parent = p.Parent;
        bool keepsSection = p.Element(WPPr)?.Element(W + "sectPr") is not null;
        bool onlyParagraph = parent.Name != W + "body" && parent.Elements(WP).Count() <= 1;
        bool lastInCell = parent.Name == W + "tc" && parent.Elements().LastOrDefault() == p;
        if (keepsSection || onlyParagraph || lastInCell)
        {
            foreach (var child in p.Elements().Where(c => c.Name != WPPr).ToList()) child.Remove();
            return;
        }
        p.Remove();
    }

    // ------------------------------------------------------------------ edit

    IReadOnlyList<SourceParagraph>? _source;

    public IReadOnlyList<SourceParagraph> SourceParagraphs => _source ??= Process(EmptyValueSource.Instance)
        .SelectMany(p => p.Paragraphs)
        .DistinctBy(i => i.Id)
        .Select(i => new SourceParagraph(i.Id, i.Plan.Text, i.Plan.Occurrences.Select(o => o.Match).ToList()))
        .OrderBy(p => p.Id)
        .ToList();

    public byte[] Edit(IReadOnlyList<SourceEdit> edits)
    {
        var byPara = edits.GroupBy(e => e.Paragraph).ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Start).ToList());
        var replaced = new Dictionary<string, byte[]>();
        var applied = new HashSet<SourceEdit>();
        foreach (var part in Process(EmptyValueSource.Instance))
        {
            bool changed = false;
            foreach (var info in part.Paragraphs)
            {
                if (!byPara.TryGetValue(info.Id, out var list)) continue;
                foreach (var e in list)
                {
                    if (e.Start < 0 || e.Start + e.Length > info.Plan.Text.Length || !info.Apply(new TextEdit(e.Start, e.Length, e.Text)))
                        throw new EvrakciException("Seçilen metin değiştirilemedi. Seçim sekme, satır sonu ya da alan kodu ile başlayıp bitmemeli.");
                    applied.Add(e);
                    changed = true;
                }
            }
            if (changed) replaced[part.Name] = SafeZip.SerializeXml(part.Doc);
        }
        if (applied.Count != edits.Distinct().Count()) throw new EvrakciException("Şablon değişmiş; seçilen yer bulunamadı. Şablonu yeniden açın.");
        return _package.Write(replaced);
    }

    // ------------------------------------------------------------------ rewrite (in-app editor)

    static readonly HashSet<XName> SimpleRunChildren =
        [WRPr, WT, W + "tab", W + "br", W + "cr", W + "noBreakHyphen", W + "softHyphen", W + "lastRenderedPageBreak"];

    public byte[] Rewrite(IReadOnlyList<EditParagraph> paragraphs, IReadOnlySet<int> shown)
    {
        var parts = Process(EmptyValueSource.Instance);
        // AlternateContent copies share an id; every copy is kept in step.
        var byId = parts.SelectMany(part => part.Paragraphs.Select(info => (Part: part, Info: info)))
            .GroupBy(x => x.Info.Id).ToDictionary(g => g.Key, g => g.ToList());
        var changed = new HashSet<PartModel>();
        var seen = new HashSet<int>();
        (PartModel Part, XElement P)? last = null;

        foreach (var ep in paragraphs)
        {
            if (ep.Id is int id && byId.TryGetValue(id, out var copies) && seen.Add(id))
            {
                foreach (var (part, info) in copies)
                {
                    if (Same(info, ep)) continue;
                    WriteRuns(info.P, ep.Runs);
                    SetAlignment(info.P, ep.Alignment);
                    changed.Add(part);
                }
                last = (copies[0].Part, copies[0].Info.P);
                continue;
            }
            // New paragraph: formatted like its neighbour, placed after the previous one.
            var anchor = last ?? byId.Values.Select(v => (v[0].Part, v[0].Info.P)).FirstOrDefault();
            if (anchor.P is null) throw new EvrakciException("Boş bir belgeye paragraf eklenemedi. Belgeyi Word'de bir kez açıp kaydedin.");
            var np = new XElement(WP);
            if (anchor.P.Element(WPPr) is { } pPr)
            {
                var copy = new XElement(pPr);
                copy.Element(W + "sectPr")?.Remove();
                np.Add(copy);
            }
            var baseRPr = anchor.P.Elements(WR).Select(r => r.Element(WRPr)).FirstOrDefault(r => r is not null);
            WriteRuns(np, ep.Runs, baseRPr);
            SetAlignment(np, ep.Alignment);
            if (last is null) anchor.P.AddBeforeSelf(np);
            else anchor.P.AddAfterSelf(np);
            last = (anchor.Part, np);
            changed.Add(anchor.Part);
        }

        foreach (var id in shown.Where(id => !seen.Contains(id) && byId.ContainsKey(id)))
            foreach (var (part, info) in byId[id])
            {
                RemoveParagraph(info.P);
                changed.Add(part);
            }

        return _package.Write(changed.ToDictionary(p => p.Name, p => SafeZip.SerializeXml(p.Doc)));
    }

    static PreviewAlignment AlignmentOf(XElement p) =>
        (string?)p.Element(WPPr)?.Element(W + "jc")?.Attribute(W + "val") switch
        {
            "center" => PreviewAlignment.Center,
            "right" or "end" => PreviewAlignment.Right,
            "both" or "distribute" => PreviewAlignment.Justify,
            _ => PreviewAlignment.Left,
        };

    static bool Same(ParaInfo info, EditParagraph ep)
    {
        if (AlignmentOf(info.P) != ep.Alignment) return false;
        var text = info.Plan.Text;
        var original = new List<EditRun>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == VariableMatcher.Opaque) continue;
            var f = info.FormatAt(i);
            original.Add(new EditRun(text[i].ToString(), f.Bold, f.Italic, f.Underline));
        }
        return EditParagraph.Normalize(original).SequenceEqual(EditParagraph.Normalize(ep.Runs));
    }

    static void SetAlignment(XElement p, PreviewAlignment alignment)
    {
        if (AlignmentOf(p) == alignment) return;
        var pPr = p.Element(WPPr);
        if (pPr is null) p.AddFirst(pPr = new XElement(WPPr));
        pPr.Element(W + "jc")?.Remove();
        if (alignment == PreviewAlignment.Left) return;
        var jc = new XElement(W + "jc", new XAttribute(W + "val", alignment switch
        {
            PreviewAlignment.Center => "center",
            PreviewAlignment.Right => "right",
            _ => "both",
        }));
        // jc comes after most paragraph properties; before rPr / sectPr / pPrChange when present.
        var before = pPr.Elements().FirstOrDefault(e => e.Name == W + "rPr" || e.Name == W + "sectPr" || e.Name == W + "pPrChange");
        if (before is null) pPr.Add(jc);
        else before.AddBeforeSelf(jc);
    }

    /// <summary>
    /// Replaces the paragraph's plain text runs with new ones. Runs holding anything else (pictures,
    /// fields, text boxes) are kept. New runs take the font of the first old run.
    /// </summary>
    static void WriteRuns(XElement p, IReadOnlyList<EditRun> runs, XElement? baseRPr = null)
    {
        var simple = p.Elements(WR).Where(r => r.Elements().All(c => SimpleRunChildren.Contains(c.Name))).ToList();
        baseRPr ??= simple.Select(r => r.Element(WRPr)).FirstOrDefault(r => r is not null);
        XNode? before = simple.FirstOrDefault()?.PreviousNode;
        bool atStart = simple.Count > 0 && before is null;
        foreach (var r in simple) r.Remove();
        foreach (var e in p.Elements(W + "proofErr").ToList()) e.Remove();

        var created = new List<XElement>();
        foreach (var run in EditParagraph.Normalize(runs))
        {
            var r = new XElement(WR);
            var rPr = baseRPr is null ? new XElement(WRPr) : new XElement(baseRPr);
            foreach (var n in new[] { "b", "bCs", "i", "iCs", "u" }) rPr.Element(W + n)?.Remove();
            if (run.Bold) rPr.AddFirst(new XElement(W + "b"));
            if (run.Italic) rPr.Add(new XElement(W + "i"));
            if (run.Underline) rPr.Add(new XElement(W + "u", new XAttribute(W + "val", "single")));
            if (rPr.HasElements) r.Add(rPr);
            var sb = new StringBuilder();
            void Flush()
            {
                if (sb.Length == 0) return;
                r.Add(new XElement(WT, new XAttribute(XNamespace.Xml + "space", "preserve"), sb.ToString()));
                sb.Clear();
            }
            foreach (var ch in run.Text)
            {
                if (ch == '\t') { Flush(); r.Add(new XElement(W + "tab")); }
                else if (ch == '\n') { Flush(); r.Add(new XElement(W + "br")); }
                else if (ch != VariableMatcher.Opaque) sb.Append(ch);
            }
            Flush();
            created.Add(r);
        }

        if (before is not null && before.Parent == p) before.AddAfterSelf(created);
        else if (atStart) p.AddFirst(created);
        else if (p.Element(WPPr) is { } pPr) pPr.AddAfterSelf(created);
        else p.AddFirst(created);
        // pPr must stay first.
        if (p.Element(WPPr) is { } first && p.FirstNode != first)
        {
            first.Remove();
            p.AddFirst(first);
        }
    }

    // ------------------------------------------------------------------ preview

    public PreviewDocument Preview(IValueSource values)
    {
        var doc = new PreviewDocument { Title = FileName };
        foreach (var part in Process(values))
        {
            var root = part.Doc.Root;
            if (root is null) continue;
            var lower = part.Name.ToLowerInvariant();
            string? title = lower.Contains("header") ? "Üst bilgi" : lower.Contains("footer") ? "Alt bilgi"
                : lower.Contains("footnotes") ? "Dipnotlar" : lower.Contains("endnotes") ? "Son notlar" : null;
            var section = new PreviewSection { Title = title };
            if (lower.EndsWith("document.xml"))
            {
                var body = root.Element(W + "body");
                if (body is not null) WalkBlocks(body, section.Blocks, part.Map, false);
            }
            else if (lower.Contains("notes"))
            {
                foreach (var note in root.Elements())
                {
                    var type = (string?)note.Attribute(W + "type");
                    if (type is "separator" or "continuationSeparator" or "continuationNotice") continue;
                    WalkBlocks(note, section.Blocks, part.Map, false);
                }
            }
            else WalkBlocks(root, section.Blocks, part.Map, false);

            if (title is null || HasText(section.Blocks)) doc.Sections.Add(section);
        }
        return doc;
    }

    static bool HasText(List<PreviewBlock> blocks) => blocks.Any(b => b switch
    {
        PreviewParagraph p => p.Segments.Any(s => s.Text.Trim().Length > 0 || s.Kind != SegmentKind.Literal),
        PreviewTable t => t.Rows.Any(r => r.Any(HasText)),
        _ => false,
    });

    void WalkBlocks(XElement container, List<PreviewBlock> blocks, Dictionary<XElement, ParaInfo> map, bool inTextBox)
    {
        foreach (var child in container.Elements())
        {
            if (child.Name == WP)
            {
                if (map.TryGetValue(child, out var info)) blocks.Add(ToParagraph(info, inTextBox));
                foreach (var box in child.Descendants(W + "txbxContent"))
                {
                    if (box.Ancestors(MC + "Fallback").Any()) continue;
                    if (box.Ancestors(WP).FirstOrDefault() != child) continue;
                    WalkBlocks(box, blocks, map, true);
                }
            }
            else if (child.Name == W + "tbl")
            {
                var table = new PreviewTable();
                foreach (var tr in child.Elements(W + "tr"))
                {
                    var row = new List<List<PreviewBlock>>();
                    foreach (var tc in tr.Elements(W + "tc"))
                    {
                        var cell = new List<PreviewBlock>();
                        WalkBlocks(tc, cell, map, inTextBox);
                        row.Add(cell);
                    }
                    table.Rows.Add(row);
                }
                blocks.Add(table);
            }
            else if (child.Name == W + "sdt") WalkBlocks(child.Element(W + "sdtContent") ?? child, blocks, map, inTextBox);
            else if (child.Name == W + "customXml" || child.Name == W + "sdtContent") WalkBlocks(child, blocks, map, inTextBox);
        }
    }

    static PreviewParagraph ToParagraph(ParaInfo info, bool inTextBox)
    {
        var pPr = info.P.Element(WPPr);
        var jc = (string?)pPr?.Element(W + "jc")?.Attribute(W + "val");
        var p = new PreviewParagraph
        {
            Alignment = jc switch
            {
                "center" => PreviewAlignment.Center,
                "right" or "end" => PreviewAlignment.Right,
                "both" or "distribute" => PreviewAlignment.Justify,
                _ => PreviewAlignment.Left,
            },
            Removed = info.Plan.Remove,
            InTextBox = inTextBox,
            Bullet = pPr?.Element(W + "numPr") is not null,
            Id = info.Id,
        };
        SegmentBuilder.Build(p, info.Plan, info.FormatAt);
        return p;
    }
}
