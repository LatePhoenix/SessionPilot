namespace SessionPilot.Core;

public sealed record ParsedSelectorList(bool Parsed, string Detail, IReadOnlyList<string> Selectors);

public static class HistoricalRuleParsers
{
    private static readonly string[] PriorityPhrases =
    [
        "above normal",
        "below normal",
        "real time",
        "realtime",
        "high",
        "normal",
        "idle"
    ];

    public static ParsedSelectorList ParseExclusions(string value)
    {
        if (value.Trim().Length == 0)
        {
            return new ParsedSelectorList(true, "Empty exclusion list.", []);
        }

        if (value.Contains(';'))
        {
            return new ParsedSelectorList(false, "OocExclusions documentation uses commas. A semicolon makes this value ambiguous.", []);
        }

        var selectors = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return new ParsedSelectorList(true, "Parsed with the documented comma separator. This is not a verified installed-version fixture.", selectors);
    }

    public static ParsedSelectorList ParseDefaultPriorities(string value)
    {
        if (value.Trim().Length == 0)
        {
            return new ParsedSelectorList(true, "Empty priority list.", []);
        }

        if (value.Contains(';'))
        {
            var entries = new List<string>();
            foreach (var record in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var comma = record.IndexOf(',');
                if (comma <= 0 || !IsKnownPriority(record[(comma + 1)..].Trim()))
                {
                    return new ParsedSelectorList(false, "Semicolon-shaped DefaultPriorities record did not match name,priority.", []);
                }

                entries.Add(record);
            }

            return new ParsedSelectorList(true, "Parsed the semicolon-separated sample shape. Serialization remains blocked.", entries);
        }

        var tokens = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        var pairs = new List<string>();
        var index = 0;
        while (index < tokens.Count)
        {
            if (index + 1 >= tokens.Count)
            {
                return new ParsedSelectorList(false, "Comma-shaped DefaultPriorities value has a dangling token.", []);
            }

            var consumed = MatchPriority(tokens, index + 1);
            if (consumed == 0)
            {
                return new ParsedSelectorList(false, "Comma-shaped DefaultPriorities value contains an unrecognized priority.", []);
            }

            pairs.Add(string.Join(",", tokens.Skip(index).Take(1 + consumed)));
            index += 1 + consumed;
        }

        return new ParsedSelectorList(true, "Parsed the comma-separated table shape. It conflicts with the semicolon sample, so serialization remains blocked.", pairs);
    }

    public static bool SerializationAllowed => false;

    public static bool IsForbiddenPriority(string value)
    {
        var token = value.Trim();
        return token.Equals("real time", StringComparison.OrdinalIgnoreCase)
            || token.Equals("realtime", StringComparison.OrdinalIgnoreCase)
            || token.Equals("high", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownPriority(string value) =>
        PriorityPhrases.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    private static int MatchPriority(IReadOnlyList<string> tokens, int start)
    {
        if (start + 1 < tokens.Count)
        {
            var two = tokens[start] + " " + tokens[start + 1];
            if (IsKnownPriority(two))
            {
                return 2;
            }
        }

        return IsKnownPriority(tokens[start]) ? 1 : 0;
    }
}

public static class ProtectedProcesses
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "audiodg.exe",
        "dwm.exe",
        "csrss.exe",
        "lsass.exe",
        "services.exe",
        "svchost.exe",
        "winlogon.exe",
        "smss.exe",
        "registry",
        "system",
        "secure system",
        "msmpeng.exe",
        "securityhealthservice.exe"
    };

    public static bool IsProtected(string executableName)
    {
        var name = executableName.Trim();
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Names.Contains(name + ".exe"))
        {
            return true;
        }

        return Names.Contains(name);
    }
}
