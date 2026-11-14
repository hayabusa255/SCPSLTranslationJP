using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace SCPSLInstaller;

internal static class WindowsElevation
{
    public static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>同じ実行ファイルを管理者として再起動し、終了を待ってその終了コードを返す。</summary>
    public static int RelaunchElevated(string[] originalArgs, string gameDir, CancellationToken ct)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            throw new InstallerException("実行ファイルのパスが取得できないため、管理者として再起動できません。");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = Options.QuoteArguments(Options.BuildRelaunchArgs(originalArgs, gameDir)),
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Environment.CurrentDirectory,
        };

        try
        {
            using var proc = Process.Start(psi) ?? throw new InstallerException("管理者としての再起動に失敗しました。");
            while (!proc.WaitForExit(200))
            {
                if (ct.IsCancellationRequested) return ExitCodes.Cancelled;
            }
            return proc.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InstallerException("管理者権限の許可がキャンセルされました。ゲームフォルダへ書き込むには管理者権限が必要です。");
        }
    }
}
