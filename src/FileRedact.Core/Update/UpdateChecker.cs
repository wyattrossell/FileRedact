using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace FileRedact.Core.Update;

public sealed record ReleaseInfo(
    Version Version, string Tag, string Title, string Notes, string PageUrl,
    string? AssetName, string? AssetUrl, long? AssetSize, string? AssetSha256, DateTimeOffset? Published);

/// <summary>
/// Looks up the latest published release of FileRedact on GitHub, compares it with the running version,
/// and downloads the installer for a silent in-place upgrade. Only the public releases endpoint is
/// called; nothing about the user or their documents is sent.
/// </summary>
public static class UpdateChecker
{
    public const string RepoOwner = "wyattrossell";
    public const string RepoName = "FileRedact";
    /// <summary>Environment variable that overrides the release feed URL (used for testing the update flow).</summary>
    public const string FeedOverrideVariable = "FILEREDACT_UPDATE_URL";

    public static string ReleasesPageUrl => $"https://github.com/{RepoOwner}/{RepoName}/releases";
    public static string LatestReleaseApiUrl
        => Environment.GetEnvironmentVariable(FeedOverrideVariable) is { Length: > 0 } o ? o : $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FileRedact", CurrentVersion.ToString(3)));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return c;
    }

    /// <summary>The version of the running application (from the assembly, i.e. the Version in Directory.Build.props or the release tag).</summary>
    public static Version CurrentVersion
    {
        get
        {
            var v = (Assembly.GetEntryAssembly() ?? typeof(UpdateChecker).Assembly).GetName().Version ?? new Version(0, 0, 0);
            return Normalise(v);
        }
    }

    /// <summary>Returns the latest release, or null when it cannot be determined (offline, no releases yet, rate-limited).</summary>
    public static async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await Http.GetAsync(LatestReleaseApiUrl, cts.Token);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(cts.Token);
            return ParseRelease(json);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Returns the newer release if one exists, otherwise null.</summary>
    public static async Task<ReleaseInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var latest = await GetLatestReleaseAsync(ct);
        return latest != null && latest.Version > CurrentVersion ? latest : null;
    }

    /// <summary>Parses the GitHub "releases/latest" JSON payload. Returns null for drafts, pre-releases and unparsable tags.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var version = ParseVersion(tag);
        if (version == null) return null;

        var title = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
        var notes = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPageUrl : ReleasesPageUrl;
        DateTimeOffset? published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(p.GetString(), out var dto) ? dto : null;

        string? assetName = null, assetUrl = null, assetSha = null;
        long? assetSize = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            JsonElement? best = null;
            foreach (var a in assets.EnumerateArray())
            {
                if (best == null || AssetScore(a) > AssetScore(best.Value)) best = a;
            }
            if (best != null && AssetScore(best.Value) > 0)
            {
                var asset = best.Value;
                assetName = asset.TryGetProperty("name", out var an) ? an.GetString() : null;
                assetUrl = asset.TryGetProperty("browser_download_url", out var au) ? au.GetString() : null;
                assetSize = asset.TryGetProperty("size", out var asz) && asz.ValueKind == JsonValueKind.Number ? asz.GetInt64() : null;
                // GitHub publishes "digest": "sha256:<hex>" for release assets.
                if (asset.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String)
                {
                    var d = dg.GetString() ?? "";
                    if (d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) assetSha = d[7..].Trim().ToLowerInvariant();
                }
            }
        }

        return new ReleaseInfo(version, tag, string.IsNullOrWhiteSpace(title) ? tag : title, notes, page, assetName, assetUrl, assetSize, assetSha, published);
    }

    private static int AssetScore(JsonElement a)
    {
        var lower = (a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "").ToLowerInvariant();
        if (lower.EndsWith(".exe") && (lower.Contains("setup") || lower.Contains("install"))) return 4;
        if (lower.EndsWith(".msi")) return 3;
        if (lower.EndsWith(".zip") && lower.Contains("win")) return 2;
        if (lower.EndsWith(".zip") || lower.EndsWith(".exe")) return 1;
        return 0;
    }

    /// <summary>True when the release asset is an installer that can be run silently.</summary>
    public static bool IsInstaller(ReleaseInfo r)
        => r.AssetName != null && r.AssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Accepts tags such as "v1.2.3", "1.2", "release-1.2.3.4".</summary>
    public static Version? ParseVersion(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var start = -1;
        for (var i = 0; i < tag.Length; i++)
        {
            if (char.IsDigit(tag[i])) { start = i; break; }
        }
        if (start < 0) return null;
        var end = start;
        while (end < tag.Length && (char.IsDigit(tag[end]) || tag[end] == '.')) end++;
        var text = tag[start..end].TrimEnd('.');
        if (!text.Contains('.')) text += ".0";
        return Version.TryParse(text, out var v) ? Normalise(v) : null;
    }

    public static string UpdatesFolder => Path.Combine(AppPaths.UserData, "Updates");

    /// <summary>
    /// Downloads the release asset into the FileRedact updates folder, verifying the size and SHA-256 digest
    /// when the release provides them. Returns the saved path. Previously downloaded copies are re-used.
    /// </summary>
    public static async Task<string> DownloadAssetAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (release.AssetUrl == null || release.AssetName == null) throw new InvalidOperationException("This release has no downloadable file.");
        Directory.CreateDirectory(UpdatesFolder);
        var target = Path.Combine(UpdatesFolder, $"{release.Version.ToString(3)}-{Path.GetFileName(release.AssetName)}");

        if (File.Exists(target) && await VerifyAsync(target, release, ct))
        {
            progress?.Report(1);
            return target;
        }

        var temp = target + ".part";
        using (var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.AssetSize;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(temp);
            var buffer = new byte[1 << 16];
            long read = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, count), ct);
                read += count;
                if (total > 0) progress?.Report(Math.Min(1, (double)read / total.Value));
            }
        }

        if (!await VerifyAsync(temp, release, ct))
        {
            try { File.Delete(temp); } catch { }
            throw new InvalidDataException("The downloaded update did not match the published checksum, so it was discarded. Please try again later.");
        }
        File.Move(temp, target, overwrite: true);
        progress?.Report(1);
        return target;
    }

    private static async Task<bool> VerifyAsync(string path, ReleaseInfo release, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0) return false;
        if (release.AssetSize is { } size && size > 0 && info.Length != size) return false;
        if (release.AssetSha256 is { Length: 64 } expected)
        {
            await using var fs = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(fs, ct);
            return string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
        }
        // Without a digest we can only check the PE header.
        await using var f = File.OpenRead(path);
        return f.ReadByte() == 'M' && f.ReadByte() == 'Z';
    }

    /// <summary>
    /// Command-line for a quiet, unattended Inno Setup upgrade. The installer closes FileRedact itself, keeps
    /// the previously chosen options, and restarts the application when finished.
    /// </summary>
    public static string BuildSilentInstallArguments(string logPath)
        => $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /CLOSEAPPLICATIONS /AUTORESTART=1 /LOG=\"{logPath}\"";

    /// <summary>Removes downloaded installers older than the running version.</summary>
    public static void CleanupOldDownloads()
    {
        try
        {
            if (!Directory.Exists(UpdatesFolder)) return;
            foreach (var f in Directory.EnumerateFiles(UpdatesFolder))
            {
                var name = Path.GetFileName(f);
                var dash = name.IndexOf('-');
                if (dash > 0 && Version.TryParse(name[..dash], out var v) && Normalise(v) <= CurrentVersion)
                    File.Delete(f);
                else if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                    File.Delete(f);
            }
        }
        catch { }
    }

    /// <summary>Versions compare with -1 for unspecified parts; normalise so 1.2 == 1.2.0 == 1.2.0.0.</summary>
    private static Version Normalise(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));
}
