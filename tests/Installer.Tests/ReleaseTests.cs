using System.Text.Json;
using SCPSLInstaller;

namespace Installer.Tests;

public class ReleaseTests
{
    [Theory]
    [InlineData("v15.0", true)]
    [InlineData("15.0.1-beta", true)]
    [InlineData("../x", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("..", false)]
    [InlineData("a..b", false)]
    [InlineData(".hidden", false)]
    [InlineData("a\0b", false)]
    public void TagValidation(string tag, bool valid) => Assert.Equal(valid, ReleaseClient.IsValidTag(tag));

    [Fact]
    public void NullTagInvalid() => Assert.False(ReleaseClient.IsValidTag(null));

    [Theory]
    [InlineData("https://github.com/o/r/releases/tag/v15.0", "v15.0")]
    [InlineData("/o/r/releases/tag/v1%2E2?x=1", "v1.2")]
    [InlineData("https://github.com/o/r/releases", null)]
    public void ParseTagFromLocation(string loc, string? expected) =>
        Assert.Equal(expected, ReleaseClient.ParseTagFromLocation(loc));

    [Fact]
    public void ApiReleasePrefersAssetUrl()
    {
        using var doc = JsonDocument.Parse("""
            {"tag_name":"v2","assets":[{"name":"other.zip","browser_download_url":"https://x/other.zip"},{"name":"v2.zip","browser_download_url":"https://x/v2.zip"}]}
            """);
        var info = ReleaseClient.ParseApiRelease(doc.RootElement, "o/r");
        Assert.Equal("v2", info.Tag);
        Assert.Equal("https://x/v2.zip", info.DownloadUrl);
    }

    [Fact]
    public void ApiReleaseFallsBackToDefaultUrl()
    {
        using var doc = JsonDocument.Parse("""{"tag_name":"v2"}""");
        Assert.Equal("https://github.com/o/r/releases/download/v2/v2.zip", ReleaseClient.ParseApiRelease(doc.RootElement, "o/r").DownloadUrl);
    }
}
