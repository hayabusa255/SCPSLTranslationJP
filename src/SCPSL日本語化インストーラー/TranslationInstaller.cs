using System.IO.Compression;
using System.Text.Json;

namespace SCPSLInstaller;

internal sealed record OldTranslation(string Path, string FolderName, string? DisplayName);

internal static class TranslationInstaller
{
    public const string ManifestFile = "manifest.json";
    public const string WorkPrefix = ".installing-";

    public static bool IsInstalled(string translationsDir, string tag) =>
        File.Exists(Path.Combine(translationsDir, tag, ManifestFile));

    public enum WriteAccess { Ok, NeedsElevation, Failed }

    /// <summary>Translations に書き込めるか、一時ファイルを作って確かめる。権限不足（昇格で直りうる）とそれ以外を区別する。</summary>
    public static WriteAccess CheckWritable(string translationsDir, out string? reason)
    {
        reason = null;
        try
        {
            Directory.CreateDirectory(translationsDir);
            string probe = Path.Combine(translationsDir, $".write-test-{Guid.NewGuid():N}");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return WriteAccess.Ok;
        }
        catch (UnauthorizedAccessException ex)
        {
            reason = ex.Message;
            return WriteAccess.NeedsElevation;
        }
        catch (IOException ex)
        {
            reason = ex.Message;
            return WriteAccess.Failed;
        }
    }

    public static bool CanWrite(string translationsDir) => CheckWritable(translationsDir, out _) == WriteAccess.Ok;

    public static readonly TimeSpan StaleWorkDirAge = TimeSpan.FromHours(1);

    /// <summary>最終更新から1時間以上経った作業フォルダ（.installing-*）を削除し、件数を返す。</summary>
    public static int CleanupStaleWorkDirs(string translationsDir) =>
        CleanupStaleWorkDirs(translationsDir, StaleWorkDirAge, DateTime.UtcNow);

    public static int CleanupStaleWorkDirs(string translationsDir, TimeSpan minAge, DateTime nowUtc)
    {
        int count = 0;
        if (!Directory.Exists(translationsDir)) return 0;
        foreach (var dir in Directory.GetDirectories(translationsDir, WorkPrefix + "*"))
        {
            try
            {
                if (nowUtc - Directory.GetLastWriteTimeUtc(dir) < minAge) continue; // 同時実行中の可能性
                Directory.Delete(dir, true);
                count++;
            }
            catch (Exception ex) { ConsoleUi.LogException(ex); }
        }
        return count;
    }

