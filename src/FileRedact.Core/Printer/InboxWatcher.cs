using System.Diagnostics;

namespace FileRedact.Core.Printer;

/// <summary>
/// Watches the printer inbox. When the spooler finishes writing a print job (the PDF file becomes
/// unlocked), the file is moved to the user's Received folder and <see cref="DocumentReceived"/> fires.
/// </summary>
public sealed class InboxWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _inbox;
    private readonly string _received;
    private CancellationTokenSource? _cts;

    public event Action<string>? DocumentReceived;
    public event Action<string>? Error;

    public InboxWatcher(string? inbox = null, string? received = null)
    {
        _inbox = inbox ?? AppPaths.PrinterInbox;
        _received = received ?? AppPaths.Received;
        Directory.CreateDirectory(_received);
        _watcher = new FileSystemWatcher
        {
            Filter = "*.pdf",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        _watcher.Created += (_, e) => _ = HandleAsync(e.FullPath);
        _watcher.Changed += (_, e) => _ = HandleAsync(e.FullPath);
        _watcher.Renamed += (_, e) => _ = HandleAsync(e.FullPath);
    }

    public bool IsRunning => _watcher.EnableRaisingEvents;

    public void Start()
    {
        if (!Directory.Exists(_inbox)) return;
        _cts = new CancellationTokenSource();
        _watcher.Path = _inbox;
        _watcher.EnableRaisingEvents = true;
        // Pick up anything printed while we were not running.
        foreach (var f in Directory.EnumerateFiles(_inbox, "*.pdf"))
            _ = HandleAsync(f);
    }

    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
        _cts?.Cancel();
    }

    private async Task HandleAsync(string path)
    {
        var ct = _cts?.Token ?? CancellationToken.None;
        // Give the spooler a moment to start writing; then serialise handling.
        await Task.Delay(400, ct).ContinueWith(_ => { });
        if (ct.IsCancellationRequested) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return;
            if (!await WaitForUnlockAsync(path, TimeSpan.FromMinutes(3), ct)) return;
            if (!File.Exists(path) || !LooksLikePdf(path)) return;

            var dest = Path.Combine(_received, $"Printed-{DateTime.Now:yyyy-MM-dd-HHmmss}.pdf");
            var n = 1;
            while (File.Exists(dest))
                dest = Path.Combine(_received, $"Printed-{DateTime.Now:yyyy-MM-dd-HHmmss}-{n++}.pdf");

            try
            {
                File.Move(path, dest);
            }
            catch (Exception)
            {
                // Fall back to copy + delete if the move across ACL boundaries fails.
                File.Copy(path, dest, overwrite: true);
                try { File.Delete(path); } catch { }
            }
            DocumentReceived?.Invoke(dest);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            Error?.Invoke(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool LooksLikePdf(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var buf = new byte[5];
            var n = fs.Read(buf, 0, buf.Length);
            return n >= 4 && buf[0] == (byte)'%' && buf[1] == (byte)'P' && buf[2] == (byte)'D' && buf[3] == (byte)'F';
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The spooler holds the file open while writing; wait until it can be opened exclusively and its size is stable.</summary>
    private static async Task<bool> WaitForUnlockAsync(string path, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long lastSize = -1;
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var size = new FileInfo(path).Length;
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                if (size > 0 && size == lastSize) return true;
                lastSize = size;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            await Task.Delay(500, ct);
        }
        return false;
    }

    public void Dispose()
    {
        Stop();
        _watcher.Dispose();
        _gate.Dispose();
    }
}
