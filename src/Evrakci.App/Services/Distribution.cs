namespace Evrakci.App.Services;

/// <summary>
/// Web addresses of the project. UpdateUrl is the only one the app contacts by itself; while it is empty updates are off
/// (forks and self-built copies should point it at their own release folder or leave it empty).
/// </summary>
public static class Distribution
{
    /// <summary>Folder the Velopack release files (releases.win.json, *.nupkg) are published to.</summary>
    public const string UpdateUrl = "https://evrakci.netlify.app/guncelleme";

    /// <summary>Source code and issue tracker; opened from the menu.</summary>
    public const string SourceUrl = "https://github.com/cahitarslan/evrakci";
}
