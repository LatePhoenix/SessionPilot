namespace SessionPilot.Core;

/// <summary>
/// Live configuration writes stay disabled until a GUI export from the installed
/// build shows the populated encoding and a reload check shows the governor
/// consumed it. Key names observed in an empty file are not that fixture.
/// </summary>
public static class LiveApplyPolicy
{
    public const bool Enabled = false;

    public const string Reason =
        "Live writes are disabled. Candidate keys were observed, but populated serialization, rule order, and governor reload are not verified. " +
        "The planning reference also treats historical INI examples as incomplete. " +
        "On the development machine the candidate configuration was not writable by a standard user.";
}

public static class CapabilityAssessor
{
    public static AdapterCapabilities Assess(string? productVersion, IReadOnlyList<IniKeyObservation> keys)
    {
        bool Has(string section, string key) => keys.Any(item =>
            string.Equals(item.Section, section, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

        bool Empty(string section, string key) => keys.Any(item =>
            string.Equals(item.Section, section, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase) &&
            item.ValueEmpty);

        var items = new List<Capability>
        {
            PerformanceMode(Has("GamingMode", "GamingModeEnabled")),
            EfficiencyMode(Has("ProcessAllowances", "EfficiencyMode"), Empty("ProcessAllowances", "EfficiencyMode")),
            CpuPriority(Has("ProcessDefaults", "DefaultPriorities"), Empty("ProcessDefaults", "DefaultPriorities")),
            ProBalance(Has("OutOfControlProcessRestraint", "OocExclusions"), Empty("OutOfControlProcessRestraint", "OocExclusions")),
            CpuSets(Has("ProcessDefaults", "CPUSets")),
            Watchdog(),
            Affinity(Has("ProcessDefaults", "DefaultAffinitiesEx"))
        };

        return new AdapterCapabilities
        {
            ObservedProductVersion = productVersion,
            Items = items
        };
    }

    private static Capability PerformanceMode(bool gamingSectionPresent) => new()
    {
        Id = "performance-mode",
        Title = "Performance Mode membership",
        Status = SupportStatus.ManualPreviewOnly,
        Evidence = "The published JSON rules schema defines ruleSets[].performanceMode as a boolean for GUI import and export. " +
                   "No INI key was identified for that field. " +
                   (gamingSectionPresent
                       ? "A [GamingMode] section was observed and is not treated as this field."
                       : "No installed configuration was available to compare section names."),
        LiveWriteEnabled = false,
        BlockReason = "JSON import is a GUI action, and this install has not produced a before/after export. Automatic file writes stay off."
    };

    private static Capability EfficiencyMode(bool keyPresent, bool empty) => new()
    {
        Id = "efficiency-mode",
        Title = "Efficiency Mode",
        Status = keyPresent ? SupportStatus.KeyObservedFormatUnverified : SupportStatus.Unsupported,
        Evidence = keyPresent
            ? "ProcessAllowances/EfficiencyMode is present" + (empty ? " and empty." : " and populated. The value was not copied into a fixture.")
            : "The current published JSON schema does not define efficiencyMode. The automation page documents the GUI feature and says the E/e symbols are not an INI codec.",
        LiveWriteEnabled = false,
        BlockReason = "Populated encoding is unverified. GUI symbols are not a file format."
    };

    private static Capability CpuPriority(bool keyPresent, bool empty) => new()
    {
        Id = "cpu-priority",
        Title = "CPU priority",
        Status = SupportStatus.Ambiguous,
        Evidence = "Historical documentation describes [ProcessDefaults] DefaultPriorities with two conflicting list shapes, and a product-update example spells priority class differently from the JSON schema enum. " +
                   (keyPresent
                       ? "The key exists" + (empty ? " and is empty, so neither shape was observed." : " and is populated. Its text was not taken as a fixture.")
                       : " The key was not observed in a readable configuration."),
        LiveWriteEnabled = false,
        BlockReason = "The published formats disagree. Real-time and High are outside the conservative policy even after a codec exists."
    };

    private static Capability ProBalance(bool keyPresent, bool empty) => new()
    {
        Id = "probalance-exclusion",
        Title = "ProBalance exclusions",
        Status = SupportStatus.DocumentedFormatUnverified,
        Evidence = "Historical documentation describes OutOfControlProcessRestraint/OocExclusions as a comma-separated selector list. " +
                   (keyPresent
                       ? "The key exists" + (empty ? " and is empty." : " and is populated. Selectors were not copied.")
                       : " The key was not observed in a readable configuration."),
        LiveWriteEnabled = false,
        BlockReason = "An empty value does not prove the delimiter, pathname matching, or reload behavior. MatchExclusionsByPathnameToo is a separate flag."
    };

    private static Capability CpuSets(bool keyPresent) => new()
    {
        Id = "cpu-sets",
        Title = "CPU Sets",
        Status = SupportStatus.Deferred,
        Evidence = keyPresent
            ? "ProcessDefaults/CPUSets exists. Its encoding is not established, and a cache index is not a placement recommendation."
            : "The automation page documents CPU Sets. The reviewed INI reference does not establish the current key. Placement stays unchanged when topology is incomplete.",
        LiveWriteEnabled = false,
        BlockReason = "Deferred until topology and a GUI-produced codec both exist."
    };

    private static Capability Watchdog() => new()
    {
        Id = "watchdog",
        Title = "Watchdog rules",
        Status = SupportStatus.Unsupported,
        Evidence = "The historical WatchdogRules example uses metric value 2, which is outside the same page's metric list of 0 and 1. The overview also alternates WatchdogRules and WatchdogActions. An installed file may contain WatchdogRules2. That example is not a fixture.",
        LiveWriteEnabled = false,
        BlockReason = "The published example is internally inconsistent. Numeric priority and affinity mappings are not invented."
    };

    private static Capability Affinity(bool extendedKeyPresent) => new()
    {
        Id = "cpu-affinity",
        Title = "CPU affinity",
        Status = SupportStatus.BlockedByPolicy,
        Evidence = extendedKeyPresent
            ? "ProcessDefaults/DefaultAffinitiesEx was observed. The historical sample key is DefaultAffinities. Initial presets do not change affinity."
            : "Historical sample key DefaultAffinities is not a complete modern codec. Initial presets do not change affinity.",
        LiveWriteEnabled = false,
        BlockReason = "Affinity changes are outside the initial policy, including SMT and universal mask tricks."
    };
}
