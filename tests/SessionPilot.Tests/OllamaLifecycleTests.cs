using System.Net;
using System.Text;

namespace SessionPilot.Tests;

public class OllamaLifecycleTests
{
    [Fact]
    public async Task Request_UnloadsTheModel_AndStopsOnlyAServerThisRequestStarted()
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        var host = new ScriptedHost(OllamaAvailability.StartedByRequest);
        var handler = new CaptureHandler(ValidBody());
        var client = new OllamaIntentClient(new HttpClient(handler), host);

        var result = await client.InterpretAsync(Request(), catalog, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("\"keep_alive\":0", handler.Body, StringComparison.Ordinal);
        Assert.Equal(1, host.Stops);
    }

    [Fact]
    public async Task ExistingServer_IsLeftRunning()
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        var host = new ScriptedHost(OllamaAvailability.AlreadyRunning);
        var client = new OllamaIntentClient(new HttpClient(new CaptureHandler(ValidBody())), host);

        var result = await client.InterpretAsync(Request(), catalog, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, host.Stops);
    }

    [Fact]
    public async Task UnavailableServer_DoesNotCallTheModel()
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        var handler = new CaptureHandler("{}");
        var client = new OllamaIntentClient(new HttpClient(handler), new ScriptedHost(OllamaAvailability.NotRunning, "Ollama is not running."));

        var result = await client.InterpretAsync(Request(), catalog, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, handler.Calls);
        Assert.Contains("not running", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("http://localhost:11434", true)]
    [InlineData("https://127.0.0.1", false)]
    [InlineData("http://192.168.1.2", false)]
    [InlineData("http://127.0.0.1.example.test", false)]
    public async Task LoopbackEndpoints_AreTheOnlyOnesAccepted(string endpoint, bool accepted)
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        var client = new OllamaIntentClient(new HttpClient(new CaptureHandler(ValidBody())), new ScriptedHost(OllamaAvailability.AlreadyRunning));
        var result = await client.InterpretAsync(new OllamaRequest { Endpoint = endpoint, Model = "example", Prompt = "restore the balanced desktop" }, catalog, CancellationToken.None);
        if (accepted)
        {
            Assert.True(result.Success);
        }
        else
        {
            Assert.False(result.Success);
            Assert.Contains("local", result.Explanation, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task NotFoundOnTheOllamaPort_DoesNotLaunch()
    {
        var launcher = new CountingLauncher();
        var host = new ProbingOllamaHost(new HttpClient(new StatusHandler(HttpStatusCode.NotFound)), launcher);
        var session = await host.AcquireAsync(new Uri("http://127.0.0.1:11434/api/chat"), TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.Equal(OllamaAvailability.Unusable, session.Availability);
        Assert.Equal("Something is listening on the Ollama port but did not answer a version check. Nothing was started.", session.Detail);
        Assert.Equal(0, launcher.Calls);
    }

    [Fact]
    public async Task ConnectionRefused_Launches()
    {
        var launcher = new CountingLauncher();
        var host = new ProbingOllamaHost(new HttpClient(new RefusedHandler()), launcher);
        await host.AcquireAsync(new Uri("http://127.0.0.1:11434/api/chat"), TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.Equal(1, launcher.Calls);
    }

    [Fact]
    public async Task ReadyVersionCheck_DoesNotLaunch()
    {
        var launcher = new CountingLauncher();
        var host = new ProbingOllamaHost(new HttpClient(new StatusHandler(HttpStatusCode.OK)), launcher);
        var session = await host.AcquireAsync(new Uri("http://127.0.0.1:11434/api/chat"), TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.Equal(OllamaAvailability.AlreadyRunning, session.Availability);
        Assert.Equal(0, launcher.Calls);
    }

    [Fact]
    public void RequestBudgets_DefaultSeparately()
    {
        var request = Request();
        Assert.Equal(TimeSpan.FromSeconds(20), request.StartupBudget);
        Assert.Equal(TimeSpan.FromSeconds(60), request.RequestTimeout);
    }

    [Fact]
    public async Task TimedOutChat_ReportsTimeout_AndDisposesTheSession()
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        var host = new ScriptedHost(OllamaAvailability.AlreadyRunning);
        var client = new OllamaIntentClient(new HttpClient(new HangingHandler()), host);
        var request = Request() with { RequestTimeout = TimeSpan.FromMilliseconds(50) };
        var result = await client.InterpretAsync(request, catalog, CancellationToken.None);
        Assert.Contains("timed out", result.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, host.Disposals);
    }

    [Fact]
    public void RelativePathEntries_AreSkipped()
    {
        var path = string.Join(Path.PathSeparator, ["ollama", @" .\local ", "\"C:\\Tools\\Ollama\""]);
        var candidates = OllamaServeLauncher.ExecutableCandidates(path, @"C:\Users\example\AppData\Local\Programs").ToList();
        Assert.Equal(@"C:\Users\example\AppData\Local\Programs\Ollama\ollama.exe", candidates[0]);
        Assert.Contains(@"C:\Tools\Ollama\ollama.exe", candidates);
        Assert.DoesNotContain(@"ollama\ollama.exe", candidates);
        Assert.DoesNotContain(@".\local\ollama.exe", candidates);
    }

    [Fact]
    public async Task ProbeTimeout_KeepsPollingUntilReady()
    {
        var calls = 0;
        var ready = await OllamaServeLauncher.WaitForReadyAsync(
            _ =>
            {
                calls++;
                if (calls < 3)
                {
                    throw new TaskCanceledException();
                }

                return Task.FromResult(true);
            },
            () => false,
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        Assert.True(ready);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task CallerCancellation_StopsAndPropagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var stopped = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OllamaServeLauncher.CompleteStartAsync(
            _ => Task.FromResult(false),
            () => false,
            () => stopped++,
            TimeSpan.FromSeconds(2),
            cts.Token));
        Assert.Equal(1, stopped);
    }

    [Fact]
    public async Task WaitFailure_StopsTheStartedProcess()
    {
        var stopped = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => OllamaServeLauncher.CompleteStartAsync(
            _ => throw new InvalidOperationException("boom"),
            () => false,
            () => stopped++,
            TimeSpan.FromSeconds(2),
            CancellationToken.None));
        Assert.Equal(1, stopped);
    }

    private static OllamaRequest Request() => new()
    {
        Endpoint = "http://127.0.0.1:11434",
        Model = "example",
        Prompt = "restore the balanced desktop"
    };

    private static string ValidBody() =>
        """
        {"message":{"content":"{\"schemaVersion\":1,\"loadoutId\":\"balanced\",\"objective\":\"restore\",\"sessionMode\":\"temporary\",\"powerPreference\":\"balanced\",\"backgroundPolicy\":\"preserve\",\"requestedApplications\":[]}"}}
        """;

    private sealed class ScriptedHost(OllamaAvailability availability, string? detail = null) : IOllamaHost
    {
        public int Stops { get; private set; }
        public int Disposals { get; private set; }

        public Task<IOllamaSession> AcquireAsync(Uri chatEndpoint, TimeSpan budget, CancellationToken cancellationToken) =>
            Task.FromResult<IOllamaSession>(new ScriptedSession(this, availability, detail));

        private sealed class ScriptedSession(ScriptedHost host, OllamaAvailability availability, string? detail) : IOllamaSession
        {
            public OllamaAvailability Availability { get; } = availability;
            public string? Detail { get; } = detail;

            public ValueTask DisposeAsync()
            {
                host.Disposals++;
                if (Availability == OllamaAvailability.StartedByRequest)
                {
                    host.Stops++;
                }

                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class CaptureHandler(string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class RefusedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused.");
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class CountingLauncher : IOllamaLauncher
    {
        public int Calls { get; private set; }

        public Task<IOllamaSession> StartAsync(Uri healthEndpoint, TimeSpan budget, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IOllamaSession>(new IdleSession());
        }

        private sealed class IdleSession : IOllamaSession
        {
            public OllamaAvailability Availability => OllamaAvailability.NotRunning;
            public string? Detail => "not started";
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
