using System.Text.Json;
using System.Text.Json.Serialization;
using FileRedact.Core.Model;

namespace FileRedact.Core.Settings;

public sealed class UserSettings
{
    public List<string> CustomTerms { get; set; } = new();
    public List<PiiCategory> DisabledCategories { get; set; } = new();
    public int OutputDpi { get; set; } = 200;
    public bool KeepTextLayer { get; set; } = true;
    public bool PropagateNames { get; set; } = true;
    public string? LastOutputFolder { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Lazy<UserSettings> Shared = new(Load);

    /// <summary>The single settings instance shared by the whole process, so no component overwrites another's changes.</summary>
    public static UserSettings Current => Shared.Value;

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(AppPaths.SettingsFile), JsonOptions) ?? new UserSettings();
        }
        catch { }
        return new UserSettings();
    }

    public void Save()
    {
        AppPaths.EnsureUserFolders();
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
    }
}
