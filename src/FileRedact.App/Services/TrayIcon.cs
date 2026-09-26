using System.Drawing;
using Font = System.Drawing.Font;
using FontStyle = System.Drawing.FontStyle;
using Icon = System.Drawing.Icon;
using SystemIcons = System.Drawing.SystemIcons;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace FileRedact.App.Services;

/// <summary>Notification-area icon shown while FileRedact waits for print jobs in the background.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _printerItem;
    private readonly WinForms.ToolStripMenuItem _startupItem;

    public event Action? OpenRequested;
    public event Action? OpenFileRequested;
    public event Action? TogglePrinterRequested;
    public event Action? ToggleStartupRequested;
    public event Action? CheckUpdatesRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        var open = new WinForms.ToolStripMenuItem("Open FileRedact");
        open.Font = new Font(open.Font, FontStyle.Bold);
        open.Click += (_, _) => OpenRequested?.Invoke();
        var openFile = new WinForms.ToolStripMenuItem("Open document…");
        openFile.Click += (_, _) => OpenFileRequested?.Invoke();
        _printerItem = new WinForms.ToolStripMenuItem("Install FileRedact printer…");
        _printerItem.Click += (_, _) => TogglePrinterRequested?.Invoke();
        _startupItem = new WinForms.ToolStripMenuItem("Start with Windows");
        _startupItem.Click += (_, _) => ToggleStartupRequested?.Invoke();
        var updates = new WinForms.ToolStripMenuItem("Check for updates�");
        updates.Click += (_, _) => CheckUpdatesRequested?.Invoke();
        var exit = new WinForms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.AddRange(new WinForms.ToolStripItem[] { open, openFile, new WinForms.ToolStripSeparator(), _printerItem, _startupItem, new WinForms.ToolStripSeparator(), updates, exit });

        _icon = new WinForms.NotifyIcon
        {
            Text = "FileRedact - waiting for print jobs",
            ContextMenuStrip = menu,
            Visible = true,
            Icon = LoadIcon(),
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public void SetState(bool printerInstalled, bool startsWithWindows)
    {
        _printerItem.Text = printerInstalled ? "Remove FileRedact printer…" : "Install FileRedact printer…";
        _startupItem.Checked = startsWithWindows;
        _icon.Text = printerInstalled ? "FileRedact - waiting for print jobs" : "FileRedact";
    }

    public void Notify(string title, string message)
    {
        try { _icon.ShowBalloonTip(4000, title, message, WinForms.ToolTipIcon.Info); } catch { }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/FileRedact.ico"));
            if (info != null) return new Icon(info.Stream);
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
