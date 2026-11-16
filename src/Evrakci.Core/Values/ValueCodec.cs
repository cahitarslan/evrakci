using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Evrakci.Core.Sets;
using Evrakci.Core.Text;

namespace Evrakci.Core.Values;

/// <summary>Parses user input into <see cref="FieldValue"/> and formats values for documents.</summary>
public static partial class ValueCodec
{
    public const string DefaultCurrency = "TRY";

    static readonly string[] DateFormats =
        ["d.M.yyyy", "dd.MM.yyyy", "d/M/yyyy", "d-M-yyyy", "ddMMyyyy", "yyyy-MM-dd", "d.M.yy"];

    public static ParseResult Parse(FieldType type, string raw, FieldDefinition? def)
    {
        var s = raw.Trim();
        if (s.Length == 0) return ParseResult.Fail("Değer girilmedi.");
        return type switch
        {
            FieldType.Metin => ParseText(s, def),
            FieldType.Para => ParseMoney(s, def?.Currency ?? DefaultCurrency),
            FieldType.Sayi => ParseNumber(s),
            FieldType.Tarih => ParseDate(s),
            FieldType.Tckn => ParseTckn(s),
            FieldType.Iban => ParseIban(s),
            FieldType.Telefon => ParsePhone(s),
            _ => ParseResult.Fail("Bilinmeyen tip."),
        };
    }

