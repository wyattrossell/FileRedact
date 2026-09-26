using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FileRedact.App.Services;

/// <summary>
/// Ensures only one FileRedact process runs per user session. Later launches (double-clicking a file,
/// the watcher handing over a printed document) forward their arguments to the running instance over a
/// named pipe and exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\FileRedact.SingleInstance";
    private const string PipeName = "FileRedact.Args";

    private readonly Mutex _mutex;
    private CancellationTokenSource? _cts;

    public bool IsFirstInstance { get; }

    public event Action<string[]>? ArgumentsReceived;

    public SingleInstance()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    public void StartServer()
    {
        if (!IsFirstInstance) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ServerLoopAsync(_cts.Token));
    }

    private async Task ServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var json = await reader.ReadToEndAsync(ct);
                var args = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
                ArgumentsReceived?.Invoke(args);
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(250, ct).ContinueWith(_ => { }); }
        }
    }

    public static bool SendToRunningInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(args));
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }
        _mutex.Dispose();
    }
}
