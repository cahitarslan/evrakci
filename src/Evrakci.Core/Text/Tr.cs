using System.Globalization;
using System.Text;

namespace Evrakci.Core.Text;

/// <summary>Turkish-culture text helpers (i/İ, ı/I aware).</summary>
public static class Tr
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("tr-TR");

    public static string Upper(string s) => s.ToUpper(Culture);

    public static string Lower(string s) => s.ToLower(Culture);

    /// <summary>"cahit  arslan" → "Cahit Arslan".</summary>
    public static string Title(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool start = true;
        foreach (var ch in s)
        {
            if (char.IsLetter(ch))
            {
                sb.Append(start ? char.ToUpper(ch, Culture) : char.ToLower(ch, Culture));
                start = false;
            }
            else
            {
                sb.Append(ch);
                start = char.IsWhiteSpace(ch) || ch == '-' || ch == '/' || ch == '(';
            }
        }
        return sb.ToString();
    }

    /// <summary>Collapses runs of whitespace into one space and trims.</summary>
    public static string CollapseSpaces(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool space = false;
        foreach (var ch in s.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!space) sb.Append(' ');
                space = true;
            }
            else
            {
                sb.Append(ch);
                space = false;
            }
        }
        return sb.ToString();
    }

    /// <summary>Identity key of a variable name: Turkish upper-case, collapsed spaces.</summary>
    public static string Key(string name) => Upper(CollapseSpaces(name));

    /// <summary>Key without any spaces or punctuation, used for "looks alike" warnings.</summary>
    public static string Compact(string key)
    {
        var sb = new StringBuilder(key.Length);
        foreach (var ch in key)
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    public static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>
    /// True when two distinct keys are probably the same variable written differently,
    /// e.g. "TARAF1 ADI" / "TARAF 1 ADI" or "TARAF1 ADI" / "TARAF1 ADİ".
    /// Names that differ only in a digit (TAKSİT1 / TAKSİT2) are intentional and not reported.
    /// </summary>
    public static bool LooksAlike(string keyA, string keyB)
    {
        if (keyA == keyB) return false;
        var a = Compact(keyA);
        var b = Compact(keyB);
        if (a == b) return true;
        if (Math.Min(a.Length, b.Length) < 5) return false;
        if (Levenshtein(a, b) != 1) return false;
        if (a.Length == b.Length)
        {
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return !(char.IsDigit(a[i]) && char.IsDigit(b[i]));
        }
        // One extra character: a stray digit (TAKSİT / TAKSİT1) is still a different name.
        var longer = a.Length > b.Length ? a : b;
        var shorter = a.Length > b.Length ? b : a;
        for (int i = 0; i < longer.Length; i++)
        {
            if (i >= shorter.Length || longer[i] != shorter[i]) return !char.IsDigit(longer[i]);
        }
        return true;
    }

    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            sb.Append(Array.IndexOf(invalid, ch) >= 0 || ch < ' ' ? '-' : ch);
        var s = CollapseSpaces(sb.ToString()).Trim('.', ' ');
        return s.Length == 0 ? "belge" : s;
    }
}
