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
            ProbeResult.Unusable => OllamaSession.Unusable("Something is listening on the Ollama port but did not answer a version check. Nothing was started."),
            _ => _launcher is null
                ? OllamaSession.NotRunning("Ollama is not running.")
                : await _launcher.StartAsync(health, budget, cancellationToken).ConfigureAwait(false)
        };
    }

    // Windows takes about 2 seconds to report a refused loopback connection, so a shorter limit
    // would read "nothing is listening" as "timed out" and never start Ollama.
    internal static readonly TimeSpan ProbeLimit = TimeSpan.FromSeconds(5);

    private async Task<ProbeResult> ProbeAsync(Uri health, TimeSpan budget, CancellationToken cancellationToken)
    {
        var limit = budget < ProbeLimit ? budget : ProbeLimit;
        using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeTimeout.CancelAfter(limit);
        try
        {
            using var response = await _http.GetAsync(health, probeTimeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? ProbeResult.Ready : ProbeResult.Unusable;
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

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var waitBudget = budget < TimeSpan.FromSeconds(20) ? budget : TimeSpan.FromSeconds(20);
        var ready = await CompleteStartAsync(
            async token =>
            {
                try
                {
                    using var response = await client.GetAsync(healthEndpoint, token).ConfigureAwait(false);
                    return response.IsSuccessStatusCode;
                }
                catch (HttpRequestException)
                {
                    return false;
                }
            },
            () => process.HasExited,
            () => OllamaSession.Stop(process),
            waitBudget,
            cancellationToken).ConfigureAwait(false);
        return ready
            ? OllamaSession.Started(process)
            : OllamaSession.NotRunning("Ollama was started for this request but did not become ready. The process was stopped.");
    }

    public static string? FindExecutable() =>
        ExecutableCandidates(
            Environment.GetEnvironmentVariable("PATH"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"))
        .FirstOrDefault(File.Exists);

    internal static IEnumerable<string> ExecutableCandidates(string? pathVariable, string programsDirectory)
    {
        yield return Path.Combine(programsDirectory, "Ollama", "ollama.exe");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            yield break;
        }

        foreach (var entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = entry.Trim().Trim('"');
            if (directory.Length == 0 || !Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            yield return Path.Combine(directory, "ollama.exe");
        }
    }

    internal static async Task<bool> WaitForReadyAsync(Func<CancellationToken, Task<bool>> probe, Func<bool> hasExited, TimeSpan budget, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hasExited())
            {
                return false;
            }

            try
            {
                if (await probe(cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    internal static async Task<bool> CompleteStartAsync(Func<CancellationToken, Task<bool>> probe, Func<bool> hasExited, Action stop, TimeSpan budget, CancellationToken cancellationToken)
    {
        try
        {
            var ready = await WaitForReadyAsync(probe, hasExited, budget, cancellationToken).ConfigureAwait(false);
            if (!ready)
            {
                stop();
            }

            return ready;
        }
        catch (Exception)
        {
            stop();
            throw;
        }
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
