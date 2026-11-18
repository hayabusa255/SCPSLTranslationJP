using SCPSLInstaller;

namespace Installer.Tests;

public class OptionsTests
{
    [Fact]
    public void Defaults()
    {
        var (o, err) = Options.Parse([]);
        Assert.Null(err);
        Assert.Equal(Options.DefaultRepo, o.Repo);
        Assert.False(o.Yes || o.Force || o.DryRun || o.NoPause || o.Help || o.Version);
        Assert.Null(o.GameDir);
    }

    [Fact]
    public void DefaultRepoIsOwnerSlashName()
    {
        Assert.Matches(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", Options.DefaultRepo);
    }

    [Fact]
    public void AllFlags()
    {
        var (o, err) = Options.Parse(["--game-dir", "/g h", "-y", "--force", "--dry-run", "--repo", "a/b", "--no-pause"]);
        Assert.Null(err);
        Assert.Equal("/g h", o.GameDir);
        Assert.True(o.Yes && o.Force && o.DryRun && o.NoPause);
        Assert.Equal("a/b", o.Repo);
    }

    [Fact]
    public void EqualsSyntaxAndLongYes()
    {
        var (o, err) = Options.Parse(["--game-dir=/x", "--yes", "-h", "--version"]);
        Assert.Null(err);
        Assert.Equal("/x", o.GameDir);
        Assert.True(o.Yes && o.Help && o.Version);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--game-dir")]
    [InlineData("--repo")]
    [InlineData("positional")]
    public void BadArgs(string arg) => Assert.NotNull(Options.Parse([arg]).Error);

    [Fact]
    public void BadRepoRejected() => Assert.NotNull(Options.Parse(["--repo", "noslash"]).Error);

    [Fact]
    public void RelaunchArgsReplaceGameDir()
    {
        var args = Options.BuildRelaunchArgs(["--yes", "--game-dir", "old", "--force", "--game-dir=older"], @"C:\Program Files\Game");
        Assert.Equal(["--yes", "--force", "--game-dir", @"C:\Program Files\Game"], args);
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\Program Files\Game", "\"C:\\Program Files\\Game\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    public void QuoteArgument(string arg, string expected) => Assert.Equal(expected, Options.QuoteArgument(arg));
}
