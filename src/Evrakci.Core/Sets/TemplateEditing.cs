using Evrakci.Core.Templates;
using Evrakci.Core.Text;

namespace Evrakci.Core.Sets;

/// <summary>A piece of template text: <c>Length</c> characters at <c>Start</c> of a paragraph.</summary>
public sealed record TextLocation(SetDocument Document, int Paragraph, int Start, int Length);

/// <summary>A file as it was before an edit, so the edit can be undone.</summary>
public sealed record FileChange(string Path, byte[]? Before);

/// <summary>
/// In-app changes to the templates of a set: turning text into a variable, replacing text, renaming
/// and removing variables. Every method writes the changed files and returns their previous contents.
/// </summary>
public static class TemplateEditing
{
    static readonly char[] Forbidden = [':', '.', '\t', '\n', '\r', '{', '}', '[', ']', '<', '>'];

    /// <summary>Null when the name can be used as a variable name, otherwise the reason in Turkish.</summary>
    public static string? CheckName(TemplateSet set, string name)
    {
        var n = Tr.CollapseSpaces(name ?? "");
        if (n.Length == 0) return "Değişken adı boş olamaz.";
        if (n.Length > 80) return "Değişken adı çok uzun.";
        if (n.IndexOfAny(Forbidden) >= 0) return "Değişken adında : . { } [ ] < > karakterleri kullanılamaz.";
        if (n.Contains(set.Definition.Delimiters.Open) || n.Contains(set.Definition.Delimiters.Close))
            return "Değişken adı ayraç içeremez.";
        if (n.Any(char.IsControl)) return "Değişken adında geçersiz karakter var.";
        return null;
    }

    public static string Token(TemplateSet set, string name) =>
        set.Definition.Delimiters.Open + Tr.CollapseSpaces(name) + set.Definition.Delimiters.Close;

    /// <summary>Places where <paramref name="text"/> appears as plain text (outside variables, on word boundaries).</summary>
    public static List<TextLocation> FindText(TemplateSet set, string text, SetDocument? only = null)
    {
        var list = new List<TextLocation>();
        if (string.IsNullOrEmpty(text)) return list;
        foreach (var doc in Docs(set, only))
            foreach (var p in doc.Template!.SourceParagraphs)
            {
                int i = 0;
                while ((i = p.Text.IndexOf(text, i, StringComparison.Ordinal)) >= 0)
                {
                    int end = i + text.Length;
                    bool overlaps = p.Variables.Any(v => i < v.End && v.Start < end);
                    bool boundary = (!char.IsLetterOrDigit(text[0]) || i == 0 || !char.IsLetterOrDigit(p.Text[i - 1])) &&
                                    (!char.IsLetterOrDigit(text[^1]) || end == p.Text.Length || !char.IsLetterOrDigit(p.Text[end]));
                    if (!overlaps && boundary) list.Add(new TextLocation(doc, p.Id, i, text.Length));
                    i = overlaps || !boundary ? i + 1 : end;
                }
            }
        return list;
    }

    /// <summary>Every occurrence of a variable (the delimited token, without its suffix).</summary>
    public static List<(TextLocation Location, VariableMatch Match)> FindVariable(TemplateSet set, string name, SetDocument? only = null)
    {
        var key = Tr.Key(name);
        var list = new List<(TextLocation, VariableMatch)>();
        foreach (var doc in Docs(set, only))
            foreach (var p in doc.Template!.SourceParagraphs)
                foreach (var m in p.Variables.Where(v => v.Ref.Key == key))
                    list.Add((new TextLocation(doc, p.Id, m.Start, m.DelimitedLength), m));
        return list;
    }

    /// <summary>The text of a location, read from the template.</summary>
    public static string TextAt(TextLocation l)
    {
        var p = l.Document.Template!.SourceParagraphs.FirstOrDefault(x => x.Id == l.Paragraph);
        if (p is null || l.Start < 0 || l.Start + l.Length > p.Text.Length) return "";
        return p.Text.Substring(l.Start, l.Length);
    }

