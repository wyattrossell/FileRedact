using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace FileRedact.Core.Update;

public sealed record ReleaseInfo(Version Version, string Tag, string Title, string Notes, string PageUrl, string? AssetName, string? AssetUrl, DateTimeOffset? Published);

/// <summary>
/// Looks up the latest published release of FileRedact on GitHub and compares it with the running version.
/// Only the public releases endpoint is called; nothing about the user or their documents is sent.
/// </summary>
public static class UpdateChecker
{
    public const string RepoOwner = "wyattrossell";
    public const string RepoName = "FileRedact";
    public static string ReleasesPageUrl => $"https://github.com/{RepoOwner}/{RepoName}/releases";
    public static string LatestReleaseApiUrl => $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FileRedact", CurrentVersion.ToString(3)));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    /// <summary>The version of the running application (from the assembly, i.e. the Version in Directory.Build.props).</summary>
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
            using var response = await Http.GetAsync(LatestReleaseApiUrl, ct);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(ct);
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
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var version = ParseVersion(tag);
        if (version == null) return null;

        var title = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPageUrl : ReleasesPageUrl;
        DateTimeOffset? published = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(p.GetString(), out var dto) ? dto : null;

        string? assetName = null, assetUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            // Prefer a Windows zip/installer; otherwise the first asset.
            JsonElement? best = null;
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                var lower = name.ToLowerInvariant();
                var score = lower.Contains("win") && (lower.EndsWith(".zip") || lower.EndsWith(".msi") || lower.EndsWith(".exe")) ? 2
                          : lower.EndsWith(".zip") || lower.EndsWith(".msi") || lower.EndsWith(".exe") ? 1 : 0;
                if (best == null || score > Score(best.Value)) best = a;
            }
            if (best != null)
            {
                assetName = best.Value.TryGetProperty("name", out var an) ? an.GetString() : null;
                assetUrl = best.Value.TryGetProperty("browser_download_url", out var au) ? au.GetString() : null;
            }
        }

        return new ReleaseInfo(version, tag, string.IsNullOrWhiteSpace(title) ? tag : title, notes, page, assetName, assetUrl, published);

        static int Score(JsonElement a)
        {
            var lower = (a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "").ToLowerInvariant();
            return lower.Contains("win") && (lower.EndsWith(".zip") || lower.EndsWith(".msi") || lower.EndsWith(".exe")) ? 2
                 : lower.EndsWith(".zip") || lower.EndsWith(".msi") || lower.EndsWith(".exe") ? 1 : 0;
        }
    }

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

    /// <summary>Downloads a release asset to the user's Downloads folder and returns the saved path.</summary>
    public static async Task<string> DownloadAssetAsync(ReleaseInfo release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (release.AssetUrl == null || release.AssetName == null) throw new InvalidOperationException("This release has no downloadable file.");
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloads)) downloads = AppPaths.UserData;
        var target = Path.Combine(downloads, release.AssetName);
        var n = 1;
        while (File.Exists(target))
            target = Path.Combine(downloads, $"{Path.GetFileNameWithoutExtension(release.AssetName)} ({n++}){Path.GetExtension(release.AssetName)}");

        using var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(target + ".part");
        var buffer = new byte[81920];
        long read = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, count), ct);
            read += count;
            if (total > 0) progress?.Report((double)read / total.Value);
        }
        file.Close();
        File.Move(target + ".part", target, overwrite: true);
        return target;
    }

    /// <summary>Versions compare with -1 for unspecified parts; normalise so 1.2 == 1.2.0 == 1.2.0.0.</summary>
    private static Version Normalise(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));
}
