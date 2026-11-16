using System.Numerics;

namespace Evrakci.Core.Values;

public static class Validators
{
    /// <summary>T.C. kimlik numarası resmî algoritması.</summary>
    public static bool IsValidTckn(string s)
    {
        if (s.Length != 11 || s[0] == '0') return false;
        var d = new int[11];
        for (int i = 0; i < 11; i++)
        {
            if (s[i] < '0' || s[i] > '9') return false;
            d[i] = s[i] - '0';
        }
        int odd = d[0] + d[2] + d[4] + d[6] + d[8];
        int even = d[1] + d[3] + d[5] + d[7];
        int d10 = ((odd * 7 - even) % 10 + 10) % 10;
        if (d[9] != d10) return false;
        int sum = 0;
        for (int i = 0; i < 10; i++) sum += d[i];
        return d[10] == sum % 10;
    }

    /// <summary>
    /// Turkish IBAN: "TR" + 2 check digits + 5 bank digits + 1 reserved "0" + 16 account characters,
    /// verified with ISO 13616 mod-97.
    /// </summary>
    public static bool IsValidTrIban(string compact)
    {
        if (compact.Length != 26 || !compact.StartsWith("TR", StringComparison.Ordinal)) return false;
        for (int i = 2; i < 26; i++)
        {
            char c = compact[i];
            bool ok = i < 10 ? char.IsAsciiDigit(c) : char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c);
            if (!ok) return false;
        }
        if (compact[9] != '0') return false;
        return Mod97(compact[4..] + compact[..4]) == 1;
    }

    static int Mod97(string s)
    {
        var digits = new System.Text.StringBuilder(s.Length * 2);
        foreach (var c in s)
            digits.Append(char.IsAsciiDigit(c) ? (c - '0').ToString() : (c - 'A' + 10).ToString());
        return (int)(BigInteger.Parse(digits.ToString()) % 97);
    }

    /// <summary>
    /// Normalises a Turkish phone number to 11 digits starting with 0 (e.g. 05321234567).
    /// Accepts +90 / 0090 / 90 prefixes, spaces, dashes and parentheses.
    /// </summary>
    public static string? NormalizeTrPhone(string raw)
    {
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        if (digits.StartsWith("0090")) digits = digits[4..];
        else if (digits.Length == 12 && digits.StartsWith("90")) digits = digits[2..];
        if (digits.Length == 10) digits = "0" + digits;
        if (digits.Length != 11 || digits[0] != '0') return null;
        // area codes 2xx/3xx/4xx, mobile 5xx, 8xx (444/850 services etc.)
        return digits[1] is '2' or '3' or '4' or '5' or '8' ? digits : null;
    }
}
