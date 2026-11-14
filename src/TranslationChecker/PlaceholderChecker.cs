using System.Text.RegularExpressions;

namespace TranslationChecker;

// Deterministic check that placeholders and rich-text tags survive translation.
// Tokens are compared as sets, so reusing a placeholder several times in JP is fine.
// Tags are checked one way only: JP may add tags, but must keep the ones EN has.
// Recognised: [word], {n} / {name}, printf-style (%s %d %i %f %u %x %X %%), and tag names (<b>, </b>, <color=...> -> <color>).
internal static class PlaceholderChecker
{
    private static readonly Regex TokenPattern = new(
        @"\[(?<bracket>[A-Za-z_]\w*)\]" +
        @"|\{(?<brace>\w+)\}" +
        @"|(?<printf>%%|%(?:\d+\$)?[-+ 0#]*\d*(?:\.\d+)?[sdifuxX])" +
        @"|<(?<tag>/?[A-Za-z][A-Za-z0-9]*)(?:[=\s][^>]*)?>",
        RegexOptions.Compiled);

    private static HashSet<string> ExtractTokens(string text)
    {
        HashSet<string> tokens = new();
        foreach (Match m in TokenPattern.Matches(text))
        {
            if (m.Groups["bracket"].Success)
            {
                tokens.Add($"[{m.Groups["bracket"].Value}]");
            }
            else if (m.Groups["brace"].Success)
            {
                tokens.Add($"{{{m.Groups["brace"].Value}}}");
            }
            else if (m.Groups["printf"].Success)
            {
                tokens.Add(m.Groups["printf"].Value);
            }
            else
            {
                tokens.Add($"<{m.Groups["tag"].Value.ToLowerInvariant()}>");
            }
        }
        return tokens;
    }

    // Returns the issue text (without the "[ ] Line n:" prefix), or null when the tokens match.
    public static string? Compare(string en, string jp)
    {
        HashSet<string> enTokens = ExtractTokens(en);
        HashSet<string> jpTokens = ExtractTokens(jp);
        List<string> missing = enTokens.Except(jpTokens).OrderBy(t => t, StringComparer.Ordinal).ToList();
        // The JP translation deliberately adds decorative tags (e.g. <color> for emphasis), so extra
        // tags are allowed; only placeholders that don't exist in EN are reported as unexpected.
        List<string> unexpected = jpTokens.Except(enTokens)
            .Where(t => !t.StartsWith('<'))
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        if (missing.Count == 0 && unexpected.Count == 0)
        {
            return null;
        }

        List<string> parts = new();
        if (missing.Count > 0)
        {
            parts.Add($"missing {string.Join(", ", missing)}");
        }
        if (unexpected.Count > 0)
        {
            parts.Add($"unexpected {string.Join(", ", unexpected)}");
        }
        return "Placeholder mismatch — " + string.Join("; ", parts);
    }
}
