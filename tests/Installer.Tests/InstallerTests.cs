using System.IO.Compression;
using SCPSLInstaller;

namespace Installer.Tests;

public class InstallerTests
{
    private static string MakeZip(string dir, params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".zip");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var e = zip.CreateEntry(name);
            using var w = new StreamWriter(e.Open());
            w.Write(content);
        }
        return path;
    }

    private const string JaManifest = """{"Name":"日本語","InterfaceLocales":["ja","ja-JP"]}""";

    [Fact]
    public void RootLayoutInstalls()
    {
        using var t = new TempDir();
        string tr = Path.Combine(t.Path, "Translations");
        Directory.CreateDirectory(tr);
        string zip = MakeZip(t.Path, ("manifest.json", JaManifest), ("sub/a.txt", "A"));

        TranslationInstaller.InstallFromZip(tr, "v1", zip, CancellationToken.None);

        Assert.True(TranslationInstaller.IsInstalled(tr, "v1"));
        Assert.Equal("A", File.ReadAllText(Path.Combine(tr, "v1", "sub", "a.txt")));
        Assert.Empty(Directory.GetDirectories(tr, ".installing-*"));
    }

    [Fact]
    public void SingleTopFolderIsStripped()
    {
        using var t = new TempDir();
        string tr = Path.Combine(t.Path, "Translations");
        Directory.CreateDirectory(tr);
        string zip = MakeZip(t.Path, ("JP/manifest.json", JaManifest), ("JP/x.txt", "X"));

        TranslationInstaller.InstallFromZip(tr, "v1", zip, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(tr, "v1", "manifest.json")));
        Assert.True(File.Exists(Path.Combine(tr, "v1", "x.txt")));
        Assert.False(Directory.Exists(Path.Combine(tr, "v1", "JP")));
    }

    [Fact]
    public void ZipWithoutManifestRejected()
    {
        using var t = new TempDir();
        string tr = Path.Combine(t.Path, "Translations");
        Directory.CreateDirectory(tr);
        string zip = MakeZip(t.Path, ("a.txt", "A"), ("b/c.txt", "C"));

        Assert.Throws<InstallerException>(() => TranslationInstaller.InstallFromZip(tr, "v1", zip, CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(tr, "v1")));
        Assert.Empty(Directory.GetDirectories(tr));
    }

    [Fact]
    public void NotAZipRejected()
    {
        using var t = new TempDir();
        string f = Path.Combine(t.Path, "bad.zip");
        File.WriteAllText(f, "not a zip");
        Assert.Throws<InstallerException>(() => TranslationInstaller.InspectZip(f));
    }

    [Fact]
    public void ZipSlipRejectedAndCleanedUp()
    {
        using var t = new TempDir();
        string tr = Path.Combine(t.Path, "Translations");
        Directory.CreateDirectory(tr);
        string zip = MakeZip(t.Path, ("manifest.json", JaManifest), ("../evil.txt", "evil"));

        Assert.Throws<InstallerException>(() => TranslationInstaller.InstallFromZip(tr, "v1", zip, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(t.Path, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(tr, "evil.txt")));
        Assert.Empty(Directory.GetDirectories(tr));
    }

    [Fact]
    public void ExistingTargetIsReplaced()
    {
        using var t = new TempDir();
        string tr = Path.Combine(t.Path, "Translations");
        Directory.CreateDirectory(Path.Combine(tr, "v1"));
        File.WriteAllText(Path.Combine(tr, "v1", "stale.txt"), "old");
        string zip = MakeZip(t.Path, ("manifest.json", JaManifest), ("new.txt", "new"));

        TranslationInstaller.InstallFromZip(tr, "v1", zip, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(tr, "v1", "stale.txt")));
        Assert.True(File.Exists(Path.Combine(tr, "v1", "new.txt")));
        Assert.Single(Directory.GetDirectories(tr));
    }

    [Fact]
    public void FailedReplaceKeepsExistingTarget()
    {
        using var t = new TempDir();
        string tr = Path.Combine(t.Path, "Translations");
        Directory.CreateDirectory(Path.Combine(tr, "v1"));
        File.WriteAllText(Path.Combine(tr, "v1", "keep.txt"), "keep");
        string zip = MakeZip(t.Path, ("manifest.json", JaManifest), ("../evil.txt", "x"));

        Assert.Throws<InstallerException>(() => TranslationInstaller.InstallFromZip(tr, "v1", zip, CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(tr, "v1", "keep.txt")));
    }

    [Fact]
    public void StaleWorkDirsCleaned()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(Path.Combine(t.Path, ".installing-v1-abc"));
        Directory.CreateDirectory(Path.Combine(t.Path, ".installing-v1-abc-old"));
        Directory.CreateDirectory(Path.Combine(t.Path, "keep"));
        var future = DateTime.UtcNow.AddHours(2);
        Assert.Equal(2, TranslationInstaller.CleanupStaleWorkDirs(t.Path, TimeSpan.FromHours(1), future));
        Assert.Equal([Path.Combine(t.Path, "keep")], Directory.GetDirectories(t.Path));
    }

    [Fact]
    public void RecentWorkDirsAreKept()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(Path.Combine(t.Path, ".installing-v1-abc"));
        Assert.Equal(0, TranslationInstaller.CleanupStaleWorkDirs(t.Path));
        Assert.Single(Directory.GetDirectories(t.Path));
    }

    [Fact]
    public void OldDetectionTagComparison()
    {
        using var t = new TempDir();
        Dir(t.Path, "V2", """{"InterfaceLocales":["ja"]}""");
        Assert.Empty(TranslationInstaller.FindOldJapaneseTranslations(t.Path, "v2", StringComparison.OrdinalIgnoreCase));
        Assert.Single(TranslationInstaller.FindOldJapaneseTranslations(t.Path, "v2", StringComparison.Ordinal));
    }

    [Fact]
    public void MoveWithRetryGivesUpAfterThreeRetries()
    {
        using var t = new TempDir();
        var waits = new List<int>();
        Assert.Throws<DirectoryNotFoundException>(() =>
            TranslationInstaller.MoveWithRetry(Path.Combine(t.Path, "none"), Path.Combine(t.Path, "x"), waits.Add));
        Assert.Equal([200, 500, 1000], waits);
    }

    [Fact]
    public void BrokenInstallWithoutManifestIsNotInstalled()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(Path.Combine(t.Path, "v1"));
        Assert.False(TranslationInstaller.IsInstalled(t.Path, "v1"));
    }

    private static void Dir(string root, string name, string? manifest)
    {
        string d = Path.Combine(root, name);
        Directory.CreateDirectory(d);
        if (manifest is not null) File.WriteAllText(Path.Combine(d, "manifest.json"), manifest);
    }

    [Fact]
    public void OldJapaneseDetection()
    {
        using var t = new TempDir();
        Dir(t.Path, "old-jp", """{"Name":"旧日本語","InterfaceLocales":["JA"]}""");
        Dir(t.Path, "old-jpjp", """{"InterfaceLocales":["ja-jp"]}""");
        Dir(t.Path, "en", """{"Name":"English","InterfaceLocales":["en"]}""");
        Dir(t.Path, "v2", JaManifest);                 // target
        Dir(t.Path, ".installing-v2-x", JaManifest);   // dot
        Dir(t.Path, "nomanifest", null);
        Dir(t.Path, "broken", "{ not json");
        Dir(t.Path, "wrongtype", """{"InterfaceLocales":"ja"}""");

        var found = TranslationInstaller.FindOldJapaneseTranslations(t.Path, "v2");

        Assert.Equal(["old-jp", "old-jpjp"], found.Select(f => f.FolderName).ToArray());
        Assert.Equal("旧日本語", found[0].DisplayName);
        Assert.Null(found[1].DisplayName);
    }

    [Fact]
    public void CanWriteCreatesDirectory()
    {
        using var t = new TempDir();
        string tr = Path.Combine(t.Path, "Translations");
        Assert.True(TranslationInstaller.CanWrite(tr));
        Assert.True(Directory.Exists(tr));
        Assert.Empty(Directory.GetFileSystemEntries(tr));
    }
}
