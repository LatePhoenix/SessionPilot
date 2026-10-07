using System.Text.Json;
using System.Text.RegularExpressions;

namespace SessionPilot.Core;

public sealed record Interpretation
{
    public bool Success { get; init; }
    public UserIntent? Intent { get; init; }
    public bool SuggestRestore { get; init; }
    public string Explanation { get; init; } = "";
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public static class DeterministicInterpreter
{
    public static Interpretation Interpret(string text)
    {
        var normalized = text.Trim();
        if (normalized.Length == 0)
        {
            return Fail("Enter a workload description or choose a loadout.");
        }

        var lower = normalized.ToLowerInvariant();
        var hits = new List<string>();
        if (Has(lower, "crowded") || Has(lower, "diagnostic"))
        {
            hits.Add("vrchat-diagnostic");
        }
        else if (Has(lower, "virtual desktop"))
        {
            hits.Add("vrchat-social");
        }
        else if (Has(lower, "vrchat") && (Has(lower, "steamvr") || Has(lower, "steam vr")))
        {
            hits.Add("vrchat-steamvr");
        }
        else
        {
            if (HasAny(lower, "local ai", "local model", "ollama", "llm", "stable diffusion"))
            {
                hits.Add("development-local-ai");
            }

            if (HasAny(lower, "compile", "compiling", "build ", "building", "msbuild", "build-heavy"))
            {
                hits.Add("development-build-heavy");
            }

            if (HasAny(lower, "movie", "movies", "media playback", "watching", "plex"))
            {
                hits.Add("media-playback");
            }

            if (HasAny(lower, "batch", "handbrake", "overnight", "background job"))
            {
                hits.Add("background-batch");
            }

            if (HasAny(lower, "coding", "visual studio", "debugging", "programming", "interactive development"))
            {
                hits.Add("development-interactive");
            }

            if (HasAny(lower, "game", "gaming", "play "))
            {
                hits.Add("desktop-gaming");
            }

            if (HasAny(lower, "balanced", "restore", "revert", "put things back", "normal desktop"))
            {
                hits.Add("balanced");
            }
        }

        hits = hits.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (hits.Count == 0)
        {
            return Fail("No known workload matched. Choose a loadout. Nothing was invented from the sentence.");
        }

        if (hits.Count > 1)
        {
            return Fail($"More than one workload matched: {string.Join(", ", hits)}. Choose one loadout.");
        }

        var loadoutId = hits[0];
        var objective = ObjectiveFor(lower, loadoutId);
        var session = HasAny(lower, "permanent", "permanently", "always keep", "keep this")
            ? Names.Persistent
            : Names.Temporary;
        var power = Has(lower, "battery") || Has(lower, "quiet")
            ? Names.Saver
            : Has(lower, "performance")
                ? Names.Performance
                : DefaultPower(loadoutId);
        var suggestRestore = HasAny(lower, "restore", "revert", "put things back");
        var applications = RequestedApplications(normalized);
        var warnings = new List<string>();
        if (HasAny(lower, "throttle background", "kill ", "terminate", "real-time", "realtime priority"))
        {
            warnings.Add("Broad throttling, termination, and Real-time priority are not available from a sentence.");
        }

        return new Interpretation
        {
            Success = true,
            SuggestRestore = suggestRestore,
            Explanation = $"Matched loadout '{loadoutId}' from the wording. Objective is {objective}.",
            Warnings = warnings,
            Intent = new UserIntent
            {
                LoadoutId = loadoutId,
                Objective = objective,
                SessionMode = session,
                PowerPreference = power,
                BackgroundPolicy = Names.Preserve,
                RequestedApplications = applications
            }
        };
    }

    private static Interpretation Fail(string explanation) => new() { Success = false, Explanation = explanation };

    private static string ObjectiveFor(string lower, string loadoutId)
    {
        if (HasAny(lower, "frame-time", "frame time", "stutter", "consistency"))
        {
            return Names.FrameTimeConsistency;
        }

        if (Has(lower, "throughput"))
        {
            return Names.Throughput;
        }

        if (Has(lower, "responsive"))
        {
            return Names.Responsiveness;
        }

        if (Has(lower, "quiet") || Has(lower, "battery"))
        {
            return Names.QuietBalanced;
        }

        return loadoutId switch
        {
            "vrchat-steamvr" or "vrchat-social" or "vrchat-diagnostic" or "desktop-gaming" => Names.FrameTimeConsistency,
            "development-build-heavy" or "development-local-ai" or "background-batch" => Names.Throughput,
            "development-interactive" => Names.Responsiveness,
            "media-playback" => Names.QuietBalanced,
            _ => Names.Restore
        };
    }

    private static string DefaultPower(string loadoutId) => loadoutId switch
    {
        "vrchat-steamvr" or "vrchat-social" or "vrchat-diagnostic" or "desktop-gaming" => Names.Performance,
        "media-playback" or "balanced" => Names.Balanced,
        _ => Names.Balanced
    };

    private static List<string> RequestedApplications(string text)
    {
        var found = new List<string>();
        if (text.Contains("VRChat", StringComparison.OrdinalIgnoreCase))
        {
            found.Add("VRChat");
        }

        if (text.Contains("SteamVR", StringComparison.OrdinalIgnoreCase) || text.Contains("Steam VR", StringComparison.OrdinalIgnoreCase))
        {
            found.Add("SteamVR");
        }

        if (text.Contains("Virtual Desktop", StringComparison.OrdinalIgnoreCase))
        {
            found.Add("Virtual Desktop");
        }

        foreach (Match match in Regex.Matches(text, "\"([^\"]{1,128})\""))
        {
            found.Add(match.Groups[1].Value);
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).Take(IntentLimits.MaxApplications).ToList();
    }

    private static bool Has(string text, string token) => text.Contains(token, StringComparison.Ordinal);

    private static bool HasAny(string text, params string[] tokens) => tokens.Any(token => Has(text, token));
}

public static class IntentValidator
{
    private static readonly HashSet<string> AllowedProperties = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "loadoutId",
        "objective",
        "sessionMode",
        "powerPreference",
        "backgroundPolicy",
        "requestedApplications"
    };

