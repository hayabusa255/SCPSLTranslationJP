using System.Runtime.Versioning;
using Microsoft.Win32;

namespace SCPSLInstaller;

internal static class SteamLocator
{
    public const string AppId = "700330";
    public const string DefaultInstallDir = "SCP Secret Laboratory";

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Steam ルート候補（存在確認・重複除去済み）。</summary>
    public static List<string> FindSteamRoots()
    {
        var seen = new HashSet<string>(PathComparer);
        var roots = new List<string>();
        foreach (var cand in CandidateRoots())
        {
            if (string.IsNullOrWhiteSpace(cand)) continue;
            try
            {
                if (!Directory.Exists(cand)) continue;
                if (seen.Add(RealPath(cand))) roots.Add(cand);
            }
            catch { }
        }
        return roots;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var p in WindowsRegistryRoots()) yield return p;
            yield return Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)", "Steam");
            yield return Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files", "Steam");
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) yield break;

            if (OperatingSystem.IsMacOS())
            {
                yield return Path.Combine(home, "Library", "Application Support", "Steam");
                yield break;
            }

            string? xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            string dataHome = string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg;

            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".steam", "root");
            yield return Path.Combine(dataHome, "Steam");
            yield return Path.Combine(home, ".steam", "debian-installation");
            yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
            yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
            yield return Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam");
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> WindowsRegistryRoots()
    {
        var list = new List<string>();
        void Add(RegistryKey? hive, string sub, string name)
        {
            try
            {
                using var key = hive?.OpenSubKey(sub);
                if (key?.GetValue(name) is string s && s.Length > 0) list.Add(s.Replace('/', '\\'));
            }
            catch { }
        }
        Add(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        Add(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        Add(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");
        return list;
    }

    /// <summary>libraryfolders.vdf の本文からライブラリのパスを列挙する（新旧両形式）。</summary>
    public static List<string> ParseLibraryPaths(string vdfText)
    {
        var result = new List<string>();
        var root = VdfParser.Parse(vdfText);
        var folders = root.Get("libraryfolders") ?? root;
        foreach (var (key, node) in folders.Children)
        {
            if (key.Length == 0 || !key.All(char.IsAsciiDigit)) continue;
            string? path = node.Value ?? node.GetString("path");
            if (!string.IsNullOrWhiteSpace(path)) result.Add(path);
        }
        return result;
    }

    /// <summary>Steam ルートから、ルート自身を含むライブラリ一覧を返す。</summary>
    public static List<string> ReadLibraries(string steamRoot)
    {
        var libs = new List<string> { steamRoot };
        string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdf)) libs.AddRange(ParseLibraryPaths(File.ReadAllText(vdf)));
        }
        catch (Exception ex)
        {
            ConsoleUi.LogException(ex);
            ConsoleUi.Warn($"libraryfolders.vdf を読めませんでした: {vdf}");
        }
        return libs;
    }

    /// <summary>appmanifest の本文から installdir を取り出す。</summary>
    public static string? ParseInstallDir(string acfText) =>
        VdfParser.Parse(acfText).Get("AppState")?.GetString("installdir");

    /// <summary>ライブラリ内の SCPSL のゲームフォルダ。なければ null。</summary>
    public static string? FindGameInLibrary(string library)
    {
        string manifest = Path.Combine(library, "steamapps", $"appmanifest_{AppId}.acf");
        if (!File.Exists(manifest)) return null;

        string? installDir = null;
        try { installDir = ParseInstallDir(File.ReadAllText(manifest)); }
        catch (Exception ex) { ConsoleUi.LogException(ex); }
        if (string.IsNullOrWhiteSpace(installDir)) installDir = DefaultInstallDir;

        // installdir にパス区切りが入っていても common 配下から出ないようにする
        if (installDir.AsSpan().IndexOfAny('/', '\\') >= 0 || installDir.Contains("..", StringComparison.Ordinal)) return null;

        string game = Path.Combine(library, "steamapps", "common", installDir);
        return Directory.Exists(game) ? game : null;
    }

    /// <summary>全候補から見つかったゲームフォルダ（実パスで重複除去、検出順）。</summary>
    public static List<string> FindGameDirs()
    {
        var seen = new HashSet<string>(PathComparer);
        var result = new List<string>();
        foreach (var root in FindSteamRoots())
        {
            foreach (var lib in ReadLibraries(root))
            {
                try
                {
                    var game = FindGameInLibrary(lib);
                    if (game is not null && seen.Add(RealPath(game))) result.Add(game);
                }
                catch (Exception ex) { ConsoleUi.LogException(ex); }
            }
        }
        return result;
    }

    /// <summary>途中のディレクトリも含めてシンボリックリンクを解決した絶対パス。</summary>
    internal static string RealPath(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? "";
        string current = root;
        var pending = new Stack<string>(full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Reverse());
        int guard = 0;
        while (pending.Count > 0)
        {
            string next = Path.Combine(current, pending.Pop());
            try
            {
                var target = new DirectoryInfo(next).LinkTarget;
                if (target is not null && guard++ < 40)
                {
                    string resolved = Path.GetFullPath(Path.Combine(current, target));
                    current = Path.GetPathRoot(resolved) ?? root;
                    foreach (var seg in resolved[current.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries).Reverse())
                        pending.Push(seg);
                    // 解決後のパスは pending に積み直したので current はルートから再構築
                    continue;
                }
            }
            catch { }
            current = next;
        }
        return current;
    }

    /// <summary>手入力・ドラッグ&ドロップされたパスの前後の空白と引用符を除く。</summary>
    public static string NormalizeUserPath(string input)
    {
        string s = input.Trim();
        while (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1].Trim();
        return s.Trim('"', '\'').Trim();
    }

    public static bool LooksLikeGameDir(string dir) =>
        Directory.Exists(dir) && (
            Directory.Exists(Path.Combine(dir, "SCPSL_Data")) ||
            File.Exists(Path.Combine(dir, "SCPSL.exe")) ||
            Directory.Exists(Path.Combine(dir, "Translations")));
}
