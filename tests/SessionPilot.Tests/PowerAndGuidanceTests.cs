namespace SessionPilot.Tests;

public class PowerAndGuidanceTests
{
    [Fact]
    public void Parser_ReadsNamesWithoutSwitching()
    {
        var plans = PowerPlanParser.Parse("""
            Existing Power Schemes (* Active)
            Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *
            Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)
            """);

        Assert.Equal(2, plans.Count);
        Assert.Equal("Balanced", plans[0].Name);
        Assert.True(plans[0].Active);
        Assert.False(plans[1].Active);
    }

    [Fact]
    public void SecondWriter_IsRefused_AndNothingSwitchesWithoutAnExport()
    {
        var recorded = PowerOwnership.Select(PowerOwnerKind.ProcessLasso, PowerOwnerKind.Unset, exportExists: false);
        Assert.Equal(PowerOwnerKind.ProcessLasso, recorded.Owner);
        Assert.False(recorded.Switched);

        var conflict = PowerOwnership.Select(PowerOwnerKind.Windows, recorded.Owner, exportExists: true);
        Assert.Equal(PowerOwnerKind.ProcessLasso, conflict.Owner);
        Assert.False(conflict.Switched);
        Assert.Contains("second power writer", conflict.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuidedVr_DoesNotWriteFiles()
    {
        Assert.All(GuidedWorkflows.All, workflow =>
        {
            Assert.Equal("guided-only", workflow.Mode);
            Assert.False(workflow.WritesFiles);
        });
        Assert.Equal(6, GuidedWorkflows.VrChat.Steps.Count);
    }
}