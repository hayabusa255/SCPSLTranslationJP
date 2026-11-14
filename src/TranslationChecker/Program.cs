using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TranslationChecker;

internal class Program
{
    private const int MaxLinesPerRequest = 400;
    private const int MaxRetries = 3;
    private const int MaxRetryAfterSeconds = 60;
    private const int InitialDelayMs = 500;
    private const int MinDelayMs = 200;
    private const int MaxDelayMs = 10000;
    private const int SuccessesBeforeSpeedUp = 5;
    // gemini-2.5-flash (the original model) is deprecated and gets rerouted to a Flash-Lite model from 2026-12-01.
    // Override with GEMINI_MODEL if the default model's free-tier quota runs out.
    private const string DefaultGeminiModel = "gemini-3.5-flash-lite";
    private static readonly string GeminiModel =
        Environment.GetEnvironmentVariable("GEMINI_MODEL") is { Length: > 0 } model ? model : DefaultGeminiModel;
    private static readonly string GeminiEndpoint =
        $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModel}:generateContent";

    private const string GuidelinesRelativePath = "docs/translation-review-guidelines.md";

    private const string ReviewInstructions =
        "You are an expert localization reviewer for Japanese (JP) translations of English (EN) strings from the game SCP: Secret Laboratory. " +
        "Return ONLY a JSON array of problems (return [] if there are none). " +
        "Each file is listed with its lines as \"L<number> EN\" / \"L<number> JP\" pairs. Only review lines whose JP is a translation; " +
        "lines whose JP is \"(untranslated)\" are context only and must not be reported. " +
        "Placeholders and rich-text tags are checked separately; never report them. " +
        "Output fields: file = the file name from the \"## \" heading; line = the number after L; " +
        "severity = CRITICAL or WARNING; message = a brief description of the problem.";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly HttpClient httpClient = new();

    private static string geminiApiKey = "";
    private static string reviewGuidelines = "";
    private static int currentDelayMs = InitialDelayMs;
    private static int consecutiveSuccesses = 0;

    // Everything known about one file: the aligned lines (for context) and the translated pairs (to review).
    private sealed record CheckedFile(
        FileCheckResult Result,
        List<ContextLine> Lines,
        List<(int LineNumber, string En, string Jp)> Pairs);

    // A non-empty EN line with its JP counterpart. Jp is "" when missing or identical to EN.
    private sealed record ContextLine(int LineNumber, string En, string Jp, bool Translated);

    // The part of one file sent in a batch.
    private sealed record Segment(CheckedFile File, List<ContextLine> Lines);

    private sealed record AiIssue(string File, int Line, string Severity, string Message);

    private static async Task Main(string[] args)
    {
        try
        {
            geminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
            if (!string.IsNullOrEmpty(geminiApiKey))
            {
                // Send the key in a header rather than the URL query so it can't leak via URLs in logs or exception messages.
                httpClient.DefaultRequestHeaders.Add("x-goog-api-key", geminiApiKey);
                Console.WriteLine($"> AI nuance check is **enabled** (batched requests, model: `{GeminiModel}`).");
            }
            else
            {
                Console.WriteLine("> AI nuance check is **disabled** (for a faster run).");
            }

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
                string guidelinesPath = Path.Combine(rootPath, GuidelinesRelativePath);
                if (File.Exists(guidelinesPath))
                {
                    reviewGuidelines = (await File.ReadAllTextAsync(guidelinesPath, Encoding.UTF8)).Trim();
                }
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
        ConcurrentBag<CheckedFile> fileResults = new();

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

        List<CheckedFile> ordered = fileResults.OrderBy(r => r.Result.FileName).ToList();

        List<string> aiErrors = new();
        if (!string.IsNullOrEmpty(geminiApiKey))
        {
            aiErrors = await ReviewWithGemini(ordered);
        }

        StringBuilder sb = new();
        sb.AppendLine("# 🔍 Translation Check Report");
        sb.AppendLine();

        int totalTranslatableLines = 0;
        int totalTranslatedLines = 0;
        int missingCount = missingFiles.Count;

        foreach (FileCheckResult result in ordered.Select(r => r.Result))
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

        if (aiErrors.Any())
        {
            sb.AppendLine("### ⚠️ AI Check Errors");
            foreach (string error in aiErrors)
            {
                sb.AppendLine($"- {error}");
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
    private static async Task<CheckedFile> CheckSingleFile(string fileName, string enFilePath, string jpFilePath)
    {
        FileCheckResult result = new() { FileName = fileName };

        List<string> enLines = await ReadFileLines(enFilePath);
        List<string> jpLines = await ReadFileLines(jpFilePath);

        int translatableCount = enLines.Count(l => !string.IsNullOrEmpty(l.Trim()));
        result.TranslatableLineCount = translatableCount;

        int translatedCount = 0;
        List<string> fileIssues = new();
        List<(int LineNumber, string En, string Jp)> translationPairs = new();
        List<ContextLine> contextLines = new();

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
                contextLines.Add(new ContextLine(i + 1, en, "", false));
            }
            else if (en == jp)
            {
                fileIssues.Add($"[ ] Line {i + 1}: Untranslated '{TruncateText(en, 30)}'");
                contextLines.Add(new ContextLine(i + 1, en, "", false));
            }
            else
            {
                translatedCount++;
                translationPairs.Add((i + 1, en, jp));
                contextLines.Add(new ContextLine(i + 1, en, jp, true));
                if (PlaceholderChecker.Compare(en, jp) is { } placeholderIssue)
                {
                    fileIssues.Add($"[ ] Line {i + 1}: {placeholderIssue}");
                }
            }
        }

        result.TranslatedLineCount = translatedCount;
        result.TranslationPercent = translatableCount > 0 ? (double)translatedCount / translatableCount * 100.0 : 100.0;


        result.Issues = fileIssues;
        return new CheckedFile(result, contextLines, translationPairs);
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

    // Splits files into batches of whole files (up to MaxLinesPerRequest lines each, counting all lines sent).
    // A file with more translated lines than that is split into chunks of MaxLinesPerRequest lines.
    private static List<List<Segment>> BuildBatches(List<CheckedFile> files)
    {
        List<List<Segment>> batches = new();
        List<Segment> current = new();
        int currentLines = 0;

        void Flush()
        {
            if (current.Count > 0)
            {
                batches.Add(current);
                current = new();
                currentLines = 0;
            }
        }

        foreach (CheckedFile file in files.Where(f => f.Pairs.Count > 0))
        {
            if (file.Pairs.Count > MaxLinesPerRequest)
            {
                Flush();
                for (int i = 0; i < file.Lines.Count; i += MaxLinesPerRequest)
                {
                    List<ContextLine> chunk = file.Lines.Skip(i).Take(MaxLinesPerRequest).ToList();
                    if (chunk.Any(l => l.Translated))
                    {
                        batches.Add(new List<Segment> { new(file, chunk) });
                    }
                }
                continue;
            }

            if (currentLines + file.Lines.Count > MaxLinesPerRequest)
            {
                Flush();
            }
            current.Add(new Segment(file, file.Lines));
            currentLines += file.Lines.Count;
        }
        Flush();
        return batches;
    }

    // Reviews every translated line in as few requests as possible.
    // AI issues are appended to each file's Issues; returns one error message per failed batch.
    private static async Task<List<string>> ReviewWithGemini(List<CheckedFile> files)
    {
        List<string> errors = new();
        List<List<Segment>> batches = BuildBatches(files);

        for (int b = 0; b < batches.Count; b++)
        {
            List<Segment> batch = batches[b];
            string error;
            try
            {
                string prompt = BuildPrompt(batch);
                (string? text, error) = await SendBatch(prompt);
                if (text is not null)
                {
                    try
                    {
                        ApplyAiIssues(batch, ParseResponse(text));
                    }
                    catch (JsonException ex)
                    {
                        error = "Invalid JSON response: " + ex.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                error = "Internal error: " + ex.Message;
            }

            if (!string.IsNullOrEmpty(error))
            {
                errors.Add($"Batch {b + 1}/{batches.Count} ({batch[0].File.Result.FileName} … {batch[^1].File.Result.FileName}): {error}");
            }

            if (b + 1 < batches.Count)
            {
                await Task.Delay(currentDelayMs);
            }
        }
        return errors;
    }

    // Every non-empty EN line of each file is sent so the model sees the surrounding context.
    private static string BuildPrompt(List<Segment> batch)
    {
        StringBuilder prompt = new();
        prompt.AppendLine(ReviewInstructions);
        if (reviewGuidelines.Length > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine(reviewGuidelines);
        }
        foreach (Segment segment in batch)
        {
            prompt.AppendLine();
            prompt.AppendLine($"## {segment.File.Result.FileName}");
            foreach (ContextLine line in segment.Lines)
            {
                prompt.AppendLine($"L{line.LineNumber} EN: {line.En}");
                prompt.AppendLine($"L{line.LineNumber} JP: {(line.Translated ? line.Jp : "(untranslated)")}");
            }
        }
        return prompt.ToString();
    }

    private static string BuildRequestJson(string prompt)
    {
        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 8192,
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "ARRAY",
                    items = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            file = new { type = "STRING" },
                            line = new { type = "INTEGER" },
                            severity = new { type = "STRING", format = "enum", @enum = new[] { "CRITICAL", "WARNING" } },
                            message = new { type = "STRING" }
                        },
                        required = new[] { "file", "line", "severity", "message" }
                    }
                }
            }
        };
        return JsonSerializer.Serialize(requestBody);
    }

    // Sends one request with retries. Returns the model's text on success, otherwise an error message.
    private static async Task<(string? Text, string Error)> SendBatch(string prompt)
    {
        string requestJson = BuildRequestJson(prompt);
        string lastError = "";

        for (int retry = 0; retry <= MaxRetries; retry++)
        {
            bool canRetry = retry < MaxRetries;
            try
            {
                // StringContent cannot be reused reliably across retries, so create it per attempt.
                using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await httpClient.PostAsync(GeminiEndpoint, content);
                string body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    RecordSuccess();
                    return (ExtractText(body) ?? "", "");
                }

                if (response.StatusCode != HttpStatusCode.ServiceUnavailable
                    && response.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    return (null, $"API error: {response.StatusCode} - {TruncateText(Regex.Replace(body, "\\s+", " "), 100)}");
                }

                lastError = $"API error after {MaxRetries + 1} attempts: {response.StatusCode} - {TruncateText(Regex.Replace(body, "\\s+", " "), 100)}";
                if (canRetry)
                {
                    int? retryAfter = response.StatusCode == HttpStatusCode.TooManyRequests ? ParseRetryDelaySeconds(body) : null;
                    await BackOff(retry, retryAfter);
                }
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("503")
                || ex.Message.Contains("Service Unavailable")
                || ex.Message.Contains("429")
                || ex.Message.Contains("Too Many Requests"))
            {
                lastError = $"HTTP error after {MaxRetries + 1} attempts: {ex.Message}";
                if (canRetry)
                {
                    await BackOff(retry, null);
                }
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                lastError = $"Timeout error after {MaxRetries + 1} attempts: {ex.Message}";
                if (canRetry)
                {
                    await BackOff(retry, null);
                }
            }
        }
        return (null, lastError);
    }

    private static string? ExtractText(string responseBody)
    {
        var json = JsonSerializer.Deserialize<JsonElement>(responseBody);
        if (json.TryGetProperty("candidates", out var candidates)
            && candidates.GetArrayLength() > 0
            && candidates[0].TryGetProperty("content", out var candidateContent)
            && candidateContent.TryGetProperty("parts", out var parts)
            && parts.GetArrayLength() > 0
            && parts[0].TryGetProperty("text", out var textElement))
        {
            return textElement.GetString();
        }
        return null;
    }

    // Reads the server-suggested wait from a 429 body (RetryInfo: "retryDelay": "37s"), capped at MaxRetryAfterSeconds.
    private static int? ParseRetryDelaySeconds(string body)
    {
        Match m = Regex.Match(body, "\"retryDelay\"\\s*:\\s*\"(\\d+(?:\\.\\d+)?)s\"");
        if (!m.Success)
        {
            return null;
        }
        double seconds = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return (int)Math.Min(MaxRetryAfterSeconds, Math.Ceiling(seconds));
    }

    private static void RecordSuccess()
    {
        consecutiveSuccesses++;
        if (consecutiveSuccesses >= SuccessesBeforeSpeedUp)
        {
            currentDelayMs = Math.Max(MinDelayMs, currentDelayMs / 2);
            consecutiveSuccesses = 0;
        }
    }

    // Doubles the delay (up to the maximum) and waits before the next attempt,
    // or waits the server-suggested time when one is given.
    private static async Task BackOff(int retry, int? retryAfterSeconds)
    {
        currentDelayMs = Math.Min(MaxDelayMs, currentDelayMs * 2);
        consecutiveSuccesses = 0;
        await Task.Delay(retryAfterSeconds is { } s ? s * 1000 : currentDelayMs * (retry + 1));
    }

    private static List<AiIssue> ParseResponse(string text)
    {
        return JsonSerializer.Deserialize<List<AiIssue>>(text, JsonOptions) ?? new List<AiIssue>();
    }

    // Maps (file, line) back to the translated lines of this batch and appends the issues in the report's usual format.
    // Issues for other files or for lines that are not translated lines of this batch are ignored.
    private static void ApplyAiIssues(List<Segment> batch, List<AiIssue> aiIssues)
    {
        Dictionary<string, Segment> byFile = batch.ToDictionary(s => s.File.Result.FileName);
        foreach (AiIssue issue in aiIssues.OrderBy(i => i.File, StringComparer.Ordinal).ThenBy(i => i.Line))
        {
            if (issue.File is null || !byFile.TryGetValue(issue.File, out Segment? segment)
                || !segment.Lines.Any(l => l.Translated && l.LineNumber == issue.Line))
            {
                continue;
            }

            string text = issue.Severity.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase)
                ? $"[ ] Line {issue.Line}: ❌ (Gemini) CRITICAL: {issue.Message}"
                : $"[ ] Line {issue.Line}: ⚠️ (Gemini) WARNING: {issue.Message}";
            segment.File.Result.Issues.Add(text);
        }
    }
}
