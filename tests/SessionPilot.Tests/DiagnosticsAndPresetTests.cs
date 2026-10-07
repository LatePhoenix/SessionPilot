namespace SessionPilot.Tests;

public class DiagnosticsAndPresetTests
{
    [Fact]
    public void CpuFraction_UsesLogicalCapacityAndCoreEquivalents()
    {
        var observation = ProcessorTimeSeries.Observe(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            logicalProcessorCount: 8,
            collectorDuration: TimeSpan.FromMilliseconds(4));

        Assert.Equal(0.125, observation.FractionOfLogicalCapacity);
        Assert.Equal(1, observation.LogicalCoreEquivalents);
        Assert.Equal(TimeSpan.FromMilliseconds(4), observation.CollectorDuration);
    }

    [Fact]
    public void MissingCpuCounter_IsNullRatherThanZero()
    {
        var observation = ProcessorTimeSeries.Observe(null, null, TimeSpan.FromSeconds(2), 16, TimeSpan.Zero);

        Assert.Null(observation.FractionOfLogicalCapacity);
        Assert.Null(observation.LogicalCoreEquivalents);
    }

    [Fact]
    public void UnknownLogicalCount_DoesNotInventAFraction()
    {
        var fraction = CpuMath.FractionOfLogicalCapacity(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 0);
        Assert.Null(fraction);
    }

    [Fact]
    public void WorkingSet_IsNotMemoryPressure_AndGpuUnavailableIsNotZero()
    {
        var memory = MemoryReadings.FromWorkingSet(50_000_000);
        var missing = MemoryReadings.FromWorkingSet(null);
        var gpu = GpuReadings.Unavailable();

        Assert.False(memory.IsMemoryPressure);
        Assert.Equal(50_000_000, memory.WorkingSetBytes);
        Assert.Null(missing.WorkingSetBytes);
        Assert.False(missing.IsMemoryPressure);
        Assert.False(gpu.Available);
        Assert.Null(gpu.EngineUtilization);
    }

    [Fact]
    public void PidReuse_DoesNotMatchTheApprovedProcess()
    {
        var created = DateTimeOffset.Parse("2026-10-07T12:00:00Z");
        var reused = created.AddMinutes(5);

        Assert.True(ProcessIdentity.IsSame(42, created, 42, created));
        Assert.True(ProcessIdentity.IsPidReuse(42, created, 42, reused));
        Assert.False(ProcessIdentity.IsSame(42, created, 42, reused));
    }

    [Fact]
    public void AccessDeniedRow_StaysExplicit()
    {
        var entry = ProcessInventory.FromRow(new ProcessQueryRow
        {
            ProcessId = 7,
            Name = "secret",
            ExecutablePath = @"C:\Users\someone\secret.exe",
            CreationTime = DateTimeOffset.UnixEpoch,
            AccessDenied = true
        });

        Assert.Equal("access-denied", entry.Access);
        Assert.Null(entry.ExecutablePath);
    }

    [Theory]
    [InlineData("python.exe")]
    [InlineData("node.exe")]
    [InlineData("chrome.exe")]
    [InlineData("powershell.exe")]
    [InlineData("windowsterminal.exe")]
    public void RuntimesBrowsersAndTerminals_AreNotBlanketTargets(string name)
    {
        var preview = CleanupClassifier.Classify(name, protectedParticipant: false, cpuFractionOfCapacity: 0.95);

        Assert.Equal(CleanupClassification.Unknown, preview.Classification);
        Assert.False(preview.CloseAuthorized);
    }

    [Fact]
    public void Editors_AreUnsavedWork_AndSystemProcessesStayExcluded()
    {
        var editor = CleanupClassifier.Classify("code.exe", false, 0.99);
        var system = CleanupClassifier.Classify("lsass.exe", false, 0.99);
        var participant = CleanupClassifier.Classify("vrchat.exe", true, 0.99);

        Assert.Equal(CleanupClassification.PossibleUnsavedWork, editor.Classification);
        Assert.Equal(CleanupClassification.SystemOrSecurity, system.Classification);
        Assert.Equal(CleanupClassification.ProtectedParticipant, participant.Classification);
        Assert.False(editor.CloseAuthorized);
        Assert.False(system.CloseAuthorized);
        Assert.False(participant.CloseAuthorized);
    }

