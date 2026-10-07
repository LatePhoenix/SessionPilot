namespace SessionPilot.Tests;

public class TriggerAndMeasurementTests
{
    [Fact]
    public void OptInTrigger_SuggestsWithoutApplying()
    {
        var decision = TriggerSuggestions.Suggest(
            new TriggerSnapshot { SignaledLoadouts = ["vrchat-social"], PendingLoadoutId = "vrchat-social", PendingSince = DateTimeOffset.Parse("2026-10-07T12:00:00Z") },
            new TriggerOptions { OptedIn = true, Debounce = TimeSpan.FromSeconds(1), MinimumDwell = TimeSpan.FromSeconds(1) },
            DateTimeOffset.Parse("2026-10-07T12:00:05Z"));

        Assert.Equal("suggest", decision.Action);
        Assert.Equal("vrchat-social", decision.LoadoutId);
        Assert.DoesNotContain("apply", decision.Action, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Engine_SuggestsOnly_AndNeverApplies()
    {
        var now = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
        var options = new TriggerOptions { OptedIn = true, Debounce = TimeSpan.FromSeconds(1), MinimumDwell = TimeSpan.FromSeconds(1), ExitGrace = TimeSpan.FromSeconds(1) };
        TriggerSnapshot[] snapshots =
        [
            new() { SignaledLoadouts = ["vrchat-social"] },
            new() { SignaledLoadouts = ["vrchat-social"], PendingLoadoutId = "vrchat-social", PendingSince = now.AddSeconds(-5) },
            new() { ManualOverrideActive = true, ManualLoadoutId = "balanced", SignaledLoadouts = ["vrchat-social"] },
            new() { ActiveLoadoutId = "vrchat-social", SessionEndedAt = now.AddMinutes(-2) },
            new() { SignaledLoadouts = [] }
        ];
        foreach (var snapshot in snapshots)
        {
            var decision = TriggerEngine.Evaluate(snapshot, options, now);
            Assert.True(decision.Action is "hold" or "wait" or "suggest");
            var wrapped = TriggerSuggestions.Suggest(snapshot, options, now);
            Assert.True(wrapped.Action is "hold" or "wait" or "suggest");
        }

        var off = TriggerEngine.Evaluate(snapshots[0], new TriggerOptions { OptedIn = false }, now);
        Assert.Equal("hold", off.Action);
    }

    [Fact]
    public void TriggersOff_DoNotSuggest()
    {
        var decision = TriggerSuggestions.Suggest(
            new TriggerSnapshot { SignaledLoadouts = ["vrchat-steamvr"] },
            new TriggerOptions { OptedIn = false },
            DateTimeOffset.UnixEpoch);
        Assert.Equal("hold", decision.Action);
    }

    [Fact]
    public void LowerPrecedenceFirstLine_ReachesSuggestAfterDwell()
    {
        var tracker = new TriggerTracker();
        var start = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
        var first = tracker.Evaluate(["balanced", "desktop-gaming"], null, true, start);
        Assert.Equal("wait", first.Action);
        Assert.Equal("desktop-gaming", tracker.PendingLoadoutId);

        var later = tracker.Evaluate(["balanced", "desktop-gaming"], null, true, start.AddSeconds(20));
        Assert.Equal("suggest", later.Action);
        Assert.Equal("desktop-gaming", later.LoadoutId);
    }

    [Fact]
    public void ChangingTopSignal_RestartsDwell()
    {
        var tracker = new TriggerTracker();
        var start = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
        tracker.Evaluate(["desktop-gaming"], null, true, start);
        var changed = tracker.Evaluate(["vrchat-social"], null, true, start.AddSeconds(20));
        Assert.Equal("wait", changed.Action);
        Assert.Equal("vrchat-social", tracker.PendingLoadoutId);
        Assert.Equal(start.AddSeconds(20), tracker.PendingSince);
    }

    [Fact]
    public void OptOut_StillHolds()
    {
        var tracker = new TriggerTracker();
        var decision = tracker.Evaluate(["desktop-gaming"], null, false, DateTimeOffset.Parse("2026-10-07T12:00:30Z"));
        Assert.Equal("hold", decision.Action);
    }

    [Fact]
    public void Measurements_DoNotClaimFrameRate()
    {
        var log = new MeasurementLog();
        var first = log.Add("quiet world", "same avatar policy");
        var second = log.Add("crowded world", "population changed");
        Assert.Equal(2, log.Runs.Count);
        Assert.All(log.Runs, run => Assert.Equal("not-measured", run.PerformanceEffect));
        Assert.Contains("desktop presentation", first.CaptureNote, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not headset", second.CaptureNote, StringComparison.OrdinalIgnoreCase);
    }
}
