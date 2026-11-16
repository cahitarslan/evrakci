using Evrakci.Core.Text;

namespace Evrakci.Core.Templates;

public enum ValueState { Value, Blank, Missing }

public readonly record struct RenderValue(ValueState State, string Text, bool RemoveParagraphWhenBlank)
{
    public static readonly RenderValue Missing = new(ValueState.Missing, "", false);
}

/// <summary>Supplies values while a template is rendered or previewed.</summary>
public interface IValueSource
{
    RenderValue Get(VariableRef r);
    /// <summary>User-corrected suffix for an occurrence (index within the document), or null.</summary>
    string? SuffixOverride(int occurrence);
}

/// <summary>Scan-only source: every variable is missing.</summary>
public sealed class EmptyValueSource : IValueSource
{
    public static readonly EmptyValueSource Instance = new();
    public RenderValue Get(VariableRef r) => RenderValue.Missing;
    public string? SuffixOverride(int occurrence) => null;
}

/// <summary>A replacement of a character range in a paragraph's text.</summary>
public readonly record struct TextEdit(int Start, int Length, string Text);

/// <summary>What happens to one variable occurrence.</summary>
public sealed record PlannedOccurrence(
    VariableMatch Match,
    int Occurrence,
    ValueState State,
    string ValueText,
    string? SuffixText,       // apostrophe + suffix as it will be written, null when there is no suffix
    string? AutoSuffix,       // suffix the engine would choose, without apostrophe
    bool SuffixOverridden);

public sealed class ParagraphPlan
{
    public required string Text { get; init; }
    public List<PlannedOccurrence> Occurrences { get; } = new();
    public bool Remove { get; set; }
    public int Unclosed { get; set; }

    /// <summary>Edits in descending order, ready to be applied one after another.</summary>
    public IEnumerable<TextEdit> Edits()
    {
        var edits = new List<TextEdit>();
        foreach (var o in Occurrences)
        {
            if (o.State == ValueState.Missing) continue;
            edits.Add(new TextEdit(o.Match.Start, o.Match.DelimitedLength, o.ValueText));
            if (o.Match.Suffix is not null)
                edits.Add(new TextEdit(o.Match.SuffixStart, o.Match.Suffix.Length + 1, o.SuffixText ?? ""));
        }
        return edits.OrderByDescending(e => e.Start);
    }

    /// <summary>Applies the plan to a plain string (used for file names and UDF text).</summary>
    public string ApplyTo(string text)
    {
        foreach (var e in Edits())
            text = text[..e.Start] + e.Text + text[(e.Start + e.Length)..];
        return text;
    }
}

public static class Planner
{
    public static ParagraphPlan Plan(string text, VariableMatcher matcher, IValueSource values, ref int occurrence)
    {
        var plan = new ParagraphPlan { Text = text };
        var matches = matcher.Find(text);
        plan.Unclosed = matcher.CountUnclosed(text, matches);
        foreach (var m in matches)
        {
            int index = occurrence++;
            var v = values.Get(m.Ref);
            string? suffixText = null, auto = null;
            bool overridden = false;
            if (m.Suffix is not null && v.State == ValueState.Value)
            {
                auto = TurkishSuffix.Adapt(v.Text, m.Suffix);
                var ov = values.SuffixOverride(index);
                overridden = ov is not null;
                var suffix = ov ?? auto;
                suffixText = suffix.Length == 0 ? "" : m.Apostrophe + suffix;
            }
            else if (m.Suffix is not null && v.State == ValueState.Blank)
            {
                suffixText = "";
            }
            if (v.State == ValueState.Blank && v.RemoveParagraphWhenBlank) plan.Remove = true;
            plan.Occurrences.Add(new PlannedOccurrence(m, index, v.State, v.State == ValueState.Value ? v.Text : "",
                suffixText, auto, overridden));
        }
        return plan;
    }

    /// <summary>Renders variables inside a file or folder name pattern.</summary>
    public static string RenderString(string pattern, VariableMatcher matcher, IValueSource values)
    {
        int occ = 0;
        var plan = Plan(pattern, matcher, NoOverrides(values), ref occ);
        return plan.ApplyTo(pattern);
    }

    static IValueSource NoOverrides(IValueSource inner) => new NoOverrideSource(inner);

    sealed class NoOverrideSource(IValueSource inner) : IValueSource
    {
        public RenderValue Get(VariableRef r) => inner.Get(r);
        public string? SuffixOverride(int occurrence) => null;
    }
}
