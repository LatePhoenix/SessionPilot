namespace SessionPilot.Core;

public sealed record HardwareInventory
{
    public int SchemaVersion { get; init; } = Schema.Current;
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? ProcessorName { get; init; }
    public DiscoveryConfidence Confidence { get; init; }
    public bool HeterogeneousCoresReported { get; init; }
    public bool Single64BitMaskCoversMachine { get; init; }
    public int GroupCount { get; init; }
    public int LogicalProcessorCount { get; init; }
    public IReadOnlyList<string> Ambiguities { get; init; } = [];
    public IReadOnlyList<LogicalProcessorRecord> LogicalProcessors { get; init; } = [];
    public IReadOnlyList<CpuSetRecord> CpuSets { get; init; } = [];
    public IReadOnlyList<CacheRecord> Caches { get; init; } = [];
    public IReadOnlyList<NumaRecord> NumaNodes { get; init; } = [];
}

public sealed record LogicalProcessorRecord
{
    public int Group { get; init; }
    public int IndexInGroup { get; init; }
    public int? EfficiencyClass { get; init; }
    public int? CoreIndex { get; init; }
}

public sealed record CpuSetRecord
{
    public uint Id { get; init; }
    public int Group { get; init; }
    public int LogicalProcessorIndex { get; init; }
    public int CoreIndex { get; init; }
    public int LastLevelCacheIndex { get; init; }
    public int NumaNodeIndex { get; init; }
    public int EfficiencyClass { get; init; }
}

public sealed record CacheRecord
{
    public int Level { get; init; }
    public uint SizeBytes { get; init; }
    public int Group { get; init; }
}

public sealed record NumaRecord
{
    public uint NodeNumber { get; init; }
    public int Group { get; init; }
}

public sealed record ApplicationIdentity
{
    public int SchemaVersion { get; init; } = Schema.Current;
    public required string DisplayName { get; init; }
    public string? ExecutableName { get; init; }
    public string? FullPath { get; init; }
    public int? ProcessId { get; init; }
    public IdentityConfidence Confidence { get; init; }
    public ApplicationRole Role { get; init; }
    public bool Collision { get; init; }
    public string Note { get; init; } = "";
}

public sealed record ConfigCandidate
{
    public required string Path { get; init; }
    public long ByteLength { get; init; }
    public string Encoding { get; init; } = "";
    public bool HasBom { get; init; }
    public string NewLineStyle { get; init; } = "";
    public string ContentHash { get; init; } = "";
    public bool CurrentUserCanWrite { get; init; }
    public string? AclNote { get; init; }
    public string? ProductWritableFlag { get; init; }
    public string? ConfigVersionKey { get; init; }
    public bool Parsed { get; init; }
    public string? ParseNote { get; init; }
    public IReadOnlyList<IniKeyObservation> Keys { get; init; } = [];
    public DiscoveryConfidence Confidence { get; init; }
    public bool UserConfirmed { get; init; }
}

public sealed record IniKeyObservation
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public bool ValueEmpty { get; init; }
    public ValueKind Kind { get; init; }
    public int ValueLength { get; init; }
}

public sealed record ProcessObservation
{
    public required string Name { get; init; }
    public int ProcessId { get; init; }
    public string? Path { get; init; }
    public string? CommandLine { get; init; }
    public string Note { get; init; } = "";
}

public sealed record ServiceObservation
{
    public required string Name { get; init; }
    public string? BinaryPath { get; init; }
    public string? Account { get; init; }
    public string? State { get; init; }
}

public sealed record ProcessLassoInstallation
{
    public int SchemaVersion { get; init; } = Schema.Current;
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? InstallDirectory { get; init; }
    public string? GuiPath { get; init; }
    public string? GovernorPath { get; init; }
    public string? ProductVersion { get; init; }
    public string? FileVersion { get; init; }
    public IReadOnlyList<ProcessObservation> Processes { get; init; } = [];
    public IReadOnlyList<ServiceObservation> Services { get; init; } = [];
    public IReadOnlyList<ConfigCandidate> ConfigCandidates { get; init; } = [];
    public IReadOnlyList<string> EmptyLocations { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
    public bool MultipleCandidates { get; init; }
    public DiscoveryConfidence Confidence { get; init; }
    public string? ConfirmedConfigPath { get; init; }
}

public sealed record Capability
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required SupportStatus Status { get; init; }
    public required string Evidence { get; init; }
    public bool LiveWriteEnabled { get; init; }
    public required string BlockReason { get; init; }
}

