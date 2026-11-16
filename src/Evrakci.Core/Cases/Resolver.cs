using Evrakci.Core.Sets;
using Evrakci.Core.Templates;
using Evrakci.Core.Text;
using Evrakci.Core.Values;

namespace Evrakci.Core.Cases;

/// <summary>What the user entered for one field.</summary>
public sealed class FieldInput
{
    public string Raw { get; set; } = "";
    public bool Blank { get; set; }
    /// <summary>Para only; null means the field's configured currency.</summary>
    public string? Currency { get; set; }
}

public sealed class ResolvedField
{
    public required FieldInfo Field { get; init; }
    public FieldValue? Value { get; set; }
    public bool Blank { get; set; }
    public string? Error { get; set; }
    /// <summary>Text written into documents.</summary>
    public string? Display { get; set; }
    public string? Words { get; set; }
    /// <summary>Value copied from the field named in defaultFrom.</summary>
    public bool FromOtherField { get; set; }
}

public sealed class Resolution
{
    public Dictionary<string, ResolvedField> Fields { get; } = new();
    /// <summary>Errors that belong to the whole case (checks), in Turkish.</summary>
    public List<string> Errors { get; } = new();
    public int ErrorCount => Fields.Values.Count(f => f.Error is not null) + Errors.Count;
    public bool IsValid => ErrorCount == 0;
    public ResolvedField? Get(string name) => Fields.GetValueOrDefault(Tr.Key(name));
}

public static class Resolver
{
    public const string MissingMessage = "Bu alan boş. Değer girin ya da “Boş bırak” seçin.";

    /// <summary>Inputs of a new case: set.json defaults and blank-by-default fields applied.</summary>
    public static Dictionary<string, FieldInput> NewCase(TemplateSet set)
    {
        var inputs = new Dictionary<string, FieldInput>();
        foreach (var f in set.Fields.Where(f => !f.IsComputed))
            inputs[f.Key] = new FieldInput
            {
                Raw = f.Definition?.Default ?? "",
                Blank = f.Definition?.BlankByDefault == true,
                Currency = f.Type == FieldType.Para ? f.Definition?.Currency ?? ValueCodec.DefaultCurrency : null,
            };
        return inputs;
    }

    /// <summary>
    /// Keys of fields needed by the chosen documents: fields used in them, fields used only in file
    /// names, computed sums or checks, and the inputs of needed computed fields.
    /// </summary>
    public static HashSet<string> ActiveFields(TemplateSet set, IReadOnlySet<string>? documents)
    {
        if (documents is null) return set.Fields.Select(f => f.Key).ToHashSet();
        var active = set.Fields
            .Where(f => !f.UsedInTemplates || f.Documents.Any(documents.Contains))
            .Select(f => f.Key).ToHashSet();
        var names = set.Documents.Where(d => documents.Contains(d.Entry.Template)).Select(d => d.OutputPattern)
            .Append(set.Definition.OutputFolder ?? "");
        foreach (var m in names.SelectMany(n => set.Matcher.Find(n))) active.Add(m.Ref.Key);
        foreach (var f in set.Fields.Where(f => f.IsComputed && active.Contains(f.Key)))
            foreach (var t in TemplateSet.ComputedTerms(f.Definition!.Computed))
                active.Add(Tr.Key(t.Name));
        foreach (var f in set.Fields.Where(f => active.Contains(f.Key) && !string.IsNullOrWhiteSpace(f.Definition?.DefaultFrom)))
            active.Add(Tr.Key(f.Definition!.DefaultFrom!));
        return active;
    }

    /// <summary>Fields outside <paramref name="active"/> are treated as left blank (their documents are not produced).</summary>
    public static Resolution Resolve(TemplateSet set, IReadOnlyDictionary<string, FieldInput> inputs, IReadOnlySet<string>? active = null)
    {
        var res = new Resolution();
        var def = set.Definition;
        var pendingDefault = new List<ResolvedField>();

        foreach (var f in set.Fields)
        {
            var r = new ResolvedField { Field = f };
            res.Fields[f.Key] = r;
            if (active is not null && !active.Contains(f.Key)) { r.Blank = true; continue; }
            if (f.IsComputed) continue;
            var input = inputs.GetValueOrDefault(f.Key) ?? new FieldInput();
            if (input.Blank) { r.Blank = true; continue; }
            if (string.IsNullOrWhiteSpace(input.Raw))
            {
                if (!string.IsNullOrWhiteSpace(f.Definition?.DefaultFrom)) pendingDefault.Add(r);
                else r.Error = MissingMessage;
                continue;
            }
            var parsed = f.Type == FieldType.Para
                ? ValueCodec.ParseMoney(input.Raw, input.Currency ?? f.Definition?.Currency ?? ValueCodec.DefaultCurrency)
                : ValueCodec.Parse(f.Type, input.Raw, f.Definition);
            if (parsed.IsOk) r.Value = parsed.Value;
            else r.Error = parsed.Error;
        }

        // defaultFrom: copy the other field's value (one level; chains resolve in field order).
        foreach (var r in pendingDefault)
        {
            var src = res.Get(r.Field.Definition!.DefaultFrom!);
            if (src?.Value is { } v)
            {
                var copied = v.Type == r.Field.Type ? v : ValueCodec.Parse(r.Field.Type, ValueCodec.ToInput(v), r.Field.Definition).Value;
                if (copied is null) r.Error = MissingMessage;
                else { r.Value = copied; r.FromOtherField = true; }
            }
            else if (src?.Blank == true) r.Blank = true;
            else r.Error = MissingMessage;
        }

        foreach (var f in set.Fields.Where(f => f.IsComputed && !res.Fields[f.Key].Blank))
            Compute(res, res.Fields[f.Key]);

        foreach (var r in res.Fields.Values.Where(r => r.Value is not null))
        {
            r.Display = ValueCodec.Format(r.Value!, def);
            r.Words = ValueCodec.Words(r.Value!, def);
        }

        foreach (var c in def.Checks) RunCheck(res, c, def);
        return res;
    }

