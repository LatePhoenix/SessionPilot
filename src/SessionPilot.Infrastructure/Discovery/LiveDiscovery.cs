using System.Diagnostics;
using System.Runtime.InteropServices;
using SessionPilot.Core;

namespace SessionPilot.Infrastructure;

public static class LiveDiscovery
{
    public static ProcessLassoInstallation Installation()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var configs = new[]
        {
            ProbeConfig(@"ProgramData\ProcessLasso\config\prolasso.ini", Path.Combine(programData, "ProcessLasso", "config", "prolasso.ini"))
        };
        var binaries = new[]
        {
            ProbeBinary("gui", @"Program Files\Process Lasso\ProcessLasso.exe", Path.Combine(programFiles, "Process Lasso", "ProcessLasso.exe")),
            ProbeBinary("governor", @"Program Files\Process Lasso\ProcessGovernor.exe", Path.Combine(programFiles, "Process Lasso", "ProcessGovernor.exe"))
        };
        return InstallationCandidates.Assemble(configs, binaries);
    }

    public static HardwareInventory Hardware()
    {
        try
        {
            var logical = NativeTopology.TryReadLogical(out var error);
            if (logical is null)
            {
                return new HardwareInventory
                {
                    Confidence = DiscoveryConfidence.None,
                    ProcessorName = null,
                    Ambiguities = [error ?? "Processor topology was not readable."]
                };
            }

            var inventory = ProcessorTopologyReader.Read(logical, [], null);
            var notes = inventory.Ambiguities.ToList();
            notes.Add("CPU set information was not collected by this probe.");
            return inventory with { Ambiguities = notes };
        }
        catch (Exception exception)
        {
            return new HardwareInventory
            {
                Confidence = DiscoveryConfidence.None,
                Ambiguities = ["Hardware probe failed: " + exception.GetType().Name]
            };
        }
    }

    public static IReadOnlyList<ProcessInventoryEntry> Processes()
    {
        var entries = new List<ProcessInventoryEntry>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                entries.Add(ReadProcess(process));
            }
            catch (Exception)
            {
                entries.Add(new ProcessInventoryEntry
                {
                    ProcessId = process.Id,
                    Name = SafeName(process),
                    Access = "access-denied"
                });
            }
            finally
            {
                process.Dispose();
            }
        }

        return entries;
    }

    public static CheckSnapshot CheckOnce()
    {
        var installation = Installation();
        var hardware = Hardware();
        var processes = Processes();
        var present = installation.ConfigCandidates.Count(item => item.ParseNote == "present");
        var missing = installation.EmptyLocations.Count;
        var denied = installation.ConfigCandidates.Count(item => item.ParseNote == "access-denied");
        var hardwareSummary = hardware.Confidence == DiscoveryConfidence.None
            ? "unavailable"
            : hardware.LogicalProcessorCount + " logical processors, confidence " + hardware.Confidence;
        return new CheckSnapshot
        {
            LiveWritesEnabled = LiveApplyPolicy.Enabled,
            InstallationVersion = installation.ProductVersion,
            ConfigCandidateCount = installation.ConfigCandidates.Count,
            PresentCandidates = present,
            MissingCandidates = missing,
            AccessDeniedCandidates = denied,
            ActiveConfigAssumed = installation.ConfirmedConfigPath is not null,
            HardwareSummary = hardwareSummary,
            ProcessCount = processes.Count,
            ProcessAccessDenied = processes.Count(item => item.Access == "access-denied"),
            Notes =
            [
                InstallationCandidates.NotActiveLimitation,
                "GPU engine counters are unavailable.",
                "Performance effect is not measured."
            ]
        };
    }

    private static ConfigProbe ProbeConfig(string label, string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new ConfigProbe { Label = label, Status = "missing", Detail = "missing" };
            }

            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
            }

            var length = new FileInfo(path).Length;
            return new ConfigProbe
            {
                Label = label,
                Status = "present",
                ByteLength = length,
                CurrentUserCanWrite = false,
                Detail = "present"
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new ConfigProbe { Label = label, Status = "access-denied", Detail = "access-denied" };
        }
        catch (IOException)
        {
            return new ConfigProbe { Label = label, Status = "access-denied", Detail = "access-denied" };
        }
    }

    private static BinaryProbe ProbeBinary(string role, string label, string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new BinaryProbe { Role = role, Label = label, Status = "missing" };
            }

            var version = FileVersionInfo.GetVersionInfo(path).ProductVersion;
            return new BinaryProbe { Role = role, Label = label, Status = "present", Version = version };
        }
        catch (UnauthorizedAccessException)
        {
            return new BinaryProbe { Role = role, Label = label, Status = "access-denied", Detail = "access-denied" };
        }
        catch (IOException)
        {
            return new BinaryProbe { Role = role, Label = label, Status = "access-denied", Detail = "access-denied" };
        }
    }

    private static ProcessInventoryEntry ReadProcess(Process process)
    {
        var name = SafeName(process);
        DateTimeOffset? created = null;
        string? path = null;
        var denied = false;
        var exited = false;
        try
        {
            created = new DateTimeOffset(DateTime.SpecifyKind(process.StartTime, DateTimeKind.Local));
        }
        catch (InvalidOperationException)
        {
            exited = true;
        }
        catch (Exception)
        {
            denied = true;
        }

        if (!denied && !exited)
        {
            try
            {
                path = process.MainModule?.FileName;
            }
            catch (InvalidOperationException)
            {
                exited = true;
            }
            catch (Exception)
            {
                denied = true;
                path = null;
            }
        }

        return ProcessInventory.FromRow(new ProcessQueryRow
        {
            ProcessId = process.Id,
            Name = name,
            CreationTime = created,
            ExecutablePath = path,
            AccessDenied = denied,
            Exited = exited
        });
    }

    private static string SafeName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

}

internal static class NativeTopology
{
    public static byte[]? TryReadLogical(out string? error)
    {
        error = null;
        var length = 0;
        GetLogicalProcessorInformationEx(0xffff, IntPtr.Zero, ref length);
        if (length <= 0)
        {
            error = "Processor topology length was not returned.";
            return null;
        }

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetLogicalProcessorInformationEx(0xffff, buffer, ref length))
            {
                error = "Processor topology was not readable.";
                return null;
            }

            var bytes = new byte[length];
            Marshal.Copy(buffer, bytes, 0, length);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref int length);
}
