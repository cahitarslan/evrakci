using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Evrakci.Core.Text;

/// <summary>
/// Re-writes an apostrophe suffix ("'nın", "'a", "'da", "'la", "'dır" …) so that it agrees with the
/// value it follows, using vowel harmony, consonant assimilation and buffer letters (n / y).
/// The template author writes the suffix for any imagined name; only its grammatical kind matters.
/// </summary>
public static partial class TurkishSuffix
{
    const string Vowels = "aeıioöuü";
    const string Back = "aıou";
    const string Rounded = "oöuü";
    const string Voiceless = "fstkçşhp";

    static readonly Dictionary<char, string> LetterNames = new()
    {
        ['a'] = "a", ['b'] = "be", ['c'] = "ce", ['ç'] = "çe", ['d'] = "de", ['e'] = "e", ['f'] = "fe",
        ['g'] = "ge", ['ğ'] = "ge", ['h'] = "he", ['ı'] = "ı", ['i'] = "i", ['j'] = "je", ['k'] = "ka",
        ['l'] = "le", ['m'] = "me", ['n'] = "ne", ['o'] = "o", ['ö'] = "ö", ['p'] = "pe", ['q'] = "kü",
        ['r'] = "re", ['s'] = "se", ['ş'] = "şe", ['t'] = "te", ['u'] = "u", ['ü'] = "ü", ['v'] = "ve",
        ['w'] = "ve", ['x'] = "iks", ['y'] = "ye", ['z'] = "ze",
    };

    record struct Tail(char LastVowel, bool EndsWithVowel, bool EndsVoiceless);

    enum Buffer { None, N, Y }

    /// <summary>Returns the suffix (without apostrophe) adapted to <paramref name="value"/>.</summary>
    public static string Adapt(string value, string templateSuffix)
    {
        if (templateSuffix.Length == 0) return templateSuffix;
        var lower = Tr.Lower(templateSuffix);
        foreach (var ch in lower)
            if (!char.IsLetter(ch)) return templateSuffix;
        var tail = Analyze(value);
        if (tail is null) return templateSuffix;

        var result = Generate(Normalize(lower), tail.Value);
        bool upper = templateSuffix == Tr.Upper(templateSuffix) && templateSuffix != lower;
        return upper ? Tr.Upper(result) : result;
    }

    static string Normalize(string s) => s.Replace('â', 'a').Replace('î', 'i').Replace('û', 'u');

    static bool IsVowel(char c) => Vowels.Contains(c);
    static bool IsHigh(char c) => c is 'ı' or 'i' or 'u' or 'ü';
    static bool IsLow(char c) => c is 'a' or 'e';

    static string Generate(string t, Tail tail)
    {
        // 1. Work out the grammatical kind from the template's spelling and drop any buffer letter.
        var buffer = Buffer.None;
        string core = t;
        if (t.Length >= 3 && t[0] == 'n' && IsHigh(t[1]) && t[2] == 'n') { buffer = Buffer.N; core = t[1..]; }
        else if (t.Length >= 2 && t[0] == 'y' && IsVowel(t[1])) { buffer = Buffer.Y; core = t[1..]; }
        else if (t.Length >= 3 && t[0] == 'y' && t[1] == 'l' && IsLow(t[2])) { buffer = Buffer.Y; core = t[1..]; }
        else if (core.Length >= 2 && IsHigh(core[0]) && core[1] == 'n') buffer = Buffer.N;          // genitive  'ın
        else if (IsLow(core[0])) buffer = Buffer.Y;                                                  // dative    'a
        else if (IsHigh(core[0])) buffer = Buffer.Y;                                                 // accusative 'ı
        else if (core.Length >= 2 && core[0] == 'l' && IsLow(core[1]) && !(core.Length >= 3 && core[2] == 'r'))
            buffer = Buffer.Y;                                                                       // instrumental 'la

        // 2. Regenerate letter by letter.
        var sb = new StringBuilder(core.Length + 1);
        char lastVowel = tail.LastVowel;
        bool endsVowel = tail.EndsWithVowel;
        bool voiceless = tail.EndsVoiceless;

        void Emit(char c)
        {
            sb.Append(c);
            if (IsVowel(c)) { lastVowel = c; endsVowel = true; voiceless = false; }
            else { endsVowel = false; voiceless = Voiceless.Contains(c); }
        }

        if (endsVowel && buffer == Buffer.N) Emit('n');
        if (endsVowel && buffer == Buffer.Y) Emit('y');

        for (int i = 0; i < core.Length; i++)
        {
            char c = core[i];
            if (i > 0 && c == 'k' && i + 1 < core.Length && core[i + 1] == 'i')
            {
                // "-ki" (Ankara'daki, İzmir'deki) does not harmonise.
                Emit('k'); Emit('i'); i++;
                continue;
            }
            bool back = Back.Contains(lastVowel);
            bool round = Rounded.Contains(lastVowel);
            if (i == 0 && c is 'd' or 't') Emit(voiceless ? 't' : 'd');
            else if (i == 0 && c is 'c' or 'ç') Emit(voiceless ? 'ç' : 'c');
            else if (IsLow(c)) Emit(back ? 'a' : 'e');
            else if (IsHigh(c)) Emit(back ? (round ? 'u' : 'ı') : (round ? 'ü' : 'i'));
            else Emit(c);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"[\d.]*\d$")]
    private static partial Regex TrailingNumber();

    [GeneratedRegex(@"\p{L}+$")]
    private static partial Regex TrailingWord();

    static Tail? Analyze(string value)
    {
        var s = value.TrimEnd();
        int end = s.Length;
        while (end > 0 && !char.IsLetterOrDigit(s[end - 1])) end--;
        s = s[..end];
        if (s.Length == 0) return null;

        string spoken;
        if (char.IsDigit(s[^1]))
        {
            var digits = TrailingNumber().Match(s).Value.Replace(".", "");
            if (digits.Length > 18) digits = digits[^18..];
            spoken = Tr.Lower(NumberToWords.Convert(BigInteger.Parse(digits)));
        }
        else
        {
            var word = Normalize(Tr.Lower(TrailingWord().Match(s).Value));
            if (word.Length == 0) return null;
            if (!word.Any(IsVowel))
            {
                // Abbreviation without vowels is read letter by letter: TL → "tele", SGK → "segeka".
                var sb = new StringBuilder();
                foreach (var ch in word)
                    sb.Append(LetterNames.TryGetValue(ch, out var n) ? n : "e");
                spoken = sb.ToString();
            }
            else spoken = word;
        }

        char lastVowel = 'e';
        foreach (var ch in spoken)
            if (IsVowel(ch)) lastVowel = ch;
        char last = spoken[^1];
        return new Tail(lastVowel, IsVowel(last), Voiceless.Contains(last));
    }
}
