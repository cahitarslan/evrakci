using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Evrakci.Tests;

static class TestPaths
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Evrakci.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        }
    }

    public static string BuiltInSets => Path.Combine(Root, "Sablonlar", "Hazır");

    public static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "evrakci-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }
}

/// <summary>Plain text of a .docx, paragraph by paragraph (nested text box text included in the outer paragraph).</summary>
static class DocxText
{
    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static List<string> Paragraphs(byte[] docx, string part = "word/document.xml")
    {
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var s = zip.GetEntry(part)!.Open();
        var doc = XDocument.Load(s);
        return doc.Descendants(W + "p")
            .Select(p => string.Concat(p.Descendants()
                .Where(e => (e.Name == W + "t" || (e.Name == W + "tab" && e.Parent?.Name == W + "r")) && e.Ancestors(W + "p").First() == p)
                .Select(e => e.Name == W + "tab" ? "\t" : e.Value)))
            .ToList();
    }

    public static string All(byte[] docx) => string.Join("\n", Paragraphs(docx));

    public static string NoSpace(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (!char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }
}
