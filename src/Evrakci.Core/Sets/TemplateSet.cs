using Evrakci.Core.Templates;
using Evrakci.Core.Text;
using Evrakci.Core.Values;

namespace Evrakci.Core.Sets;

public enum IssueSeverity { Info, Warning, Error }

public sealed record SetIssue(IssueSeverity Severity, string Message, string? Document = null);

public enum TypeSource { Template, Settings, Inferred }

/// <summary>A variable of the set as the form shows it (merged over all templates).</summary>
public sealed class FieldInfo
{
    public required string Name { get; init; }
    public required string Key { get; init; }
    public FieldType Type { get; set; }
    public TypeSource TypeSource { get; set; }
    public FieldDefinition? Definition { get; set; }
    public bool UsesWords { get; set; }
    public HashSet<string> Documents { get; } = new();
    public int Occurrences { get; set; }
    /// <summary>Referenced only by computed fields, checks or file names.</summary>
    public bool UsedInTemplates => Occurrences > 0;

    public string Label => string.IsNullOrWhiteSpace(Definition?.Label) ? Name : Definition!.Label!;
    public string Group => string.IsNullOrWhiteSpace(Definition?.Group) ? "Diğer alanlar" : Definition!.Group!;
    public bool IsComputed => !string.IsNullOrWhiteSpace(Definition?.Computed);
}

public sealed class SetDocument
{
    public required DocumentEntry Entry { get; init; }
    public required string Path { get; init; }
    public ITemplateDocument? Template { get; init; }
    public string? LoadError { get; init; }
    public string OutputPattern => string.IsNullOrWhiteSpace(Entry.Output) ? Entry.Template : Entry.Output!;
}

/// <summary>An opened template set: folder, set.json and scanned templates.</summary>
public sealed class TemplateSet
{
    public string Folder { get; }
    public SetDefinition Definition { get; }
    public bool HasSetFile { get; }
    public VariableMatcher Matcher { get; }
    public List<SetDocument> Documents { get; } = new();
    public List<FieldInfo> Fields { get; } = new();
    public List<SetIssue> Issues { get; } = new();

    readonly Dictionary<string, FieldInfo> _byKey = new();
    readonly Dictionary<string, FieldDefinition> _defsByKey = new();

    public string Name => string.IsNullOrWhiteSpace(Definition.Name) ? System.IO.Path.GetFileName(Folder) : Definition.Name!;
    public string SetFilePath => System.IO.Path.Combine(Folder, SetDefinition.FileName);
    public bool CanGenerate => !Issues.Any(i => i.Severity == IssueSeverity.Error) && Documents.Count > 0;

    public FieldInfo? Field(string keyOrName) => _byKey.GetValueOrDefault(Tr.Key(keyOrName));

    TemplateSet(string folder, SetDefinition def, bool hasFile)
    {
        Folder = folder;
        Definition = def;
        HasSetFile = hasFile;
        Matcher = new VariableMatcher(def.Delimiters.Open, def.Delimiters.Close);
    }