    /// <summary>Replaces each location with <paramref name="text"/> and writes the templates.</summary>
    public static List<FileChange> Replace(IEnumerable<TextLocation> locations, string text) =>
        Apply(locations.Select(l => (l, text)));

    /// <summary>
    /// Turns plain text into a variable and records its settings in set.json. Returns the changed files,
    /// set.json included.
    /// </summary>
    public static List<FileChange> MakeVariable(TemplateSet set, IEnumerable<TextLocation> locations, string name,
        FieldDefinition? settings)
    {
        if (CheckName(set, name) is { } err) throw new EvrakciException(err);
        var changes = Replace(locations, Token(set, name));
        if (settings is not null) changes.Add(UpdateDefinition(set, name, settings));
        return changes;
    }

    /// <summary>
    /// Renames a variable everywhere in the set — templates, output names and set.json (field, computed
    /// sums, defaultFrom and checks). Inline types and ".Yazıyla" are kept.
    /// </summary>
    public static List<FileChange> RenameVariable(TemplateSet set, string oldName, string newName)
    {
        if (CheckName(set, newName) is { } err) throw new EvrakciException(err);
        newName = Tr.CollapseSpaces(newName);
        var oldKey = Tr.Key(oldName);
        if (oldKey == Tr.Key(newName) && oldName == newName) return new();
        if (oldKey != Tr.Key(newName) && set.Field(newName) is not null)
            throw new EvrakciException($"“{newName}” adında bir değişken zaten var. Başka bir ad seçin.");

        var open = set.Definition.Delimiters.Open;
        var close = set.Definition.Delimiters.Close;
        string Rewrite(VariableMatch m) =>
            open + newName + (m.Ref.InlineType is { } t ? ":" + Values.FieldTypes.DisplayName(t) : "") +
            (m.Ref.Modifier == VariableModifier.Words ? ".Yazıyla" : "") + close;

        var changes = Apply(FindVariable(set, oldName).Select(x => (x.Location, Rewrite(x.Match))));

        var def = set.Definition;
        var before = File.Exists(set.SetFilePath) ? File.ReadAllBytes(set.SetFilePath) : null;
        string Fix(string s) => string.Join("", RewriteTokens(set, s, oldKey, newName));
        foreach (var d in def.Documents ?? new())
            if (d.Output is not null) d.Output = Fix(d.Output);
        if (def.OutputFolder is not null) def.OutputFolder = Fix(def.OutputFolder);
        var existing = def.Fields.FirstOrDefault(kv => Tr.Key(kv.Key) == oldKey);
        if (existing.Key is not null)
        {
            var rebuilt = new Dictionary<string, FieldDefinition>();
            foreach (var (k, v) in def.Fields) rebuilt[k == existing.Key ? newName : k] = v;
            def.Fields = rebuilt;
        }
        foreach (var f in def.Fields.Values)
        {
            if (f.DefaultFrom is not null && Tr.Key(f.DefaultFrom) == oldKey) f.DefaultFrom = newName;
            if (f.Computed is not null)
                f.Computed = string.Join(" ", TemplateSet.ComputedTerms(f.Computed)
                    .Select((t, i) => (i == 0 ? (t.Sign < 0 ? "- " : "") : t.Sign < 0 ? "- " : "+ ") + (Tr.Key(t.Name) == oldKey ? newName : t.Name)));
        }
        foreach (var c in def.Checks)
        {
            c.Fields = c.Fields.Select(x => Tr.Key(x) == oldKey ? newName : x).ToList();
            if (c.EqualsField is not null && Tr.Key(c.EqualsField) == oldKey) c.EqualsField = newName;
        }
        SaveDefinition(set);
        changes.Add(new FileChange(set.SetFilePath, before));
        return changes;
    }

