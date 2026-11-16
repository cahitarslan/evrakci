using Velopack;
using Velopack.Sources;

namespace Evrakci.App.Services;

/// <summary>
/// Automatic updates (Velopack). On start the app asks <see cref="Distribution.UpdateUrl"/> for a newer version
/// and downloads it in the background; only the version number is compared, nothing about the user is sent.
/// The update is installed when the app closes, or at once with "Güncellemeyi yükle".
/// Does nothing in development builds and while no update address is set.
/// </summary>
public sealed class UpdateService
{
    UpdateManager? _manager;
    UpdateInfo? _update;

    public bool IsReady => _update is not null;

    /// <summary>Raised on the calling thread once an update has been downloaded.</summary>
    public event Action? Ready;

    public async Task CheckAsync()
    {
        if (string.IsNullOrWhiteSpace(Distribution.UpdateUrl)) return;
        try
        {
            var manager = new UpdateManager(new SimpleWebSource(Distribution.UpdateUrl));
            if (!manager.IsInstalled) return;
            var update = await manager.CheckForUpdatesAsync();
            if (update is null) return;
            await manager.DownloadUpdatesAsync(update);
            _manager = manager;
            _update = update;
            Ready?.Invoke();
        }
        // No internet, server down, disk full …: try again on the next start.
        catch (Exception) { }
    }

    /// <summary>Closes the app, installs the downloaded update and starts the new version.</summary>
    public void ApplyAndRestart()
    {
        if (_manager is not null && _update is not null) _manager.ApplyUpdatesAndRestart(_update.TargetFullRelease);
    }

    /// <summary>Called on exit: the downloaded update is installed once the app has closed.</summary>
    public void ApplyOnExit()
    {
        if (_manager is null || _update is null) return;
        try { _manager.WaitExitThenApplyUpdates(_update.TargetFullRelease, silent: true, restart: false); }
        catch (Exception) { }
    }
}
