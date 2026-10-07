using System.Diagnostics;
using System.Text.Json.Nodes;

namespace SessionPilot.Core;

public enum OllamaAvailability
{
    AlreadyRunning,
    StartedByRequest,
    NotRunning,
    TimedOut,
    Unusable
}

public interface IOllamaSession : IAsyncDisposable
{
    OllamaAvailability Availability { get; }
    string? Detail { get; }
}

public interface IOllamaHost
{
    Task<IOllamaSession> AcquireAsync(Uri chatEndpoint, TimeSpan budget, CancellationToken cancellationToken);
}

public interface IOllamaLauncher
{
    Task<IOllamaSession> StartAsync(Uri healthEndpoint, TimeSpan budget, CancellationToken cancellationToken);
}

/// <summary>
/// Starts <c>ollama serve</c> only when nothing is listening, and stops only that process.
/// An Ollama the user already had open is left running. The chat request still sends
/// keep_alive 0 so the model weights are unloaded either way.
/// </summary>
public sealed class ProbingOllamaHost : IOllamaHost
{
    private readonly HttpClient _http;
    private readonly IOllamaLauncher? _launcher;

    public ProbingOllamaHost(HttpClient http, IOllamaLauncher? launcher = null)
    {
        _http = http;
        _launcher = launcher;
    }

    public async Task<IOllamaSession> AcquireAsync(Uri chatEndpoint, TimeSpan budget, CancellationToken cancellationToken)
    {
        var health = new Uri(chatEndpoint.GetLeftPart(UriPartial.Authority) + "/api/version");
        var probe = await ProbeAsync(health, budget, cancellationToken).ConfigureAwait(false);
        return probe switch
        {
            ProbeResult.Ready => OllamaSession.Existing(),
            ProbeResult.TimedOut => OllamaSession.TimedOut(),
            ProbeResult.Unusable => OllamaSession.Unusable("Ollama is already running but did not answer a version check. It was left running."),
            _ => _launcher is null
                ? OllamaSession.NotRunning("Ollama is not running.")
                : await _launcher.StartAsync(health, budget, cancellationToken).ConfigureAwait(false)
        };
    }

    private async Task<ProbeResult> ProbeAsync(Uri health, TimeSpan budget, CancellationToken cancellationToken)
    {
        var limit = budget < TimeSpan.FromSeconds(2) ? budget : TimeSpan.FromSeconds(2);
        using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeTimeout.CancelAfter(limit);
        try
        {
            using var response = await _http.GetAsync(health, probeTimeout.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return ProbeResult.Ready;
            }

            return response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? ProbeResult.Down
                : ProbeResult.Unusable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProbeResult.TimedOut;
        }
        catch (HttpRequestException)
        {
            return ProbeResult.Down;
        }
    }

    private enum ProbeResult
    {
        Ready,
        Down,
        TimedOut,
        Unusable
    }
}

public sealed class OllamaServeLauncher : IOllamaLauncher
{
    public async Task<IOllamaSession> StartAsync(Uri healthEndpoint, TimeSpan budget, CancellationToken cancellationToken)
    {
        var executable = FindExecutable();
        if (executable is null)
        {
            return OllamaSession.NotRunning("Ollama was not found on PATH or in the per-user Programs folder. SessionPilot does not install it.");
        }

        Process? process = null;
        try
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.StartInfo.Environment["OLLAMA_KEEP_ALIVE"] = "0";
            if (!process.Start())
            {
                process.Dispose();
                return OllamaSession.NotRunning("Ollama serve did not start.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (process is not null)
            {
                OllamaSession.Stop(process);
            }

            return OllamaSession.NotRunning("Ollama serve could not be started. " + exception.Message);
        }

        var ready = await WaitAsync(healthEndpoint, budget, process, cancellationToken).ConfigureAwait(false);
        if (ready)
        {
            return OllamaSession.Started(process);
        }

        OllamaSession.Stop(process);
        return OllamaSession.NotRunning("Ollama was started for this request but did not become ready. The process was stopped.");
    }

    public static string? FindExecutable()
    {
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe")
        };
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                candidates.Add(Path.Combine(directory.Trim(), "ollama.exe"));
            }
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<bool> WaitAsync(Uri healthEndpoint, TimeSpan budget, Process process, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTimeOffset.UtcNow + (budget < TimeSpan.FromSeconds(20) ? budget : TimeSpan.FromSeconds(20));
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                return false;
            }

            try
            {
                using var response = await client.GetAsync(healthEndpoint, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }
}

public sealed class OllamaSession : IOllamaSession
{
    private readonly Process? _process;
    private int _disposed;

    private OllamaSession(OllamaAvailability availability, string? detail, Process? process)
    {
        Availability = availability;
        Detail = detail;
        _process = process;
    }

    public OllamaAvailability Availability { get; }
    public string? Detail { get; }

    public static OllamaSession Existing() => new(OllamaAvailability.AlreadyRunning, null, null);
    public static OllamaSession TimedOut() => new(OllamaAvailability.TimedOut, "Ollama did not answer in time.", null);
    public static OllamaSession Unusable(string detail) => new(OllamaAvailability.Unusable, detail, null);
    public static OllamaSession NotRunning(string detail) => new(OllamaAvailability.NotRunning, detail, null);
    public static OllamaSession Started(Process process) => new(OllamaAvailability.StartedByRequest, null, process);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && _process is not null)
        {
            Stop(_process);
        }

        return ValueTask.CompletedTask;
    }

    public static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }
}
