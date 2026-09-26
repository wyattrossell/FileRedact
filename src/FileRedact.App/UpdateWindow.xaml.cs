using System.Diagnostics;
using System.IO;
using System.Windows;
using FileRedact.Core.Update;

namespace FileRedact.App;

public enum UpdateChoice { Later, Skip, Downloaded, OpenedPage }

public partial class UpdateWindow : Window
{
    private readonly ReleaseInfo _release;
    private CancellationTokenSource? _cts;

    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    public UpdateWindow(ReleaseInfo release, Version current)
    {
        InitializeComponent();
        _release = release;
        Headline.Text = $"FileRedact {release.Version.ToString(3)} is available";
        var when = release.Published is { } p ? $" Released {p.LocalDateTime:d}." : "";
        VersionLine.Text = $"You are running version {current.ToString(3)}.{when}";
        Notes.Text = string.IsNullOrWhiteSpace(release.Notes) ? "(No release notes were provided.)" : release.Notes.Trim();
        if (release.AssetUrl == null)
        {
            DownloadButton.Content = "Open download page";
        }
        else
        {
            DownloadButton.ToolTip = $"Downloads {release.AssetName} to your Downloads folder";
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_release.AssetUrl == null)
        {
            OpenPage();
            Choice = UpdateChoice.OpenedPage;
            Close();
            return;
        }

        DownloadButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        LaterButton.Content = "Cancel";
        Progress.Visibility = Visibility.Visible;
        ProgressText.Visibility = Visibility.Visible;
        ProgressText.Text = $"Downloading {_release.AssetName}…";
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(v => { Progress.Value = v; ProgressText.Text = $"Downloading {_release.AssetName}… {v:P0}"; });
            var path = await UpdateChecker.DownloadAssetAsync(_release, progress, _cts.Token);
            Choice = UpdateChoice.Downloaded;
            ProgressText.Text = $"Saved to {path}";
            MessageBox.Show(this,
                $"The update was saved to:\n{path}\n\nClose FileRedact, extract the files over your current installation (or run the installer), then start FileRedact again.",
                "FileRedact update downloaded", MessageBoxButton.OK, MessageBoxImage.Information);
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
            Close();
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Download cancelled.";
            ResetButtons();
        }
        catch (Exception ex)
        {
            ProgressText.Text = "Download failed: " + ex.Message;
            ResetButtons();
        }
    }

    private void ResetButtons()
    {
        DownloadButton.IsEnabled = true;
        SkipButton.IsEnabled = true;
        LaterButton.Content = "Remind me later";
        Progress.Visibility = Visibility.Collapsed;
        _cts = null;
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        Choice = UpdateChoice.Later;
        Close();
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        Choice = UpdateChoice.Skip;
        Close();
    }

    private void PageLink_Click(object sender, RoutedEventArgs e) => OpenPage();

    private void OpenPage()
    {
        try { Process.Start(new ProcessStartInfo(_release.PageUrl) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "FileRedact", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
