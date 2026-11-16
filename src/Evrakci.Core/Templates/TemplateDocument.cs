namespace Evrakci.Core.Templates;

public enum DocumentFormat { Docx, Udf }

/// <summary>A variable occurrence found while scanning a template.</summary>
public sealed record ScannedOccurrence(VariableRef Ref, int Occurrence, string? Suffix);

/// <summary>A change to the template text: replace <c>Length</c> characters at <c>Start</c> of paragraph <c>Paragraph</c>.</summary>
public readonly record struct SourceEdit(int Paragraph, int Start, int Length, string Text);

/// <summary>Template text of one paragraph, as the editor sees it, with the ranges taken by variables.</summary>
public sealed record SourceParagraph(int Id, string Text, IReadOnlyList<VariableMatch> Variables);

/// <summary>A piece of text with character formatting, as written by the in-app editor.</summary>
public sealed record EditRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false);

/// <summary>
/// A paragraph as the in-app editor returns it. <c>Id</c> is the template paragraph it came from, or
/// null for a new paragraph (inserted after the previous paragraph of the list).
/// </summary>
public sealed record EditParagraph(int? Id, IReadOnlyList<EditRun> Runs, PreviewAlignment Alignment)
{
    public string Text => string.Concat(Runs.Select(r => r.Text));

    /// <summary>Runs with empty pieces dropped and neighbours of equal formatting merged.</summary>
    public static List<EditRun> Normalize(IEnumerable<EditRun> runs)
    {
        var list = new List<EditRun>();
        foreach (var r in runs.Where(r => r.Text.Length > 0))
        {
            if (list.Count > 0 && list[^1] is var last && last.Bold == r.Bold && last.Italic == r.Italic && last.Underline == r.Underline)
                list[^1] = last with { Text = last.Text + r.Text };
            else list.Add(r);
        }
        return list;
    }
}

/// <summary>A loaded template. Template bytes are kept in memory and never written back.</summary>
public interface ITemplateDocument
{
    string FileName { get; }
    DocumentFormat Format { get; }
    IReadOnlyList<ScannedOccurrence> Occurrences { get; }
    /// <summary>Non-fatal problems found while scanning (Turkish, no document content).</summary>
    IReadOnlyList<string> Warnings { get; }
    byte[] Render(IValueSource values);
    PreviewDocument Preview(IValueSource values);
    /// <summary>Paragraphs of the template in the order of their ids.</summary>
    IReadOnlyList<SourceParagraph> SourceParagraphs { get; }
    /// <summary>Returns a copy of the template with the edits applied. Edits may not overlap.</summary>
    byte[] Edit(IReadOnlyList<SourceEdit> edits);
    /// <summary>
    /// Returns a copy of the template rewritten from the editor: paragraphs whose text, formatting or
    /// alignment changed are rewritten, new ones inserted, and paragraphs of <paramref name="shown"/> that
    /// are missing from the list deleted. Paragraphs that did not change stay byte for byte.
    /// </summary>
    byte[] Rewrite(IReadOnlyList<EditParagraph> paragraphs, IReadOnlySet<int> shown);
}

public static class TemplateLoader
{
    public static readonly string[] Extensions = [".docx", ".udf"];

    public static bool IsTemplateFile(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("~$", StringComparison.Ordinal)) return false;   // Word lock files
        return Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }

    public static ITemplateDocument Load(string path, VariableMatcher matcher)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".docm" or ".dotm")
            throw new EvrakciException("Makro içeren belgeler (.docm) güvenlik nedeniyle kabul edilmez.");
        var package = SafeZip.Read(path);
        return ext switch
        {
            ".docx" => new Docx.DocxTemplate(Path.GetFileName(path), package, matcher),
            ".udf" => new Udf.UdfTemplate(Path.GetFileName(path), package, matcher),
            _ => throw new EvrakciException("Desteklenmeyen dosya türü. Yalnızca .docx ve .udf kullanılabilir."),
        };
    }
}