    [Fact]
    public void SampleWindow_StopsAtCapacityAndWhenClosed()
    {
        var window = new RollingSampleWindow<int>(capacity: 30);
        for (var i = 0; i < 40; i++)
        {
            if (window.ShouldTakeSample(i))
            {
                Assert.True(window.TryAdd(i));
            }
        }

        Assert.Equal(30, window.Count);
        window.Close();
        Assert.False(window.ShouldTakeSample(0));
        Assert.False(window.TryAdd(99));
    }

    [Fact]
    public void SteamVrSentence_StillSelectsSteamVrLoadout()
    {
        var interpretation = DeterministicInterpreter.Interpret("I'm going to play VRChat via SteamVR. Favor frame-time consistency.");
        Assert.Equal("vrchat-steamvr", interpretation.Intent!.LoadoutId);
    }

    [Theory]
    [InlineData("VRChat through Virtual Desktop", "vrchat-social")]
    [InlineData("crowded VRChat instance", "vrchat-diagnostic")]
    [InlineData("run a VRChat diagnostic", "vrchat-diagnostic")]
    public void NewVrPhrases_SelectTheMatchingLoadout(string text, string loadoutId)
    {
        var interpretation = DeterministicInterpreter.Interpret(text);
        Assert.True(interpretation.Success);
        Assert.Equal(loadoutId, interpretation.Intent!.LoadoutId);
    }

