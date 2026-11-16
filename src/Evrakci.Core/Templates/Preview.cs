namespace Evrakci.Core.Templates;

public enum SegmentKind { Literal, Value, Suffix, Missing, Blank }

public enum PreviewAlignment { Left, Center, Right, Justify }

public sealed class PreviewSegment
{
    public required string Text { get; init; }
    public SegmentKind Kind { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public string? Variable { get; init; }
    /// <summary>Occurrence index within the document (for suffix edits).</summary>
    public int Occurrence { get; init; } = -1;
    /// <summary>For suffix segments: the template's suffix and the automatic choice.</summary>
    public string? TemplateSuffix { get; init; }
    public string? AutoSuffix { get; init; }
    public bool Overridden { get; init; }
    /// <summary>Position of the segment in the paragraph's template text (-1 when unknown).</summary>
    public int SourceStart { get; init; } = -1;
    public int SourceLength { get; init; }
}

public abstract class PreviewBlock;

public sealed class PreviewParagraph : PreviewBlock
{
    public List<PreviewSegment> Segments { get; } = new();
    public PreviewAlignment Alignment { get; init; }
    public bool Removed { get; init; }
    public bool InTextBox { get; init; }
    public bool Bullet { get; init; }
    /// <summary>Paragraph number within the template, used to edit the template (see <see cref="SourceEdit"/>).</summary>
    public int Id { get; init; } = -1;
}

public sealed class PreviewTable : PreviewBlock
{
    public List<List<List<PreviewBlock>>> Rows { get; } = new();
}

public sealed class PreviewSection
{
    public string? Title { get; init; }
    public List<PreviewBlock> Blocks { get; } = new();
}

public sealed class PreviewDocument
{
    public required string Title { get; init; }
    public List<PreviewSection> Sections { get; } = new();
}

/// <summary>Builds preview segments from a planned paragraph and per-character formatting.</summary>
public static class SegmentBuilder
{
    public readonly record struct CharFormat(bool Bold, bool Italic, bool Underline);

    public static void Build(PreviewParagraph target, ParagraphPlan plan, Func<int, CharFormat> formatAt)
    {
        var text = plan.Text;
        int pos = 0;
        foreach (var o in plan.Occurrences.OrderBy(o => o.Match.Start))
        {
            AddLiteral(target, text, pos, o.Match.Start, formatAt);
            var f = formatAt(o.Match.Start);
            switch (o.State)
            {
                case ValueState.Value:
                    target.Segments.Add(new PreviewSegment
                    {
                        Text = o.ValueText, Kind = SegmentKind.Value, Variable = o.Match.Ref.Name,
                        Occurrence = o.Occurrence, Bold = f.Bold, Italic = f.Italic, Underline = f.Underline,
                        SourceStart = o.Match.Start, SourceLength = o.Match.DelimitedLength,
                    });
                    break;
                case ValueState.Blank:
                    target.Segments.Add(new PreviewSegment
                    {
                        Text = "", Kind = SegmentKind.Blank, Variable = o.Match.Ref.Name, Occurrence = o.Occurrence,
                        SourceStart = o.Match.Start, SourceLength = o.Match.DelimitedLength,
                    });
                    break;
                default:
                    target.Segments.Add(new PreviewSegment
                    {
                        Text = "[" + o.Match.Ref.Name + "]", Kind = SegmentKind.Missing, Variable = o.Match.Ref.Name,
                        Occurrence = o.Occurrence, Bold = f.Bold, Italic = f.Italic, Underline = f.Underline,
                        SourceStart = o.Match.Start, SourceLength = o.Match.DelimitedLength,
                    });
                    break;
            }
            pos = o.Match.SuffixStart;
            if (o.Match.Suffix is not null)
            {
                if (o.State == ValueState.Value)
                {
                    var sf = formatAt(o.Match.SuffixStart);
                    target.Segments.Add(new PreviewSegment
                    {
                        Text = o.SuffixText ?? "", Kind = SegmentKind.Suffix, Variable = o.Match.Ref.Name,
                        Occurrence = o.Occurrence, TemplateSuffix = o.Match.Suffix, AutoSuffix = o.AutoSuffix,
                        Overridden = o.SuffixOverridden, Bold = sf.Bold, Italic = sf.Italic, Underline = sf.Underline,
                        SourceStart = o.Match.SuffixStart, SourceLength = o.Match.Suffix.Length + 1,
                    });
                    pos = o.Match.End;
                }
                else if (o.State == ValueState.Blank) pos = o.Match.End;
            }
        }
        AddLiteral(target, text, pos, text.Length, formatAt);
    }

    static void AddLiteral(PreviewParagraph target, string text, int from, int to, Func<int, CharFormat> formatAt)
    {
        // One segment per run of equal formatting; opaque positions (fields, symbols) split segments so
        // every segment is a contiguous piece of the source text.
        int i = from;
        while (i < to)
        {
            if (text[i] == VariableMatcher.Opaque) { i++; continue; }
            var f = formatAt(i);
            int j = i + 1;
            while (j < to && text[j] != VariableMatcher.Opaque && formatAt(j) == f) j++;
            target.Segments.Add(new PreviewSegment
            {
                Text = text[i..j], Kind = SegmentKind.Literal, Bold = f.Bold, Italic = f.Italic, Underline = f.Underline,
                SourceStart = i, SourceLength = j - i,
            });
            i = j;
        }
    }
}
