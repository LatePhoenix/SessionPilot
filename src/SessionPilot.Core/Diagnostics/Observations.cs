namespace SessionPilot.Core;

public sealed record CpuObservation
{
    public TimeSpan? ProcessorTimeDelta { get; init; }
    public TimeSpan WallInterval { get; init; }
    public int LogicalProcessorCount { get; init; }
    public double? FractionOfLogicalCapacity { get; init; }
    public double? LogicalCoreEquivalents { get; init; }
    public TimeSpan CollectorDuration { get; init; }
}

public static class CpuMath
{
    public static double? FractionOfLogicalCapacity(TimeSpan? processorTimeDelta, TimeSpan wallInterval, int logicalProcessorCount)
    {
        if (processorTimeDelta is null || wallInterval <= TimeSpan.Zero || logicalProcessorCount < 1)
        {
            return null;
        }

        var fraction = processorTimeDelta.Value.TotalSeconds / (wallInterval.TotalSeconds * logicalProcessorCount);
        if (double.IsNaN(fraction) || double.IsInfinity(fraction))
        {
            return null;
        }

        return fraction;
    }

    public static double? LogicalCoreEquivalents(double? fractionOfLogicalCapacity, int logicalProcessorCount)
    {
        if (fractionOfLogicalCapacity is null || logicalProcessorCount < 1)
        {
            return null;
        }

        return fractionOfLogicalCapacity.Value * logicalProcessorCount;
    }
}

public static class ProcessorTimeSeries
{
    public static CpuObservation Observe(
        TimeSpan? previousProcessorTime,
        TimeSpan? currentProcessorTime,
        TimeSpan wallInterval,
        int logicalProcessorCount,
        TimeSpan collectorDuration)
    {
        TimeSpan? delta = null;
        if (previousProcessorTime is not null && currentProcessorTime is not null)
        {
            var value = currentProcessorTime.Value - previousProcessorTime.Value;
            delta = value < TimeSpan.Zero ? null : value;
        }

        var fraction = CpuMath.FractionOfLogicalCapacity(delta, wallInterval, logicalProcessorCount);
        return new CpuObservation
        {
            ProcessorTimeDelta = delta,
            WallInterval = wallInterval,
            LogicalProcessorCount = logicalProcessorCount,
            FractionOfLogicalCapacity = fraction,
            LogicalCoreEquivalents = CpuMath.LogicalCoreEquivalents(fraction, logicalProcessorCount),
            CollectorDuration = collectorDuration
        };
    }
}

public sealed record MemoryReading
{
    public long? WorkingSetBytes { get; init; }
    public bool IsMemoryPressure { get; init; }
    public required string Note { get; init; }
}

public static class MemoryReadings
{
    public static MemoryReading FromWorkingSet(long? workingSetBytes) => new()
    {
        WorkingSetBytes = workingSetBytes,
        IsMemoryPressure = false,
        Note = workingSetBytes is null
            ? "Working set was not available. It is not reported as zero and it is not memory pressure."
            : "Working set is not memory pressure."
    };
}

public sealed record GpuReading
{
    public bool Available { get; init; }
    public double? EngineUtilization { get; init; }
    public required string Note { get; init; }
}

public static class GpuReadings
{
    public static GpuReading Unavailable() => new()
    {
        Available = false,
        EngineUtilization = null,
        Note = "GPU engine counters are unavailable."
    };
}

public sealed class RollingSampleWindow<T>
{
    public const int DefaultCapacity = 30;
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);
    private readonly Queue<T> _samples = new();

    public RollingSampleWindow(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Capacity = capacity;
    }

    public int Capacity { get; }
    public bool IsClosed { get; private set; }
    public int Count => _samples.Count;
    public IReadOnlyList<T> Samples => _samples.ToList();

    public void Close() => IsClosed = true;

    // The window is bounded by dropping the oldest sample, not by a sample count, so sampling
    // continues for as long as the Diagnostics page is open.
    public bool ShouldTakeSample() => !IsClosed;

    public bool TryAdd(T sample)
    {
        if (IsClosed)
        {
            return false;
        }

        if (_samples.Count >= Capacity)
        {
            _samples.Dequeue();
        }

        _samples.Enqueue(sample);
        return true;
    }
}

public static class ProcessIdentity
{
    public static bool IsSame(int approvedPid, DateTimeOffset approvedCreation, int currentPid, DateTimeOffset currentCreation) =>
        approvedPid == currentPid && approvedCreation == currentCreation;

    public static bool IsPidReuse(int approvedPid, DateTimeOffset approvedCreation, int currentPid, DateTimeOffset currentCreation) =>
        approvedPid == currentPid && approvedCreation != currentCreation;
}

public sealed record HonestStatus
{
    public string Compiled { get; init; } = "not compiled";
    public string Persisted { get; init; } = "not-written";
    public string Governor { get; init; } = "not-verified";
    public string EffectiveSetting { get; init; } = "not-observed";
    public string PerformanceEffect { get; init; } = "not-measured";
}