    static void Compute(Resolution res, ResolvedField target)
    {
        decimal sum = 0;
        string? currency = target.Field.Definition?.Currency;
        foreach (var term in TemplateSet.ComputedTerms(target.Field.Definition!.Computed))
        {
            var src = res.Get(term.Name);
            if (src is null) { target.Error = $"Hesaplanamadı: “{term.Name}” alanı yok."; return; }
            if (src.Blank) continue;
            if (src.Value?.Number is not { } n)
            {
                target.Error = $"“{src.Field.Label}” girilmeden hesaplanamaz.";
                return;
            }
            currency ??= src.Value.Currency;
            if (src.Value.Currency != currency)
            {
                target.Error = "Hesaplanan tutardaki alanların para birimleri farklı.";
                return;
            }
            sum += term.Sign * n;
        }
        if (sum < 0) { target.Error = "Hesaplanan tutar eksi çıktı."; return; }
        target.Value = new FieldValue { Type = FieldType.Para, Number = sum, Currency = currency ?? ValueCodec.DefaultCurrency };
    }

    static void RunCheck(Resolution res, CheckDefinition c, SetDefinition def)
    {
        var fields = c.Fields.Select(res.Get).Where(f => f is not null).Select(f => f!).ToList();
        switch (c.Kind.ToLowerInvariant())
        {
            case "sumequals":
            {
                var target = c.EqualsField is null ? null : res.Get(c.EqualsField);
                if (target?.Value?.Number is not { } expected) return;
                if (fields.Any(f => !f.Blank && f.Value is null)) return;   // field errors are shown already
                var sum = fields.Where(f => f.Value?.Number is not null).Sum(f => f.Value!.Number!.Value);
                if (sum != expected)
                {
                    var diff = ValueCodec.FormatMoney(Math.Abs(sum - expected), target.Value.Currency ?? ValueCodec.DefaultCurrency, def);
                    res.Errors.Add((c.Message ?? $"Toplam, “{target.Field.Label}” ile tutmuyor.") + $" (Fark: {diff})");
                }
                break;
            }
            case "blanktogether":
            {
                if (fields.Any(f => f.Blank) && fields.Any(f => !f.Blank))
                    res.Errors.Add(c.Message ?? "Bu alanlar birlikte doldurulmalı ya da birlikte boş bırakılmalı: " +
                                   string.Join(", ", fields.Select(f => f.Field.Label)));
                break;
            }
            case "atmostone":
            {
                // Alternatives such as "meeting place" and "video conference": at most one may be filled.
                var filled = fields.Where(f => !f.Blank && f.Value is not null).ToList();
                if (filled.Count > 1)
                    res.Errors.Add(c.Message ?? "Bu alanlardan yalnızca biri doldurulmalı, diğerleri boş bırakılmalı: " +
                                   string.Join(", ", filled.Select(f => f.Field.Label)));
                break;
            }
            case "dateorder":
            {
                DateOnly? last = null;
                foreach (var f in fields)
                {
                    if (f.Value?.Date is not { } d) continue;
                    if (last is { } l && d < l)
                    {
                        res.Errors.Add(c.Message ?? "Tarihler sıralı değil.");
                        return;
                    }
                    last = d;
                }
                break;
            }
        }
    }
}

/// <summary>Per-document suffix corrections made in the preview: template file → occurrence → suffix.</summary>
public sealed class SuffixOverrides
{
    public Dictionary<string, Dictionary<int, string>> ByDocument { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<int, string> For(string document) =>
        ByDocument.TryGetValue(document, out var d) ? d : new Dictionary<int, string>();

    public void Set(string document, int occurrence, string? suffix)
    {
        if (!ByDocument.TryGetValue(document, out var d)) ByDocument[document] = d = new();
        if (suffix is null) d.Remove(occurrence);
        else d[occurrence] = suffix;
    }

    public void Clear() => ByDocument.Clear();
}

/// <summary>Feeds a <see cref="Resolution"/> into template rendering.</summary>
public sealed class ResolvedValues(Resolution res, IReadOnlyDictionary<int, string>? overrides = null) : IValueSource
{
    public RenderValue Get(VariableRef r)
    {
        var f = res.Fields.GetValueOrDefault(r.Key);
        if (f is null) return RenderValue.Missing;
        if (f.Blank) return new RenderValue(ValueState.Blank, "", f.Field.Definition?.RemoveParagraphIfBlank == true);
        if (f.Value is null || f.Display is null) return RenderValue.Missing;
        var text = r.Modifier == VariableModifier.Words ? f.Words ?? f.Display : f.Display;
        return new RenderValue(ValueState.Value, text, false);
    }

    public string? SuffixOverride(int occurrence) => overrides?.GetValueOrDefault(occurrence);
}
