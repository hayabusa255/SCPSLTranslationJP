using System.Diagnostics;
using System.Text;
using TranslationEditor;

// Usage: TranslationEditor [repo root] [--port N] [--no-browser]
string? rootArg = null;
int port = int.TryParse(Environment.GetEnvironmentVariable("TRANSLATION_EDITOR_PORT"), out var envPort) ? envPort : 5080;
bool openBrowser = true;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
    {
        port = p;
        i++;
    }
    else if (args[i] == "--no-browser")
    {
        openBrowser = false;
    }
    else if (!args[i].StartsWith("--"))
    {
        rootArg = args[i];
    }
}

string? root = rootArg is not null ? Path.GetFullPath(rootArg) : FindRoot();
if (root is null || !Directory.Exists(Path.Combine(root, "en")) || !Directory.Exists(Path.Combine(root, "JP")))
{
    Console.Error.WriteLine("en と JP を両方持つリポジトリのルートが見つかりません。引数でルートを指定してください。");
    return 1;
}

var store = new TranslationStore(Path.Combine(root, "en"), Path.Combine(root, "JP"));

var builder = WebApplication.CreateBuilder();
string url = $"http://localhost:{port}";
builder.WebHost.UseUrls(url);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/files", () => store.List());

app.MapGet("/api/files/{name}", (string name) =>
    store.Get(name) is { } result ? Results.Ok(result) : Results.NotFound());

app.MapPut("/api/files/{name}", (string name, SaveRequest req) =>
{
    if (!store.Exists(name))
    {
        return Results.NotFound();
    }
    if (req.Lines is null || req.Lines.Length != store.EnLineCount(name))
    {
        return Results.BadRequest(new { error = "行数が en と一致しません。" });
    }
    if (req.Lines.Any(l => l is null || l.Contains('\n') || l.Contains('\r')))
    {
        return Results.BadRequest(new { error = "行の中に改行は含められません。" });
    }
    store.Save(name, req.Lines);
    return Results.Ok(new { saved = true });
});

Console.WriteLine($"ルート: {root}");
Console.WriteLine($"翻訳エディタ: {url}");
if (openBrowser)
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore: the URL has already been printed.
        }
    });
}

app.Run();
return 0;

static string? FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "en")) && Directory.Exists(Path.Combine(dir.FullName, "JP")))
        {
            return dir.FullName;
        }
    }
    return null;
}

internal record SaveRequest(string[]? Lines);
