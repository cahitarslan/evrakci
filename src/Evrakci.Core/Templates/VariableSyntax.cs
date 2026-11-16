using System.Text.RegularExpressions;
using Evrakci.Core.Text;
using Evrakci.Core.Values;

namespace Evrakci.Core.Templates;

public enum VariableModifier { None, Words }

/// <summary>
/// One variable reference as written inside the delimiters:
/// "AD", "Miktar1:Para", "Miktar1.Yazıyla", "Miktar1:Para.Yazıyla".
/// </summary>
public sealed record VariableRef(string Name, string Key, FieldType? InlineType, string? UnknownTypeText, VariableModifier Modifier)
{
    public static VariableRef Parse(string inner)
    {
        var s = inner.Trim();
        var modifier = VariableModifier.None;
        int dot = s.LastIndexOf('.');
        if (dot > 0 && Tr.Key(s[(dot + 1)..]) is "YAZIYLA" or "YAZİYLA")
        {
            modifier = VariableModifier.Words;
            s = s[..dot];
        }
        FieldType? type = null;
        string? unknown = null;
        int colon = s.LastIndexOf(':');
        if (colon > 0)
        {
            var typeText = s[(colon + 1)..].Trim();
            type = FieldTypes.Parse(typeText);
            if (type is null) unknown = typeText;
            s = s[..colon];
        }
        var name = Tr.CollapseSpaces(s);
        return new VariableRef(name, Tr.Key(name), type, unknown, modifier);
    }
}

/// <summary>A variable found in a piece of text, with the apostrophe suffix right after it, if any.</summary>
public sealed record VariableMatch(int Start, int DelimitedLength, VariableRef Ref, char? Apostrophe, string? Suffix)
{
    public int SuffixStart => Start + DelimitedLength;
    public int End => Start + DelimitedLength + (Suffix is null ? 0 : Suffix.Length + 1);
}

public sealed class VariableMatcher
{
    /// <summary>Marks text positions that are not editable characters (tabs, breaks, fields).</summary>
    public const char Opaque = '\u0001';

    readonly Regex _regex;
    readonly string _open;

    public VariableMatcher(string open, string close)
    {
        if (string.IsNullOrEmpty(open) || string.IsNullOrEmpty(close))
            throw new EvrakciException("Değişken ayraçları boş olamaz.");
        _open = open;
        var o = Regex.Escape(open);
        var c = Regex.Escape(close);
        _regex = new Regex(
            $@"{o}(?<inner>(?:(?!{o})[^\u0001\t\n\r])+?){c}(?:(?<apo>['’])(?<suf>\p{{L}}+))?",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    }

    public string Open => _open;

    public List<VariableMatch> Find(string text)
    {
        var list = new List<VariableMatch>();
        foreach (Match m in _regex.Matches(text))
        {
            var inner = m.Groups["inner"].Value;
            if (inner.Trim().Length == 0) continue;
            var r = VariableRef.Parse(inner);
            var delimited = m.Groups["apo"].Success ? m.Groups["apo"].Index - m.Index : m.Length;
            list.Add(new VariableMatch(m.Index, delimited, r,
                m.Groups["apo"].Success ? m.Groups["apo"].Value[0] : null,
                m.Groups["suf"].Success ? m.Groups["suf"].Value : null));
        }
        return list;
    }

    /// <summary>Number of opening delimiters not consumed by a complete variable.</summary>
    public int CountUnclosed(string text, List<VariableMatch> matches)
    {
        int total = 0, idx = 0;
        while ((idx = text.IndexOf(_open, idx, StringComparison.Ordinal)) >= 0)
        {
            total++;
            idx += _open.Length;
        }
        return Math.Max(0, total - matches.Count);
    }
}
