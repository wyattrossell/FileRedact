using System.Diagnostics;
using System.IO;
using System.Windows;
using FileRedact.Core;
using FileRedact.Core.Settings;
using FileRedact.Core.Update;

namespace FileRedact.App.Services;

/// <summary>
/// Keeps FileRedact up to date with as little effort from the user as possible: the new installer is
/// downloaded in the background, verified, and then the user is asked once whether to install it now.
/// Installing runs the setup silently and restarts FileRedact; no web pages or manual steps are involved.
/// </summary>
public sealed class UpdateService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(20);
    private bool _busy;

    /// <summary>Automatic check shortly after start-up, at most once per day, honouring the user's opt-out.</summary>
    public async Task CheckOnStartupAsync()
    {
        UpdateChecker.CleanupOldDownloads();
        var settings = UserSettings.Current;
        if (!settings.CheckForUpdates) return;
        if (settings.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < CheckInterval) return;
        await Task.Delay(TimeSpan.FromSeconds(8)); // let the main window settle first
        await CheckAsync(manual: false, owner: null);
    }

    public async Task CheckAsync(bool manual, Window? owner)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var settings = UserSettings.Current;
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            TrySave(settings);

            var latest = await UpdateChecker.GetLatestReleaseAsync();
            var current = UpdateChecker.CurrentVersion;
            owner ??= App.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.IsVisible);

            if (latest == null)
            {
                if (manual)
                    Info(owner, $"FileRedact could not reach the update server right now.\n\nYou are running version {current.ToString(3)}. Please try again later.");
                return;
            }

            if (latest.Version <= current)
            {
                if (manual) Info(owner, $"FileRedact is up to date (version {current.ToString(3)}).");
                return;
            }

            if (!UpdateChecker.IsInstaller(latest))
            {
                // A release without an installer cannot be applied automatically; point at the page as a fallback.
                if (manual && MessageBox.Show(owner!, $"Version {latest.Version.ToString(3)} is available but has no installer attached. Open the download page?",
                        "FileRedact update", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(latest.PageUrl) { UseShellExecute = true });
                return;
            }

            // Automatic checks fetch the installer quietly first, so the prompt is a single "install now" question.
            string? downloaded = null;
            if (!manual)
            {
                try { downloaded = await UpdateChecker.DownloadAssetAsync(latest); }
                catch (Exception ex) { App.Log("Background update download failed: " + ex.Message); return; }
            }

            var window = new UpdateWindow(latest, current, downloaded);
            if (owner is { IsVisible: true }) window.Owner = owner;
            window.ShowDialog();

            if (window.InstallerPath != null)
                LaunchInstaller(window.InstallerPath, owner);
        }
        catch (Exception ex)
        {
            App.Log("Update check failed: " + ex);
            if (manual) Info(owner, "The update could not be completed: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Starts the silent upgrade and exits FileRedact. The installer restarts the app when it is done.</summary>
    private static void LaunchInstaller(string installerPath, Window? owner)
    {
        AppPaths.EnsureUserFolders();
        var log = Path.Combine(AppPaths.UserData, "update-install.log");
        try
        {
            Process.Start(new ProcessStartInfo(installerPath)
            {
                Arguments = UpdateChecker.BuildSilentInstallArguments(log),
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath),
            });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Info(owner, "The update was cancelled. You can install it later from \"Check for updates\".");
            return;
        }
        catch (Exception ex)
        {
            Info(owner, "The update could not be started: " + ex.Message);
            return;
        }
        App.Current.ExitApplication();
    }

    private static void Info(Window? owner, string message)
    {
        if (owner is { IsVisible: true })
            MessageBox.Show(owner, message, "FileRedact update", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(message, "FileRedact update", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void TrySave(UserSettings settings)
    {
        try { settings.Save(); } catch { }
    }
}
