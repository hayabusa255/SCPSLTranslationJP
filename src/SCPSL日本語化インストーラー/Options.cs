using System.Reflection;
using System.Text.RegularExpressions;

namespace SCPSLInstaller;

internal sealed class Options
{
    private static readonly Regex RepoPattern = new(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    /// <summary>ビルド時に指定がないときの取得元。</summary>
    public const string FallbackRepo = "hayabusa255/SCPSLTranslationJP";

    /// <summary>既定の取得元。ビルド時の InstallerRepository（AssemblyMetadata）を使い、読めない・形式不正なら FallbackRepo。</summary>
    public static string DefaultRepo { get; } = ResolveDefaultRepo();

    private static string ResolveDefaultRepo()
    {
        foreach (var attr in typeof(Options).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (attr.Key == "InstallerRepository" && attr.Value is { } v && RepoPattern.IsMatch(v))
                return v;
        }
        return FallbackRepo;
    }

    public string? GameDir { get; set; }
    public bool Yes { get; set; }
    public bool Force { get; set; }
    public bool DryRun { get; set; }
    public string Repo { get; set; } = DefaultRepo;
    public bool NoPause { get; set; }
    public bool Help { get; set; }
    public bool Version { get; set; }

    /// <summary>引数を解析する。失敗時は Error にメッセージが入る。</summary>
    public static (Options Options, string? Error) Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? inlineValue = null;
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                int eq = arg.IndexOf('=');
                if (eq > 0)
                {
                    inlineValue = arg[(eq + 1)..];
                    arg = arg[..eq];
                }
            }

            switch (arg)
            {
                case "--game-dir":
                    if (!TryValue(args, ref i, inlineValue, out string? dir)) return (o, "--game-dir にはパスを指定してください。");
                    o.GameDir = dir;
                    break;
                case "--repo":
                    if (!TryValue(args, ref i, inlineValue, out string? repo)) return (o, "--repo には owner/name を指定してください。");
                    if (!RepoPattern.IsMatch(repo!)) return (o, $"--repo の形式が正しくありません: {repo}（owner/name の形式で指定してください）");
                    o.Repo = repo!;
                    break;
                case "-y":
                case "--yes":
                    o.Yes = true;
                    break;
                case "--force":
                    o.Force = true;
                    break;
                case "--dry-run":
                    o.DryRun = true;
                    break;
                case "--no-pause":
                    o.NoPause = true;
                    break;
                case "-h":
                case "--help":
                    o.Help = true;
                    break;
                case "--version":
                    o.Version = true;
                    break;
                default:
                    return (o, $"不明な引数です: {args[i]}");
            }
        }
        return (o, null);
    }

    private static bool TryValue(string[] args, ref int i, string? inlineValue, out string? value)
    {
        if (inlineValue is not null)
        {
            value = inlineValue;
            return inlineValue.Length > 0;
        }
        if (i + 1 < args.Length)
        {
            value = args[++i];
            return value.Length > 0;
        }
        value = null;
        return false;
    }

    /// <summary>昇格して再起動する際の引数。元の --game-dir は除き、検出したゲームフォルダを付ける。</summary>
    public static string[] BuildRelaunchArgs(string[] original, string gameDir)
    {
        var result = new List<string>();
        for (int i = 0; i < original.Length; i++)
        {
            string a = original[i];
            if (a == "--game-dir") { i++; continue; }
            if (a.StartsWith("--game-dir=", StringComparison.Ordinal)) continue;
            result.Add(a);
        }
        result.Add("--game-dir");
        result.Add(gameDir);
        return result.ToArray();
    }

    /// <summary>Windows のコマンドライン規則（CommandLineToArgvW）に従って引数を連結する。</summary>
    public static string QuoteArguments(IEnumerable<string> args) => string.Join(' ', args.Select(QuoteArgument));

    internal static string QuoteArgument(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0) return arg;

        var sb = new System.Text.StringBuilder("\"");
        int backslashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                backslashes++;
            }
            else if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
            }
            else
            {
                sb.Append('\\', backslashes).Append(c);
                backslashes = 0;
            }
        }
        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }

    public static string Usage => $"""
        使い方: SCPSL [オプション]

        SCP: Secret Laboratory の日本語化ファイルを最新版に更新します。

        オプション:
          --game-dir <path>   ゲームフォルダを指定する（自動検出をスキップ）
          -y, --yes           確認をすべて Yes として進める
          --force             最新版が入っていても入れ直す
          --dry-run           検出結果・最新タグ・削除予定を表示するだけで変更しない
          --repo <owner/name> 取得元リポジトリ（既定: {DefaultRepo}）
          --no-pause          終了時にキー入力を待たない
          -h, --help          このヘルプを表示
          --version           バージョンを表示

        終了コード: 0 成功/既に最新, 1 エラー, 2 引数エラー, 3 ゲームが見つからない, 130 中断
        """;
}
