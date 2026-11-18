using System.Net;
using System.Text.Json;

namespace SCPSLInstaller;

internal sealed record ReleaseInfo(string Tag, string DownloadUrl);

internal static class ReleaseClient
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    private static readonly Lazy<HttpClient> LazyHttp = new(() =>
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"SCPSLTranslationJP-Installer/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    });

    private static HttpClient Http => LazyHttp.Value;

    public static string DefaultDownloadUrl(string repo, string tag) =>
        $"https://github.com/{repo}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(tag)}.zip";

    /// <summary>タグがフォルダ名として安全かを検証する。</summary>
    public static bool IsValidTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > 100) return false;
        if (tag != tag.Trim()) return false;
        if (tag.Contains("..", StringComparison.Ordinal) || tag.StartsWith('.')) return false;
        if (tag.AsSpan().IndexOfAny('/', '\\') >= 0) return false;
        if (tag.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return true;
    }

    /// <summary>Location ヘッダの値からタグを取り出す。</summary>
    public static string? ParseTagFromLocation(string location)
    {
        const string marker = "/releases/tag/";
        int i = location.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return null;
        string rest = location[(i + marker.Length)..];
        int end = rest.IndexOfAny(['?', '#', '/']);
        if (end >= 0) rest = rest[..end];
        if (rest.Length == 0) return null;
        try { return Uri.UnescapeDataString(rest); } catch (UriFormatException) { return null; }
    }

    public static async Task<ReleaseInfo> GetLatestAsync(string repo, CancellationToken ct)
    {
        string? tag = null;
        try
        {
            tag = await RetryAsync(GetTagFromRedirectAsync, "最新タグの取得", ct, repo);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            ConsoleUi.LogException(ex);
            ConsoleUi.Warn("リリースページから最新タグを取得できませんでした。GitHub API で再試行します。");
        }

        if (tag is not null)
        {
            if (!IsValidTag(tag)) throw new InstallerException($"最新タグの値が不正です: {tag}");
            return new ReleaseInfo(tag, DefaultDownloadUrl(repo, tag));
        }

        try
        {
            var info = await RetryAsync(GetFromApiAsync, "GitHub API からの最新リリース取得", ct, repo);
            if (!IsValidTag(info.Tag)) throw new InstallerException($"最新タグの値が不正です: {info.Tag}");
            return info;
        }
        catch (Exception ex) when (ex is not InstallerException && !ct.IsCancellationRequested)
        {
            throw new InstallerException($"最新リリースを取得できませんでした。ネットワーク接続を確認してください。({ex.Message})", ex);
        }
    }

    private static async Task<string?> GetTagFromRedirectAsync(string repo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://github.com/{repo}/releases/latest");
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
        {
            string? loc = resp.Headers.Location?.ToString();
            return loc is null ? null : ParseTagFromLocation(loc);
        }
        resp.EnsureSuccessStatusCode();
        return null; // 200 が返ったらリリースが無いなど。API にフォールバック
    }

    private static async Task<ReleaseInfo> GetFromApiAsync(string repo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseApiRelease(doc.RootElement, repo);
    }

    internal static ReleaseInfo ParseApiRelease(JsonElement root, string repo)
    {
        if (!root.TryGetProperty("tag_name", out var tagEl) || tagEl.GetString() is not { Length: > 0 } tag)
            throw new InstallerException("GitHub API の応答に tag_name がありません。");

        string url = DefaultDownloadUrl(repo, tag);
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                if (a.TryGetProperty("name", out var n) && n.GetString() == tag + ".zip"
                    && a.TryGetProperty("browser_download_url", out var u) && u.GetString() is { } s
                    && s.StartsWith("https://", StringComparison.Ordinal))
                {
                    url = s;
                    break;
                }
            }
        }
        return new ReleaseInfo(tag, url);
    }

    /// <summary>URL をファイルへダウンロードする。リトライ・停止検出・サイズ検証つき。</summary>
    public static Task DownloadAsync(string url, string destPath, Action<long, long?>? progress, CancellationToken ct, string? tag = null) =>
        RetryAsync(async (u, c) =>
        {
            await DownloadOnceAsync(u, destPath, progress, c, tag);
            return 0;
        }, "ダウンロード", ct, url);

    private static async Task DownloadOnceAsync(string url, string destPath, Action<long, long?>? progress, CancellationToken ct, string? tag)
    {
        var uri = new Uri(url);
        HttpResponseMessage? resp = null;
        try
        {
            // リダイレクトは自分で追う（https のみ許可）
            for (int hop = 0; ; hop++)
            {
                if (uri.Scheme != Uri.UriSchemeHttps) throw new InstallerException($"https 以外のダウンロード先は許可されません: {uri}");
                if (hop > 8) throw new InstallerException("リダイレクトが多すぎます。");
                resp?.Dispose();
                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } loc)
                {
                    uri = loc.IsAbsoluteUri ? loc : new Uri(uri, loc);
                    continue;
                }
                break;
            }
            if (resp.StatusCode == HttpStatusCode.NotFound)
                throw new InstallerException($"最新版（{tag ?? "不明"}）の配布ファイルがまだ見つかりません。リリース直後で準備中の可能性があります。数分後にもう一度実行してください。");
            resp.EnsureSuccessStatusCode();

            long? expected = resp.Content.Headers.ContentLength;
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stall.CancelAfter(StallTimeout);

            await using var src = await resp.Content.ReadAsStreamAsync(stall.Token);
            await using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buf = new byte[81920];
            long total = 0;
            int n;
            while ((n = await src.ReadAsync(buf, stall.Token)) > 0)
            {
                stall.CancelAfter(StallTimeout);
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                total += n;
                progress?.Invoke(total, expected);
            }
            await dst.FlushAsync(ct);

            if (expected is { } e && e != total)
                throw new IOException($"ダウンロードしたサイズが一致しません（{total} / {e} バイト）。");
        }
        finally
        {
            resp?.Dispose();
        }
    }

    private static bool IsTransient(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        return ex switch
        {
            InstallerException => false,
            HttpRequestException h => h.StatusCode is null || (int)h.StatusCode >= 500
                || h.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
            TaskCanceledException => true, // 自分のキャンセルでなければタイムアウト
            TimeoutException => true,
            OperationCanceledException => true,
            IOException => true,
            _ => false,
        };
    }

    private static async Task<T> RetryAsync<T>(Func<string, CancellationToken, Task<T>> op, string what, CancellationToken ct, string arg)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await op(arg, ct);
            }
            catch (Exception ex) when (attempt < MaxRetries && IsTransient(ex, ct))
            {
                var delay = TimeSpan.FromSeconds(1 << attempt);
                ConsoleUi.LogException(ex);
                ConsoleUi.Warn($"{what}に失敗しました（{ex.Message}）。{delay.TotalSeconds:0} 秒後に再試行します（{attempt + 1}/{MaxRetries}）。");
                await Task.Delay(delay, ct);
            }
        }
    }
}
