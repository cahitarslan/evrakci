using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Evrakci.Core.Sets;

namespace Evrakci.App.Services;

/// <summary>
/// The ready-made sets are embedded in the program (from Sablonlar\Hazır) and copied into the
/// library's "Hazır Şablonlar" folder. A set the user has not changed is refreshed when a newer
/// program ships newer templates; a changed set is left alone until the user restores it.
/// </summary>
public static class BuiltInTemplates
{
    const string Prefix = "Hazir/";
    const string MarkerName = ".evrakci-hazir.json";

    sealed class Marker
    {
        public string Version { get; set; } = "";
        public Dictionary<string, string> Files { get; set; } = new();
    }

    static readonly Assembly Asm = typeof(BuiltInTemplates).Assembly;

    /// <summary>Folder name → (file name → resource name).</summary>
    static Dictionary<string, Dictionary<string, string>> Sets()
    {
        var sets = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var res in Asm.GetManifestResourceNames())
        {
            var name = res.Replace('\\', '/');
            if (!name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            var parts = name[Prefix.Length..].Split('/', 2);
            if (parts.Length != 2) continue;
            if (!sets.TryGetValue(parts[0], out var files)) sets[parts[0]] = files = new(StringComparer.OrdinalIgnoreCase);
            files[parts[1]] = res;
        }
        return sets;
    }

    static string Version => Asm.GetName().Version?.ToString() ?? "1";

    /// <summary>Copies missing sets and refreshes unchanged ones. Never throws.</summary>
    public static void EnsureExtracted(TemplateLibrary library)
    {
        foreach (var (folder, files) in Sets())
        {
            try
            {
                var target = Path.Combine(library.BuiltInRoot, folder);
                if (!Directory.Exists(target)) Extract(target, files);
                else if (ReadMarker(target) is { } m && m.Version != Version && Unchanged(target, m)) Extract(target, files);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public static bool IsKnown(string folder) => Sets().ContainsKey(Path.GetFileName(folder.TrimEnd('\\', '/')));

    /// <summary>Puts the set back exactly as shipped. Files the user added are removed.</summary>
    public static void Restore(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        if (!Sets().TryGetValue(name, out var files)) throw new Core.EvrakciException("Bu set için orijinal şablon bulunamadı.");
        foreach (var f in Directory.EnumerateFiles(folder))
        {
            try { File.Delete(f); }
            catch (IOException) { throw new Core.EvrakciException($"“{Path.GetFileName(f)}” silinemedi; Word'de açık olabilir."); }
        }
        Extract(folder, files);
    }

    static void Extract(string target, Dictionary<string, string> files)
    {
        Directory.CreateDirectory(target);
        var marker = new Marker { Version = Version };
        foreach (var (file, res) in files)
        {
            using var s = Asm.GetManifestResourceStream(res)!;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var bytes = ms.ToArray();
            File.WriteAllBytes(Path.Combine(target, file), bytes);
            marker.Files[file] = Hash(bytes);
        }
        var markerPath = Path.Combine(target, MarkerName);
        File.WriteAllText(markerPath, JsonSerializer.Serialize(marker));
        File.SetAttributes(markerPath, FileAttributes.Hidden);
    }

    static Marker? ReadMarker(string folder)
    {
        try
        {
            var path = Path.Combine(folder, MarkerName);
            return File.Exists(path) ? JsonSerializer.Deserialize<Marker>(File.ReadAllText(path)) : null;
        }
        catch (JsonException) { return null; }
    }

    static bool Unchanged(string folder, Marker m)
    {
        var present = Directory.EnumerateFiles(folder).Select(Path.GetFileName).Where(n => n != MarkerName).ToList();
        if (present.Count != m.Files.Count) return false;
        foreach (var (file, hash) in m.Files)
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path) || Hash(File.ReadAllBytes(path)) != hash) return false;
        }
        return true;
    }

    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
