using System.Text;

namespace SCPSLInstaller;

internal sealed class VdfNode
{
    /// <summary>文字列値。子を持つオブジェクトなら null。</summary>
    public string? Value { get; init; }
    public List<KeyValuePair<string, VdfNode>> Children { get; } = new();

    public VdfNode? Get(string key)
    {
        foreach (var kv in Children)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return null;
    }

    public string? GetString(string key) => Get(key)?.Value;
}

/// <summary>KeyValues1 テキスト形式（Steam の .vdf / .acf）の最小パーサ。</summary>
internal static class VdfParser
{
    private enum Tok { Eof, Open, Close, Str }

    public static VdfNode Parse(string text)
    {
        int pos = 0;
        var root = new VdfNode();
        ParseObject(text, ref pos, root, topLevel: true);
        return root;
    }

    private static void ParseObject(string s, ref int pos, VdfNode into, bool topLevel)
    {
        while (true)
        {
            var (kind, key) = Next(s, ref pos);
            if (kind == Tok.Eof)
            {
                if (topLevel) return;
                throw new FormatException("VDF: '}' が足りません。");
            }
            if (kind == Tok.Close)
            {
                if (topLevel) throw new FormatException("VDF: 対応しない '}' があります。");
                return;
            }
            if (kind == Tok.Open) throw new FormatException("VDF: キーの位置に '{' があります。");

            var (vkind, val) = Next(s, ref pos);
            if (vkind == Tok.Open)
            {
                var child = new VdfNode();
                ParseObject(s, ref pos, child, topLevel: false);
                into.Children.Add(new(key, child));
            }
            else if (vkind == Tok.Str)
            {
                into.Children.Add(new(key, new VdfNode { Value = val }));
            }
            else
            {
                throw new FormatException($"VDF: キー \"{key}\" の値がありません。");
            }
        }
    }

    private static (Tok, string) Next(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            char c = s[pos];
            if (char.IsWhiteSpace(c) || c == '﻿') { pos++; continue; }
            if (c == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n') pos++;
                continue;
            }
            break;
        }
        if (pos >= s.Length) return (Tok.Eof, "");

        char ch = s[pos];
        if (ch == '{') { pos++; return (Tok.Open, ""); }
        if (ch == '}') { pos++; return (Tok.Close, ""); }

        var sb = new StringBuilder();
        if (ch == '"')
        {
            pos++;
            while (true)
            {
                if (pos >= s.Length) throw new FormatException("VDF: 文字列が閉じられていません。");
                char d = s[pos++];
                if (d == '"') break;
                if (d == '\\' && pos < s.Length)
                {
                    char e = s[pos++];
                    switch (e)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append('\\').Append(e); break;
                    }
                }
                else sb.Append(d);
            }
            return (Tok.Str, sb.ToString());
        }

        // 引用符なしトークン
        while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '"' && s[pos] != '{' && s[pos] != '}')
            sb.Append(s[pos++]);
        return (Tok.Str, sb.ToString());
    }
}
