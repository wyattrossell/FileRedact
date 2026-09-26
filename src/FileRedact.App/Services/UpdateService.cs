using System.Windows;
using FileRedact.Core.Settings;
using FileRedact.Core.Update;

namespace FileRedact.App.Services;

/// <summary>Decides when to look for a new release and shows the update prompt.</summary>
public sealed class UpdateService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(20);
    private bool _checking;

    /// <summary>Automatic check shortly after start-up, at most once per day, honouring the user's opt-out and skipped versions.</summary>
    public async Task CheckOnStartupAsync()
    {
        var settings = UserSettings.Current;
        if (!settings.CheckForUpdates) return;
        if (settings.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < CheckInterval) return;
        await Task.Delay(TimeSpan.FromSeconds(6)); // let the main window settle first
        await CheckAsync(manual: false, owner: null);
    }

    public async Task CheckAsync(bool manual, Window? owner)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var settings = UserSettings.Current;
            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            TrySave(settings);

            var latest = await UpdateChecker.GetLatestReleaseAsync();
            var current = UpdateChecker.CurrentVersion;

            if (latest == null)
            {
                if (manual)
                    MessageBox.Show(owner ?? App.Current.MainWindow!, $"Could not retrieve release information from GitHub.\n\nYou are running FileRedact {current.ToString(3)}. Releases are published at:\n{UpdateChecker.ReleasesPageUrl}",
                        "FileRedact - check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (latest.Version <= current)
            {
                if (manual)
                    MessageBox.Show(owner ?? App.Current.MainWindow!, $"You have the latest version of FileRedact ({current.ToString(3)}).",
                        "FileRedact - check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!manual && string.Equals(settings.SkippedUpdateVersion, latest.Tag, StringComparison.OrdinalIgnoreCase))
                return;

            var window = new UpdateWindow(latest, current);
            if (owner != null && owner.IsVisible) window.Owner = owner;
            else if (App.Current.MainWindow is { IsVisible: true } mw) window.Owner = mw;
            window.ShowDialog();

            if (window.Choice == UpdateChoice.Skip)
            {
                settings.SkippedUpdateVersion = latest.Tag;
                TrySave(settings);
            }
            else if (window.Choice == UpdateChoice.Downloaded)
            {
                settings.SkippedUpdateVersion = null;
                TrySave(settings);
            }
        }
        catch (Exception ex)
        {
            App.Log("Update check failed: " + ex);
            if (manual)
                MessageBox.Show(ex.Message, "FileRedact - check for updates", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _checking = false;
        }
    }

    private static void TrySave(UserSettings settings)
    {
        try { settings.Save(); } catch { }
    }
}
