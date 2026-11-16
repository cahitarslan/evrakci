using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Evrakci.Core;
using Evrakci.Core.Cases;
using Evrakci.Core.Text;

namespace Evrakci.App.Services;

/// <summary>A mediation file in the list on the home screen.</summary>
public sealed record StoredCase(string Path, string Title, string? SetName, string? SetFolder, DateTimeOffset Modified,
    string Status, bool Generated);

/// <summary>
/// The user's mediation files ("Dosyalarım"), saved automatically in Belgelerim\Evrakçı\Dosyalar.
/// Each file is a small JSON envelope whose content is encrypted for the current Windows user
/// (DPAPI): no password is asked, and the file cannot be read on another computer or account.
/// "Dışa aktar" writes a portable copy in the regular .evrak format (optionally password protected).
/// </summary>
public sealed class CaseStore(string folder)
{
    const string Protection = "windows-user";
    static readonly byte[] Entropy = "Evrakçı dosya"u8.ToArray();

    public string Folder { get; } = folder;

    public static bool IsWindowsProtected(string path)
    {
        try { return JsonNode.Parse(File.ReadAllBytes(path))?["protection"]?.GetValue<string>() == Protection; }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or UnauthorizedAccessException) { return false; }
    }

    public CaseData Load(string path)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { throw new EvrakciException("Dosya okunamadı; biçimi bozuk."); }
        catch (IOException) { throw new EvrakciException("Dosya okunamadı."); }
        if (node?["protection"]?.GetValue<string>() != Protection) throw new EvrakciException("Bu, Evrakçı'nın kendi dosyası değil.");
        try
        {
            var plain = ProtectedData.Unprotect(Convert.FromBase64String(node["data"]!.GetValue<string>()), Entropy, DataProtectionScope.CurrentUser);
            return CaseFile.Parse(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or NullReferenceException)
        {
            throw new EvrakciException("Bu dosya başka bir bilgisayarda ya da başka bir Windows kullanıcısıyla kaydedilmiş; burada açılamaz. Orada “Dışa aktar” ile kopyasını alıp burada “İçe aktar” kullanın.");
        }
    }

    /// <summary>Writes the case; the file is named after its title and renamed when the title changes. Returns the path.</summary>
    public string Save(string? path, CaseData data)
    {
        Directory.CreateDirectory(Folder);
        var name = Tr.SanitizeFileName(string.IsNullOrWhiteSpace(data.Title) ? "Adsız dosya" : data.Title!);
        string target;
        if (path is not null && string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)) target = path;
        else
        {
            target = Path.Combine(Folder, name + CaseFile.Extension);
            for (int n = 2; File.Exists(target) && !string.Equals(target, path, StringComparison.OrdinalIgnoreCase); n++)
                target = Path.Combine(Folder, $"{name} ({n}){CaseFile.Extension}");
        }
        var envelope = new JsonObject
        {
            ["formatVersion"] = CaseFile.CurrentFormatVersion,
            ["app"] = "Evrakçı",
            ["protection"] = Protection,
            ["data"] = Convert.ToBase64String(ProtectedData.Protect(CaseFile.Serialize(data), Entropy, DataProtectionScope.CurrentUser)),
        };
        var tmp = target + ".tmp";
        File.WriteAllText(tmp, envelope.ToJsonString());
        File.Move(tmp, target, overwrite: true);
        if (path is not null && !string.Equals(path, target, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) File.Delete(path);
        return target;
    }

    public List<StoredCase> List()
    {
        var list = new List<StoredCase>();
        if (!Directory.Exists(Folder)) return list;
        foreach (var path in Directory.EnumerateFiles(Folder, "*" + CaseFile.Extension))
        {
            try
            {
                var d = Load(path);
                list.Add(new StoredCase(path, d.Title ?? Path.GetFileNameWithoutExtension(path), d.SetName, d.SetFolder,
                    d.SavedAt == default ? File.GetLastWriteTime(path) : d.SavedAt, StatusOf(d), d.GeneratedAt is not null));
            }
            catch (EvrakciException)
            {
                list.Add(new StoredCase(path, Path.GetFileNameWithoutExtension(path), null, null, File.GetLastWriteTime(path), "Açılamıyor", false));
            }
        }
        return list.OrderByDescending(c => c.Modified).ToList();
    }

    /// <summary>"Anlaşma", "Anlaşamama" … from the chosen documents; "Belgeler üretildi" once produced.</summary>
    static string StatusOf(CaseData d)
    {
        var docs = d.Documents ?? new();
        string? outcome = docs.Any(x => x.Contains("Katılmama", StringComparison.OrdinalIgnoreCase)) ? "Katılmama"
            : docs.Any(x => x.Contains("Anlaşamama", StringComparison.OrdinalIgnoreCase)) ? "Anlaşamama"
            : docs.Any(x => x.Contains("Anlaşma", StringComparison.OrdinalIgnoreCase)) ? "Anlaşma"
            : null;
        var state = d.GeneratedAt is { } g ? $"Belgeler üretildi · {g.LocalDateTime:dd.MM.yyyy}" : "Devam ediyor";
        return outcome is null ? state : $"{outcome} · {state}";
    }
}
