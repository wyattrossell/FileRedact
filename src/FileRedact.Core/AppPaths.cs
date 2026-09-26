namespace FileRedact.Core;

/// <summary>Well-known folders used by the application and the virtual printer.</summary>
public static class AppPaths
{
    public const string PrinterName = "FileRedact";

    /// <summary>Machine-wide folder the print spooler writes into. Must be writable by SYSTEM and readable by users.</summary>
    public static string PrinterInbox => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FileRedact", "Inbox");

    /// <summary>The "port" of the virtual printer: the Microsoft Print To PDF driver writes the job to this file.</summary>
    public static string PrinterPortFile => Path.Combine(PrinterInbox, "FileRedact-print.pdf");

    public static string UserData => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileRedact");
    public static string Received => Path.Combine(UserData, "Received");
    public static string Work => Path.Combine(UserData, "Work");
    public static string SettingsFile => Path.Combine(UserData, "settings.json");
    public static string LogFile => Path.Combine(UserData, "fileredact.log");

    public static void EnsureUserFolders()
    {
        Directory.CreateDirectory(UserData);
        Directory.CreateDirectory(Received);
        Directory.CreateDirectory(Work);
    }

    public static string NewWorkFile(string extension)
    {
        EnsureUserFolders();
        return Path.Combine(Work, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{extension}");
    }
}
