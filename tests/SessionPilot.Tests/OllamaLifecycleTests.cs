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

        public Task<IOllamaSession> AcquireAsync(Uri chatEndpoint, TimeSpan budget, CancellationToken cancellationToken) =>
            Task.FromResult<IOllamaSession>(new ScriptedSession(this, availability, detail));

        private sealed class ScriptedSession(ScriptedHost host, OllamaAvailability availability, string? detail) : IOllamaSession
        {
            public OllamaAvailability Availability { get; } = availability;
            public string? Detail { get; } = detail;

            public ValueTask DisposeAsync()
            {
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
}
