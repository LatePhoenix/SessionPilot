using System.Diagnostics;

namespace SessionPilot.Infrastructure;

public static class PowerPlanReader
{
    public static async Task<string?> TryListAsync()
    {
        Process? process = null;
        try
        {
            process = Process.Start(CreateListStartInfo());
            if (process is null)
            {
                return null;
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return await output.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    internal static ProcessStartInfo CreateListStartInfo() => new()
    {
        FileName = Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
        Arguments = "/list",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    private static void Kill(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception)
        {
        }
    }
}
