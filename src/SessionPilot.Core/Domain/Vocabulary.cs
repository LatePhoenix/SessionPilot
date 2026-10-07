namespace SessionPilot.Core;

public static class Schema
{
    public const int Current = 1;
}

public enum Objective
{
    FrameTimeConsistency,
    Responsiveness,
    Throughput,
    QuietBalanced,
    Restore
}

public enum SessionMode
{
    Temporary,
    Persistent
}

public enum PowerPreference
{
    Performance,
    Balanced,
    Saver
}

public enum BackgroundPolicy
{
    Preserve,
    ExplicitWorkersOnly
}

public enum SupportStatus
{
    NoChange,
    DocumentedFormatUnverified,
    KeyObservedFormatUnverified,
    ManualPreviewOnly,
    Unsupported,
    BlockedByPolicy,
    Ambiguous,
    Deferred
}

public enum RiskCategory
{
    None,
    Low,
    Medium,
    High,
    Blocked
}

public enum DiscoveryConfidence
{
    None,
    Low,
    Medium,
    High
}

public enum IdentityConfidence
{
    Unknown,
    Hypothetical,
    Observed,
    Confirmed
}

public enum ApplicationRole
{
    Unknown,
    Game,
    Compositor,
    Streaming,
    Audio,
    Companion,
    Development,
    Media,
    System
}

public enum ValueKind
{
    Empty,
    Boolean,
    Integer,
    CommaList,
    SemicolonList,
    MixedDelimiter,
    Other
}

public static class Names
{
    public const string FrameTimeConsistency = "frame-time-consistency";
    public const string Responsiveness = "responsiveness";
    public const string Throughput = "throughput";
    public const string QuietBalanced = "quiet-balanced";
    public const string Restore = "restore";

    public const string Temporary = "temporary";
    public const string Persistent = "persistent";

    public const string Performance = "performance";
    public const string Balanced = "balanced";
    public const string Saver = "saver";

    public const string Preserve = "preserve";
    public const string ExplicitWorkersOnly = "explicit-workers-only";

    public const string Unchanged = "unchanged";
    public const string ExcludeFromProBalance = "exclude-from-probalance";

    public static bool TryParseObjective(string? value, out Objective objective) =>
        Try(value, ObjectiveMap, out objective);

    public static bool TryParseSession(string? value, out SessionMode mode) =>
        Try(value, SessionMap, out mode);

    public static bool TryParsePower(string? value, out PowerPreference preference) =>
        Try(value, PowerMap, out preference);

    public static bool TryParseBackground(string? value, out BackgroundPolicy policy) =>
        Try(value, BackgroundMap, out policy);

    public static string ToToken(Objective value) => Token(ObjectiveMap, value);
    public static string ToToken(SessionMode value) => Token(SessionMap, value);
    public static string ToToken(PowerPreference value) => Token(PowerMap, value);
    public static string ToToken(BackgroundPolicy value) => Token(BackgroundMap, value);

    private static readonly Dictionary<string, Objective> ObjectiveMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [FrameTimeConsistency] = Objective.FrameTimeConsistency,
        [Responsiveness] = Objective.Responsiveness,
        [Throughput] = Objective.Throughput,
        [QuietBalanced] = Objective.QuietBalanced,
        [Restore] = Objective.Restore
    };

    private static readonly Dictionary<string, SessionMode> SessionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [Temporary] = SessionMode.Temporary,
        [Persistent] = SessionMode.Persistent
    };

    private static readonly Dictionary<string, PowerPreference> PowerMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [Performance] = PowerPreference.Performance,
        [Balanced] = PowerPreference.Balanced,
        [Saver] = PowerPreference.Saver
    };

    private static readonly Dictionary<string, BackgroundPolicy> BackgroundMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [Preserve] = BackgroundPolicy.Preserve,
        [ExplicitWorkersOnly] = BackgroundPolicy.ExplicitWorkersOnly
    };

    private static bool Try<T>(string? value, Dictionary<string, T> map, out T parsed) where T : struct
    {
        if (value is not null && map.TryGetValue(value.Trim(), out var found))
        {
            parsed = found;
            return true;
        }

        parsed = default;
        return false;
    }

    private static string Token<T>(Dictionary<string, T> map, T value) where T : struct
    {
        foreach (var pair in map)
        {
            if (EqualityComparer<T>.Default.Equals(pair.Value, value))
            {
                return pair.Key;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "Unmapped token.");
    }
}

public static class IntentLimits
{
    public const int MaxApplications = 16;
    public const int MaxNameLength = 128;
    public const int MaxLoadoutIdLength = 64;
    public const int MaxJustificationLength = 400;
    public const int MinJustificationLength = 8;
    public const int MaxOllamaResponseBytes = 65_536;
}
