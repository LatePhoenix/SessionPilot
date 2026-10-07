namespace SessionPilot.Tests;

public class ApprovalAndRecoveryTests
{
    [Fact]
    public void ChangedPlanOrConfig_InvalidatesApproval()
    {
        var plan = new CompiledPlan
        {
            PlanId = "p",
            Intent = new UserIntent
            {
                LoadoutId = "balanced",
                Objective = Names.Restore,
                SessionMode = Names.Temporary,
                PowerPreference = Names.Balanced,
                BackgroundPolicy = Names.Preserve,
                RequestedApplications = []
            },
            LoadoutId = "balanced",
            Summary = "keep",
            Changes = [new PlanChange
            {
                ChangeId = "priority-unchanged",
                TargetIdentity = "CPU priority",
                ExistingValue = "Unchanged",
                ProposedValue = "Unchanged",
                Rationale = "No write.",
                SupportStatus = SupportStatus.NoChange,
                Risk = RiskCategory.None,
                Evidence = "policy",
                VerificationStrategy = "none",
                Writable = false
            }]
        };
        var hash = ApprovalBinding.HashPlan(plan);
        Assert.True(ApprovalBinding.StillValid(hash, "abc", hash, "abc"));
        var changed = plan with { Summary = "different" };
        Assert.False(ApprovalBinding.StillValid(hash, "abc", ApprovalBinding.HashPlan(changed), "abc"));
        Assert.False(ApprovalBinding.StillValid(hash, "abc", hash, "def"));
        var sectionChanged = plan with { Changes = [plan.Changes[0] with { Section = "Other" }] };
        Assert.NotEqual(hash, ApprovalBinding.HashPlan(sectionChanged));
        var pipeLeft = plan with { Changes = [plan.Changes[0] with { Section = "a|b", Key = "c" }] };
        var pipeRight = plan with { Changes = [plan.Changes[0] with { Section = "a", Key = "b|c" }] };
        Assert.NotEqual(ApprovalBinding.HashPlan(pipeLeft), ApprovalBinding.HashPlan(pipeRight));
    }

    [Fact]
    public void Recompile_KeepsTheSameApprovalHash()
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        Assert.True(catalog.TryGet("balanced", out var loadout));
        var intent = new UserIntent
        {
            LoadoutId = loadout.Id,
            Objective = loadout.Objective,
            SessionMode = loadout.SessionMode,
            PowerPreference = loadout.PowerPreference,
            BackgroundPolicy = loadout.BackgroundPolicy,
            RequestedApplications = []
        };
        var first = PlanCompiler.Compile(new CompileInput { Intent = intent, Loadout = loadout });
        var second = PlanCompiler.Compile(new CompileInput { Intent = intent, Loadout = loadout });
        Assert.NotEqual(first.PlanId, second.PlanId);
        Assert.Equal(ApprovalBinding.HashPlan(first), ApprovalBinding.HashPlan(second));
    }

    [Fact]
    public void Startup_DescribesAnIncompleteJournal_WithoutRollback()
    {
        var root = Path.Combine(Path.GetTempPath(), "sessionpilot-journal-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "copy.ini");
            File.WriteAllText(target, "[Custom]\r\nAlpha=one\r\n");
            var before = File.ReadAllBytes(target);
            var journal = "{\"schemaVersion\":1,\"id\":\"abc\",\"targetPath\":" + System.Text.Json.JsonSerializer.Serialize(target) +
                          ",\"state\":\"Replacing\",\"baselineHash\":\"" + ContentHashing.Sha256(before) +
                          "\",\"ownedValues\":[],\"createdUtc\":\"2026-10-07T00:00:00Z\",\"updatedUtc\":\"2026-10-07T00:00:00Z\"}";
            File.WriteAllText(Path.Combine(root, "abc.json"), journal);
            var notes = StartupRecovery.DescribeIncomplete(root);
            Assert.Equal(before, File.ReadAllBytes(target));
            Assert.Contains(notes, note => note.Contains("No rollback was applied", StringComparison.Ordinal));
            Assert.Contains(notes, note => note.Contains("Nothing was rolled back", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CorruptJournal_IsSkipped()
    {
        var root = NewRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "bad.json"), "{");
            var scan = JournalRecovery.Scan(root);
            Assert.Empty(scan.Incomplete);
            Assert.Equal("bad.json", Assert.Single(scan.UnreadableFileNames));
            var notes = StartupRecovery.DescribeIncomplete(root);
            Assert.Contains("bad.json: It was not opened or rolled back.", notes);
            Assert.DoesNotContain(notes, note => note.Contains(root, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EmptyJournal_IsSkipped()
    {
        var root = NewRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "empty.json"), "");
            var scan = JournalRecovery.Scan(root);
            Assert.Empty(JournalRecovery.FindIncomplete(root));
            Assert.Equal("empty.json", Assert.Single(scan.UnreadableFileNames));
            Assert.Contains("empty.json: It was not opened or rolled back.", StartupRecovery.DescribeIncomplete(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ValidIncompleteJournal_SurvivesACorruptNeighbor()
    {
        var root = NewRoot();
        try
        {
            var target = Path.Combine(root, "copy.ini");
            File.WriteAllText(target, "[Custom]\r\nAlpha=one\r\n");
            var before = File.ReadAllBytes(target);
            File.WriteAllText(Path.Combine(root, "bad.json"), "{");
            File.WriteAllText(Path.Combine(root, "ok.json"), IncompleteJournal(target, before));
            var scan = JournalRecovery.Scan(root);
            Assert.Single(scan.Incomplete);
            Assert.Equal("bad.json", Assert.Single(scan.UnreadableFileNames));
            var notes = StartupRecovery.DescribeIncomplete(root);
            Assert.Equal(before, File.ReadAllBytes(target));
            Assert.Contains(notes, note => note.Contains("No rollback was applied", StringComparison.Ordinal));
            Assert.Contains("bad.json: It was not opened or rolled back.", notes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LockedTarget_IsDescribedWithoutRollback()
    {
        var root = NewRoot();
        try
        {
            var target = Path.Combine(root, "copy.ini");
            File.WriteAllText(target, "[Custom]\r\nAlpha=one\r\n");
            using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var description = JournalRecovery.Describe(new JournalRecord { TargetPath = target, State = "Replacing" });
                Assert.Equal("The target could not be read. No rollback was applied.", description);
            }

            Assert.Equal("[Custom]\r\nAlpha=one\r\n", File.ReadAllText(target));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LockedIsolatedCopy_IsNotHashed()
    {
        var root = NewRoot();
        try
        {
            var target = Path.Combine(root, "copy.ini");
            File.WriteAllText(target, "[Custom]\r\nAlpha=one\r\n");
            using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.False(IsolatedCopyHash.TryRead(target, out _));
            }

            Assert.Equal("The isolated copy could not be read. Nothing was written.", IsolatedCopyHash.UnreadableMessage);
            Assert.True(IsolatedCopyHash.TryRead(target, out var hash));
            Assert.Equal(ContentHashing.Sha256(File.ReadAllBytes(target)), hash);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "sessionpilot-journal-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string IncompleteJournal(string target, byte[] before) =>
        "{\"schemaVersion\":1,\"id\":\"abc\",\"targetPath\":" + System.Text.Json.JsonSerializer.Serialize(target) +
        ",\"state\":\"Replacing\",\"baselineHash\":\"" + ContentHashing.Sha256(before) +
        "\",\"ownedValues\":[],\"createdUtc\":\"2026-10-07T00:00:00Z\",\"updatedUtc\":\"2026-10-07T00:00:00Z\"}";
}
