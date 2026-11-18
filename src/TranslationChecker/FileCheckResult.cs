namespace TranslationChecker;

internal class FileCheckResult
{
    public string FileName { get; set; } = "";

    public int TranslatableLineCount { get; set; }

    public int TranslatedLineCount { get; set; }

    public double TranslationPercent { get; set; }

    public List<string> Issues { get; set; } = new();
}
