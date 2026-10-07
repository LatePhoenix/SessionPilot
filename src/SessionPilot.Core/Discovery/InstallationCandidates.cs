namespace SessionPilot.Core;

public sealed record ConfigProbe
{
    public required string Label { get; init; }
    public required string Status { get; init; }
    public bool? CurrentUserCanWrite { get; init; }
    public long? ByteLength { get; init; }
    public string? Detail { get; init; }
}

public sealed record BinaryProbe
{
    public required string Role { get; init; }
    public required string Label { get; init; }
    public required string Status { get; init; }
    public string? Version { get; init; }
    public string? Detail { get; init; }
}

public static class InstallationCandidates
{
    public const string NotActiveLimitation =
        "A detected prolasso.ini is a candidate. The first present file is not the active configuration.";

    public static ProcessLassoInstallation Assemble(IReadOnlyList<ConfigProbe> configs, IReadOnlyList<BinaryProbe> binaries)
    {
        var candidates = configs.Select(probe => new ConfigCandidate
        {
            Path = probe.Label,
            ByteLength = probe.ByteLength ?? 0,
            CurrentUserCanWrite = probe.CurrentUserCanWrite == true,
            AclNote = probe.CurrentUserCanWrite switch
            {
                true => "Write access was reported by the probe.",
                false => "Write access was not confirmed.",
                _ => probe.Status == "access-denied" ? "Access denied." : "Write access was not probed."
            },
            Parsed = false,
            ParseNote = probe.Detail ?? probe.Status,
            Confidence = DiscoveryConfidence.Low,
            UserConfirmed = false
        }).ToList();

        var gui = binaries.FirstOrDefault(item => item.Role == "gui" && item.Status == "present");
        var governor = binaries.FirstOrDefault(item => item.Role == "governor" && item.Status == "present");
        var limitations = new List<string> { NotActiveLimitation };
        if (configs.Any(item => item.Status == "access-denied"))
        {
            limitations.Add("At least one candidate path was access-denied.");
        }

        if (configs.Any(item => item.Status == "missing"))
        {
            limitations.Add("At least one candidate path was missing.");
        }

        return new ProcessLassoInstallation
        {
            InstallDirectory = null,
            GuiPath = gui?.Label,
            GovernorPath = governor?.Label,
            ProductVersion = gui?.Version,
            FileVersion = gui?.Version,
            ConfigCandidates = candidates,
            EmptyLocations = configs.Where(item => item.Status == "missing").Select(item => item.Label).ToList(),
            Limitations = limitations,
            MultipleCandidates = configs.Count(item => item.Status == "present") > 1,
            Confidence = DiscoveryConfidence.Low,
            ConfirmedConfigPath = null
        };
    }
}

public sealed record ProcessInventoryEntry
{
    public required int ProcessId { get; init; }
    public DateTimeOffset? CreationTime { get; init; }
    public required string Name { get; init; }
    public string? ExecutablePath { get; init; }
    public required string Access { get; init; }
}

public sealed record ProcessQueryRow
{
    public required int ProcessId { get; init; }
    public DateTimeOffset? CreationTime { get; init; }
    public required string Name { get; init; }
    public string? ExecutablePath { get; init; }
    public bool AccessDenied { get; init; }
    public bool Exited { get; init; }
}

public static class ProcessInventory
{
    public static ProcessInventoryEntry FromRow(ProcessQueryRow row)
    {
        var access = row.Exited ? "exited" : row.AccessDenied ? "access-denied" : "ok";
        return new ProcessInventoryEntry
        {
            ProcessId = row.ProcessId,
            CreationTime = row.CreationTime,
            Name = row.Name,
            ExecutablePath = access == "ok" ? row.ExecutablePath : null,
            Access = access
        };
    }
}
