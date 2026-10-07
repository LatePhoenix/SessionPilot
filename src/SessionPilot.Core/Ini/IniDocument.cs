using System.Security.Cryptography;
using System.Text;

namespace SessionPilot.Core;

public sealed class IniDocument
{
    private readonly List<IniLine> _lines;

    private IniDocument(byte[] originalBytes, TextEncoding encoding, bool hasBom, IReadOnlyList<IniLine> lines)
    {
        OriginalBytes = originalBytes;
        Encoding = encoding;
        HasBom = hasBom;
        _lines = lines.ToList();
        ContentHash = ContentHashing.Sha256(originalBytes);
    }

    public byte[] OriginalBytes { get; }
    public TextEncoding Encoding { get; }
    public bool HasBom { get; }
    public string ContentHash { get; }
    public IReadOnlyList<IniLine> Lines => _lines;

    public static IniDocument Parse(byte[] bytes)
    {
        var (encoding, hasBom, text) = Decode(bytes);
        var lines = new List<IniLine>();
        var section = "";
        foreach (var (content, ending) in SplitLines(text))
        {
            lines.Add(Classify(content, ending, section));
            if (lines[^1] is IniSectionLine sectionLine)
            {
                section = sectionLine.Name;
            }
        }

        return new IniDocument(bytes, encoding, hasBom, lines);
    }

    public static IniDocument Parse(string text, TextEncoding encoding = TextEncoding.Utf8, bool hasBom = false)
    {
        var bytes = Encode(text, encoding, hasBom);
        return Parse(bytes);
    }

    public byte[] Serialize()
    {
        var text = string.Concat(_lines.Select(line => line.Content + line.Ending));
        var bytes = Encode(text, Encoding, HasBom);
        if (bytes.AsSpan().SequenceEqual(OriginalBytes))
        {
            return OriginalBytes.ToArray();
        }

        return bytes;
    }

    public IReadOnlyList<IniKeyObservation> Observe()
    {
        return _lines.OfType<IniKeyLine>()
            .Select(line => new IniKeyObservation
            {
                Section = line.Section,
                Key = line.Key,
                ValueEmpty = line.Value.Trim().Length == 0,
                Kind = ClassifyValue(line.Value),
                ValueLength = line.Value.Length
            })
            .ToList();
    }

    public IniLookup Find(string section, string key)
    {
        var matches = _lines.OfType<IniKeyLine>()
            .Where(line => NameEquals(line.Section, section) && NameEquals(line.Key, key))
            .ToList();
        return matches.Count switch
        {
            0 => IniLookup.Missing(),
            1 => IniLookup.Found(matches[0]),
            _ => IniLookup.Ambiguous(matches.Count)
        };
    }

