using System.Text;

namespace TranslationEditor;

internal record ChangedItem(int Line, string Jp);
internal record FileSummary(string Name, int EnLines, int JpLines, int Untranslated, int Changed, ChangedItem[] ChangedItems);
internal record LineEntry(int Line, string En, string Jp, string? Base);
internal record FileDetail(string Name, LineEntry[] Lines, string[] Extra, bool BaseExists);

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

    // A line counts as changed when the base translation differs from the current one.
    // A missing base line equals an empty line; a file missing from the base is changed on every line.
    private static bool IsChanged(ParsedFile? baseFile, int i, string jp) =>
        baseFile is null || (i < baseFile.Lines.Count ? baseFile.Lines[i] : "") != jp;

    public FileSummary[] List(BaseContext? b)
    {
        var changedNames = b?.ChangedNames;
        return Names().Select(name => Summarize(name, b, changedNames)).ToArray();
    }

    private FileSummary Summarize(string name, BaseContext? b, HashSet<string>? changedNames)
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
        var items = new List<ChangedItem>();
        if (b is not null && changedNames!.Contains(name))
        {
            var baseFile = b.Read(name);
            for (int i = 0; i < en.Lines.Count; i++)
            {
                string j = jp is not null && i < jp.Lines.Count ? jp.Lines[i] : "";
                if (IsChanged(baseFile, i, j))
                {
                    items.Add(new ChangedItem(i + 1, j));
                }
            }
        }
        return new FileSummary(name, en.Lines.Count, jp?.Lines.Count ?? 0, untranslated, items.Count, items.ToArray());
    }

    public FileDetail? Get(string name, BaseContext? b)
    {
        if (!Exists(name))
        {
            return null;
        }
        var en = ParsedFile.Parse(File.ReadAllBytes(Path.Combine(_enDir, name)));
        var jp = ReadJp(name);
        var baseFile = b?.Read(name);
        var lines = en.Lines.Select((e, i) =>
            new LineEntry(i + 1, e, jp is not null && i < jp.Lines.Count ? jp.Lines[i] : "",
                baseFile is not null && i < baseFile.Lines.Count ? baseFile.Lines[i] : null)).ToArray();
        string[] extra = jp is not null && jp.Lines.Count > en.Lines.Count ? jp.Lines.Skip(en.Lines.Count).ToArray() : [];
        return new FileDetail(name, lines, extra, baseFile is not null);
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
