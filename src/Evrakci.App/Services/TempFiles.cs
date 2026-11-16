using System.Diagnostics;
using System.IO;

namespace Evrakci.App.Services;

/// <summary>
/// Documents opened "in Word" from the preview are written to %TEMP%\Evrakci\&lt;session&gt;.
/// They contain personal data, so the folder is deleted on exit and leftovers on the next start.
/// </summary>
public static class TempFiles
{
    static readonly string Root = Path.Combine(Path.GetTempPath(), "Evrakci");
    static readonly string Session = Path.Combine(Root, Guid.NewGuid().ToString("N"));

    public static string Write(string fileName, byte[] bytes)
    {
        Directory.CreateDirectory(Session);
        var path = Path.Combine(Session, fileName);
        for (int n = 2; File.Exists(path) && !TryDelete(path); n++)
            path = Path.Combine(Session, $"{Path.GetFileNameWithoutExtension(fileName)} ({n}){Path.GetExtension(fileName)}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>A new empty folder inside this session's temporary folder.</summary>
    public static string NewFolder()
    {
        var dir = Path.Combine(Session, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void OpenWithDefaultProgram(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public static void CleanupSession() => TryDeleteDir(Session);

    public static void CleanupStale()
    {
        try
        {
            if (!Directory.Exists(Root)) return;
            foreach (var dir in Directory.EnumerateDirectories(Root))
                if (!string.Equals(dir, Session, StringComparison.OrdinalIgnoreCase)) TryDeleteDir(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    static bool TryDelete(string path)
    {
        try { File.Delete(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
