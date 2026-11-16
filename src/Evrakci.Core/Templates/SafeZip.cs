using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Evrakci.Core.Templates;

/// <summary>An OOXML / UDF package read fully into memory with size limits (templates are untrusted).</summary>
public sealed class ZipPackage
{
    /// <summary>Entries in original order.</summary>
    public List<(string Name, byte[] Data)> Entries { get; } = new();

    public byte[]? Get(string name)
    {
        foreach (var (n, d) in Entries)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return d;
        return null;
    }

    public bool Has(string name) => Get(name) is not null;

    public byte[] Write(IReadOnlyDictionary<string, byte[]> replaced)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in Entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var s = entry.Open();
                s.Write(replaced.TryGetValue(name, out var r) ? r : data);
            }
        }
        return ms.ToArray();
    }
}

public static class SafeZip
{
    public const long MaxFileBytes = 50L * 1024 * 1024;
    public const long MaxEntryBytes = 100L * 1024 * 1024;
    public const long MaxTotalBytes = 250L * 1024 * 1024;
    public const int MaxEntries = 3000;
    const int MaxRatio = 200;

    public static ZipPackage Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new EvrakciException("Dosya bulunamadı.");
        if (info.Length > MaxFileBytes) throw new EvrakciException("Dosya 50 MB sınırını aşıyor.");
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (IOException) { throw new EvrakciException("Dosya okunamadı. Başka bir programda açık olabilir."); }
        catch (UnauthorizedAccessException) { throw new EvrakciException("Dosyayı okuma izni yok."); }
        return Read(bytes);
    }

    public static ZipPackage Read(byte[] bytes)
    {
        if (bytes.Length > MaxFileBytes) throw new EvrakciException("Dosya 50 MB sınırını aşıyor.");
        var package = new ZipPackage();
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries) throw new EvrakciException("Dosyanın iç yapısı olağandışı (çok fazla parça).");
            long total = 0;
            var buffer = new byte[81920];
            foreach (var e in zip.Entries)
            {
                if (e.FullName.EndsWith('/')) continue;
                if (e.Length > MaxEntryBytes || (e.CompressedLength > 0 && e.Length > 1_000_000 && e.Length / e.CompressedLength > MaxRatio))
                    throw new EvrakciException("Dosya olağandışı derecede sıkıştırılmış; güvenlik nedeniyle açılmadı.");
                using var s = e.Open();
                using var ms = new MemoryStream();
                int read;
                // Count what is actually decompressed; header sizes can lie.
                while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (ms.Length + read > MaxEntryBytes || total > MaxTotalBytes)
                        throw new EvrakciException("Dosya açıldığında çok büyük; güvenlik nedeniyle açılmadı.");
                    ms.Write(buffer, 0, read);
                }
                package.Entries.Add((e.FullName, ms.ToArray()));
            }
        }
        catch (InvalidDataException)
        {
            throw new EvrakciException("Dosya bozuk ya da geçerli bir belge değil.");
        }
        return package;
    }

    static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 200_000_000,
        IgnoreWhitespace = false,
    };

    public static XDocument ParseXml(byte[] data)
    {
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(data), XmlSettings);
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            throw new EvrakciException("Belgenin içeriği bozuk (XML okunamadı).");
        }
    }

    public static byte[] SerializeXml(XDocument doc)
    {
        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(false),
            Indent = false,
            NewLineHandling = NewLineHandling.None,
        };
        using (var w = XmlWriter.Create(ms, settings)) doc.Save(w);
        return ms.ToArray();
    }
}
