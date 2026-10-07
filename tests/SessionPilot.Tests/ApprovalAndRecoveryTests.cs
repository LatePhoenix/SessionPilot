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
}
