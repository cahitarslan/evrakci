using System.Numerics;
using System.Text;

namespace Evrakci.Core.Text;

/// <summary>Turkish number words in title case: 44000 → "Kırk Dört Bin".</summary>
public static class NumberToWords
{
    static readonly string[] Ones = ["", "Bir", "İki", "Üç", "Dört", "Beş", "Altı", "Yedi", "Sekiz", "Dokuz"];
    static readonly string[] Tens = ["", "On", "Yirmi", "Otuz", "Kırk", "Elli", "Altmış", "Yetmiş", "Seksen", "Doksan"];
    static readonly string[] Scales = ["", "Bin", "Milyon", "Milyar", "Trilyon", "Katrilyon", "Kentilyon"];

    public static readonly BigInteger Max = BigInteger.Pow(1000, Scales.Length) - 1;

    public static string Convert(BigInteger n)
    {
        if (n.Sign < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n > Max) throw new ArgumentOutOfRangeException(nameof(n));
        if (n.IsZero) return "Sıfır";

        var groups = new List<int>();
        while (!n.IsZero)
        {
            groups.Add((int)(n % 1000));
            n /= 1000;
        }

        var parts = new List<string>();
        for (int i = groups.Count - 1; i >= 0; i--)
        {
            int g = groups[i];
            if (g == 0) continue;
            // "Bin", not "Bir Bin"
            if (!(i == 1 && g == 1)) parts.Add(Hundreds(g));
            if (i > 0) parts.Add(Scales[i]);
        }
        return string.Join(' ', parts.Where(p => p.Length > 0));
    }

    static string Hundreds(int n)
    {
        var sb = new List<string>(4);
        int h = n / 100, t = n / 10 % 10, o = n % 10;
        if (h == 1) sb.Add("Yüz");
        else if (h > 1) { sb.Add(Ones[h]); sb.Add("Yüz"); }
        if (t > 0) sb.Add(Tens[t]);
        if (o > 0) sb.Add(Ones[o]);
        return string.Join(' ', sb);
    }

    /// <summary>
    /// Money in words: 44000.25 → "Kırk Dört Bin Türk Lirası Yirmi Beş Kuruş".
    /// The amount must already have at most two decimals; no rounding happens here.
    /// </summary>
    public static string Money(decimal amount, string mainUnit, string subUnit)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var whole = decimal.Truncate(amount);
        var frac = (amount - whole) * 100;
        if (frac != decimal.Truncate(frac))
            throw new ArgumentException("Kuruş kısmı en fazla iki hane olabilir.", nameof(amount));

        var main = new BigInteger(whole);
        var sub = (int)frac;
        var sb = new StringBuilder();
        if (!main.IsZero || sub == 0)
            sb.Append(Convert(main)).Append(' ').Append(mainUnit);
        if (sub > 0)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Convert(sub)).Append(' ').Append(subUnit);
        }
        return sb.ToString();
    }
}