    /// <summary>Writes field settings for <paramref name="name"/> into set.json (merging with what is there).</summary>
    public static FileChange UpdateDefinition(TemplateSet set, string name, FieldDefinition settings)
    {
        var before = File.Exists(set.SetFilePath) ? File.ReadAllBytes(set.SetFilePath) : null;
        var d = set.DefinitionFor(Tr.CollapseSpaces(name));
        if (!string.IsNullOrWhiteSpace(settings.Type)) d.Type = settings.Type;
        if (!string.IsNullOrWhiteSpace(settings.Label)) d.Label = settings.Label;
        if (!string.IsNullOrWhiteSpace(settings.Group)) d.Group = settings.Group;
        if (!string.IsNullOrWhiteSpace(settings.Hint)) d.Hint = settings.Hint;
        if (settings.RemoveParagraphIfBlank) d.RemoveParagraphIfBlank = true;
        if (!string.IsNullOrWhiteSpace(d.Group) && !(set.Definition.Groups ?? new()).Contains(d.Group!))
            (set.Definition.Groups ??= new()).Add(d.Group!);
        SaveDefinition(set);
        return new FileChange(set.SetFilePath, before);
    }

    /// <summary>Restores files to their previous contents (undo).</summary>
    public static void Restore(IEnumerable<FileChange> changes)
    {
        // Oldest content wins when a file was changed more than once.
        foreach (var c in changes.Reverse())
        {
            if (c.Before is null) TryDelete(c.Path);
            else WriteFile(c.Path, c.Before);
        }
    }

    // ------------------------------------------------------------------ helpers

    static IEnumerable<SetDocument> Docs(TemplateSet set, SetDocument? only) =>
        (only is null ? set.Documents : [only]).Where(d => d.Template is not null);

    static IEnumerable<string> RewriteTokens(TemplateSet set, string s, string oldKey, string newName)
    {
        int pos = 0;
        foreach (var m in set.Matcher.Find(s))
        {
            yield return s[pos..m.Start];
            yield return m.Ref.Key == oldKey
                ? set.Definition.Delimiters.Open + newName + set.Definition.Delimiters.Close
                : s.Substring(m.Start, m.DelimitedLength);
            pos = m.Start + m.DelimitedLength;
        }
        yield return s[pos..];
    }

    static List<FileChange> Apply(IEnumerable<(TextLocation Location, string Text)> edits)
    {
        var changes = new List<FileChange>();
        foreach (var group in edits.GroupBy(e => e.Location.Document))
        {
            var doc = group.Key;
            var list = group.Select(e => new SourceEdit(e.Location.Paragraph, e.Location.Start, e.Location.Length, e.Text))
                .Distinct().ToList();
            CheckOverlap(list);
            var bytes = doc.Template!.Edit(list);
            var before = File.ReadAllBytes(doc.Path);
            WriteFile(doc.Path, bytes);
            changes.Add(new FileChange(doc.Path, before));
        }
        return changes;
    }

    static void CheckOverlap(List<SourceEdit> list)
    {
        foreach (var g in list.GroupBy(e => e.Paragraph))
        {
            int lastEnd = -1;
            foreach (var e in g.OrderBy(e => e.Start))
            {
                if (e.Start < lastEnd) throw new EvrakciException("Değiştirilecek yerler iç içe geçiyor.");
                lastEnd = e.Start + e.Length;
            }
        }
    }

    static void SaveDefinition(TemplateSet set)
    {
        try { set.Definition.Save(set.SetFilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new EvrakciException("set.json kaydedilemedi. Klasöre yazma izniniz olmayabilir.");
        }
    }

    public static void WriteFile(string path, byte[] bytes)
    {
        var tmp = path + ".evrakci-tmp";
        try
        {
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tmp);
            throw new EvrakciException($"“{Path.GetFileName(path)}” kaydedilemedi. Dosya Word'de açık olabilir; kapatıp tekrar deneyin.");
        }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
