using System.Diagnostics;
using SessionPilot.Core;

namespace SessionPilot.Infrastructure;

public sealed record ProcessReading
{
    public required string Name { get; init; }
    public required int Pid { get; init; }
    public DateTimeOffset? Created { get; init; }
    public TimeSpan? ProcessorTime { get; init; }
    public long? WorkingSet { get; init; }
    public string Access { get; init; } = "ok";
}

public sealed record SampledProcess
{
    public string Name { get; init; } = "";
    public string Pid { get; init; } = "";
    public string Created { get; init; } = "";
    public string Access { get; init; } = "";
    public string Cpu { get; init; } = "";
    public string WorkingSet { get; init; } = "";
    public string Classification { get; init; } = "";
    public DateTimeOffset? CreationTime { get; init; }
}

public sealed record ProcessSample
{
    public required IReadOnlyList<SampledProcess> Rows { get; init; }
    public required IReadOnlyDictionary<string, TimeSpan> Cpu { get; init; }
}

public static class ProcessSampler
{
    public static ProcessSample Sample(IReadOnlyDictionary<string, TimeSpan> previousCpu, TimeSpan wall, int logicalProcessors)
    {
        var readings = new List<ProcessReading>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                readings.Add(Read(process));
            }
            catch (Exception)
            {
                readings.Add(new ProcessReading { Name = "unknown", Pid = TryPid(process), Access = "access-denied" });
            }
            finally
            {
                process.Dispose();
            }
        }

        return Project(readings, previousCpu, wall, logicalProcessors);
    }

    public static ProcessSample Project(
        IReadOnlyList<ProcessReading> readings,
        IReadOnlyDictionary<string, TimeSpan> previousCpu,
        TimeSpan wall,
        int logicalProcessors)
    {
        var rows = new List<SampledProcess>(readings.Count);
        var cpu = new Dictionary<string, TimeSpan>(readings.Count);
        foreach (var reading in readings)
        {
            var key = reading.Pid + "|" + (reading.Created?.UtcTicks.ToString() ?? "none");
            TimeSpan? previous = previousCpu.TryGetValue(key, out var stored) ? stored : null;
            var current = reading.Access == "ok" ? reading.ProcessorTime : null;
            if (current is not null)
            {
                cpu[key] = current.Value;
            }

            var observation = ProcessorTimeSeries.Observe(previous, current, wall, logicalProcessors, TimeSpan.Zero);
            var memory = MemoryReadings.FromWorkingSet(reading.Access == "ok" ? reading.WorkingSet : null);
            var preview = CleanupClassifier.Classify(reading.Name, protectedParticipant: false, observation.FractionOfLogicalCapacity);
            rows.Add(new SampledProcess
            {
                Name = reading.Name,
                Pid = reading.Pid.ToString(),
                Created = reading.Created?.ToString("yyyy-MM-dd HH:mm:ss") ?? "unavailable",
                Access = reading.Access,
                Cpu = FormatCpu(observation),
                WorkingSet = memory.WorkingSetBytes is null ? "unavailable" : memory.WorkingSetBytes.Value.ToString("N0") + " bytes",
                Classification = preview.Classification.ToString(),
                CreationTime = reading.Access == "ok" ? reading.Created : null
            });
        }

        return new ProcessSample { Rows = rows, Cpu = cpu };
    }

    private static ProcessReading Read(Process process)
    {
        try
        {
            var created = new DateTimeOffset(DateTime.SpecifyKind(process.StartTime, DateTimeKind.Local));
            return new ProcessReading
            {
                Name = process.ProcessName,
                Pid = process.Id,
                Created = created,
                ProcessorTime = process.TotalProcessorTime,
                WorkingSet = process.WorkingSet64,
                Access = "ok"
            };
        }
        catch (InvalidOperationException)
        {
            return new ProcessReading { Name = process.ProcessName, Pid = process.Id, Access = "exited" };
        }
        catch (Exception)
        {
            return new ProcessReading { Name = process.ProcessName, Pid = process.Id, Access = "access-denied" };
        }
    }

    private static int TryPid(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string FormatCpu(CpuObservation observation)
    {
        if (observation.FractionOfLogicalCapacity is null || observation.LogicalCoreEquivalents is null)
        {
            return "unavailable";
        }

        return observation.FractionOfLogicalCapacity.Value.ToString("0.0%") + " of logical capacity (" +
               observation.LogicalCoreEquivalents.Value.ToString("0.00") + " logical cores)";
    }
}
