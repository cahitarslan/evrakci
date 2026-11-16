using Evrakci.Core.Text;

namespace Evrakci.Core.Values;

public enum FieldType
{
    Metin,
    Para,
    Tarih,
    Sayi,
    Tckn,
    Iban,
    Telefon,
}

public static class FieldTypes
{
    public static readonly FieldType[] All = Enum.GetValues<FieldType>();

    public static string DisplayName(FieldType t) => t switch
    {
        FieldType.Metin => "Metin",
        FieldType.Para => "Para",
        FieldType.Tarih => "Tarih",
        FieldType.Sayi => "Sayı",
        FieldType.Tckn => "TCKN",
        FieldType.Iban => "IBAN",
        FieldType.Telefon => "Telefon",
        _ => t.ToString(),
    };

    /// <summary>Parses a type name as written in a template or set.json ("Para", "sayı", "TCKN" …).</summary>
    public static FieldType? Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Tr.Key(name) switch
        {
            "METİN" or "METIN" => FieldType.Metin,
            "PARA" => FieldType.Para,
            "TARİH" or "TARIH" => FieldType.Tarih,
            "SAYI" or "SAYİ" => FieldType.Sayi,
            "TCKN" => FieldType.Tckn,
            "IBAN" or "İBAN" => FieldType.Iban,
            "TELEFON" => FieldType.Telefon,
            _ => null,
        };
    }

    /// <summary>Guesses a type from the variable name; used when neither the template nor set.json says.</summary>
    public static FieldType Infer(string key, bool usedInWords)
    {
        var words = key.Split([' ', '.', '-', '_', '/'], StringSplitOptions.RemoveEmptyEntries);
        bool Has(string part) => key.Contains(part, StringComparison.Ordinal);
        bool Word(string w) => words.Contains(w);

        if (Has("TCKN") || Has("KİMLİK NO") || Has("KİMLİK NUMARA") || Word("TC")) return FieldType.Tckn;
        if (Has("IBAN") || Has("İBAN")) return FieldType.Iban;
        if (Has("TARİH")) return FieldType.Tarih;
        if (Has("TELEFON") || Word("TEL") || Word("GSM") || Word("CEP")) return FieldType.Telefon;
        if (usedInWords) return FieldType.Para;
        string[] money = ["TUTAR", "MİKTAR", "BEDEL", "ÜCRET", "ALACA", "TAZMİNAT", "TOPLAM", "PARA", "FİYAT", "KİRA", "BORÇ"];
        if (money.Any(Has)) return FieldType.Para;
        if (Has("ADET") || Word("SAYI") || Word("SAYISI")) return FieldType.Sayi;
        return FieldType.Metin;
    }
}
