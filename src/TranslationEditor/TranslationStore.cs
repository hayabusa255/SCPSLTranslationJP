using System.Text;

namespace TranslationEditor;

internal record FileSummary(string Name, int EnLines, int JpLines, int Untranslated);
internal record LineEntry(int Line, string En, string Jp);
internal record FileDetail(string Name, LineEntry[] Lines, string[] Extra);

// A parsed text file: line contents plus each line's own terminator, so mixed LF/CRLF files round-trip.
internal sealed class ParsedFile
{
    public bool Bom;
    public List<string> Lines = new();
    public List<string> Terminators = new();

    public static ParsedFile Parse(byte[] bytes)
    {
        var file = new ParsedFile { Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF };
        string text = new UTF8Encoding(false).GetString(bytes, file.Bom ? 3 : 0, bytes.Length - (file.Bom ? 3 : 0));
        int start = 0;
        while (start < text.Length)
        {
            int nl = text.IndexOf('\n', start);
            if (nl < 0)
            {
                file.Lines.Add(text[start..]);
                file.Terminators.Add("");
                break;
            }
            bool cr = nl > start && text[nl - 1] == '\r';
            file.Lines.Add(text[start..(cr ? nl - 1 : nl)]);
            file.Terminators.Add(cr ? "\r\n" : "\n");
            start = nl + 1;
        }
        return file;
    }

    // The most common terminator; LF when there is none to go by.
    public string Dominant()
    {
        int crlf = Terminators.Count(t => t == "\r\n");
        int lf = Terminators.Count(t => t == "\n");
        return crlf > lf ? "\r\n" : "\n";
    }
}

internal sealed class TranslationStore
{
    private readonly string _enDir;
    private readonly string _jpDir;
    private readonly object _lock = new();

    public TranslationStore(string enDir, string jpDir)
    {
        _enDir = enDir;
        _jpDir = jpDir;
    }

    private IEnumerable<string> Names() =>
        Directory.EnumerateFiles(_enDir, "*.txt").Select(p => Path.GetFileName(p)).OrderBy(n => n, StringComparer.Ordinal);

    // Only names of real en/*.txt files are accepted, which rules out path traversal.
    public bool Exists(string name) => Names().Contains(name, StringComparer.Ordinal);

    private ParsedFile? ReadJp(string name)
    {
        string path = Path.Combine(_jpDir, name);
        return File.Exists(path) ? ParsedFile.Parse(File.ReadAllBytes(path)) : null;
    }

    public int EnLineCount(string name) => ParsedFile.Parse(File.ReadAllBytes(Path.Combine(_enDir, name))).Lines.Count;

    public FileSummary[] List() => Names().Select(name =>
    {
        var en = ParsedFile.Parse(File.ReadAllBytes(Path.Combine(_enDir, name)));
        var jp = ReadJp(name);
        int untranslated = 0;
        for (int i = 0; i < en.Lines.Count; i++)
        {
            string j = jp is not null && i < jp.Lines.Count ? jp.Lines[i] : "";
            if (j.Length == 0 || j == en.Lines[i])
            {
                untranslated++;
            }
        }
        return new FileSummary(name, en.Lines.Count, jp?.Lines.Count ?? 0, untranslated);
    }).ToArray();

    public FileDetail? Get(string name)
    {
        if (!Exists(name))
        {
            return null;
        }
        var en = ParsedFile.Parse(File.ReadAllBytes(Path.Combine(_enDir, name)));
        var jp = ReadJp(name);
        var lines = en.Lines.Select((e, i) =>
            new LineEntry(i + 1, e, jp is not null && i < jp.Lines.Count ? jp.Lines[i] : "")).ToArray();
        string[] extra = jp is not null && jp.Lines.Count > en.Lines.Count ? jp.Lines.Skip(en.Lines.Count).ToArray() : [];
        return new FileDetail(name, lines, extra);
    }

    public void Save(string name, string[] lines)
    {
        lock (_lock)
        {
            string path = Path.Combine(_jpDir, name);
            var old = ReadJp(name);
            bool bom = old?.Bom ?? false;
            string dominant = old?.Dominant() ?? "\n";
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append(lines[i]);
                bool last = i == lines.Length - 1;
                if (old is not null && old.Lines.Count > 0)
                {
                    if (last)
                    {
                        // Keep whether the original ended with a newline, and which kind.
                        // An empty last line needs a terminator, or it would vanish when read back.
                        string t = old.Terminators[^1];
                        sb.Append(t.Length == 0 && lines[i].Length == 0 ? dominant : t);
                    }
                    else
                    {
                        string t = i < old.Terminators.Count ? old.Terminators[i] : "";
                        sb.Append(t.Length > 0 ? t : dominant);
                    }
                }
                else
                {
                    sb.Append(dominant);
                }
            }
            var bytes = new List<byte>();
            if (bom)
            {
                bytes.AddRange(new byte[] { 0xEF, 0xBB, 0xBF });
            }
            bytes.AddRange(new UTF8Encoding(false).GetBytes(sb.ToString()));
            File.WriteAllBytes(path, bytes.ToArray());
        }
    }
}
