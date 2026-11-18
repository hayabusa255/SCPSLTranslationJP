using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace TranslationEditor;

internal record BaseInfo(bool Available, string? Ref, string? Commit, string? Subject, string? Error);

// Comparison base for the review feature: a git commit-ish. All git calls use an argument list, never a shell.
internal sealed class GitBase
{
    private static readonly Regex RefPattern = new(@"^[A-Za-z0-9_./~^@{}-]+$", RegexOptions.Compiled);
    private readonly string _root;

    private GitBase(string root, bool available)
    {
        _root = root;
        Available = available;
    }

    public bool Available { get; }

    public static GitBase Detect(string root)
    {
        var r = Run(root, "rev-parse", "--is-inside-work-tree");
        bool ok = r is { Code: 0 } && Encoding.UTF8.GetString(r.Value.Out).Trim() == "true";
        return new GitBase(root, ok);
    }

    private static (int Code, byte[] Out)? Run(string root, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(root);
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }
            using var p = Process.Start(psi);
            if (p is null)
            {
                return null;
            }
            p.StandardInput.Close();
            var ms = new MemoryStream();
            var outTask = p.StandardOutput.BaseStream.CopyToAsync(ms);
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(30000))
            {
                try { p.Kill(true); } catch { }
                return null;
            }
            outTask.Wait();
            errTask.Wait();
            return (p.ExitCode, ms.ToArray());
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    // Validates the ref and resolves it to a commit. Returns null when it is not acceptable.
    public BaseContext? Resolve(string? reference)
    {
        if (!Available || string.IsNullOrEmpty(reference) || reference.StartsWith('-') || !RefPattern.IsMatch(reference))
        {
            return null;
        }
        var rev = Run(_root, "rev-parse", "--verify", "--quiet", reference + "^{commit}");
        if (rev is not { Code: 0 })
        {
            return null;
        }
        string hash = Encoding.UTF8.GetString(rev.Value.Out).Trim();
        if (hash.Length == 0)
        {
            return null;
        }
        var log = Run(_root, "log", "-1", "--format=%h%x00%s", hash);
        string[] parts = log is { Code: 0 } ? Encoding.UTF8.GetString(log.Value.Out).Trim().Split('\0', 2) : [hash[..7], ""];
        return new BaseContext(this, reference, hash, parts[0], parts.Length > 1 ? parts[1] : "");
    }

    internal HashSet<string> ChangedNames(string hash)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var args in new[]
        {
            new[] { "diff", "--name-only", "-z", "--relative", hash, "--", "JP/" },
            new[] { "ls-files", "--others", "--exclude-standard", "-z", "--", "JP/" },
        })
        {
            var r = Run(_root, args);
            if (r is not { Code: 0 })
            {
                continue;
            }
            foreach (string path in Encoding.UTF8.GetString(r.Value.Out).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (path.StartsWith("JP/", StringComparison.Ordinal) && path.IndexOf('/', 3) < 0)
                {
                    set.Add(path[3..]);
                }
            }
        }
        return set;
    }

    internal ParsedFile? Show(string hash, string name)
    {
        var r = Run(_root, "show", hash + ":./JP/" + name);
        return r is { Code: 0 } ? ParsedFile.Parse(r.Value.Out) : null;
    }
}

internal sealed class BaseContext
{
    private readonly GitBase _git;
    private HashSet<string>? _changed;

    public BaseContext(GitBase git, string reference, string hash, string shortHash, string subject)
    {
        _git = git;
        Ref = reference;
        Hash = hash;
        Short = shortHash;
        Subject = subject;
    }

    public string Ref { get; }
    public string Hash { get; }
    public string Short { get; }
    public string Subject { get; }

    public BaseInfo Info => new(true, Ref, Short, Subject, null);
    public HashSet<string> ChangedNames => _changed ??= _git.ChangedNames(Hash);
    public ParsedFile? Read(string name) => _git.Show(Hash, name);
}
