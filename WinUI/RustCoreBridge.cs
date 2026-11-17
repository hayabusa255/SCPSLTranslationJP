using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SCPSL.WinUI;

/// <summary>
/// Thin IPC bridge to the standalone Rust core. All UI is real WinUI 3/XAML.
/// No Tauri, Chromium, WebView2 or browser-based rendering.
/// </summary>
public sealed class RustCoreBridge
{
    private readonly string _executable =
        Path.Combine(AppContext.BaseDirectory, "scpsl-native-core.exe");

    public sealed record ProgressUpdate(int Percent, string Stage, string Detail);

    public async Task<JsonElement> RunAsync(
        string command,
        string? gameDirectory = null,
        Action<ProgressUpdate>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_executable))
            throw new FileNotFoundException(
                "Rustの実行ファイルが見つかりません。scpsl-native-core.exe をアプリと同じフォルダーに配置してください。",
                _executable);

        var start = new ProcessStartInfo
        {
            FileName = _executable,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add(command);
        if (!string.IsNullOrWhiteSpace(gameDirectory))
            start.ArgumentList.Add(gameDirectory);

        using var process = new Process { StartInfo = start };
        if (!process.Start())
            throw new InvalidOperationException("Rust翻訳エンジンを起動できません");

        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        JsonElement? result = null;
        string? error = null;

        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var message = doc.RootElement;
            if (!message.TryGetProperty("kind", out var kindValue)) continue;
            switch (kindValue.GetString())
            {
                case "progress":
                    var p = new ProgressUpdate(
                        message.TryGetProperty("percent", out var num) ? num.GetInt32() : 0,
                        message.TryGetProperty("stage", out var stage) ? stage.GetString() ?? "" : "",
                        message.TryGetProperty("detail", out var detail) ? detail.GetString() ?? "" : "");
                    onProgress?.Invoke(p);
                    break;
                case "result":
                    result = message.GetProperty("data").Clone();
                    break;
                case "error":
                    error = message.GetProperty("message").GetString();
                    break;
            }
        }

        await process.WaitForExitAsync(cancellationToken);
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || error is not null)
            throw new InvalidOperationException(error ?? stderr.Trim() ?? "Rust翻訳エンジンでエラーが発生しました");
        return result ?? throw new InvalidOperationException("Rustから結果が返されませんでした");
    }
}