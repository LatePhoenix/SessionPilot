using System.Diagnostics;

namespace SessionPilot.Infrastructure;

public static class PowerPlanReader
{
    public static string? TryList()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = "/list",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(4000))
            {
                return null;
            }

            return output;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
