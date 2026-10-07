using System.Text.Json;

namespace SessionPilot.Core;

public static class ApplicationMatcher
{
    public static IReadOnlyList<ApplicationIdentity> MarkCollisions(IReadOnlyList<ApplicationIdentity> identities)
    {
        var groups = identities
            .Where(identity => !string.IsNullOrWhiteSpace(identity.ExecutableName))
            .GroupBy(identity => identity.ExecutableName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.FullPath).Where(path => path is not null).Distinct(StringComparer.OrdinalIgnoreCase).Count(), StringComparer.OrdinalIgnoreCase);

        return identities.Select(identity =>
        {
            var collision = identity.ExecutableName is not null && groups.TryGetValue(identity.ExecutableName, out var paths) && paths > 1;
            return collision ? identity with { Collision = true, Note = Append(identity.Note, "Executable name matches more than one path.") } : identity;
        }).ToList();
    }

    public static ApplicationIdentity? Select(string requestedName, IReadOnlyList<ApplicationIdentity> identities)
    {
        var wanted = BaseName(requestedName);
        var matches = identities.Where(identity =>
            string.Equals(BaseName(identity.DisplayName), wanted, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(BaseName(identity.ExecutableName), wanted, StringComparison.OrdinalIgnoreCase)).ToList();

        var confirmed = matches.Where(identity => identity.Confidence == IdentityConfidence.Confirmed && !identity.Collision).ToList();
        if (confirmed.Count == 1)
        {
            return confirmed[0];
        }

        return null;
    }

    public static string BaseName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var name = Path.GetFileName(value.Trim());
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static string Append(string existing, string note) =>
        string.IsNullOrWhiteSpace(existing) ? note : existing + " " + note;
}

public sealed record CompileInput
{
    public required UserIntent Intent { get; init; }
    public required Loadout Loadout { get; init; }
    public IReadOnlyList<ApplicationIdentity> Applications { get; init; } = [];
    public AdapterCapabilities? Capabilities { get; init; }
    public HardwareInventory? Hardware { get; init; }
    public IReadOnlyList<WorkerRequest> Workers { get; init; } = [];
}

public static class PlanCompiler
{
    public static CompiledPlan Compile(CompileInput input)
    {
        var error = LoadoutValidator.Validate(input.Loadout);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }

