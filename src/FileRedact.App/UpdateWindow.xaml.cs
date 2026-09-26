using System.Windows;
using FileRedact.Core.Update;

namespace FileRedact.App;

/// <summary>
/// One simple question for the user: install the new version now, or not now. The installer is fetched
/// automatically (or has already been fetched in the background) and the button stays disabled until the
/// download has been verified.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly ReleaseInfo _release;
    private readonly CancellationTokenSource _cts = new();
    private string? _downloadedPath;
    private bool _downloading;

    /// <summary>Set when the user chose to install; the path of the verified installer.</summary>
    public string? InstallerPath { get; private set; }

    public UpdateWindow(ReleaseInfo release, Version current, string? alreadyDownloadedPath)
    {
        InitializeComponent();
        _release = release;
        _downloadedPath = alreadyDownloadedPath;

        Headline.Text = $"A new version of FileRedact is ready ({release.Version.ToString(3)})";
        Detail.Text = $"You have version {current.ToString(3)}. Installing takes about a minute. FileRedact will close, update itself and open again automatically.";
        Notes.Text = string.IsNullOrWhiteSpace(release.Notes) ? "(No release notes were provided.)" : release.Notes.Trim();
        NotesExpander.Visibility = string.IsNullOrWhiteSpace(release.Notes) ? Visibility.Collapsed : Visibility.Visible;

        if (_downloadedPath != null)
        {
            ProgressText.Text = "The update has been downloaded and checked.";
            Progress.Value = 1;
            InstallButton.IsEnabled = true;
        }
        else
        {
            InstallButton.IsEnabled = false;
            Loaded += async (_, _) => await DownloadAsync();
        }
    }

    private async Task DownloadAsync()
    {
        _downloading = true;
        ProgressText.Text = "Downloading the update…";
        try
        {
            var progress = new Progress<double>(v =>
            {
                Progress.Value = v;
                ProgressText.Text = v >= 1 ? "Checking the download…" : $"Downloading the update… {v:P0}";
            });
            _downloadedPath = await UpdateChecker.DownloadAssetAsync(_release, progress, _cts.Token);
            ProgressText.Text = "The update has been downloaded and checked.";
            Progress.Value = 1;
            InstallButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            // window closed
        }
        catch (Exception ex)
        {
            ProgressText.Text = "The download failed: " + ex.Message + "\nPlease try again later from \"Check for updates\".";
            Progress.Value = 0;
        }
        finally
        {
            _downloading = false;
        }
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadedPath == null) return;
        InstallerPath = _downloadedPath;
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        InstallerPath = null;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_downloading) _cts.Cancel();
        base.OnClosed(e);
    }
}
