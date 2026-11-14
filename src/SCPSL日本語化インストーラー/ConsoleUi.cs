using System.Runtime.InteropServices;

namespace SCPSLInstaller;

internal static partial class NativeMethods
{
    [LibraryImport("kernel32.dll")]
    internal static partial uint GetConsoleProcessList(ref uint processList, uint processCount);
}

internal static class ConsoleUi
{
    private static readonly object Gate = new();
    private static bool _progressLineOpen;

    public static string LogPath => Path.Combine(Path.GetTempPath(), "SCPSLTranslationJP-installer.log");

    public static bool IsInteractive => !Console.IsInputRedirected;

    public static void Info(string text) => Write(ConsoleColor.Green, "Info", text);
    public static void Warn(string text) => Write(ConsoleColor.Yellow, "Warn", text);
    public static void Error(string text) => Write(ConsoleColor.Red, "Error", text);

    /// <summary>色なしの通常出力。</summary>
    public static void Line(string text = "")
    {
        lock (Gate)
        {
            EndProgressLine();
            Console.WriteLine(text);
        }
        AppendLog("Out", text);
    }

    private static void Write(ConsoleColor color, string level, string text)
    {
        lock (Gate)
        {
            EndProgressLine();
            try { Console.ForegroundColor = color; } catch { }
            Console.WriteLine($"[{level}] {text}");
            try { Console.ResetColor(); } catch { }
        }
        AppendLog(level, text);
    }

    public static void LogException(Exception ex) => AppendLog("Exception", ex.ToString());

    private static void AppendLog(string level, string text)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {text}{Environment.NewLine}");
        }
        catch
        {
            // ログに書けなくても処理は続ける
        }
    }

    private static void EndProgressLine()
    {
        if (_progressLineOpen)
        {
            Console.WriteLine();
            _progressLineOpen = false;
        }
    }

    /// <summary>[Y/n] 形式の確認。--yes なら確認なしで true。非対話で yes でない場合は false。</summary>
    public static bool Confirm(string question, bool assumeYes)
    {
        if (assumeYes) return true;
        if (!IsInteractive) return false;
        while (true)
        {
            lock (Gate) { EndProgressLine(); Console.Write($"{question} [Y/n] "); }
            string? input = Console.ReadLine();
            if (input is null) return false;
            input = input.Trim();
            if (input.Length == 0 || input.Equals("y", StringComparison.OrdinalIgnoreCase) || input.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (input.Equals("n", StringComparison.OrdinalIgnoreCase) || input.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
        }
    }

    public static string? Prompt(string message)
    {
        lock (Gate) { EndProgressLine(); Console.Write(message); }
        return Console.ReadLine();
    }

    /// <summary>ダウンロード進捗の表示用コールバックと、終了処理を返す。</summary>
    public static (Action<long, long?> Report, Action Finish) CreateDownloadProgress()
    {
        bool tty = !Console.IsOutputRedirected;
        long lastTicks = 0;
        int lastBucket = -1;
        bool printed = false;

        void Report(long done, long? total)
        {
            string text;
            int bucket = -1;
            if (total is > 0)
            {
                int pct = (int)(done * 100 / total.Value);
                bucket = pct / 25;
                text = $"ダウンロード中: {Mb(done)} / {Mb(total.Value)} ({pct}%)";
            }
            else text = $"ダウンロード中: {Mb(done)}";

            lock (Gate)
            {
                if (tty)
                {
                    long now = Environment.TickCount64;
                    bool last = total is > 0 && done >= total.Value;
                    if (now - lastTicks < 100 && !last) return;
                    lastTicks = now;
                    Console.Write("\r" + text.PadRight(60));
                    _progressLineOpen = true;
                    printed = true;
                }
                else
                {
                    if (bucket < 0 || bucket == lastBucket) return;
                    lastBucket = bucket;
                    Console.WriteLine(text);
                    printed = true;
                }
            }
        }

        void Finish()
        {
            lock (Gate)
            {
                if (printed) EndProgressLine();
            }
        }

        return (Report, Finish);
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:0.0} MB";

    /// <summary>ダブルクリック起動（Windows で自分専用コンソール）のときだけ待つ。</summary>
    public static bool ShouldPause(bool noPause)
    {
        if (noPause || Console.IsInputRedirected || !OperatingSystem.IsWindows()) return false;
        try
        {
            uint pid = 0;
            return NativeMethods.GetConsoleProcessList(ref pid, 1) == 1;
        }
        catch
        {
            return false;
        }
    }

    public static void Pause()
    {
        try
        {
            Console.WriteLine();
            Console.WriteLine("何かキーを押すと終了します。");
            Console.ReadKey(true);
        }
        catch
        {
        }
    }
}
