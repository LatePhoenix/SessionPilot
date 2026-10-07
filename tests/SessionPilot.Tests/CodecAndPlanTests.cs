namespace SessionPilot.Tests;

public class CodecAndPlanTests
{
    [Fact]
    public void HistoricalWatchdogExample_IsNotExecutable()
    {
        var capabilities = CapabilityAssessor.Assess("18.4.0.48", []);
        var watchdog = Assert.Single(capabilities.Items, item => item.Id == "watchdog");
        Assert.Equal(SupportStatus.Unsupported, watchdog.Status);
        Assert.False(watchdog.LiveWriteEnabled);
        Assert.Contains("metric value 2", watchdog.Evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultPriorities_ParsesBothDocumentedShapes_AndDoesNotSerialize()
    {
        var semicolon = HistoricalRuleParsers.ParseDefaultPriorities("notepad.exe,above normal;calc.exe,above normal");
        var commas = HistoricalRuleParsers.ParseDefaultPriorities("taskmgr.exe, high,game.exe,idle");
        Assert.True(semicolon.Parsed);
        Assert.True(commas.Parsed);
        Assert.False(HistoricalRuleParsers.SerializationAllowed);
        Assert.True(HistoricalRuleParsers.IsForbiddenPriority("Real time"));
        Assert.True(HistoricalRuleParsers.IsForbiddenPriority("High"));
    }

    [Theory]
    [InlineData("OocExclusions", "taskmgr.exe,game*.exe", true)]
    [InlineData("OocExclusions", "game.exe;notepad.exe", false)]
    public void ExclusionParser_HonorsTheDocumentedCommaList(string _, string value, bool parsed)
    {
        var result = HistoricalRuleParsers.ParseExclusions(value);
        Assert.Equal(parsed, result.Parsed);
    }

    [Fact]
    public void GamingModeSection_IsNotPerformanceMode()
    {
        var keys = new[]
        {
            new IniKeyObservation { Section = "GamingMode", Key = "GamingModeEnabled", ValueEmpty = false, Kind = ValueKind.Boolean, ValueLength = 4 },
            new IniKeyObservation { Section = "ProcessAllowances", Key = "EfficiencyMode", ValueEmpty = true, Kind = ValueKind.Empty },
            new IniKeyObservation { Section = "ProcessDefaults", Key = "DefaultPriorities", ValueEmpty = true, Kind = ValueKind.Empty },
            new IniKeyObservation { Section = "OutOfControlProcessRestraint", Key = "OocExclusions", ValueEmpty = true, Kind = ValueKind.Empty },
            new IniKeyObservation { Section = "ProcessDefaults", Key = "CPUSets", ValueEmpty = true, Kind = ValueKind.Empty },
            new IniKeyObservation { Section = "ProcessDefaults", Key = "DefaultAffinitiesEx", ValueEmpty = true, Kind = ValueKind.Empty }
        };
        var capabilities = CapabilityAssessor.Assess("18.4.0.48", keys);
        Assert.All(capabilities.Items, item => Assert.False(item.LiveWriteEnabled));
        Assert.Contains("not treated as this field", capabilities.Items.Single(item => item.Id == "performance-mode").Evidence, StringComparison.OrdinalIgnoreCase);
        Assert.False(LiveApplyPolicy.Enabled);
    }

    [Fact]
    public void VrChatSentence_CompilesADryRunWithoutWritableEdits()
    {
        var catalog = LoadCatalog();
        var interpretation = DeterministicInterpreter.Interpret("I'm going to play VRChat via SteamVR. Favor frame-time consistency.");
        Assert.True(interpretation.Success);
        Assert.Equal("vrchat-steamvr", interpretation.Intent!.LoadoutId);
        Assert.Equal("frame-time-consistency", interpretation.Intent.Objective);
        Assert.Contains("VRChat", interpretation.Intent.RequestedApplications);
        Assert.Contains("SteamVR", interpretation.Intent.RequestedApplications);
        var plan = PlanCompiler.Compile(new CompileInput
        {
            Intent = interpretation.Intent,
            Loadout = catalog.All.Single(item => item.Id == "vrchat-steamvr"),
            Capabilities = CapabilityAssessor.Assess("18.4.0.48", []),
            Hardware = AmbiguousHardware()
        });
        Assert.DoesNotContain(plan.Changes, change => change.Writable);
        Assert.Contains(plan.Stages, stage => stage.Stage == "Performance effect measured" && stage.Status == "not-measured");
        Assert.Contains(plan.Changes, change => change.TargetIdentity == "ProBalance" && change.ProposedValue == "Preserve");
    }

    [Fact]
    public void ConfirmedGame_PreviewsPerformanceMode_AndLeavesPriorityAlone()
    {
        var catalog = LoadCatalog();
        var loadout = catalog.All.Single(item => item.Id == "desktop-gaming");
        var plan = PlanCompiler.Compile(new CompileInput
        {
            Intent = new UserIntent
            {
                LoadoutId = loadout.Id,
                Objective = loadout.Objective,
                SessionMode = loadout.SessionMode,
                PowerPreference = loadout.PowerPreference,
                BackgroundPolicy = loadout.BackgroundPolicy,
                RequestedApplications = ["Demo Game"]
            },
            Loadout = loadout,
            Applications =
            [
                new ApplicationIdentity
                {
                    DisplayName = "Demo Game",
                    ExecutableName = "demogame.exe",
                    FullPath = @"C:\Games\demogame.exe",
                    Confidence = IdentityConfidence.Confirmed,
                    Role = ApplicationRole.Game
                }
            ]
        });
        Assert.Contains("performanceMode", plan.ManualImportPreviewJson, StringComparison.Ordinal);
        Assert.Contains("Not an export", plan.ManualImportPreviewJson, StringComparison.Ordinal);
        Assert.DoesNotContain("priorityClass", plan.ManualImportPreviewJson, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Changes, change => change.Writable);
    }

    [Fact]
    public void ExecutableCollision_IsVisible_AndIsNotASelector()
    {
        var identities = ApplicationMatcher.MarkCollisions(
        [
            new ApplicationIdentity { DisplayName = "Notes", ExecutableName = "notepad.exe", FullPath = @"C:\Windows\notepad.exe", Confidence = IdentityConfidence.Observed },
            new ApplicationIdentity { DisplayName = "Notes copy", ExecutableName = "notepad.exe", FullPath = @"D:\Tools\notepad.exe", Confidence = IdentityConfidence.Observed }
        ]);
        Assert.All(identities, identity => Assert.True(identity.Collision));
        Assert.Null(ApplicationMatcher.Select("notepad", identities));
    }

    [Fact]
    public void ProtectedWorker_IsBlocked()
    {
        var catalog = LoadCatalog();
        var loadout = catalog.All.Single(item => item.Id == "background-batch");
        var plan = PlanCompiler.Compile(new CompileInput
        {
            Intent = IntentFor(loadout),
            Loadout = loadout,
            Workers = [new WorkerRequest { ExecutableName = "audiodg.exe", Treatment = "exclude-from-probalance", Justification = "I want the audio engine excluded" }]
        });
        var change = plan.Changes.Single(item => item.TargetIdentity == "audiodg.exe");
        Assert.Equal(SupportStatus.BlockedByPolicy, change.SupportStatus);
        Assert.False(change.Writable);
    }

    [Theory]
    [InlineData("compiling a large solution", "development-build-heavy")]
    [InlineData("interactive development in Visual Studio", "development-interactive")]
    [InlineData("running a local AI model", "development-local-ai")]
    [InlineData("watching a movie", "media-playback")]
    [InlineData("restore the balanced desktop", "balanced")]
    [InlineData("playing games tonight", "desktop-gaming")]
    [InlineData("nightly builds of the solution", "development-build-heavy")]
    [InlineData("run VRChat diagnostics", "vrchat-diagnostic")]
    public void DeterministicPhrases_MatchKnownLoadouts(string text, string loadoutId)
    {
        var interpretation = DeterministicInterpreter.Interpret(text);
        Assert.True(interpretation.Success);
        Assert.Equal(loadoutId, interpretation.Intent!.LoadoutId);
    }

    [Fact]
    public void OverlappingWorkloads_DoNotInventALoadout()
    {
        var interpretation = DeterministicInterpreter.Interpret("play games and watch a movie");
        Assert.False(interpretation.Success);
        Assert.Null(interpretation.Intent);
    }

    [Fact]
    public void DisplayAndDiagnostics_DoNotFalsePositive()
    {
        Assert.False(DeterministicInterpreter.Interpret("adjust display settings").Success);
        Assert.False(DeterministicInterpreter.Interpret("display").Success);
        var coding = DeterministicInterpreter.Interpret("run diagnostics while coding");
        Assert.True(coding.Success);
        Assert.Equal("development-interactive", coding.Intent!.LoadoutId);
        var rebuild = DeterministicInterpreter.Interpret("rebuild the solution");
        Assert.True(rebuild.Success);
        Assert.Equal("development-build-heavy", rebuild.Intent!.LoadoutId);
    }

    [Fact]
    public void QuotedPathLikeNames_AreFiltered()
    {
        var interpretation = DeterministicInterpreter.Interpret("play \"notepad\" and \"C:\\game.exe\" and \"..\\secret\"");
        Assert.True(interpretation.Success);
        Assert.Contains("notepad", interpretation.Intent!.RequestedApplications);
        Assert.DoesNotContain(interpretation.Intent.RequestedApplications, name => name.Contains('\\', StringComparison.Ordinal) || name.Contains("..", StringComparison.Ordinal));
    }

    private static LoadoutCatalog LoadCatalog() => LoadoutCatalog.Load(PresetDirectory());

    private static string PresetDirectory() => Path.Combine(AppContext.BaseDirectory, "presets");

    private static UserIntent IntentFor(Loadout loadout) => new()
    {
        LoadoutId = loadout.Id,
        Objective = loadout.Objective,
        SessionMode = loadout.SessionMode,
        PowerPreference = loadout.PowerPreference,
        BackgroundPolicy = loadout.BackgroundPolicy,
        RequestedApplications = []
    };

    private static HardwareInventory AmbiguousHardware() => new()
    {
        Confidence = DiscoveryConfidence.Low,
        Single64BitMaskCoversMachine = false,
        GroupCount = 2,
        Ambiguities = ["groups"]
    };
}
