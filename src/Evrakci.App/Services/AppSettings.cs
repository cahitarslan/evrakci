using System.IO;
using System.Text.Json;

namespace Evrakci.App.Services;

/// <summary>
/// Per-user preferences in %APPDATA%\Evrakci\ayarlar.json. Only folder paths are stored here —
/// never case values or other personal data.
/// </summary>
public sealed class AppSettings
{
    public List<string> RecentSets { get; set; } = new();
    public string? LastOutputFolder { get; set; }
    /// <summary>Output formats chosen last ("Word", "Udf" or "Both").</summary>
    public string? OutputFormats { get; set; }
    /// <summary>"Sistem", "Açık" or "Koyu".</summary>
    public string? Theme { get; set; }
    public string? LastCaseFolder { get; set; }
    /// <summary>Template library folder; null means Belgelerim\Evrakçı\Şablonlar.</summary>
    public string? LibraryFolder { get; set; }
    /// <summary>Ask the update address for a newer version on start (the only automatic network access).</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>The user has been asked once whether updates may be checked automatically.</summary>
    public bool UpdateChoiceMade { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string LibraryRoot => string.IsNullOrWhiteSpace(LibraryFolder)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Evrakçı", "Şablonlar")
        : LibraryFolder!;

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Evrakci");
    static string FilePath => Path.Combine(Dir, "ayarlar.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void AddRecentSet(string folder)
    {
        RecentSets.RemoveAll(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase));
        RecentSets.Insert(0, folder);
        if (RecentSets.Count > 10) RecentSets.RemoveRange(10, RecentSets.Count - 10);
        Save();
    }
}