    /// <summary>zip を検証し、展開時に剥がすトップフォルダ接頭辞（"" または "Name/"）を返す。</summary>
    public static string InspectZip(string zipPath)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            throw new InstallerException("ダウンロードしたファイルが有効な zip ではありません。", ex);
        }

        using (archive)
        {
            var names = archive.Entries.Select(e => Normalize(e.FullName)).Where(n => n.Length > 0).ToList();
            if (names.Count == 0) throw new InstallerException("zip が空です。");
            if (names.Contains(ManifestFile, StringComparer.Ordinal)) return "";

            var tops = names.Select(n => { int i = n.IndexOf('/'); return i < 0 ? null : n[..i]; }).Distinct().ToList();
            if (tops.Count == 1 && tops[0] is { } top && names.Contains($"{top}/{ManifestFile}", StringComparer.Ordinal))
                return top + "/";

            throw new InstallerException($"zip に {ManifestFile} が見つかりません。日本語化ファイルの zip ではない可能性があります。");
        }
    }

    private static string Normalize(string name) => name.Replace('\\', '/');

    /// <summary>zip を destDir に展開する。展開先が destDir の外になるエントリがあれば中止。</summary>
    public static void Extract(string zipPath, string stripPrefix, string destDir, CancellationToken ct)
    {
        string root = Path.GetFullPath(destDir);
        string rootWithSep = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        Directory.CreateDirectory(root);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            string rel = Normalize(entry.FullName);
            if (stripPrefix.Length > 0)
            {
                if (!rel.StartsWith(stripPrefix, StringComparison.Ordinal)) continue;
                rel = rel[stripPrefix.Length..];
            }
            if (rel.Length == 0) continue;

            string dest = Path.GetFullPath(Path.Combine(root, rel));
            bool isDir = rel.EndsWith('/');
            char[] seps = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
            bool inside = dest.StartsWith(rootWithSep, cmp) || string.Equals(dest.TrimEnd(seps), root.TrimEnd(seps), cmp);
            if (!inside)
                throw new InstallerException($"zip に展開先の外へ出るパスが含まれているため中止しました: {entry.FullName}");

            if (isDir)
            {
                Directory.CreateDirectory(dest);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }
    }

    /// <summary>zip から Translations/&lt;tag&gt; を作る（作業フォルダ経由。既存があれば置き換え）。</summary>
    public static void InstallFromZip(string translationsDir, string tag, string zipPath, CancellationToken ct)
    {
        string prefix = InspectZip(zipPath);
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string work = Path.Combine(translationsDir, $"{WorkPrefix}{tag}-{suffix}");
        string backup = work + "-old";
        string target = Path.Combine(translationsDir, tag);

        try
        {
            Extract(zipPath, prefix, work, ct);
            if (!File.Exists(Path.Combine(work, ManifestFile)))
                throw new InstallerException($"展開結果に {ManifestFile} がありません。");
            ct.ThrowIfCancellationRequested();

            bool hadOld = Directory.Exists(target);
            if (hadOld) MoveWithRetry(target, backup);
            try
            {
                MoveWithRetry(work, target);
            }
            catch
            {
                if (hadOld && !Directory.Exists(target) && Directory.Exists(backup))
                {
                    try { MoveWithRetry(backup, target); } catch (Exception ex) { ConsoleUi.LogException(ex); }
                }
                throw;
            }
            if (hadOld) TryDelete(backup);
        }
        finally
        {
            TryDelete(work);
        }
    }

    private static readonly int[] MoveRetryDelaysMs = [200, 500, 1000];

    /// <summary>ウイルス対策ソフト等が一時的にフォルダを掴む場合に備え、最大3回再試行して移動する。</summary>
    internal static void MoveWithRetry(string source, string dest, Action<int>? sleep = null)
    {
        sleep ??= Thread.Sleep;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Move(source, dest);
                return;
            }
            catch (Exception ex) when (attempt < MoveRetryDelaysMs.Length && ex is IOException or UnauthorizedAccessException)
            {
                ConsoleUi.LogException(ex);
                sleep(MoveRetryDelaysMs[attempt]);
            }
        }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception ex) { ConsoleUi.LogException(ex); }
    }

    /// <summary>Windows / macOS は大文字小文字を区別しないファイルシステムが既定。</summary>
    public static StringComparison DefaultFolderComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>target 以外で、manifest の InterfaceLocales に ja / ja-JP を含むフォルダ（旧日本語訳）。</summary>
    public static List<OldTranslation> FindOldJapaneseTranslations(string translationsDir, string targetTag, StringComparison? tagComparison = null)
    {
        var result = new List<OldTranslation>();
        if (!Directory.Exists(translationsDir)) return result;
        var cmp = tagComparison ?? DefaultFolderComparison;

        foreach (var dir in Directory.GetDirectories(translationsDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith('.') || string.Equals(name, targetTag, cmp)) continue;

            string manifest = Path.Combine(dir, ManifestFile);
            if (!File.Exists(manifest)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                if (!doc.RootElement.TryGetProperty("InterfaceLocales", out var locales) || locales.ValueKind != JsonValueKind.Array) continue;

                bool ja = locales.EnumerateArray().Any(l => l.ValueKind == JsonValueKind.String &&
                    (string.Equals(l.GetString(), "ja", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(l.GetString(), "ja-JP", StringComparison.OrdinalIgnoreCase)));
                if (!ja) continue;

                string? display = doc.RootElement.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                result.Add(new OldTranslation(dir, name, display));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // 壊れた manifest は無視
            }
        }
        return result;
    }
}