    static ParseResult ParseText(string s, FieldDefinition? def)
    {
        s = Tr.CollapseSpaces(s);
        s = def?.Transform?.ToLowerInvariant() switch
        {
            "upper" => Tr.Upper(s),
            "lower" => Tr.Lower(s),
            "title" => Tr.Title(s),
            _ => s,
        };
        if (!string.IsNullOrEmpty(def?.Pattern))
        {
            bool ok;
            try { ok = Regex.IsMatch(s, def.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)); }
            catch (ArgumentException) { ok = true; }
            catch (RegexMatchTimeoutException) { ok = false; }
            if (!ok)
                return ParseResult.Fail(def.Hint is { Length: > 0 } h
                    ? $"Değer beklenen biçimde değil. {h}"
                    : "Değer beklenen biçimde değil.");
        }
        return ParseResult.Ok(new FieldValue { Type = FieldType.Metin, Text = s });
    }

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+(,\d+)?$|^\d+(,\d+)?$")]
    private static partial Regex TurkishNumber();

    [GeneratedRegex(@"^\d+\.\d{1,2}$")]
    private static partial Regex DotDecimal();

    /// <summary>
    /// "44.000,25", "44000,25", "44000.25", "44.000" (= 44 000) are accepted.
    /// More than two decimals is an error — never rounded silently.
    /// </summary>
    public static ParseResult ParseMoney(string s, string currency)
    {
        var cleaned = StripCurrency(s);
        if (cleaned.StartsWith('-')) return ParseResult.Fail("Tutar eksi olamaz.");
        if (!TryParseTurkishDecimal(cleaned, allowDotDecimal: true, out var value, out var decimals))
            return ParseResult.Fail("Geçerli bir tutar girin, ör. 44.000,25");
        if (decimals > 2) return ParseResult.Fail("Kuruş kısmı en fazla iki hane olabilir.");
        if (value > 999_999_999_999_999m) return ParseResult.Fail("Tutar çok büyük.");
        return ParseResult.Ok(new FieldValue { Type = FieldType.Para, Number = value, Currency = currency });
    }

    static ParseResult ParseNumber(string s)
    {
        var cleaned = s.Replace(" ", "");
        bool neg = cleaned.StartsWith('-');
        if (neg) cleaned = cleaned[1..];
        if (!TryParseTurkishDecimal(cleaned, allowDotDecimal: false, out var value, out _))
            return ParseResult.Fail("Geçerli bir sayı girin, ör. 1.250 ya da 12,5");
        return ParseResult.Ok(new FieldValue { Type = FieldType.Sayi, Number = neg ? -value : value });
    }

    static string StripCurrency(string s)
    {
        var sb = new StringBuilder(s.Length);
        var upper = Tr.Upper(s);
        foreach (var token in new[] { ".-TL", ".-", "TL", "TRY", "USD", "EUR", "₺", "$", "€", "LİRA" })
            upper = upper.Replace(token, " ");
        foreach (var ch in upper)
            if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        return sb.ToString().TrimEnd('.');
    }

    static bool TryParseTurkishDecimal(string s, bool allowDotDecimal, out decimal value, out int decimals)
    {
        value = 0; decimals = 0;
        string invariant;
        if (TurkishNumber().IsMatch(s))
        {
            var comma = s.IndexOf(',');
            decimals = comma < 0 ? 0 : s.Length - comma - 1;
            invariant = s.Replace(".", "").Replace(',', '.');
        }
        else if (allowDotDecimal && DotDecimal().IsMatch(s))
        {
            decimals = s.Length - s.IndexOf('.') - 1;
            invariant = s;
        }
        else return false;
        return decimal.TryParse(invariant, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    static ParseResult ParseDate(string s)
    {
        if (DateTime.TryParseExact(s, DateFormats, Tr.Culture, DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(s, Tr.Culture, DateTimeStyles.None, out dt))
        {
            if (dt.Year < 1900 || dt.Year > 2200) return ParseResult.Fail("Tarih makul bir aralıkta değil.");
            return ParseResult.Ok(new FieldValue { Type = FieldType.Tarih, Date = DateOnly.FromDateTime(dt) });
        }
        return ParseResult.Fail("Geçerli bir tarih girin, ör. 15.10.2026");
    }

    static ParseResult ParseTckn(string s)
    {
        var digits = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (digits.Length != 11 || !digits.All(char.IsAsciiDigit))
            return ParseResult.Fail("TCKN 11 haneli olmalı.");
        if (!Validators.IsValidTckn(digits))
            return ParseResult.Fail("Geçersiz TCKN: kontrol haneleri tutmuyor.");
        return ParseResult.Ok(new FieldValue { Type = FieldType.Tckn, Text = digits });
    }

    static ParseResult ParseIban(string s)
    {
        var compact = new string(Tr.Upper(s).Where(char.IsLetterOrDigit).ToArray());
        if (!compact.StartsWith("TR")) return ParseResult.Fail("Yalnızca TR ile başlayan Türkiye IBAN'ları desteklenir.");
        if (compact.Length != 26) return ParseResult.Fail("TR IBAN 26 karakter olmalı.");
        if (!Validators.IsValidTrIban(compact)) return ParseResult.Fail("Geçersiz IBAN: kontrol haneleri tutmuyor.");
        return ParseResult.Ok(new FieldValue { Type = FieldType.Iban, Text = compact });
    }

    static ParseResult ParsePhone(string s)
    {
        var n = Validators.NormalizeTrPhone(s);
        return n is null
            ? ParseResult.Fail("Geçerli bir Türkiye telefon numarası girin, ör. 0532 123 45 67")
            : ParseResult.Ok(new FieldValue { Type = FieldType.Telefon, Text = n });
    }

    // ---------------- formatting ----------------

    public static string Format(FieldValue v, SetDefinition set) => v.Type switch
    {
        FieldType.Metin => v.Text!,
        FieldType.Para => FormatMoney(v.Number!.Value, v.Currency ?? DefaultCurrency, set),
        FieldType.Sayi => v.Number!.Value.ToString("#,##0.##########", Tr.Culture),
        FieldType.Tarih => FormatDate(v.Date!.Value, set.Formats.Date),
        FieldType.Tckn => v.Text!,
        FieldType.Iban => GroupBy4(v.Text!),
        FieldType.Telefon => FormatPhone(v.Text!, set.Formats.Phone),
        _ => v.Text ?? "",
    };

    /// <summary>Para in words; null for other types.</summary>
    public static string? Words(FieldValue v, SetDefinition set)
    {
        if (v.Type != FieldType.Para) return null;
        var names = CurrencyFor(v.Currency, set);
        return NumberToWords.Money(v.Number!.Value, names.Main, names.Sub);
    }

    public static CurrencyNames CurrencyFor(string? code, SetDefinition set)
    {
        code ??= DefaultCurrency;
        if (set.Currencies.TryGetValue(code, out var n)) return n;
        return CurrencyNames.Defaults().TryGetValue(code, out var d) ? d : new CurrencyNames { Symbol = code, Main = code, Sub = "" };
    }

    public static string FormatMoney(decimal amount, string currency, SetDefinition set)
    {
        var number = amount.ToString("#,##0.00", Tr.Culture);
        var names = CurrencyFor(currency, set);
        var pattern = string.IsNullOrWhiteSpace(set.Formats.Money) ? "{tutar} {simge}" : set.Formats.Money;
        return pattern.Replace("{tutar}", number).Replace("{simge}", names.Symbol).Replace("{kod}", currency);
    }

    public static string FormatDate(DateOnly d, string? pattern)
    {
        try { return d.ToString(string.IsNullOrWhiteSpace(pattern) ? "dd.MM.yyyy" : pattern, Tr.Culture); }
        catch (FormatException) { return d.ToString("dd.MM.yyyy", Tr.Culture); }
    }

    /// <summary>Fills the 10 national digits (after the leading 0) into the pattern's # marks.</summary>
    public static string FormatPhone(string digits11, string? pattern)
    {
        var national = digits11[1..];
        if (string.IsNullOrEmpty(pattern) || pattern.Count(c => c == '#') != 10) pattern = "0### ### ## ##";
        var sb = new StringBuilder(pattern.Length);
        int i = 0;
        foreach (var c in pattern) sb.Append(c == '#' ? national[i++] : c);
        return sb.ToString();
    }

    static string GroupBy4(string s)
    {
        var sb = new StringBuilder(s.Length + s.Length / 4);
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>A value as the user would type it back (used when copying defaultFrom values).</summary>
    public static string ToInput(FieldValue v) => v.Type switch
    {
        FieldType.Para or FieldType.Sayi => v.Number!.Value.ToString("#,##0.##", Tr.Culture),
        FieldType.Tarih => v.Date!.Value.ToString("dd.MM.yyyy", Tr.Culture),
        _ => v.Text ?? "",
    };
}