    public static TemplateSet Open(string folder)
    {
        folder = System.IO.Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) throw new EvrakciException("Klasör bulunamadı.");
        var file = System.IO.Path.Combine(folder, SetDefinition.FileName);
        bool has = File.Exists(file);
        var def = has ? SetDefinition.Load(file) : new SetDefinition { Name = System.IO.Path.GetFileName(folder) };
        var set = new TemplateSet(folder, def, has);
        set.Scan();
        return set;
    }

    /// <summary>Writes set.json and re-opens the set so changes (delimiters, types) take effect.</summary>
    public TemplateSet SaveAndReload()
    {
        Definition.Save(SetFilePath);
        return Open(Folder);
    }

    public FieldDefinition DefinitionFor(string name)
    {
        var key = Tr.Key(name);
        if (_defsByKey.TryGetValue(key, out var d)) return d;
        d = new FieldDefinition();
        Definition.Fields[name] = d;
        _defsByKey[key] = d;
        return d;
    }

    void Scan()
    {
        foreach (var (name, d) in Definition.Fields)
        {
            if (!_defsByKey.TryAdd(Tr.Key(name), d))
                Issues.Add(new(IssueSeverity.Warning, $"set.json içinde \"{name}\" alanı birden fazla kez tanımlanmış."));
        }

        LoadDocuments();

        // Variables in templates
        var inlineTypes = new Dictionary<string, HashSet<FieldType>>();
        foreach (var doc in Documents.Where(d => d.Template is not null))
        {
            foreach (var w in doc.Template!.Warnings)
                Issues.Add(new(w.StartsWith("UDF") ? IssueSeverity.Info : IssueSeverity.Warning, w, doc.Entry.Template));
            foreach (var o in doc.Template.Occurrences)
            {
                var f = Touch(o.Ref.Name);
                f.Occurrences++;
                f.Documents.Add(doc.Entry.Template);
                if (o.Ref.Modifier == VariableModifier.Words) f.UsesWords = true;
                if (o.Ref.InlineType is { } t)
                {
                    if (!inlineTypes.TryGetValue(f.Key, out var set)) inlineTypes[f.Key] = set = new();
                    set.Add(t);
                }
                if (o.Ref.UnknownTypeText is { } u)
                    Issues.Add(new(IssueSeverity.Warning, $"\"{o.Ref.Name}\" için bilinmeyen tip \"{u}\" yazılmış; tip yok sayıldı.", doc.Entry.Template));
            }
        }

        // Variables in file and folder names
        foreach (var pattern in Documents.Select(d => d.OutputPattern).Append(Definition.OutputFolder ?? ""))
            foreach (var m in Matcher.Find(pattern))
                Touch(m.Ref.Name);

        // Inputs of computed fields, defaultFrom sources and checks must be fields too.
        foreach (var f in Fields.ToList())
        {
            var d = f.Definition;
            if (d is null) continue;
            foreach (var r in ComputedTerms(d.Computed).Select(t => t.Name).Append(d.DefaultFrom))
                if (!string.IsNullOrWhiteSpace(r)) RequireDefined(r, f.Name);
        }
        foreach (var c in Definition.Checks)
            foreach (var r in c.Fields.Append(c.EqualsField))
                if (!string.IsNullOrWhiteSpace(r)) RequireDefined(r, "kontrol");

        // Types: template wins, then set.json, then the name.
        foreach (var f in Fields)
        {
            if (inlineTypes.TryGetValue(f.Key, out var types))
            {
                f.Type = types.First();
                f.TypeSource = TypeSource.Template;
                if (types.Count > 1)
                    Issues.Add(new(IssueSeverity.Warning,
                        $"\"{f.Name}\" şablonlarda farklı tiplerle yazılmış ({string.Join(", ", types.Select(FieldTypes.DisplayName))}); {FieldTypes.DisplayName(f.Type)} kullanılıyor."));
            }
            else if (FieldTypes.Parse(f.Definition?.Type) is { } st)
            {
                f.Type = st;
                f.TypeSource = TypeSource.Settings;
            }
            else
            {
                f.Type = f.IsComputed ? FieldType.Para : FieldTypes.Infer(f.Key, f.UsesWords);
                f.TypeSource = TypeSource.Inferred;
            }
            if (f.UsesWords && f.Type != FieldType.Para)
                Issues.Add(new(IssueSeverity.Error, $"\"{f.Name}.Yazıyla\" kullanılmış ama \"{f.Name}\" bir Para alanı değil."));
            if (f.IsComputed && f.Type != FieldType.Para)
                Issues.Add(new(IssueSeverity.Error, $"\"{f.Name}\" hesaplanan bir alan; tipi Para olmalı."));
        }
        foreach (var f in Fields.Where(f => f.IsComputed))
            foreach (var term in ComputedTerms(f.Definition!.Computed))
                if (Field(term.Name) is { } src && src.Type != FieldType.Para)
                    Issues.Add(new(IssueSeverity.Error, $"\"{f.Name}\" hesabındaki \"{src.Name}\" bir Para alanı değil."));

        // Look-alike names
        var keys = Fields.Select(f => f.Key).ToList();
        for (int i = 0; i < keys.Count; i++)
            for (int j = i + 1; j < keys.Count; j++)
                if (Tr.LooksAlike(keys[i], keys[j]))
                    Issues.Add(new(IssueSeverity.Warning,
                        $"\"{Fields[i].Name}\" ile \"{Fields[j].Name}\" çok benziyor. Aynı alan olması gerekiyorsa şablonlarda aynı şekilde yazın."));

        foreach (var (name, _) in Definition.Fields)
            if (!_byKey.ContainsKey(Tr.Key(name)))
                Issues.Add(new(IssueSeverity.Info, $"set.json'daki \"{name}\" alanı hiçbir şablonda kullanılmıyor."));

        OrderFields();
    }

    void LoadDocuments()
    {
        List<DocumentEntry> entries;
        if (Definition.Documents is { Count: > 0 } list) entries = list;
        else
        {
            entries = Directory.EnumerateFiles(Folder)
                .Where(TemplateLoader.IsTemplateFile)
                .OrderBy(p => p, StringComparer.Create(Tr.Culture, true))
                .Select(p => new DocumentEntry { Template = System.IO.Path.GetFileName(p) })
                .ToList();
        }
        if (entries.Count == 0)
            Issues.Add(new(IssueSeverity.Error, "Bu klasörde .docx ya da .udf şablonu bulunamadı."));

        foreach (var entry in entries)
        {
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Folder, entry.Template));
            if (!path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase))
            {
                Issues.Add(new(IssueSeverity.Error, "Şablon set klasörünün dışında olamaz.", entry.Template));
                continue;
            }
            try
            {
                Documents.Add(new SetDocument { Entry = entry, Path = path, Template = TemplateLoader.Load(path, Matcher) });
            }
            catch (EvrakciException ex)
            {
                Documents.Add(new SetDocument { Entry = entry, Path = path, LoadError = ex.Message });
                Issues.Add(new(IssueSeverity.Error, ex.Message, entry.Template));
            }
            catch (Exception)
            {
                const string msg = "Belge açılamadı; dosya bozuk olabilir.";
                Documents.Add(new SetDocument { Entry = entry, Path = path, LoadError = msg });
                Issues.Add(new(IssueSeverity.Error, msg, entry.Template));
            }
        }

        if (Definition.Documents is { Count: > 0 })
        {
            var listed = new HashSet<string>(Definition.Documents.Select(d => d.Template), StringComparer.OrdinalIgnoreCase);
            foreach (var p in Directory.EnumerateFiles(Folder).Where(TemplateLoader.IsTemplateFile))
                if (!listed.Contains(System.IO.Path.GetFileName(p)))
                    Issues.Add(new(IssueSeverity.Info, "Klasörde set listesinde olmayan bir şablon var; üretimde kullanılmaz.", System.IO.Path.GetFileName(p)));
        }
    }

    FieldInfo Touch(string name)
    {
        var key = Tr.Key(name);
        if (_byKey.TryGetValue(key, out var f)) return f;
        var def = _defsByKey.GetValueOrDefault(key);
        // Prefer the spelling used in set.json for display.
        var display = def is null ? Tr.CollapseSpaces(name) : Definition.Fields.First(kv => Tr.Key(kv.Key) == key).Key;
        f = new FieldInfo { Name = display, Key = key, Definition = def };
        _byKey[key] = f;
        Fields.Add(f);
        return f;
    }

    void RequireDefined(string name, string usedBy)
    {
        if (_byKey.ContainsKey(Tr.Key(name))) return;
        if (_defsByKey.ContainsKey(Tr.Key(name))) Touch(name);
        else Issues.Add(new(IssueSeverity.Error, $"\"{usedBy}\" tanımında geçen \"{name}\" alanı bulunamadı."));
    }

    void OrderFields()
    {
        var groupOrder = new List<string>(Definition.Groups ?? new());
        foreach (var f in Fields)
            if (!groupOrder.Contains(f.Group)) groupOrder.Add(f.Group);
        var defOrder = Definition.Fields.Keys.Select(Tr.Key).ToList();
        var firstSeen = Fields.Select((f, i) => (f.Key, i)).ToDictionary(x => x.Key, x => x.i);
        var ordered = Fields
            .OrderBy(f => groupOrder.IndexOf(f.Group))
            .ThenBy(f => defOrder.IndexOf(f.Key) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(f => firstSeen[f.Key])
            .ToList();
        Fields.Clear();
        Fields.AddRange(ordered);
    }

    public readonly record struct ComputedTerm(string Name, int Sign);

    /// <summary>"A + B - C" → (A,+1) (B,+1) (C,-1).</summary>
    public static List<ComputedTerm> ComputedTerms(string? expression)
    {
        var list = new List<ComputedTerm>();
        if (string.IsNullOrWhiteSpace(expression)) return list;
        int sign = 1, start = 0;
        for (int i = 0; i <= expression.Length; i++)
        {
            if (i == expression.Length || expression[i] is '+' or '-')
            {
                var name = expression[start..i].Trim();
                // A '-' inside a name ("E-POSTA") is part of the name unless surrounded by spaces.
                if (i < expression.Length && expression[i] == '-' && !(i > 0 && expression[i - 1] == ' '))
                    continue;
                if (name.Length > 0) list.Add(new ComputedTerm(name, sign));
                if (i < expression.Length) sign = expression[i] == '+' ? 1 : -1;
                start = i + 1;
            }
        }
        return list;
    }
}
