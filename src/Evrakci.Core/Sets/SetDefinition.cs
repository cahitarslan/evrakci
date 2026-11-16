using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Evrakci.Core.Sets;

/// <summary>Contents of a template set's set.json.</summary>
public sealed class SetDefinition
{
    public const string FileName = "set.json";
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string? Name { get; set; }
    public string? Description { get; set; }
    /// <summary>Kind of file the set is for (e.g. "İşçi – İşveren"); the library groups sets by it.</summary>
    public string? Category { get; set; }
    public Delimiters Delimiters { get; set; } = new();
    public SetFormats Formats { get; set; } = new();
    public Dictionary<string, CurrencyNames> Currencies { get; set; } = CurrencyNames.Defaults();
    /// <summary>Optional sub-folder created inside the chosen output folder; may contain variables.</summary>
    public string? OutputFolder { get; set; }
    /// <summary>Templates of the set. When null every .docx / .udf in the folder is used.</summary>
    public List<DocumentEntry>? Documents { get; set; }
    public List<string>? Groups { get; set; }
    public Dictionary<string, FieldDefinition> Fields { get; set; } = new();
    public List<CheckDefinition> Checks { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    const long MaxBytes = 2 * 1024 * 1024;

    public static SetDefinition Load(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) throw new EvrakciException("set.json dosyası çok büyük.");
        SetDefinition? def;
        try
        {
            def = JsonSerializer.Deserialize<SetDefinition>(File.ReadAllBytes(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new EvrakciException($"set.json okunamadı (satır {ex.LineNumber + 1}). Dosyanın biçimi bozuk.");
        }
        if (def is null) throw new EvrakciException("set.json boş.");
        if (def.FormatVersion > CurrentFormatVersion)
            throw new EvrakciException("Bu set, Evrakçı'nın daha yeni bir sürümüyle oluşturulmuş. Lütfen uygulamayı güncelleyin.");
        def.Delimiters ??= new();
        def.Formats ??= new();
        def.Currencies ??= CurrencyNames.Defaults();
        foreach (var (code, names) in CurrencyNames.Defaults())
            def.Currencies.TryAdd(code, names);
        def.Fields ??= new();
        def.Checks ??= new();
        return def;
    }

    public void Save(string path)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }
}

public sealed class Delimiters
{
    public string Open { get; set; } = "{{{";
    public string Close { get; set; } = "}}}";
}

public sealed class SetFormats
{
    /// <summary>.NET date pattern with Turkish month names, e.g. "dd.MM.yyyy" or "d MMMM yyyy".</summary>
    public string Date { get; set; } = "dd.MM.yyyy";
    /// <summary>Money pattern with {tutar}, {simge} and {kod} placeholders.</summary>
    public string Money { get; set; } = "{tutar} {simge}";
    /// <summary>Phone pattern; each # takes one of the 10 national digits.</summary>
    public string Phone { get; set; } = "0### ### ## ##";
}

public sealed class CurrencyNames
{
    public string Symbol { get; set; } = "";
    public string Main { get; set; } = "";
    public string Sub { get; set; } = "";

    public static Dictionary<string, CurrencyNames> Defaults() => new()
    {
        ["TRY"] = new() { Symbol = "TL", Main = "Türk Lirası", Sub = "Kuruş" },
        ["USD"] = new() { Symbol = "USD", Main = "Amerikan Doları", Sub = "Sent" },
        ["EUR"] = new() { Symbol = "EUR", Main = "Avro", Sub = "Sent" },
    };
}

public sealed class DocumentEntry
{
    public string Template { get; set; } = "";
    /// <summary>Output file name; may contain variables. Defaults to the template name.</summary>
    public string? Output { get; set; }
    /// <summary>Not chosen by default for a new case (e.g. the "no agreement" record next to the agreement).</summary>
    public bool Optional { get; set; }
    /// <summary>Short explanation shown when choosing the documents of a case.</summary>
    public string? Note { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class FieldDefinition
{
    public string? Type { get; set; }
    public string? Label { get; set; }
    public string? Group { get; set; }
    public string? Hint { get; set; }
    /// <summary>Value pre-filled for a new case, written as the user would type it.</summary>
    public string? Default { get; set; }
    /// <summary>When left empty, the field takes the value of this other field.</summary>
    public string? DefaultFrom { get; set; }
    /// <summary>Sum of money fields, e.g. "A + B - C".</summary>
    public string? Computed { get; set; }
    /// <summary>"upper", "lower" or "title" — applied to Metin values.</summary>
    public string? Transform { get; set; }
    /// <summary>Regular expression a Metin value must match.</summary>
    public string? Pattern { get; set; }
    public string? Currency { get; set; }
    public bool RemoveParagraphIfBlank { get; set; }
    public bool BlankByDefault { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class CheckDefinition
{
    /// <summary>"sumEquals", "blankTogether", "atMostOne" or "dateOrder".</summary>
    public string Kind { get; set; } = "";
    public List<string> Fields { get; set; } = new();
    [JsonPropertyName("equals")]
    public string? EqualsField { get; set; }
    public string? Message { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
