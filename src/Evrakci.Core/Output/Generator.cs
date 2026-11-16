using Evrakci.Core.Cases;
using Evrakci.Core.Sets;
using Evrakci.Core.Templates;
using Evrakci.Core.Text;

namespace Evrakci.Core.Output;

/// <summary>Formats to produce from Word templates; UDF templates always produce UDF.</summary>
[Flags]
public enum OutputFormats { Word = 1, Udf = 2, Both = Word | Udf }

public sealed record PlannedOutput(SetDocument Document, string Path, DocumentFormat Format = DocumentFormat.Docx)
{
    public bool Exists => File.Exists(Path);
}

public static class Generator
{
    /// <summary>Works out output paths: base folder / rendered outputFolder / rendered file name.</summary>
    /// <param name="documents">Template names to produce; null for all.</param>
    public static List<PlannedOutput> Plan(TemplateSet set, Resolution res, string baseFolder, IReadOnlySet<string>? documents = null,
        OutputFormats formats = OutputFormats.Word)
    {
        if ((formats & OutputFormats.Both) == 0) formats = OutputFormats.Word;
        var values = new ResolvedValues(res);
        var folder = Path.GetFullPath(baseFolder);
        if (!string.IsNullOrWhiteSpace(set.Definition.OutputFolder))
        {
            var rendered = Planner.RenderString(set.Definition.OutputFolder!, set.Matcher, values);
            foreach (var part in rendered.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = Tr.SanitizeFileName(part);
                if (clean is "." or "..") continue;
                folder = Path.Combine(folder, clean);
            }
        }

        var templates = new HashSet<string>(set.Documents.Select(d => Path.GetFullPath(d.Path)), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plan = new List<PlannedOutput>();
        foreach (var doc in set.Documents.Where(d => d.Template is not null && (documents is null || documents.Contains(d.Entry.Template))))
        {
            var name = Tr.SanitizeFileName(Planner.RenderString(doc.OutputPattern, set.Matcher, values));
            var targets = doc.Template!.Format == DocumentFormat.Udf
                ? [DocumentFormat.Udf]
                : new[] { DocumentFormat.Docx, DocumentFormat.Udf }.Where(f => formats.HasFlag(f == DocumentFormat.Docx ? OutputFormats.Word : OutputFormats.Udf));
            foreach (var format in targets)
            {
                var ext = format == DocumentFormat.Udf ? ".udf" : ".docx";
                // Output is in the chosen format whatever extension the name pattern has.
                var stem = Path.GetExtension(name).ToLowerInvariant() is ".docx" or ".udf" ? Path.GetFileNameWithoutExtension(name) : name;
                var path = Path.Combine(folder, stem + ext);
                for (int n = 2; used.Contains(path); n++)
                    path = Path.Combine(folder, $"{stem} ({n}){ext}");
                if (templates.Contains(Path.GetFullPath(path)))
                    throw new EvrakciException("Çıktı dosyası bir şablonla aynı yere düşüyor; şablonlar değiştirilemez. Başka bir klasör seçin.");
                used.Add(path);
                plan.Add(new PlannedOutput(doc, path, format));
            }
        }
        return plan;
    }

    /// <summary>Renders a planned output, converting a Word template to UDF when that format was chosen.</summary>
    public static byte[] Render(PlannedOutput output, Resolution res, SuffixOverrides overrides)
    {
        var bytes = Render(output.Document, res, overrides);
        return output.Format == DocumentFormat.Udf && output.Document.Template!.Format == DocumentFormat.Docx
            ? Templates.Udf.DocxToUdf.Convert(bytes)
            : bytes;
    }

    public static byte[] Render(SetDocument doc, Resolution res, SuffixOverrides overrides)
    {
        if (!res.IsValid) throw new EvrakciException("Eksik ya da hatalı alanlar varken belge üretilemez.");
        return doc.Template!.Render(new ResolvedValues(res, overrides.For(doc.Entry.Template)));
    }

    /// <summary>Writes every planned file. Existing files are only replaced when <paramref name="overwrite"/> is set.</summary>
    public static void Write(IEnumerable<PlannedOutput> plan, Resolution res, SuffixOverrides overrides, bool overwrite)
    {
        var items = plan.ToList();
        if (!overwrite && items.Any(i => i.Exists))
            throw new EvrakciException("Bazı dosyalar zaten var; üzerine yazmak için onay gerekli.");
        // Render everything first so a failure leaves no half-written set behind.
        var rendered = items.Select(i => (i.Path, Bytes: Render(i, res, overrides))).ToList();
        foreach (var (path, bytes) in rendered)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".evrakci-tmp";
            try
            {
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, path, overwrite);
            }
            catch (IOException)
            {
                TryDelete(tmp);
                throw new EvrakciException($"“{Path.GetFileName(path)}” yazılamadı. Dosya başka bir programda açık olabilir.");
            }
            catch (UnauthorizedAccessException)
            {
                TryDelete(tmp);
                throw new EvrakciException("Seçilen klasöre yazma izni yok.");
            }
        }
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