    [Fact]
    public void NewPresets_LoadBesideSteamVr_WithoutFileWrites()
    {
        var catalog = LoadoutCatalog.Load(Path.Combine(AppContext.BaseDirectory, "presets"));
        Assert.True(catalog.TryGet("vrchat-social", out var social));
        Assert.True(catalog.TryGet("vrchat-diagnostic", out var diagnostic));
        Assert.True(catalog.TryGet("vrchat-steamvr", out _));
        Assert.Equal(Names.Unchanged, social.PriorityPolicy);
        Assert.Equal(Names.Preserve, diagnostic.ProBalancePolicy);
        Assert.All(GuidedWorkflows.All, workflow => Assert.False(workflow.WritesFiles));
        Assert.Contains(GuidedWorkflows.VrChat.Steps, step => step.Title == "Avatar rank");
        Assert.Contains(GuidedWorkflows.VrChat.Steps, step => step.Title == "Mirrors");
        Assert.DoesNotContain("steamvr.vrsettings", GuidedWorkflows.SteamVr.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FirstPresentConfig_IsNotTheActiveConfiguration()
    {
        var installation = InstallationCandidates.Assemble(
            [
                new ConfigProbe { Label = @"ProgramData\ProcessLasso\config\prolasso.ini", Status = "present", ByteLength = 10, Detail = "present" }
            ],
            [
                new BinaryProbe { Role = "gui", Label = @"Program Files\Process Lasso\ProcessLasso.exe", Status = "missing" }
            ]);

        Assert.Null(installation.ConfirmedConfigPath);
        Assert.Equal(DiscoveryConfidence.Low, installation.Confidence);
        Assert.Contains(InstallationCandidates.NotActiveLimitation, installation.Limitations);
    }

    [Fact]
    public void MissingAndDeniedCandidates_StayExplicit()
    {
        var installation = InstallationCandidates.Assemble(
            [
                new ConfigProbe { Label = "missing-candidate", Status = "missing", Detail = "missing" },
                new ConfigProbe { Label = "denied-candidate", Status = "access-denied", Detail = "access-denied" }
            ],
            []);

        Assert.Contains("missing-candidate", installation.EmptyLocations);
        Assert.Contains(installation.Limitations, note => note.Contains("access-denied", StringComparison.Ordinal));
        Assert.Null(installation.ConfirmedConfigPath);
    }

    [Fact]
    public void CheckReport_RedactsPaths_AndDoesNotSample()
    {
        var report = CheckReport.Format(new CheckSnapshot
        {
            Notes = [@"See C:\Users\someone\secret.ini before acting."]
        });

        Assert.Contains("Sampling loop: not started", report, StringComparison.Ordinal);
        Assert.Contains("not-measured", report, StringComparison.Ordinal);
        Assert.Contains("[path]", report, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", report, StringComparison.Ordinal);
        Assert.DoesNotContain("secret.ini", report, StringComparison.Ordinal);
        Assert.False(LiveApplyPolicy.Enabled);
    }

    [Fact]
    public void FailureText_RedactsTheDialog_AndKeepsTheCheckOnOneLine()
    {
        var dialog = FailureText.ForDialog(@"Cannot read C:\Users\someone\secret.ini");
        Assert.Contains("[path]", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", dialog, StringComparison.Ordinal);
        Assert.EndsWith("Nothing was written.", dialog, StringComparison.Ordinal);

        var line = FailureText.ForCheck("failed\r\nC:\\Users\\someone\\secret.ini");
        Assert.DoesNotContain("\n", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", line, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", line, StringComparison.Ordinal);
        Assert.Contains("[path]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidUserLoadoutJson_IsSkipped()
    {
        var user = NewUserDirectory();
        try
        {
            File.WriteAllText(Path.Combine(user, "broken.json"), "{");
            var catalog = LoadoutCatalog.Load(Presets(), user);
            Assert.True(catalog.TryGet("balanced", out _));
            Assert.Equal("broken.json: The file is not valid JSON.", Assert.Single(catalog.LoadErrors));
            Assert.DoesNotContain(catalog.LoadErrors, error => error.Contains(user, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(user, true);
        }
    }

    [Fact]
    public void UserLoadout_WithAHighPriorityPolicy_IsSkipped()
    {
        var user = NewUserDirectory();
        try
        {
            File.WriteAllText(Path.Combine(user, "custom.json"), LoadoutJson("custom", "high"));
            var catalog = LoadoutCatalog.Load(Presets(), user);
            Assert.True(catalog.TryGet("balanced", out _));
            Assert.False(catalog.TryGet("custom", out _));
            var error = Assert.Single(catalog.LoadErrors);
            Assert.StartsWith("custom.json: ", error, StringComparison.Ordinal);
            Assert.Contains("priorityPolicy", error, StringComparison.Ordinal);
            Assert.DoesNotContain(user, error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(user, true);
        }
    }

    [Fact]
    public void UserLoadout_ThatCollidesWithBalanced_IsSkipped()
    {
        var user = NewUserDirectory();
        try
        {
            File.WriteAllText(Path.Combine(user, "copy.json"), LoadoutJson("balanced", "unchanged"));
            var catalog = LoadoutCatalog.Load(Presets(), user);
            Assert.True(catalog.TryGet("balanced", out var balanced));
            Assert.True(balanced.BuiltIn);
            var error = Assert.Single(catalog.LoadErrors);
            Assert.StartsWith("copy.json: ", error, StringComparison.Ordinal);
            Assert.Contains("collides with an existing id", error, StringComparison.Ordinal);
            Assert.DoesNotContain(user, error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(user, true);
        }
    }

    [Fact]
    public void BrokenBuiltInLoadout_StillThrows()
    {
        var builtIn = NewUserDirectory();
        try
        {
            File.WriteAllText(Path.Combine(builtIn, "balanced.json"), "{");
            Assert.Throws<InvalidDataException>(() => LoadoutCatalog.Load(builtIn));
        }
        finally
        {
            Directory.Delete(builtIn, true);
        }
    }

    private static string Presets() => Path.Combine(AppContext.BaseDirectory, "presets");

    private static string NewUserDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sessionpilot-loadouts-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string LoadoutJson(string id, string priorityPolicy) =>
        $$"""
        {
          "schemaVersion": 1,
          "id": "{{id}}",
          "displayName": "Example",
          "summary": "Example",
          "objective": "restore",
          "sessionMode": "temporary",
          "powerPreference": "balanced",
          "backgroundPolicy": "preserve",
          "offerPerformanceMode": false,
          "offerEfficiencyModeOff": false,
          "priorityPolicy": "{{priorityPolicy}}",
          "cpuPlacementPolicy": "unchanged",
          "proBalancePolicy": "preserve",
          "requiresExplicitWorkers": false,
          "participantRoles": [],
          "notes": []
        }
        """;
}
