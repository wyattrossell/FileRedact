using System.Diagnostics;
using Microsoft.Win32;

namespace FileRedact.Core.Printer;

/// <summary>
/// Installs the "FileRedact" virtual printer. Rather than shipping a custom (signed) printer driver, the
/// printer re-uses the "Microsoft Print To PDF" driver that ships with Windows, attached to a local port
/// whose name is a file path. Printing to it silently writes a PDF to that file; <see cref="InboxWatcher"/>
/// picks the file up and opens it in FileRedact.
/// </summary>
public static class PrinterInstaller
{
    public const string DriverName = "Microsoft Print To PDF";
    public const string RunKeyName = "FileRedactWatcher";

    public static bool IsInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers\" + AppPaths.PrinterName);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when the watcher starts at sign-in, either per user (set by the app) or machine-wide (set by the installer).</summary>
    public static bool IsWatcherRegisteredAtLogin()
    {
        using var user = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (user?.GetValue(RunKeyName) is string) return true;
        try
        {
            using var machine = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return machine?.GetValue(RunKeyName) is string;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when the installer registered the watcher machine-wide (removing it then needs the uninstaller or an admin).</summary>
    public static bool IsWatcherRegisteredMachineWide()
    {
        try
        {
            using var machine = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return machine?.GetValue(RunKeyName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void RegisterWatcherAtLogin(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key.SetValue(RunKeyName, $"\"{exePath}\" --watch");
    }

    public static void UnregisterWatcherAtLogin()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key.DeleteValue(RunKeyName, throwOnMissingValue: false);
    }

    /// <summary>PowerShell that creates the inbox folder, the file port and the printer. Requires elevation.</summary>
    public static string BuildInstallScript()
    {
        var inbox = AppPaths.PrinterInbox;
        var port = AppPaths.PrinterPortFile;
        var name = AppPaths.PrinterName;
        return $$"""
            $ErrorActionPreference = 'Stop'
            $inbox = '{{inbox}}'
            $port  = '{{port}}'
            $name  = '{{name}}'
            New-Item -ItemType Directory -Force -Path $inbox | Out-Null
            # The spooler (SYSTEM) writes the file; interactive users must be able to move/delete it.
            icacls $inbox /grant '*S-1-5-32-545:(OI)(CI)M' /grant '*S-1-5-18:(OI)(CI)F' /T | Out-Null
            if (Test-Path $port) { Remove-Item -Force $port }
            if (-not (Get-PrinterDriver -Name '{{DriverName}}' -ErrorAction SilentlyContinue)) {
                throw "The '{{DriverName}}' driver is not installed. Enable the 'Microsoft Print to PDF' Windows feature and try again."
            }
            if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) { Add-PrinterPort -Name $port }
            if (Get-Printer -Name $name -ErrorAction SilentlyContinue) { Remove-Printer -Name $name }
            Add-Printer -Name $name -DriverName '{{DriverName}}' -PortName $port -Comment 'Print here to open the document in FileRedact for PII redaction'
            Write-Host "Printer '$name' installed."
            """;
    }

    public static string BuildUninstallScript()
    {
        var port = AppPaths.PrinterPortFile;
        var name = AppPaths.PrinterName;
        return $$"""
            $ErrorActionPreference = 'Continue'
            if (Get-Printer -Name '{{name}}' -ErrorAction SilentlyContinue) { Remove-Printer -Name '{{name}}' }
            if (Get-PrinterPort -Name '{{port}}' -ErrorAction SilentlyContinue) { Remove-PrinterPort -Name '{{port}}' }
            Write-Host "Printer '{{name}}' removed."
            """;
    }

    /// <summary>Runs the script in an elevated PowerShell (UAC prompt). Returns the exit code, or null if the user declined.</summary>
    public static async Task<int?> RunElevatedAsync(string script, CancellationToken ct = default)
    {
        AppPaths.EnsureUserFolders();
        var scriptPath = Path.Combine(AppPaths.Work, $"printer-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(scriptPath, script, ct);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            await p.WaitForExitAsync(ct);
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC prompt declined.
            return null;
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    public static Task<int?> InstallAsync(CancellationToken ct = default) => RunElevatedAsync(BuildInstallScript(), ct);
    public static Task<int?> UninstallAsync(CancellationToken ct = default) => RunElevatedAsync(BuildUninstallScript(), ct);
}
