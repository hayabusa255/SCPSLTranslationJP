using System.Text;

namespace SCPSLInstaller;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        var (opts, error) = Options.Parse(args);
        bool pause = ConsoleUi.ShouldPause(opts.NoPause);

        int code;
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            if (error is not null)
            {
                ConsoleUi.Error(error);
                ConsoleUi.Line(Options.Usage);
                code = ExitCodes.BadArgs;
            }
            else if (opts.Help)
            {
                ConsoleUi.Line(Options.Usage);
                code = ExitCodes.Success;
            }
            else if (opts.Version)
            {
                ConsoleUi.Line($"SCPSL {AppInfo.Version}");
                code = ExitCodes.Success;
            }
            else
            {
                (code, bool relaunched) = await RunAsync(opts, args, cts.Token);
                if (relaunched) pause = false; // 昇格した子プロセス側で待つ
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            ConsoleUi.Warn("中断しました。");
            code = ExitCodes.Cancelled;
        }
        catch (InstallerException ex)
        {
            ConsoleUi.LogException(ex);
            ConsoleUi.Error(ex.Message);
            ConsoleUi.Line($"詳細はログを確認してください: {ConsoleUi.LogPath}");
            code = ExitCodes.Error;
        }
        catch (Exception ex)
        {
            ConsoleUi.LogException(ex);
            ConsoleUi.Error($"予期しないエラーが発生しました: {ex.Message}");
            ConsoleUi.Line($"詳細はログを確認してください: {ConsoleUi.LogPath}");
            code = ExitCodes.Error;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }

        if (pause) ConsoleUi.Pause();
        return code;
    }

    private static async Task<(int Code, bool Relaunched)> RunAsync(Options opts, string[] args, CancellationToken ct)
    {
        // 1. ゲームフォルダ
        string? gameDir = ResolveGameDir(opts);
        if (gameDir is null) return (ExitCodes.GameNotFound, false);
        string translationsDir = Path.Combine(gameDir, "Translations");
        ConsoleUi.Info($"ゲームフォルダ: {gameDir}");

        // 2. 最新タグ
        ConsoleUi.Info($"最新バージョンを確認しています ({opts.Repo}) ...");
        var release = await ReleaseClient.GetLatestAsync(opts.Repo, ct);
        string tag = release.Tag;
        ConsoleUi.Info($"最新バージョン: {tag}");

        bool installed = TranslationInstaller.IsInstalled(translationsDir, tag);

        if (opts.DryRun)
        {
            ConsoleUi.Line();
            ConsoleUi.Info("[dry-run] 変更は行いません。");
            ConsoleUi.Line($"  Translations: {translationsDir} ({(Directory.Exists(translationsDir) ? "存在" : "未作成")})");
            ConsoleUi.Line($"  ダウンロード元: {release.DownloadUrl}");
            ConsoleUi.Line($"  最新版 {tag}: {(installed ? "インストール済み" : Directory.Exists(Path.Combine(translationsDir, tag)) ? "壊れたインストール（入れ直し対象）" : "未インストール")}");
            if (installed && !opts.Force) ConsoleUi.Line("  → 最新版の入れ直しは不要です。");
            var old = TranslationInstaller.FindOldJapaneseTranslations(translationsDir, tag);
            if (old.Count == 0) ConsoleUi.Line("  削除予定の古い日本語訳: なし");
            foreach (var o in old) ConsoleUi.Line($"  削除予定の古い日本語訳: {o.FolderName}{(o.DisplayName is null ? "" : $" ({o.DisplayName})")}");
            return (ExitCodes.Success, false);
        }

        // 3. 書き込み権限
        var access = TranslationInstaller.CheckWritable(translationsDir, out string? reason);
        if (access == TranslationInstaller.WriteAccess.Failed)
        {
            ConsoleUi.Error($"Translations フォルダに書き込めません: {reason}");
            ConsoleUi.Line($"  {translationsDir}");
            return (ExitCodes.Error, false);
        }
        if (access == TranslationInstaller.WriteAccess.NeedsElevation)
        {
            if (OperatingSystem.IsWindows() && !WindowsElevation.IsAdministrator())
            {
                ConsoleUi.Warn("ゲームフォルダへの書き込み権限がありません。管理者として再起動します。");
                int childCode = WindowsElevation.RelaunchElevated(args, gameDir, ct);
                return (childCode, true);
            }
            ConsoleUi.Error($"Translations フォルダへ書き込めません: {translationsDir}");
            ConsoleUi.Line(OperatingSystem.IsWindows()
                ? "管理者権限でも書き込めません。フォルダの権限やセキュリティソフトを確認してください。"
                : "フォルダの所有者と権限を確認してください（sudo での実行はおすすめしません）。");
            return (ExitCodes.Error, false);
        }

        // 4. 取り残しの掃除と最新版判定
        int stale = TranslationInstaller.CleanupStaleWorkDirs(translationsDir);
        if (stale > 0) ConsoleUi.Info($"前回の作業フォルダを {stale} 件掃除しました。");

        if (installed && !opts.Force)
        {
            ConsoleUi.Info($"すでに最新版 ({tag}) の日本語化ファイルがインストールされています。");
            RemoveOldTranslations(translationsDir, tag, opts);
            return (ExitCodes.Success, false);
        }
        if (!installed && Directory.Exists(Path.Combine(translationsDir, tag)))
            ConsoleUi.Warn($"{tag} フォルダが不完全なため入れ直します。");

        // 5. ダウンロード
        string tempDir = Path.Combine(Path.GetTempPath(), $"SCPSLTranslationJP-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string zipPath = Path.Combine(tempDir, tag + ".zip");
            ConsoleUi.Info($"{tag}.zip をダウンロードします。");
            var (report, finish) = ConsoleUi.CreateDownloadProgress();
            try { await ReleaseClient.DownloadAsync(release.DownloadUrl, zipPath, report, ct, tag); }
            finally { finish(); }

            // 6. 展開と配置
            ConsoleUi.Info($"展開しています: {Path.Combine(translationsDir, tag)}");
            TranslationInstaller.InstallFromZip(translationsDir, tag, zipPath, ct);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch (Exception ex) { ConsoleUi.LogException(ex); }
        }
        ConsoleUi.Info($"{tag} のインストールが完了しました。");

        // 7. 旧版の削除（新版の配置に成功した後）
        RemoveOldTranslations(translationsDir, tag, opts);

        ConsoleUi.Info("ゲーム内の設定で言語を日本語に変更し、ゲームを再起動してください。");
        return (ExitCodes.Success, false);
    }

    private static void RemoveOldTranslations(string translationsDir, string tag, Options opts)
    {
        var old = TranslationInstaller.FindOldJapaneseTranslations(translationsDir, tag);
        foreach (var o in old)
        {
            string label = $"古い日本語訳フォルダ「{o.FolderName}」{(o.DisplayName is null ? "" : $" ({o.DisplayName})")}";
            if (!opts.Yes && !ConsoleUi.IsInteractive)
            {
                ConsoleUi.Warn($"{label} が残っています。削除するには --yes を付けて実行してください。");
                continue;
            }
            if (!ConsoleUi.Confirm($"{label} を削除しますか？", opts.Yes))
            {
                ConsoleUi.Info("削除をスキップしました。");
                continue;
            }
            try
            {
                Directory.Delete(o.Path, true);
                ConsoleUi.Info($"削除しました: {o.FolderName}");
            }
            catch (Exception ex)
            {
                ConsoleUi.LogException(ex);
                ConsoleUi.Warn($"削除できませんでした: {o.FolderName} ({ex.Message})");
            }
        }
    }

    /// <summary>ゲームフォルダを決める。見つからなければ null（メッセージ表示済み）。</summary>
    private static string? ResolveGameDir(Options opts)
    {
        if (opts.GameDir is not null)
        {
            string dir = SteamLocator.NormalizeUserPath(opts.GameDir);
            if (SteamLocator.LooksLikeGameDir(dir)) return Path.GetFullPath(dir);
            ConsoleUi.Error($"指定されたフォルダがゲームフォルダとして認識できません: {dir}（SCPSL_Data / SCPSL.exe / Translations のいずれかが必要です）");
            return null;
        }

        var found = SteamLocator.FindGameDirs();
        if (found.Count > 0)
        {
            if (found.Count > 1)
            {
                ConsoleUi.Warn($"ゲームフォルダが {found.Count} か所見つかりました。最初のものを使います。");
                foreach (var f in found.Skip(1)) ConsoleUi.Line($"  他の候補: {f}");
            }
            return found[0];
        }

        ConsoleUi.Warn("SCP: Secret Laboratory のゲームフォルダが自動では見つかりませんでした。");
        if (!ConsoleUi.IsInteractive)
        {
            ConsoleUi.Error("--game-dir でゲームフォルダを指定してください。");
            return null;
        }

        while (true)
        {
            string? input = ConsoleUi.Prompt("ゲームフォルダのパスを入力してください（空欄で終了）: ");
            if (input is null) return null;
            string dir = SteamLocator.NormalizeUserPath(input);
            if (dir.Length == 0) return null;
            if (SteamLocator.LooksLikeGameDir(dir)) return Path.GetFullPath(dir);
            ConsoleUi.Warn("SCPSL_Data / SCPSL.exe / Translations のいずれも見つかりません。もう一度入力してください。");
        }
    }
}
