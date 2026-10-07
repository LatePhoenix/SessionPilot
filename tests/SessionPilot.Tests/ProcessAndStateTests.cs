using SessionPilot.Infrastructure;

namespace SessionPilot.Tests;

public class ProcessAndStateTests
{
    [Fact]
    public void Sample_PrunesStaleCpu_AndReportsTheDelta()
    {
        var created = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var key = "9|" + created.UtcTicks;
        var previous = new Dictionary<string, TimeSpan>
        {
            ["stale|1"] = TimeSpan.FromSeconds(50),
            [key] = TimeSpan.FromSeconds(1)
        };
        var sample = ProcessSampler.Project(
            [
                new ProcessReading
                {
                    Name = "editor",
                    Pid = 9,
                    Created = created,
                    ProcessorTime = TimeSpan.FromSeconds(2),
                    WorkingSet = 4096,
                    Access = "ok"
                },
                new ProcessReading { Name = "gone", Pid = 3, Access = "exited" },
                new ProcessReading { Name = "locked", Pid = 4, Access = "access-denied" }
            ],
            previous,
            TimeSpan.FromSeconds(1),
            logicalProcessors: 4);

        Assert.Equal([key], sample.Cpu.Keys);
        Assert.Equal(TimeSpan.FromSeconds(2), sample.Cpu[key]);
        var live = Assert.Single(sample.Rows, row => row.Pid == "9");
        Assert.Equal("ok", live.Access);
        Assert.Equal(0.25.ToString("0.0%") + " of logical capacity (" + 1d.ToString("0.00") + " logical cores)", live.Cpu);
        Assert.Equal("exited", Assert.Single(sample.Rows, row => row.Pid == "3").Access);
        var denied = Assert.Single(sample.Rows, row => row.Pid == "4");
        Assert.Equal("access-denied", denied.Access);
        Assert.Equal("unavailable", denied.Cpu);
        Assert.Equal("unavailable", denied.WorkingSet);
    }

    [Fact]
    public void State_RoundTrips_AndIgnoresACorruptOrMissingFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sessionpilot-" + Guid.NewGuid().ToString("n"));
        var path = Path.Combine(directory, "state.json");
        var missing = AppStateStore.Load(path, out var missingNote);
        Assert.Null(missingNote);
        Assert.Equal(nameof(PowerOwnerKind.Unset), missing.PowerOwner);
        Assert.Empty(missing.Measurements);
        Assert.False(Directory.Exists(directory));

        AppStateStore.Save(path, new AppState
        {
            PowerOwner = nameof(PowerOwnerKind.ProcessLasso),
            Measurements =
            [
                new MeasurementRun { Label = "desk", Confounders = "none", PerformanceEffect = "faster" }
            ]
        });
        var loaded = AppStateStore.Load(path, out var loadedNote);
        Assert.Null(loadedNote);
        Assert.Equal(nameof(PowerOwnerKind.ProcessLasso), loaded.PowerOwner);
        var run = Assert.Single(loaded.Measurements);
        Assert.Equal("desk", run.Label);
        Assert.Equal("not-measured", run.PerformanceEffect);

        File.WriteAllText(path, "{");
        var corrupt = AppStateStore.Load(path, out var corruptNote);
        Assert.Equal("state.json The file is not valid JSON.", corruptNote);
        Assert.Equal(nameof(PowerOwnerKind.Unset), corrupt.PowerOwner);
        Assert.Empty(corrupt.Measurements);
        Directory.Delete(directory, recursive: true);
    }
}