        if (!string.Equals(input.Intent.LoadoutId, input.Loadout.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Intent loadout does not match the compiled loadout.");
        }

        var changes = new List<PlanChange>();
        var warnings = new List<string>();
        changes.Add(Change(
            "probalance-preserve",
            "ProBalance",
            "Existing configuration",
            "Preserve",
            "Initial presets keep the current ProBalance behavior.",
            SupportStatus.NoChange,
            RiskCategory.None,
            "Product policy. OutOfControlProcessRestraint remains untouched.",
            "No write. Re-read is unnecessary."));

        changes.Add(Change(
            "priority-unchanged",
            "CPU priority",
            "Unchanged",
            "Unchanged",
            "Elevated priority is not used to chase frame time. Real-time and High stay forbidden.",
            SupportStatus.NoChange,
            RiskCategory.None,
            "Automation guidance says above-normal priority does not make an application faster. DefaultPriorities serialization is also ambiguous.",
            "No write."));

        changes.Add(PlacementChange(input.Hardware));

        if (!string.Equals(input.Intent.PowerPreference, Names.Balanced, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Power preference is intent only. Application power-profile keys are not edited.");
        }

        if (input.Loadout.OfferPerformanceMode)
        {
            changes.AddRange(PerformanceChanges(input));
        }

        if (input.Loadout.OfferEfficiencyModeOff)
        {
            changes.AddRange(EfficiencyChanges(input));
        }

        if (input.Loadout.RequiresExplicitWorkers && input.Workers.Count == 0)
        {
            changes.Add(Change(
                "workers-required",
                "Background workers",
                "None selected",
                "No rule",
                "Build-heavy and background policies wait for an explicit worker selection. Editors, browsers, terminals, Python, and Node are not treated as expendable.",
                SupportStatus.BlockedByPolicy,
                RiskCategory.Blocked,
                "Product policy.",
                "Select a worker and a justification before a future verified exclusion codec can be considered."));
        }

        foreach (var worker in input.Workers)
        {
            changes.Add(WorkerChange(worker));
        }

        var plan = new CompiledPlan
        {
            PlanId = Guid.NewGuid().ToString("n"),
            Intent = input.Intent,
            LoadoutId = input.Loadout.Id,
            Summary = input.Loadout.Summary,
            Changes = changes,
            Warnings = warnings,
            Stages = Stages(),
            ManualImportPreviewJson = ManualPreview(changes)
        };

        if (plan.Changes.Any(change => change.Writable))
        {
            throw new InvalidOperationException("The compiler produced a writable change while live codecs are unverified.");
        }

        return plan;
    }

    private static IEnumerable<PlanChange> PerformanceChanges(CompileInput input)
    {
        var roles = input.Loadout.ParticipantRoles.Where(role => role is "game" or "compositor").ToList();
        if (roles.Count == 0)
        {
            roles.Add("game");
        }

        foreach (var role in roles)
        {
            var confirmed = ConfirmedForRole(input, role);
            if (confirmed is null)
            {
                yield return Change(
                    "performance-" + role,
                    role,
                    "Unconfirmed",
                    "Performance Mode membership, if later verified",
                    $"No confirmed {role} identity. A name guess is not a selector.",
                    SupportStatus.ManualPreviewOnly,
                    RiskCategory.Low,
                    CapabilityEvidence(input, "performance-mode"),
                    "Confirm the process, then compare a GUI Export Rules file before any import.");
            }
            else
            {
                yield return Change(
                    "performance-" + confirmed.ExecutableName,
                    confirmed.ExecutableName ?? confirmed.DisplayName,
                    "Unknown persisted membership",
                    "performanceMode true in a manual JSON preview",
                    "Offer game-triggered Performance Mode for a confirmed participant. This does not change priority or CPU placement.",
                    SupportStatus.ManualPreviewOnly,
                    RiskCategory.Low,
                    CapabilityEvidence(input, "performance-mode"),
                    "Do not import until the preview is compared with File > Export Rules from this Process Lasso build. Governor consumption is not verified.");
            }
        }
    }

    private static IEnumerable<PlanChange> EfficiencyChanges(CompileInput input)
    {
        foreach (var role in input.Loadout.ParticipantRoles)
        {
            var confirmed = ConfirmedForRole(input, role);
            var target = confirmed?.ExecutableName ?? role;
            yield return Change(
                "efficiency-" + target,
                target,
                confirmed is null ? "Unconfirmed" : "Unknown persisted value",
                "Efficiency Mode OFF for this participant, encoding unknown",
                "Critical participants should not be broadly classified as background work. The OFF value's file encoding is not verified.",
                SupportStatus.KeyObservedFormatUnverified,
                RiskCategory.Medium,
                CapabilityEvidence(input, "efficiency-mode"),
                "Capture a one-rule GUI export before any write. Empty ProcessAllowances/EfficiencyMode is not a value fixture.");
        }
    }

    private static PlanChange WorkerChange(WorkerRequest worker)
    {
        if (ProtectedProcesses.IsProtected(worker.ExecutableName))
        {
            return Change(
                "worker-" + worker.ExecutableName,
                worker.ExecutableName,
                "Protected",
                "Refused",
                "Audio, compositor, security, and system processes are not worker targets.",
                SupportStatus.BlockedByPolicy,
                RiskCategory.Blocked,
                "Product policy.",
                "No write.");
        }

        if (!string.Equals(worker.Treatment, Names.ExcludeFromProBalance, StringComparison.OrdinalIgnoreCase))
        {
            return Change(
                "worker-" + worker.ExecutableName,
                worker.ExecutableName,
                "Unchanged",
                "Refused",
                "The only worker treatment under consideration is a ProBalance exclusion, and it still is not written.",
                SupportStatus.BlockedByPolicy,
                RiskCategory.Blocked,
                "Product policy.",
                "No write.");
        }

        if (worker.Justification.Trim().Length < IntentLimits.MinJustificationLength)
        {
            return Change(
                "worker-" + worker.ExecutableName,
                worker.ExecutableName,
                "Unchanged",
                "Exclusion withheld",
                "A ProBalance exclusion needs a specific justification.",
                SupportStatus.BlockedByPolicy,
                RiskCategory.Blocked,
                "Historical OocExclusions is a comma-separated list, and the installed value format is unverified.",
                "Provide a justification. A future write still waits on a fixture.");
        }

        return Change(
            "worker-" + worker.ExecutableName,
            worker.ExecutableName,
            "Unknown",
            "Would append to OocExclusions after codec verification",
            worker.Justification.Trim(),
            SupportStatus.DocumentedFormatUnverified,
            RiskCategory.Medium,
            "OocExclusions is documented as comma-separated. Populated encoding and pathname matching are unverified.",
            "No write until a before/after export matches the parser.");
    }

    private static PlanChange PlacementChange(HardwareInventory? hardware)
    {
        var ambiguous = hardware is null || hardware.Confidence == DiscoveryConfidence.None || hardware.Ambiguities.Count > 0 || !hardware.Single64BitMaskCoversMachine;
        return Change(
            "placement-unchanged",
            "CPU placement",
            "Unchanged",
            "Unchanged",
            ambiguous
                ? "Topology is incomplete or spans processor groups. Affinity and CPU Sets stay unchanged."
                : "Topology was read. Placement still stays unchanged until a CPU Sets codec is verified.",
            SupportStatus.Deferred,
            RiskCategory.None,
            "GetLogicalProcessorInformationEx and CPU Set information. A cache index is not a gaming CCD.",
            "No write.");
    }

    private static ApplicationIdentity? ConfirmedForRole(CompileInput input, string role)
    {
        if (!Enum.TryParse<ApplicationRole>(role, true, out var parsed))
        {
            return null;
        }

        return input.Applications.FirstOrDefault(identity =>
            identity.Role == parsed &&
            identity.Confidence == IdentityConfidence.Confirmed &&
            !identity.Collision &&
            !string.IsNullOrWhiteSpace(identity.ExecutableName));
    }

    private static string? ManualPreview(IReadOnlyList<PlanChange> changes)
    {
        var names = changes
            .Where(change => change.ChangeId.StartsWith("performance-", StringComparison.Ordinal) && change.ProposedValue.Contains("performanceMode", StringComparison.Ordinal))
            .Select(change => change.TargetIdentity)
            .Where(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            return null;
        }

        var document = new
        {
            schemaVersionNote = "Unverified preview. Compare with File > Export Rules before any import.",
            id = "https://bitsum.com/json-schemas/2026-01-17-00/processlasso.schema.json",
            schema = "https://json-schema.org/draft/2020-12/schema",
            title = "Process Lasso Rules",
            description = "SessionPilot manual preview. Not an export from Process Lasso.",
            ruleSets = names.Select(name => new { processName = name, performanceMode = true }).ToList()
        };
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string CapabilityEvidence(CompileInput input, string id)
    {
        var capability = input.Capabilities?.Items.FirstOrDefault(item => item.Id == id);
        return capability is null ? "No installed capability scan was attached." : capability.Evidence;
    }

    private static IReadOnlyList<StageState> Stages() =>
    [
        new() { Stage = "Plan compiled", Status = "yes", Detail = "The dry-run plan is ready for review." },
        new() { Stage = "Configuration persisted", Status = "no", Detail = "Live writes are disabled." },
        new() { Stage = "Governor consumption verified", Status = "not-verified", Detail = "Reload after a third-party file replacement has not been tested." },
        new() { Stage = "Effective setting observed", Status = "not-observed", Detail = "A persisted rule would still not prove the live Windows setting." },
        new() { Stage = "Performance effect measured", Status = "not-measured", Detail = "No frame-time or FPS claim is made." }
    ];

    private static PlanChange Change(
        string id,
        string target,
        string existing,
        string proposed,
        string rationale,
        SupportStatus status,
        RiskCategory risk,
        string evidence,
        string verification) => new()
    {
        ChangeId = id,
        TargetIdentity = target,
        ExistingValue = existing,
        ProposedValue = proposed,
        Rationale = rationale,
        SupportStatus = status,
        Risk = risk,
        Evidence = evidence,
        VerificationStrategy = verification,
        Writable = false
    };
}
