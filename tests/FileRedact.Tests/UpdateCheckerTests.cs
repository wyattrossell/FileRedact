using FileRedact.Core.Update;
using Xunit;

namespace FileRedact.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3.0")]
    [InlineData("1.2", "1.2.0.0")]
    [InlineData("release-0.3.1", "0.3.1.0")]
    [InlineData("v2.0.0-beta", "2.0.0.0")]
    public void Tags_parse_to_versions(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    public void Tags_without_numbers_are_rejected(string tag)
    {
        Assert.Null(UpdateChecker.ParseVersion(tag));
    }

    private const string SampleJson = """
        {
          "tag_name": "v0.2.0",
          "name": "FileRedact 0.2.0",
          "body": "- Better name detection\n- Update checker",
          "html_url": "https://github.com/wyattrossell/FileRedact/releases/tag/v0.2.0",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-10-01T12:00:00Z",
          "assets": [
            { "name": "Source.zip", "browser_download_url": "https://example.invalid/source.zip", "size": 10 },
            { "name": "FileRedact-win-x64.zip", "browser_download_url": "https://example.invalid/FileRedact-win-x64.zip", "size": 20 },
            { "name": "FileRedact-Setup-0.2.0.exe", "browser_download_url": "https://example.invalid/FileRedact-Setup-0.2.0.exe", "size": 12345, "digest": "sha256:ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789" }
          ]
        }
        """;

    [Fact]
    public void Release_json_is_parsed_and_installer_asset_preferred()
    {
        var r = UpdateChecker.ParseRelease(SampleJson);
        Assert.NotNull(r);
        Assert.Equal(new Version(0, 2, 0, 0), r!.Version);
        Assert.Equal("FileRedact 0.2.0", r.Title);
        Assert.Equal("FileRedact-Setup-0.2.0.exe", r.AssetName);
        Assert.Contains("Setup", r.AssetUrl);
        Assert.Equal(12345, r.AssetSize);
        Assert.Equal("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", r.AssetSha256);
        Assert.NotNull(r.Published);
        Assert.True(UpdateChecker.IsInstaller(r));
        Assert.True(r.Version > new Version(0, 1, 0, 0));
    }

    [Fact]
    public void Zip_is_chosen_when_no_installer_is_attached()
    {
        var start = SampleJson.IndexOf("{ \"name\": \"FileRedact-Setup", StringComparison.Ordinal);
        var end = SampleJson.IndexOf('}', start) + 1;
        var json = SampleJson.Remove(start, end - start);
        json = json.Remove(json.LastIndexOf(',', start), 1);
        var r = UpdateChecker.ParseRelease(json);
        Assert.NotNull(r);
        Assert.Equal("FileRedact-win-x64.zip", r!.AssetName);
        Assert.False(UpdateChecker.IsInstaller(r));
    }

    [Fact]
    public void Prereleases_and_drafts_are_ignored()
    {
        Assert.Null(UpdateChecker.ParseRelease(SampleJson.Replace("\"prerelease\": false", "\"prerelease\": true")));
        Assert.Null(UpdateChecker.ParseRelease(SampleJson.Replace("\"draft\": false", "\"draft\": true")));
    }

    [Fact]
    public void Silent_install_arguments_are_unattended_and_restart_the_app()
    {
        var args = UpdateChecker.BuildSilentInstallArguments(@"C:\logs\u.log");
        Assert.Contains("/VERYSILENT", args);
        Assert.Contains("/SUPPRESSMSGBOXES", args);
        Assert.Contains("/NORESTART", args);
        Assert.Contains("/CLOSEAPPLICATIONS", args);
        Assert.Contains("/AUTORESTART=1", args);
        Assert.Contains("/LOG=\"C:\\logs\\u.log\"", args);
    }

    [Fact]
    public void Current_version_matches_the_project_version()
    {
        Assert.True(UpdateChecker.CurrentVersion >= new Version(0, 1, 0, 0));
    }

    [Fact]
    public void Feed_url_can_be_overridden_for_testing()
    {
        var old = Environment.GetEnvironmentVariable(UpdateChecker.FeedOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(UpdateChecker.FeedOverrideVariable, "http://localhost:1/latest.json");
            Assert.Equal("http://localhost:1/latest.json", UpdateChecker.LatestReleaseApiUrl);
            Environment.SetEnvironmentVariable(UpdateChecker.FeedOverrideVariable, null);
            Assert.Contains("api.github.com/repos/wyattrossell/FileRedact", UpdateChecker.LatestReleaseApiUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateChecker.FeedOverrideVariable, old);
        }
    }
}
