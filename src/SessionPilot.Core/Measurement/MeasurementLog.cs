namespace SessionPilot.Core;

public sealed record MeasurementRun
{
    public required string Label { get; init; }
    public required string Confounders { get; init; }
    public string PerformanceEffect { get; init; } = "not-measured";
    public string CaptureNote { get; init; } = "No PresentMon import. A desktop PresentMon file would describe desktop presentation, not headset frame delivery.";
}

public sealed class MeasurementLog
{
    private readonly List<MeasurementRun> _runs = [];

    public IReadOnlyList<MeasurementRun> Runs => _runs;

    public MeasurementRun Add(string label, string confounders)
    {
        var run = new MeasurementRun
        {
            Label = string.IsNullOrWhiteSpace(label) ? "unlabeled" : label.Trim(),
            Confounders = confounders.Trim()
        };
        _runs.Add(run);
        return run;
    }
}