public sealed record AdapterCapabilities
{
    public int SchemaVersion { get; init; } = Schema.Current;
    public string? ObservedProductVersion { get; init; }
    public IReadOnlyList<Capability> Items { get; init; } = [];
}

public sealed record UserIntent
{
    public int SchemaVersion { get; init; } = Schema.Current;
    public required string LoadoutId { get; init; }
    public required string Objective { get; init; }
    public required string SessionMode { get; init; }
    public required string PowerPreference { get; init; }
    public required string BackgroundPolicy { get; init; }
    public required IReadOnlyList<string> RequestedApplications { get; init; }
}

public sealed record Loadout
{
    public int SchemaVersion { get; init; } = Schema.Current;
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Summary { get; init; }
    public required string Objective { get; init; }
    public required string SessionMode { get; init; }
    public required string PowerPreference { get; init; }
    public required string BackgroundPolicy { get; init; }
    public bool OfferPerformanceMode { get; init; }
    public bool OfferEfficiencyModeOff { get; init; }
    public required string PriorityPolicy { get; init; }
    public required string CpuPlacementPolicy { get; init; }
    public required string ProBalancePolicy { get; init; }
    public bool RequiresExplicitWorkers { get; init; }
    public IReadOnlyList<string> ParticipantRoles { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
    public bool BuiltIn { get; init; }
    public string? SourcePath { get; init; }
}

public sealed record WorkerRequest
{
    public required string ExecutableName { get; init; }
    public required string Treatment { get; init; }
    public required string Justification { get; init; }
}

public sealed record PlanChange
{
    public required string ChangeId { get; init; }
    public required string TargetIdentity { get; init; }
    public required string ExistingValue { get; init; }
    public required string ProposedValue { get; init; }
    public required string Rationale { get; init; }
    public required SupportStatus SupportStatus { get; init; }
    public required RiskCategory Risk { get; init; }
    public required string Evidence { get; init; }
    public required string VerificationStrategy { get; init; }
    public bool Writable { get; init; }
    public string? Section { get; init; }
    public string? Key { get; init; }
}

public sealed record StageState
{
    public required string Stage { get; init; }
    public required string Status { get; init; }
    public required string Detail { get; init; }
}

public sealed record CompiledPlan
{
    public int SchemaVersion { get; init; } = Schema.Current;
    public required string PlanId { get; init; }
    public required UserIntent Intent { get; init; }
    public required string LoadoutId { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<PlanChange> Changes { get; init; } = [];
    public IReadOnlyList<StageState> Stages { get; init; } = [];
    public string? ManualImportPreviewJson { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record OwnedValue
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public required string BaselineValue { get; init; }
    public required string WrittenValue { get; init; }
}

public sealed record ApplyTransaction
{
    public int SchemaVersion { get; init; } = Schema.Current;
    public required string Id { get; init; }
    public required string Status { get; init; }
    public required string Message { get; init; }
    public string? TargetPath { get; init; }
    public string? BaselineHash { get; init; }
    public string? IntendedHash { get; init; }
    public string? ObservedHash { get; init; }
    public string? BackupPath { get; init; }
    public string? JournalPath { get; init; }
    public string PersistenceStatus { get; init; } = "not-written";
    public string GovernorStatus { get; init; } = "not-verified";
    public string EffectiveStatus { get; init; } = "not-observed";
    public string PerformanceStatus { get; init; } = "not-measured";
    public IReadOnlyList<OwnedValue> OwnedValues { get; init; } = [];
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record VerificationResult
{
    public int SchemaVersion { get; init; } = Schema.Current;
    public required string Persistence { get; init; }
    public required string GovernorConsumption { get; init; }
    public required string EffectiveSetting { get; init; }
    public required string PerformanceEffect { get; init; }
    public required string Detail { get; init; }
}

public sealed record RestoreConflict
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public required string BaselineValue { get; init; }
    public required string WrittenValue { get; init; }
    public required string CurrentValue { get; init; }
    public required string Reason { get; init; }
}

public sealed record RestoreAction
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public required string FromValue { get; init; }
    public required string ToValue { get; init; }
}

public sealed record RestoreAnalysis
{
    public int SchemaVersion { get; init; } = Schema.Current;
    public IReadOnlyList<RestoreAction> Restorable { get; init; } = [];
    public IReadOnlyList<RestoreConflict> Conflicts { get; init; } = [];
    public IReadOnlyList<string> AlreadyOriginal { get; init; } = [];
    public bool WholeFileRestoreRequired { get; init; }
    public string Summary { get; init; } = "";
}
