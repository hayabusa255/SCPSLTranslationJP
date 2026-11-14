using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TranslationChecker;

internal class Program
{
    private const int MaxLinesPerRequest = 200;
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

    private const string ReviewInstructions =
        "You are an expert localization reviewer. Review the Japanese translations (JP) of English (EN) UI/subtitle strings " +
        "from the game SCP: Secret Laboratory. Check accuracy, nuance, naturalness and context. " +
        "Placeholders such as {0} or %s, rich-text tags such as <color=...>, and \\n must be preserved; if missing or altered, report CRITICAL. " +
        "Return ONLY lines with problems (omit lines that are fine; return [] if none). " +
        "severity: CRITICAL = wrong meaning or major grammar errors; WARNING = awkward or slightly unnatural. " +
        "message: brief description in Japanese. id: the line id.";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly HttpClient httpClient = new();

    private static string geminiApiKey = "";
    private static int currentDelayMs = InitialDelayMs;
    private static int consecutiveSuccesses = 0;

    // One translated line to review. Id is local to a single batch.
    private sealed record ReviewLine(int Id, FileCheckResult File, int LineNumber, string En, string Jp);

    private sealed record AiIssue(int Id, string Severity, string Message);

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
        ConcurrentBag<(FileCheckResult Result, List<(int LineNumber, string En, string Jp)> Pairs)> fileResults = new();

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

        List<(FileCheckResult Result, List<(int LineNumber, string En, string Jp)> Pairs)> ordered =
            fileResults.OrderBy(r => r.Result.FileName).ToList();

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
    private static async Task<(FileCheckResult Result, List<(int LineNumber, string En, string Jp)> Pairs)> CheckSingleFile(string fileName, string enFilePath, string jpFilePath)
    {
        FileCheckResult result = new() { FileName = fileName };

        List<string> enLines = await ReadFileLines(enFilePath);
        List<string> jpLines = await ReadFileLines(jpFilePath);

        int translatableCount = enLines.Count(l => !string.IsNullOrEmpty(l.Trim()));
        result.TranslatableLineCount = translatableCount;

        int translatedCount = 0;
        List<string> fileIssues = new();
        List<(int LineNumber, string En, string Jp)> translationPairs = new();

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
                translationPairs.Add((i + 1, en, jp));
            }
        }

        result.TranslatedLineCount = translatedCount;
        result.TranslationPercent = translatableCount > 0 ? (double)translatedCount / translatableCount * 100.0 : 100.0;


        result.Issues = fileIssues;
        return (result, translationPairs);
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

    // Reviews every translated line across all files in as few requests as possible.
    // AI issues are appended to each file's Issues; returns one error message per failed batch.
    private static async Task<List<string>> ReviewWithGemini(
        List<(FileCheckResult Result, List<(int LineNumber, string En, string Jp)> Pairs)> files)
    {
        List<string> errors = new();
        var allLines = files
            .SelectMany(f => f.Pairs.Select(p => (f.Result, p.LineNumber, p.En, p.Jp)))
            .ToList();

        int batchNumber = 0;
        for (int start = 0; start < allLines.Count; start += MaxLinesPerRequest)
        {
            batchNumber++;
            List<ReviewLine> batch = allLines.Skip(start).Take(MaxLinesPerRequest)
                .Select((l, i) => new ReviewLine(i + 1, l.Result, l.LineNumber, l.En, l.Jp))
                .ToList();

            string error;
            try
            {
                (string? text, error) = await SendBatch(BuildPrompt(batch));
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
                errors.Add($"Batch {batchNumber} ({batch[0].File.FileName} … {batch[^1].File.FileName}): {error}");
            }

            if (start + MaxLinesPerRequest < allLines.Count)
            {
                await Task.Delay(currentDelayMs);
            }
        }
        return errors;
    }

    // Groups the lines under "## File:" headers so the model has file context without repeating names per line.
    private static string BuildPrompt(List<ReviewLine> batch)
    {
        StringBuilder prompt = new();
        prompt.AppendLine(ReviewInstructions);
        FileCheckResult? currentFile = null;
        foreach (ReviewLine line in batch)
        {
            if (!ReferenceEquals(line.File, currentFile))
            {
                currentFile = line.File;
                prompt.AppendLine();
                prompt.AppendLine($"## File: {currentFile.FileName}");
            }
            prompt.AppendLine($"{line.Id}");
            prompt.AppendLine($"EN: {line.En}");
            prompt.AppendLine($"JP: {line.Jp}");
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
                            id = new { type = "INTEGER" },
                            severity = new { type = "STRING", format = "enum", @enum = new[] { "CRITICAL", "WARNING" } },
                            message = new { type = "STRING" }
                        },
                        required = new[] { "id", "severity", "message" }
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

    // Maps batch-local ids back to (file, line) and appends the issues in the report's usual format.
    private static void ApplyAiIssues(List<ReviewLine> batch, List<AiIssue> aiIssues)
    {
        Dictionary<int, ReviewLine> byId = batch.ToDictionary(l => l.Id);
        foreach (AiIssue issue in aiIssues.OrderBy(i => i.Id))
        {
            if (!byId.TryGetValue(issue.Id, out ReviewLine? line))
            {
                continue;
            }

            string text = issue.Severity.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase)
                ? $"[ ] Line {line.LineNumber}: ❌ (Gemini) CRITICAL: {issue.Message}"
                : $"[ ] Line {line.LineNumber}: ⚠️ (Gemini) WARNING: {issue.Message}";
            line.File.Issues.Add(text);
        }
    }
}
