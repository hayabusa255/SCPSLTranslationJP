using SCPSLInstaller;

namespace Installer.Tests;

public class VdfTests
{
    [Fact]
    public void NewFormatLibraryFolders()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"      "/home/u/.local/share/Steam"
                    "label"     ""
                    "apps" { "700330" "123" }
                }
                "1"
                {
                    "path"      "/mnt/games/SteamLibrary"
                }
            }
            """;
        Assert.Equal(["/home/u/.local/share/Steam", "/mnt/games/SteamLibrary"], SteamLocator.ParseLibraryPaths(vdf));
    }

    [Fact]
    public void OldFormatLibraryFolders()
    {
        const string vdf = "\"LibraryFolders\"\n{\n\t\"TimeNextStatsReport\"\t\"123\"\n\t\"ContentStatsID\"\t\"-9\"\n\t\"1\"\t\t\"D:\\\\SteamLibrary\"\n}\n";
        Assert.Equal([@"D:\SteamLibrary"], SteamLocator.ParseLibraryPaths(vdf));
    }

    [Fact]
    public void EscapedWindowsPathAndComments()
    {
        const string vdf = """
            // comment
            "libraryfolders" {
                "0" { // trailing
                    "path" "C:\\Program Files (x86)\\Steam"
                }
            }
            """;
        Assert.Equal([@"C:\Program Files (x86)\Steam"], SteamLocator.ParseLibraryPaths(vdf));
    }

    [Fact]
    public void AppManifestInstallDir()
    {
        const string acf = "\"AppState\"\n{\n\t\"appid\"\t\"700330\"\n\t\"installdir\"\t\"SCP Secret Laboratory\"\n}\n";
        Assert.Equal("SCP Secret Laboratory", SteamLocator.ParseInstallDir(acf));
    }

    [Fact]
    public void EscapesNewlineTabQuote()
    {
        var root = VdfParser.Parse("\"k\" \"a\\nb\\tc\\\"d\"");
        Assert.Equal("a\nb\tc\"d", root.GetString("k"));
    }

    [Fact]
    public void UnclosedBraceThrows() =>
        Assert.Throws<FormatException>(() => VdfParser.Parse("\"a\" { \"b\" \"c\""));

    [Fact]
    public void FindGameInLibraryUsesInstallDirOrDefault()
    {
        using var t = new TempDir();
        string lib = Path.Combine(t.Path, "lib");
        Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common", "Custom Dir"));
        File.WriteAllText(Path.Combine(lib, "steamapps", "appmanifest_700330.acf"), "\"AppState\" { \"installdir\" \"Custom Dir\" }");
        Assert.Equal(Path.Combine(lib, "steamapps", "common", "Custom Dir"), SteamLocator.FindGameInLibrary(lib));

        File.WriteAllText(Path.Combine(lib, "steamapps", "appmanifest_700330.acf"), "\"AppState\" { }");
        Assert.Null(SteamLocator.FindGameInLibrary(lib));
        Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common", "SCP Secret Laboratory"));
        Assert.NotNull(SteamLocator.FindGameInLibrary(lib));
    }

    [Theory]
    [InlineData("  \"/a/b c\"  ", "/a/b c")]
    [InlineData("'/a/b'", "/a/b")]
    [InlineData("/a/b", "/a/b")]
    public void NormalizeUserPath(string input, string expected) =>
        Assert.Equal(expected, SteamLocator.NormalizeUserPath(input));
}

public class SteamLocatorLinkTests
{
    [Fact]
    public void SymlinkedLibraryIsDeduplicated()
    {
        if (OperatingSystem.IsWindows()) return;
        using var t = new TempDir();
        string real = Path.Combine(t.Path, "real");
        Directory.CreateDirectory(Path.Combine(real, "steamapps", "common", "SCP Secret Laboratory"));
        File.WriteAllText(Path.Combine(real, "steamapps", "appmanifest_700330.acf"), "\"AppState\" { }");
        string link = Path.Combine(t.Path, "link");
        Directory.CreateSymbolicLink(link, real);

        string? a = SteamLocator.FindGameInLibrary(real);
        string? b = SteamLocator.FindGameInLibrary(link);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(SteamLocator.RealPath(a), SteamLocator.RealPath(b));
    }
}
