using System.IO;
using System.Windows;
using System.Windows.Threading;
using FileRedact.App.Services;
using FileRedact.Core;
using FileRedact.Core.Printer;

namespace FileRedact.App;

public partial class App : Application
{
    private SingleInstance? _single;
    private TrayIcon? _tray;
    private InboxWatcher? _watcher;

    public static new App Current => (App)Application.Current;

    /// <summary>True while the printer inbox is being watched; closing the last window then hides it instead of exiting.</summary>
    public bool KeepRunningInBackground => _watcher != null;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) => { Log(args.Exception.ToString()); args.SetObserved(); };

        _single = new SingleInstance();
        if (!_single.IsFirstInstance)
        {
            SingleInstance.SendToRunningInstance(e.Args);
            Shutdown();
            return;
        }

        AppPaths.EnsureUserFolders();
        _single.ArgumentsReceived += args => Dispatcher.BeginInvoke(() => HandleArguments(args));
        _single.StartServer();

        _tray = new TrayIcon();
        _tray.OpenRequested += () => ShowMainWindow();
        _tray.OpenFileRequested += () => ShowMainWindow().ViewModel.OpenFileCommand.Execute(null);
        _tray.TogglePrinterRequested += () => ShowMainWindow().ViewModel.TogglePrinterCommand.Execute(null);
        _tray.ToggleStartupRequested += ToggleStartup;
        _tray.ExitRequested += ExitApplication;

        SyncWatcher();
        HandleArguments(e.Args);
    }

    private void HandleArguments(string[] args)
    {
        var files = args.Where(a => !a.StartsWith("--") && File.Exists(a)).ToList();
        var watchOnly = args.Contains("--watch", StringComparer.OrdinalIgnoreCase);

        if (files.Count > 0)
        {
            foreach (var f in files) OpenDocument(f);
            return;
        }
        if (!watchOnly) ShowMainWindow();
        // --watch with no file: stay in the notification area and wait for print jobs.
    }

    /// <summary>Starts or stops the inbox watcher to match whether the virtual printer is installed.</summary>
    public void SyncWatcher()
    {
        var installed = PrinterInstaller.IsInstalled();
        if (installed && _watcher == null)
        {
            _watcher = new InboxWatcher();
            _watcher.DocumentReceived += path => Dispatcher.BeginInvoke(() =>
            {
                _tray?.Notify("FileRedact", "A printed document arrived and is being scanned for personal information.");
                OpenDocument(path);
            });
            _watcher.Error += msg => Log("Watcher: " + msg);
            _watcher.Start();
        }
        else if (!installed && _watcher != null)
        {
            _watcher.Dispose();
            _watcher = null;
        }
        _tray?.SetState(installed, PrinterInstaller.IsWatcherRegisteredAtLogin());
    }

    public MainWindow ShowMainWindow()
    {
        var w = Windows.OfType<MainWindow>().FirstOrDefault(x => x.IsVisible) ?? Windows.OfType<MainWindow>().FirstOrDefault() ?? new MainWindow();
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        return w;
    }

    public async void OpenDocument(string path)
    {
        // Re-use an empty or hidden window; otherwise open the document alongside the current one.
        var w = Windows.OfType<MainWindow>().FirstOrDefault(x => !x.IsVisible)
                ?? Windows.OfType<MainWindow>().FirstOrDefault(x => !x.ViewModel.HasDocument && !x.ViewModel.IsBusy)
                ?? new MainWindow();
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        await w.ViewModel.LoadFileAsync(path);
    }

    private void ToggleStartup()
    {
        if (PrinterInstaller.IsWatcherRegisteredAtLogin()) PrinterInstaller.UnregisterWatcherAtLogin();
        else PrinterInstaller.RegisterWatcherAtLogin(Environment.ProcessPath ?? "FileRedact.exe");
        _tray?.SetState(PrinterInstaller.IsInstalled(), PrinterInstaller.IsWatcherRegisteredAtLogin());
    }

    public void ExitApplication()
    {
        _watcher?.Dispose();
        _watcher = null;
        _tray?.Dispose();
        _tray = null;
        _single?.Dispose();
        Shutdown();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception.ToString());
        MessageBox.Show(e.Exception.Message, "FileRedact - unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    public static void Log(string message)
    {
        try
        {
            AppPaths.EnsureUserFolders();
            File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:s} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
