using System.Net;
using System.Text;
using System.Text.Json;

namespace SessionPilot.Tests;

public class TransactionAndSafetyTests
{
    [Fact]
    public void StaleBaseline_DoesNotWrite()
    {
        using var dir = new TempWorkspace();
        var target = dir.Write("config.ini", "[Custom]\r\nAlpha=one\r\n");
        var original = File.ReadAllBytes(target);
        var result = new TransactionCoordinator().Apply(new ApplyRequest
        {
            TargetPath = target,
            ExpectedBaselineHash = "deadbeef",
            Approved = true,
            JournalDirectory = dir.Journal,
            Edits = [new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" }]
        });
        Assert.Equal("failed", result.Status);
        Assert.Equal(original, File.ReadAllBytes(target));
    }

    [Fact]
    public void LiveCandidatePath_IsRefused()
    {
        using var dir = new TempWorkspace();
        var target = dir.Write("prolasso.ini", "[Custom]\r\nAlpha=one\r\n");
        var original = File.ReadAllBytes(target);
        var result = new TransactionCoordinator().Apply(new ApplyRequest
        {
            TargetPath = target,
            ExpectedBaselineHash = ContentHashing.Sha256(original),
            Approved = true,
            JournalDirectory = dir.Journal,
            LiveCandidatePaths = [target],
            Edits = [new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" }]
        });
        Assert.Equal("refused", result.Status);
        Assert.Equal(original, File.ReadAllBytes(target));
    }

    [Fact]
    public void IsolatedEdit_PreservesUnknownContent_AndRestoresOwnedValues()
    {
        using var dir = new TempWorkspace();
        var target = dir.Write("config.ini", "[Custom]\r\nAlpha=one\r\n[Secret]\r\nKeep=yes\r\n");
        var hash = ContentHashing.Sha256(File.ReadAllBytes(target));
        var applied = new TransactionCoordinator().Apply(new ApplyRequest
        {
            TargetPath = target,
            ExpectedBaselineHash = hash,
            Approved = true,
            JournalDirectory = dir.Journal,
            Edits = [new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" }]
        });
        Assert.Equal("completed-isolated", applied.Status);
        Assert.Equal("not-measured", applied.PerformanceStatus);
        var written = IniDocument.Parse(File.ReadAllBytes(target));
        Assert.Equal("yes", written.Find("Secret", "Keep").Line!.Value);
        var current = written.Apply([new IniEdit { Section = "Custom", Key = "Alpha", Value = "external" }]).Document;
        var analysis = RestorePlanner.Analyze(IniDocument.Parse(File.ReadAllBytes(applied.BackupPath!)), written, current, applied.OwnedValues);
        Assert.Empty(analysis.Restorable);
        Assert.Single(analysis.Conflicts);
    }

    [Fact]
    public void MatchingOwnedValue_IsRestorable()
    {
        var baseline = IniDocument.Parse("[Custom]\r\nAlpha=one\r\n");
        var written = baseline.Apply([new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" }]).Document;
        var analysis = RestorePlanner.Analyze(baseline, written, written, [new OwnedValue { Section = "Custom", Key = "Alpha", BaselineValue = "one", WrittenValue = "two" }]);
        var action = Assert.Single(analysis.Restorable);
        Assert.Equal("one", action.ToValue);
    }

    [Fact]
    public void ReplacerMismatch_IsFailureWithoutClaimingSuccess()
    {
        using var dir = new TempWorkspace();
        var target = dir.Write("config.ini", "[Custom]\r\nAlpha=one\r\n");
        var result = new TransactionCoordinator(new CorruptReplacer()).Apply(new ApplyRequest
        {
            TargetPath = target,
            ExpectedBaselineHash = ContentHashing.Sha256(File.ReadAllBytes(target)),
            Approved = true,
            JournalDirectory = dir.Journal,
            Edits = [new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" }]
        });
        Assert.Equal("failed", result.Status);
        Assert.Equal("verify-failed", result.PersistenceStatus);
        Assert.Equal("not-verified", result.GovernorStatus);
    }

    [Fact]
    public void InterruptedJournal_IsReportedAndNotRolledBack()
    {
        using var dir = new TempWorkspace();
        var target = dir.Write("config.ini", "[Custom]\r\nAlpha=changed\r\n");
        var record = new JournalRecord
        {
            SchemaVersion = 1,
            Id = "interrupted",
            TargetPath = target,
            State = "Replacing",
            BaselineHash = "abc",
            IntendedHash = "def"
        };
        File.WriteAllText(Path.Combine(dir.Journal, "interrupted.json"), JsonSerializer.Serialize(record));
        var found = JournalRecovery.FindIncomplete(dir.Journal);
        Assert.Single(found);
        var description = JournalRecovery.Describe(found[0]);
        Assert.Contains("neither", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alpha=changed", File.ReadAllText(target), StringComparison.Ordinal);
    }

    [Fact]
    public void UnapprovedPlan_DoesNotWrite()
    {
        using var dir = new TempWorkspace();
        var target = dir.Write("config.ini", "[Custom]\r\nAlpha=one\r\n");
        var result = new TransactionCoordinator().Apply(new ApplyRequest
        {
            TargetPath = target,
            ExpectedBaselineHash = ContentHashing.Sha256(File.ReadAllBytes(target)),
            Approved = false,
            JournalDirectory = dir.Journal,
            Edits = [new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" }]
        });
        Assert.Equal("refused", result.Status);
        Assert.Contains("Alpha=one", File.ReadAllText(target), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidTargetPath_IsRefused_AndCreatesNothing()
    {
        var root = Path.Combine(Path.GetTempPath(), "sessionpilot-invalid-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var journal = Path.Combine(root, "journals");
            var result = new TransactionCoordinator().Apply(new ApplyRequest
            {
                TargetPath = Path.Combine(root, "config.ini") + "\0",
                ExpectedBaselineHash = "abc",
                Approved = true,
                JournalDirectory = journal,
                Edits = [new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" }]
            });
            Assert.Equal("refused", result.Status);
            Assert.Equal("The target path is not valid.", result.Message);
            Assert.False(Directory.Exists(journal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void UnapprovedRequest_DoesNotCreateTheJournalDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "sessionpilot-unapproved-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var target = Path.Combine(root, "config.ini");
            File.WriteAllText(target, "[Custom]\r\nAlpha=one\r\n");
            var journal = Path.Combine(root, "journals");
            var result = new TransactionCoordinator().Apply(new ApplyRequest
            {
                TargetPath = target,
                ExpectedBaselineHash = ContentHashing.Sha256(File.ReadAllBytes(target)),
                Approved = false,
                JournalDirectory = journal,
                Edits = [new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" }]
            });
            Assert.Equal("refused", result.Status);
            Assert.False(Directory.Exists(journal));
            Assert.Contains("Alpha=one", File.ReadAllText(target), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void MalformedLiveCandidate_DoesNotThrow()
    {
        using var dir = new TempWorkspace();
        var target = dir.Write("config.ini", "[Custom]\r\nAlpha=one\r\n");
        var result = new TransactionCoordinator().Apply(new ApplyRequest
        {
            TargetPath = target,
            ExpectedBaselineHash = ContentHashing.Sha256(File.ReadAllBytes(target)),
            Approved = true,
            JournalDirectory = dir.Journal,
            LiveCandidatePaths = ["bad\0path"],
            Edits = [new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" }]
        });
        Assert.Equal("completed-isolated", result.Status);
    }

    [Fact]
    public void ReadOnlyTarget_FailsWithoutLeavingATempFile()
    {
        using var dir = new TempWorkspace();
        var target = dir.Write("config.ini", "[Custom]\r\nAlpha=one\r\n");
        File.SetAttributes(target, FileAttributes.ReadOnly);
        try
        {
            var result = new TransactionCoordinator().Apply(new ApplyRequest
            {
                TargetPath = target,
                ExpectedBaselineHash = ContentHashing.Sha256(File.ReadAllBytes(target)),
                Approved = true,
                JournalDirectory = dir.Journal,
                Edits = [new IniEdit { Section = "Custom", Key = "Alpha", Value = "two" }]
            });
            Assert.Equal("failed", result.Status);
            Assert.Contains("read-only", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, ".sessionpilot-*.tmp"));
            Assert.Contains("Alpha=one", File.ReadAllText(target), StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1e30")]
    [InlineData("\"1\"")]
    public void NonIntegerSchemaVersion_FailsWithoutThrowing(string version)
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        var json = "{\"schemaVersion\":" + version + ",\"loadoutId\":\"balanced\",\"objective\":\"restore\",\"sessionMode\":\"temporary\",\"powerPreference\":\"balanced\",\"backgroundPolicy\":\"preserve\",\"requestedApplications\":[]}";
        var interpretation = IntentValidator.ValidateJson(json, catalog);
        Assert.False(interpretation.Success);
        Assert.Contains("schemaVersion", interpretation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelOutput_WithAShellField_IsRejected()
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        var payload = """
            {"schemaVersion":1,"loadoutId":"balanced","objective":"restore","sessionMode":"temporary","powerPreference":"balanced","backgroundPolicy":"preserve","requestedApplications":[],"shell":"powershell"}
            """;
        var interpretation = IntentValidator.ValidateJson(payload, catalog);
        Assert.False(interpretation.Success);
        var remote = await new OllamaIntentClient(new HttpClient(new FixedHandler(Wrap(payload)))).InterpretAsync(
            new OllamaRequest { Endpoint = "http://127.0.0.1:11434", Model = "example", Prompt = "restore" },
            catalog,
            CancellationToken.None);
        Assert.False(remote.Success);
    }

    [Fact]
    public void UnknownLoadout_IsRejected()
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        var interpretation = IntentValidator.ValidateJson(
            """{"schemaVersion":1,"loadoutId":"invented-by-model","objective":"restore","sessionMode":"temporary","powerPreference":"balanced","backgroundPolicy":"preserve","requestedApplications":[]}""",
            catalog);
        Assert.False(interpretation.Success);
        Assert.Contains("Unknown loadout", interpretation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderTimeout_IsReported()
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        var client = new OllamaIntentClient(new HttpClient(new HangingHandler()));
        var result = await client.InterpretAsync(
            new OllamaRequest { Endpoint = "http://127.0.0.1:11434", Model = "example", Prompt = "games", Timeout = TimeSpan.FromMilliseconds(50) },
            catalog,
            CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("timed out", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoteEndpoint_IsRejected()
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        var client = new OllamaIntentClient(new HttpClient(new FixedHandler("{}")));
        var result = await client.InterpretAsync(
            new OllamaRequest { Endpoint = "http://example.com", Model = "example", Prompt = "games" },
            catalog,
            CancellationToken.None);
        Assert.Contains("local", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    private static string Wrap(string content) =>
        JsonSerializer.Serialize(new { message = new { content } });

    private sealed class FixedHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class CorruptReplacer : IFileReplacer
    {
        public void Replace(string targetPath, byte[] contents) => File.WriteAllBytes(targetPath, Encoding.UTF8.GetBytes("corrupt"));
    }

    private sealed class TempWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "sessionpilot-" + Guid.NewGuid().ToString("n"));
        public string Journal { get; }

        public TempWorkspace()
        {
            Directory.CreateDirectory(_root);
            Journal = Path.Combine(_root, "journals");
            Directory.CreateDirectory(Journal);
        }

        public string Write(string name, string text)
        {
            var path = Path.Combine(_root, name);
            File.WriteAllText(path, text);
            return path;
        }

        public void Dispose() => Directory.Delete(_root, true);
    }
}
