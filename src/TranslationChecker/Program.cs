using System.Collections.Concurrent;
using System.Text;

namespace TranslationChecker;

internal class Program
{
    private static async Task Main(string[] args)
    {
        try
        {
            string? rootPath = args.Length > 0 ? args[0] : FindRepositoryRoot();
            string enPath = Path.Combine(rootPath ?? "", "en");
            string jpPath = Path.Combine(rootPath ?? "", "JP");

            if (rootPath is null || !Directory.Exists(enPath))
            {
                Console.WriteLine("❌ English folder 'en' not found!");
            }
            else if (!Directory.Exists(jpPath))
            {
                Console.WriteLine("❌ Japanese folder 'JP' not found!");
            }
            else
            {
                Console.WriteLine(await CheckTranslations(enPath, jpPath));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("❌ An unexpected error occurred: " + ex.Message);
        }
    }

    // The checker can be run from the repository root or any subfolder, so walk up
    // from the current directory until a folder containing both 'en' and 'JP' is found.
    private static string? FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "en")) &&
                Directory.Exists(Path.Combine(dir.FullName, "JP")))
            {
                return dir.FullName;
            }
        }
        return null;
    }

    private static async Task<string> CheckTranslations(string enPath, string jpPath)
    {
        List<string> enFiles = Directory.GetFiles(enPath, "*.txt").Select(f => Path.GetFileName(f)).ToList();
        HashSet<string> jpFiles = Directory.GetFiles(jpPath, "*.txt").Select(f => Path.GetFileName(f)).ToHashSet();
        int totalFiles = enFiles.Count;
        List<string> missingFiles = new();
        ConcurrentBag<FileCheckResult> fileResults = new();

        await Task.WhenAll(enFiles.OrderBy(x => x).Select(async fileName =>
        {
            string enFilePath = Path.Combine(enPath, fileName);
            string jpFilePath = Path.Combine(jpPath, fileName);

            if (!jpFiles.Contains(fileName))
            {
                lock (missingFiles)
                {
                    missingFiles.Add(fileName);
                    return;
                }
            }

            fileResults.Add(await CheckSingleFile(fileName, enFilePath, jpFilePath));
        }));

        StringBuilder sb = new();
        sb.AppendLine("# 🔍 Translation Check Report");
        sb.AppendLine();

        int totalTranslatableLines = 0;
        int totalTranslatedLines = 0;
        int missingCount = missingFiles.Count;

        foreach (FileCheckResult result in fileResults.OrderBy(result => result.FileName))
        {
            totalTranslatableLines += result.TranslatableLineCount;
            totalTranslatedLines += result.TranslatedLineCount;

            if (!result.Issues.Any())
            {
                continue;
            }

            sb.AppendLine($"### 📄 {result.FileName}");
            sb.AppendLine($"**Progress**: {result.TranslatedLineCount}/{result.TranslatableLineCount} lines ({result.TranslationPercent:F1}%)");
            sb.AppendLine("**Issues found**:");
            foreach (string issue in result.Issues)
            {
                sb.AppendLine($"- {issue}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("## 📊 Summary");

        double filePercent = totalFiles > 0 ? (double)(totalFiles - missingCount) / totalFiles * 100.0 : 0.0;
        sb.AppendLine($"- **Translated Files**: {totalFiles - missingCount}/{totalFiles} ({filePercent:F1}%)");

        double linePercent = totalTranslatableLines > 0 ? (double)totalTranslatedLines / totalTranslatableLines * 100.0 : 0.0;
        sb.AppendLine($"- **Translated Lines**: {totalTranslatedLines}/{totalTranslatableLines} ({linePercent:F1}%)");

        if (missingFiles.Any())
        {
            sb.AppendLine();
            sb.AppendLine("### ❌ Missing Files");
            foreach (string file in missingFiles.OrderBy(f => f))
            {
                sb.AppendLine($"[ ] - `{file}`");
            }
        }

        return sb.ToString();
    }

    private static async Task<FileCheckResult> CheckSingleFile(string fileName, string enFilePath, string jpFilePath)
    {
        FileCheckResult result = new() { FileName = fileName };

        List<string> enLines = await ReadFileLines(enFilePath);
        List<string> jpLines = await ReadFileLines(jpFilePath);

        int translatableCount = enLines.Count(l => !string.IsNullOrEmpty(l.Trim()));
        result.TranslatableLineCount = translatableCount;

        int translatedCount = 0;
        List<string> fileIssues = new();

        if (enLines.Count != jpLines.Count)
        {
            fileIssues.Add($"[ ] Line count differs (en {enLines.Count} / JP {jpLines.Count}) — lines may be misaligned");
        }

        for (int i = 0; i < enLines.Count; i++)
        {
            string en = enLines[i].Trim();
            string jp = i < jpLines.Count ? jpLines[i].Trim() : "";

            if (string.IsNullOrEmpty(en))
            {
                continue;
            }

            if (string.IsNullOrEmpty(jp))
            {
                fileIssues.Add($"[ ] Line {i + 1}: Missing translation for '{TruncateText(en, 30)}'");
            }
            else if (en == jp)
            {
                fileIssues.Add($"[ ] Line {i + 1}: Untranslated '{TruncateText(en, 30)}'");
            }
            else
            {
                translatedCount++;
                if (PlaceholderChecker.Compare(en, jp) is { } placeholderIssue)
                {
                    fileIssues.Add($"[ ] Line {i + 1}: {placeholderIssue}");
                }
            }
        }

        result.TranslatedLineCount = translatedCount;
        result.TranslationPercent = translatableCount > 0 ? (double)translatedCount / translatableCount * 100.0 : 100.0;

        result.Issues = fileIssues;
        return result;
    }

    private static async Task<List<string>> ReadFileLines(string filePath)
    {
        try
        {
            return (await File.ReadAllLinesAsync(filePath, Encoding.UTF8)).ToList();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (text.Length > maxLength)
        {
            return text.Substring(0, maxLength) + "...";
        }
        return text;
    }
}