    public IniEditResult Apply(IReadOnlyList<IniEdit> edits)
    {
        if (edits.Count == 0)
        {
            return IniEditResult.Unchanged(this);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<(IniKeyLine Line, IniEdit Edit)>();
        foreach (var edit in edits)
        {
            if (!seen.Add(edit.Section + "\n" + edit.Key))
            {
                return IniEditResult.Rejected($"[{edit.Section}] {edit.Key} is edited more than once.");
            }

            if (edit.Value.Contains('\r') || edit.Value.Contains('\n'))
            {
                return IniEditResult.Rejected($"Value for [{edit.Section}] {edit.Key} contains a line break.");
            }

            if (!CanRepresent(edit.Value, Encoding))
            {
                return IniEditResult.Rejected($"Value for [{edit.Section}] {edit.Key} cannot be represented in the file's {Encoding} encoding.");
            }

            if (HasInlineComment(edit.Value))
            {
                return IniEditResult.Rejected($"[{edit.Section}] {edit.Key} new value would read as an inline comment. The edit was refused.");
            }

            var lookup = Find(edit.Section, edit.Key);
            if (lookup.Status == IniLookupStatus.Missing)
            {
                return IniEditResult.Rejected($"[{edit.Section}] {edit.Key} does not exist. New keys are not created.");
            }

            if (lookup.Status == IniLookupStatus.Ambiguous)
            {
                return IniEditResult.Rejected($"[{edit.Section}] {edit.Key} is ambiguous ({lookup.MatchCount} occurrences).");
            }

            var line = lookup.Line!;
            if (HasInlineComment(line.Value))
            {
                return IniEditResult.Rejected($"[{edit.Section}] {edit.Key} has an inline comment. The edit was refused.");
            }

            resolved.Add((line, edit));
        }

        var updated = _lines.Select(line =>
        {
            var match = resolved.FirstOrDefault(item => ReferenceEquals(item.Line, line));
            if (match.Line is null)
            {
                return line;
            }

            var equalsAt = match.Line.Content.IndexOf('=');
            var raw = PreserveSpacing(match.Line.Value, match.Edit.Value);
            var content = match.Line.Content[..(equalsAt + 1)] + raw;
            return match.Line with { Content = content, Value = raw };
        }).ToList();

        var document = new IniDocument(OriginalBytes, Encoding, HasBom, updated);
        return IniEditResult.Changed(document);
    }

    public string NewLineStyle
    {
        get
        {
            var endings = _lines.Select(line => line.Ending).Where(ending => ending.Length > 0).Distinct().ToList();
            return endings.Count switch
            {
                0 => "none",
                1 when endings[0] == "\r\n" => "crlf",
                1 when endings[0] == "\n" => "lf",
                1 when endings[0] == "\r" => "cr",
                _ => "mixed"
            };
        }
    }

    private static IniLine Classify(string content, string ending, string section)
    {
        if (content.Trim().Length == 0)
        {
            return new IniBlankLine(content, ending);
        }

        var trimmed = content.TrimStart();
        if (trimmed.StartsWith('#') || trimmed.StartsWith(';'))
        {
            return new IniCommentLine(content, ending);
        }

        if (trimmed.StartsWith('['))
        {
            var close = trimmed.IndexOf(']');
            if (close > 1)
            {
                var rest = trimmed[(close + 1)..].Trim();
                if (rest.Length == 0 || rest.StartsWith('#') || rest.StartsWith(';'))
                {
                    return new IniSectionLine(content, ending, trimmed[1..close].Trim());
                }
            }

            return new IniUnrecognizedLine(content, ending);
        }

        var equalsAt = content.IndexOf('=');
        if (equalsAt > 0)
        {
            var key = content[..equalsAt].Trim();
            if (key.Length > 0 && !key.Contains('[') && !key.Contains(']'))
            {
                return new IniKeyLine(content, ending, section, key, content[(equalsAt + 1)..]);
            }
        }

        return new IniUnrecognizedLine(content, ending);
    }

    internal static ValueKind ClassifyValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return ValueKind.Empty;
        }

        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return ValueKind.Boolean;
        }

        if (trimmed.All(character => char.IsAsciiDigit(character) || character == '-'))
        {
            return ValueKind.Integer;
        }

        var comma = trimmed.Contains(',');
        var semi = trimmed.Contains(';');
        if (comma && semi)
        {
            return ValueKind.MixedDelimiter;
        }

        if (comma)
        {
            return ValueKind.CommaList;
        }

        if (semi)
        {
            return ValueKind.SemicolonList;
        }

        return ValueKind.Other;
    }

    private static string PreserveSpacing(string original, string value)
    {
        var leading = original[..(original.Length - original.TrimStart().Length)];
        var core = original.Trim();
        var trailing = core.Length == 0 ? "" : original[original.TrimEnd().Length..];
        return leading + value + trailing;
    }

    private static bool CanRepresent(string value, TextEncoding encoding)
    {
        var codec = encoding switch
        {
            TextEncoding.Latin1 => System.Text.Encoding.GetEncoding(28591, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            TextEncoding.Utf16Le => new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true),
            TextEncoding.Utf16Be => new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true),
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
        };
        try
        {
            _ = codec.GetBytes(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool HasInlineComment(string value)
    {
        return value.Contains(" ;", StringComparison.Ordinal) || value.Contains(" #", StringComparison.Ordinal);
    }

    private static bool NameEquals(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(string Content, string Ending)> SplitLines(string text)
    {
        var buffer = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                var ending = i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r";
                yield return (buffer.ToString(), ending);
                buffer.Clear();
                if (ending.Length == 2)
                {
                    i++;
                }
            }
            else if (text[i] == '\n')
            {
                yield return (buffer.ToString(), "\n");
                buffer.Clear();
            }
            else
            {
                buffer.Append(text[i]);
            }
        }

        if (buffer.Length > 0)
        {
            yield return (buffer.ToString(), "");
        }
    }

    private static (TextEncoding Encoding, bool HasBom, string Text) Decode(byte[] bytes)
    {
        if (StartsWith(bytes, [0xEF, 0xBB, 0xBF]))
        {
            return (TextEncoding.Utf8, true, System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        }

        if (StartsWith(bytes, [0xFF, 0xFE]))
        {
            return (TextEncoding.Utf16Le, true, System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2));
        }

        if (StartsWith(bytes, [0xFE, 0xFF]))
        {
            return (TextEncoding.Utf16Be, true, System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2));
        }

        if (LooksLikeUtf16Le(bytes))
        {
            return (TextEncoding.Utf16Le, false, System.Text.Encoding.Unicode.GetString(bytes));
        }

        if (System.Text.Encoding.UTF8.GetCharCount(bytes) >= 0 && IsValidUtf8(bytes))
        {
            return (TextEncoding.Utf8, false, System.Text.Encoding.UTF8.GetString(bytes));
        }

        return (TextEncoding.Latin1, false, System.Text.Encoding.Latin1.GetString(bytes));
    }

    private static byte[] Encode(string text, TextEncoding encoding, bool hasBom)
    {
        Encoding codec = encoding switch
        {
            TextEncoding.Utf16Le => System.Text.Encoding.Unicode,
            TextEncoding.Utf16Be => System.Text.Encoding.BigEndianUnicode,
            TextEncoding.Latin1 => System.Text.Encoding.Latin1,
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };

        var body = codec.GetBytes(text);
        if (!hasBom)
        {
            return body;
        }

        var preamble = encoding switch
        {
            TextEncoding.Utf16Le => new byte[] { 0xFF, 0xFE },
            TextEncoding.Utf16Be => new byte[] { 0xFE, 0xFF },
            TextEncoding.Utf8 => new byte[] { 0xEF, 0xBB, 0xBF },
            _ => []
        };
        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    private static bool StartsWith(byte[] bytes, byte[] prefix) =>
        bytes.Length >= prefix.Length && bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static bool LooksLikeUtf16Le(byte[] bytes)
    {
        var sample = Math.Min(bytes.Length, 64);
        if (sample < 4 || sample % 2 != 0)
        {
            return false;
        }

        var zeros = 0;
        for (var i = 1; i < sample; i += 2)
        {
            if (bytes[i] == 0)
            {
                zeros++;
            }
        }

        return zeros >= sample / 4;
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            _ = new UTF8Encoding(false, true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

public enum TextEncoding
{
    Utf8,
    Utf16Le,
    Utf16Be,
    Latin1
}

public abstract record IniLine(string Content, string Ending);

public sealed record IniBlankLine(string Content, string Ending) : IniLine(Content, Ending);

public sealed record IniCommentLine(string Content, string Ending) : IniLine(Content, Ending);

public sealed record IniSectionLine(string Content, string Ending, string Name) : IniLine(Content, Ending);

public sealed record IniKeyLine(string Content, string Ending, string Section, string Key, string Value) : IniLine(Content, Ending);

public sealed record IniUnrecognizedLine(string Content, string Ending) : IniLine(Content, Ending);

public enum IniLookupStatus
{
    Found,
    Missing,
    Ambiguous
}

public sealed record IniLookup(IniLookupStatus Status, IniKeyLine? Line, int MatchCount)
{
    public static IniLookup Found(IniKeyLine line) => new(IniLookupStatus.Found, line, 1);
    public static IniLookup Missing() => new(IniLookupStatus.Missing, null, 0);
    public static IniLookup Ambiguous(int count) => new(IniLookupStatus.Ambiguous, null, count);
}

public sealed record IniEdit
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public required string Value { get; init; }
}

public sealed record IniEditResult(bool Succeeded, string Message, IniDocument Document)
{
    public static IniEditResult Unchanged(IniDocument document) => new(true, "No edits.", document);
    public static IniEditResult Changed(IniDocument document) => new(true, "Edited.", document);
    public static IniEditResult Rejected(string message) => new(false, message, null!);
}

public static class ContentHashing
{
    public static string Sha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