    private static readonly string[] DangerousKeys =
    [
        "shell", "command", "cmd", "exec", "path", "filepath", "ini", "inifragment",
        "registry", "affinity", "cpumask", "mask", "service", "terminate", "kill"
    ];

    public static Interpretation ValidateJson(string json, LoadoutCatalog catalog)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return new Interpretation { Success = false, Explanation = "Model output was not JSON. " + exception.Message };
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Interpretation { Success = false, Explanation = "Model output must be a JSON object." };
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!AllowedProperties.Contains(property.Name))
                {
                    return new Interpretation { Success = false, Explanation = $"Model output contained '{property.Name}', which is outside the intent schema." };
                }
            }

            if (ContainsDangerousContent(document.RootElement))
            {
                return new Interpretation { Success = false, Explanation = "Model output contained an operational instruction and was rejected." };
            }

            if (!document.RootElement.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != Schema.Current)
            {
                return new Interpretation { Success = false, Explanation = "schemaVersion must be 1." };
            }

            if (!TryString(document.RootElement, "loadoutId", out var loadoutId) || LoadoutValidator.ValidateId(loadoutId) is not null)
            {
                return new Interpretation { Success = false, Explanation = "loadoutId is invalid." };
            }

            if (!catalog.TryGet(loadoutId, out _))
            {
                return new Interpretation { Success = false, Explanation = $"Unknown loadout '{loadoutId}'. Model-invented loadouts are rejected." };
            }

            if (!TryString(document.RootElement, "objective", out var objective) || !Names.TryParseObjective(objective, out _))
            {
                return new Interpretation { Success = false, Explanation = "objective is invalid." };
            }

            if (!TryString(document.RootElement, "sessionMode", out var session) || !Names.TryParseSession(session, out _))
            {
                return new Interpretation { Success = false, Explanation = "sessionMode is invalid." };
            }

            if (!TryString(document.RootElement, "powerPreference", out var power) || !Names.TryParsePower(power, out _))
            {
                return new Interpretation { Success = false, Explanation = "powerPreference is invalid." };
            }

            if (!TryString(document.RootElement, "backgroundPolicy", out var background) || !Names.TryParseBackground(background, out _))
            {
                return new Interpretation { Success = false, Explanation = "backgroundPolicy is invalid." };
            }

            if (!document.RootElement.TryGetProperty("requestedApplications", out var apps) || apps.ValueKind != JsonValueKind.Array)
            {
                return new Interpretation { Success = false, Explanation = "requestedApplications must be an array." };
            }

            var names = new List<string>();
            foreach (var item in apps.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return new Interpretation { Success = false, Explanation = "requestedApplications must contain strings." };
                }

                var name = item.GetString() ?? "";
                if (name.Length == 0 || name.Length > IntentLimits.MaxNameLength || name.IndexOfAny(['\\', '/', ':', '*', '?', '"']) >= 0 || name.Contains("..", StringComparison.Ordinal))
                {
                    return new Interpretation { Success = false, Explanation = "An application name is empty, too long, or path-like." };
                }

                names.Add(name);
            }

            if (names.Count > IntentLimits.MaxApplications)
            {
                return new Interpretation { Success = false, Explanation = "Too many requested applications." };
            }

            return new Interpretation
            {
                Success = true,
                Explanation = "Model output matched the intent schema and a known loadout.",
                Intent = new UserIntent
                {
                    LoadoutId = loadoutId,
                    Objective = objective,
                    SessionMode = session,
                    PowerPreference = power,
                    BackgroundPolicy = background,
                    RequestedApplications = names
                }
            };
        }
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return value.Length > 0;
    }

    private static bool ContainsDangerousContent(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (DangerousKeys.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (ContainsDangerousContent(property.Value))
                    {
                        return true;
                    }
                }

                return false;
            case JsonValueKind.Array:
                return element.EnumerateArray().Any(ContainsDangerousContent);
            case JsonValueKind.String:
                var text = element.GetString() ?? "";
                return text.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("powershell", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("HKEY_", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("prolasso.ini", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("reg add", StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }
}
