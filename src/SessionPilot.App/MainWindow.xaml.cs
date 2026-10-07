using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SessionPilot.Core;
using SessionPilot.Infrastructure;

namespace SessionPilot.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _samples = new() { Interval = RollingSampleWindow<int>.DefaultInterval };
    private readonly RollingSampleWindow<CpuObservation> _window = new();
    private readonly Dictionary<string, TimeSpan> _previousCpu = new();
    private readonly Stopwatch _sampleClock = new();
    private LoadoutCatalog? _catalog;
    private readonly SessionCoordinator _session = new();
    private readonly MeasurementLog _measurements = new();
    private readonly TriggerTracker _triggers = new();
    private CancellationTokenSource? _ollamaRequest;
    private CompiledPlan? _plan;
    private PowerOwnerKind _powerOwner = PowerOwnerKind.Unset;
    private string? _approvedPlanHash;
    private string? _approvedConfigHash;
    private HonestStatus _status = new();
    private int _logicalProcessors;
    private int _samplesTaken;
    private int _sampling;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        _samples.Tick += SampleTick;
        Closed += (_, _) =>
        {
            _ollamaRequest?.Cancel();
            _window.Close();
            _samples.Stop();
        };
        Loaded += async (_, _) => await LoadShell();
    }

    private async Task LoadShell()
    {
        DashboardBody.Text = "Discovering…";
        PowerPlans.Text = "Discovering…";
        GuidedBody.Text = "Discovering…";
        LoadoutDetail.Text = "Discovering…";
        ShowPage(PageDashboard, NavDashboard, "Dashboard");

        // Saved state is a small local file. Read it before the first await so a measurement or owner
        // recorded while discovery runs is added to it instead of replacing it.
        var state = AppStateStore.Load(AppPaths.StateFile, out var stateNote);
        _powerOwner = AppStateStore.TryParseOwner(state.PowerOwner, out var owner) ? owner : PowerOwnerKind.Unset;
        _measurements.Restore(state.Measurements);
        ShowMeasurements();
        PowerDecisionText.Text = _powerOwner == PowerOwnerKind.Unset
            ? "No power owner is selected. Nothing was switched."
            : "Owner " + _powerOwner + " is recorded. Nothing was switched.";

        var presetDirectory = Path.Combine(AppContext.BaseDirectory, "presets");
        var loadoutDirectory = AppPaths.LoadoutDirectory;
        var journalDirectory = AppPaths.JournalDirectory;
        var discovered = await Task.Run(() => Discover(presetDirectory, loadoutDirectory, journalDirectory));
        var listing = await PowerPlanReader.TryListAsync();
        if (!IsLoaded)
        {
            return;
        }

        _logicalProcessors = discovered.Hardware.Confidence == DiscoveryConfidence.None ? 0 : discovered.Hardware.LogicalProcessorCount;
        _catalog = discovered.Catalog;
        LoadoutList.ItemsSource = _catalog.All.Select(loadout => loadout.DisplayName).ToList();
        DashboardBody.Text = DescribeInstallation(discovered.Installation, discovered.Hardware);
        if (_catalog.LoadErrors.Count > 0)
        {
            DashboardBody.Text += Environment.NewLine + Environment.NewLine + "Skipped loadout files:" + Environment.NewLine +
                                  string.Join(Environment.NewLine, _catalog.LoadErrors);
        }

        GuidedBody.Text = string.Join(Environment.NewLine + Environment.NewLine, GuidedWorkflows.All.Select(workflow =>
            workflow.Title + " (" + workflow.Mode + ")" + Environment.NewLine +
            string.Join(Environment.NewLine, workflow.Steps.Select(step => "• " + step.Title + ": " + step.Instruction)) +
            Environment.NewLine + workflow.Note));
        ApplyStatus();
        DashboardBody.Text += Environment.NewLine + Environment.NewLine + "Startup recovery:" + Environment.NewLine +
                              string.Join(Environment.NewLine, discovered.Recovery);
        if (stateNote is not null)
        {
            DashboardBody.Text += Environment.NewLine + stateNote;
        }

        var plans = listing is null ? [] : PowerPlanParser.Parse(listing);
        PowerPlans.Text = plans.Count == 0
            ? "Power plans: unavailable. Nothing was switched."
            : "Installed plans (read-only): " + string.Join(", ", plans.Select(plan => plan.Name + (plan.Active ? " (active)" : "")));
    }

    private static ShellSnapshot Discover(string presetDirectory, string loadoutDirectory, string journalDirectory) =>
        new(
            LiveDiscovery.Installation(),
            LiveDiscovery.Hardware(),
            LoadoutCatalog.Load(presetDirectory, loadoutDirectory),
            StartupRecovery.DescribeIncomplete(journalDirectory));

    private static string DescribeInstallation(ProcessLassoInstallation installation, HardwareInventory hardware)
    {
        var hardwareText = hardware.Confidence == DiscoveryConfidence.None
            ? "Hardware: unavailable"
            : "Hardware: " + hardware.LogicalProcessorCount + " logical processors (" + hardware.Confidence + ")";
        return "Process Lasso version: " + (installation.ProductVersion ?? "not observed") + Environment.NewLine +
               "Confirmed config: none. " + InstallationCandidates.NotActiveLimitation + Environment.NewLine +
               "Candidates: " + installation.ConfigCandidates.Count + Environment.NewLine +
               hardwareText + Environment.NewLine +
               "GPU: unavailable" + Environment.NewLine +
               "Performance effect: not-measured";
    }

    private void ShowDashboard(object sender, RoutedEventArgs e) => ShowPage(PageDashboard, NavDashboard, "Dashboard");
    private void ShowDiagnostics(object sender, RoutedEventArgs e)
    {
        ShowPage(PageDiagnostics, NavDiagnostics, "Diagnostics");
        if (!_samples.IsEnabled && _window.ShouldTakeSample(_samplesTaken))
        {
            _sampleClock.Restart();
            _samples.Start();
        }
    }

    private void ShowLoadouts(object sender, RoutedEventArgs e) => ShowPage(PageLoadouts, NavLoadouts, "Loadouts");
    private void ShowPrompt(object sender, RoutedEventArgs e) => ShowPage(PagePrompt, NavPrompt, "Prompt");
    private void ShowPlan(object sender, RoutedEventArgs e) => ShowPage(PagePlan, NavPlan, "Plan review");
    private void ShowSession(object sender, RoutedEventArgs e) => ShowPage(PageSession, NavSession, "Session");
    private void ShowMeasurement(object sender, RoutedEventArgs e) => ShowPage(PageMeasurement, NavMeasurement, "Measurement");

    private void ShowPage(UIElement page, Button active, string title)
    {
        PageDashboard.Visibility = Visibility.Collapsed;
        PageDiagnostics.Visibility = Visibility.Collapsed;
        PageLoadouts.Visibility = Visibility.Collapsed;
        PagePrompt.Visibility = Visibility.Collapsed;
        PagePlan.Visibility = Visibility.Collapsed;
        PageSession.Visibility = Visibility.Collapsed;
        PageMeasurement.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
        PageTitle.Text = title;
        foreach (var button in new[] { NavDashboard, NavDiagnostics, NavLoadouts, NavPrompt, NavPlan, NavSession, NavMeasurement })
        {
            button.Background = Brushes.Transparent;
        }

        active.Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x5F, 0xAF));
        if (page != PageDiagnostics)
        {
            _samples.Stop();
        }
    }

    private async void SampleTick(object? sender, EventArgs e)
    {
        if (!_window.ShouldTakeSample(_samplesTaken))
        {
            _samples.Stop();
            return;
        }

        if (Interlocked.Exchange(ref _sampling, 1) == 1)
        {
            return;
        }

        var wall = _sampleClock.Elapsed;
        _sampleClock.Restart();
        var previous = new Dictionary<string, TimeSpan>(_previousCpu);
        var processors = _logicalProcessors;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var sample = await Task.Run(() => ProcessSampler.Sample(previous, wall, processors));
            if (!IsLoaded)
            {
                return;
            }

            var duration = Stopwatch.GetElapsedTime(started);
            _previousCpu.Clear();
            foreach (var pair in sample.Cpu)
            {
                _previousCpu[pair.Key] = pair.Value;
            }

            var observation = ProcessorTimeSeries.Observe(null, null, wall, processors, duration);
            _window.TryAdd(observation);
            _samplesTaken++;
            ProcessList.ItemsSource = sample.Rows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToList();
            DiagnosticsMeta.Text = "Samples " + _window.Count + "/" + _window.Capacity +
                                   " at " + (int)RollingSampleWindow<int>.DefaultInterval.TotalSeconds + "s. Collector " +
                                   duration.TotalMilliseconds.ToString("0") + " ms. Logical processors: " +
                                   (processors < 1 ? "unavailable" : processors.ToString()) + ".";
            if (!_window.ShouldTakeSample(_samplesTaken))
            {
                _samples.Stop();
            }
        }
        catch (Exception)
        {
            if (IsLoaded)
            {
                DiagnosticsMeta.Text = "The sample could not be read. Nothing was changed.";
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    private void HoldCloseTarget(object sender, RoutedEventArgs e) => _samples.Stop();

    private void ReleaseCloseTarget(object sender, RoutedEventArgs e)
    {
        if (PageDiagnostics.Visibility == Visibility.Visible && _window.ShouldTakeSample(_samplesTaken))
        {
            _sampleClock.Restart();
            _samples.Start();
        }
    }

    private void RequestClose(object sender, RoutedEventArgs e)
    {
        if (ProcessList.SelectedItem is not SampledProcess row || row.CreationTime is null || !int.TryParse(row.Pid, out var processId))
        {
            CloseResult.Text = "Select one process with a known creation time. Nothing was closed.";
            return;
        }

        if (row.Classification is "SystemOrSecurity" or "ProtectedParticipant")
        {
            CloseResult.Text = "System, security, and protected participants are not close targets.";
            ApproveClose.IsChecked = false;
            return;
        }

        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);
            var current = new DateTimeOffset(DateTime.SpecifyKind(process.StartTime, DateTimeKind.Local));
            var optional = ApproveClose.IsChecked == true && row.Classification is "Unknown" or "PossibleUnsavedWork";
            var outcome = GracefulClosePolicy.Request(
                processId,
                row.CreationTime.Value,
                process.Id,
                current,
                ApproveClose.IsChecked == true,
                optional,
                new LiveWindowCloser(process),
                unsavedPromptVisible: false);
            if (!outcome.ApprovalStillValid)
            {
                ApproveClose.IsChecked = false;
            }

            CloseResult.Text = outcome.Detail + (row.Classification == "PossibleUnsavedWork"
                ? " If a save prompt appears, it belongs to the application."
                : "");
        }
        catch (Exception)
        {
            ApproveClose.IsChecked = false;
            CloseResult.Text = "The process could not be revalidated. No close request was sent.";
        }
        finally
        {
            process?.Dispose();
        }
    }

    private void LoadoutSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_catalog is null || LoadoutList.SelectedIndex < 0)
        {
            return;
        }

        var loadout = _catalog.All[LoadoutList.SelectedIndex];
        _session.ChooseManually(loadout.Id);
        LoadoutDetail.Text = loadout.DisplayName + Environment.NewLine + loadout.Summary + Environment.NewLine +
                             string.Join(Environment.NewLine, loadout.Notes);
        Compile(loadout, new UserIntent
        {
            LoadoutId = loadout.Id,
            Objective = loadout.Objective,
            SessionMode = loadout.SessionMode,
            PowerPreference = loadout.PowerPreference,
            BackgroundPolicy = loadout.BackgroundPolicy,
            RequestedApplications = []
        });
    }

    private void ClearManualLoadout(object sender, RoutedEventArgs e)
    {
        _session.ClearManualSelection();
        LoadoutList.SelectedIndex = -1;
        LoadoutDetail.Text = "No manual selection. Nothing was applied.";
    }

    private void InterpretPrompt(object sender, RoutedEventArgs e)
    {
        if (_catalog is null)
        {
            return;
        }

        var interpretation = DeterministicInterpreter.Interpret(PromptBox.Text);
        PromptResult.Text = WithWarnings(interpretation);
        if (!interpretation.Success || interpretation.Intent is null || !_catalog.TryGet(interpretation.Intent.LoadoutId, out var loadout))
        {
            return;
        }

        PromptResult.Text += Environment.NewLine + "Presets work without Ollama. Nothing was written.";
        Compile(loadout, interpretation.Intent);
    }

    private static string WithWarnings(Interpretation interpretation) =>
        string.Join(Environment.NewLine, interpretation.Warnings.Prepend(interpretation.Explanation));

    private async void InterpretWithOllama(object sender, RoutedEventArgs e)
    {
        if (!OllamaSessionGate.Allow(_session.Phase))
        {
            PromptResult.Text = "Ollama stays unloaded while a session is Active. Nothing was sent.";
            return;
        }

        if (_catalog is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(OllamaModel.Text))
        {
            var installed = OllamaModels.Installed(OllamaModels.DefaultDirectory());
            PromptResult.Text = "Choose an installed Ollama model. SessionPilot does not download one." + Environment.NewLine +
                                (installed.Count == 0 ? "No installed models were found." : "Installed: " + string.Join(", ", installed) + ".");
            return;
        }

        InterpretOllama.IsEnabled = false;
        _ollamaRequest?.Cancel();
        _ollamaRequest?.Dispose();
        _ollamaRequest = new CancellationTokenSource();
        try
        {
            using var http = new HttpClient();
            var client = new OllamaIntentClient(http);
            var interpretation = await client.InterpretAsync(new OllamaRequest
            {
                Endpoint = OllamaSessionGate.LoopbackEndpoint,
                Model = OllamaModel.Text.Trim(),
                Prompt = PromptBox.Text
            }, _catalog, _ollamaRequest.Token);
            PromptResult.Text = WithWarnings(interpretation) + Environment.NewLine + "The prompt was not stored.";
            if (!OllamaSessionGate.CompileAfterRequest(_session.Phase))
            {
                PromptResult.Text += Environment.NewLine + OllamaSessionGate.ActiveSessionNote;
                return;
            }

            if (interpretation.Success && interpretation.Intent is not null && _catalog.TryGet(interpretation.Intent.LoadoutId, out var loadout))
            {
                Compile(loadout, interpretation.Intent);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            PromptResult.Text = "Ollama could not be used. Presets still work without it. Nothing was stored.";
        }
        finally
        {
            InterpretOllama.IsEnabled = true;
        }
    }

    private void Compile(Loadout loadout, UserIntent intent)
    {
        _plan = PlanCompiler.Compile(new CompileInput { Intent = intent, Loadout = loadout });
        _status = _status with { Compiled = "compiled (dry run)" };
        ApplyStatus();
        _approvedPlanHash = null;
        _approvedConfigHash = null;
        ApprovePlan.IsChecked = false;
        PlanChanges.ItemsSource = _plan.Changes.Select(change =>
            change.TargetIdentity + "  " + change.ExistingValue + " → " + change.ProposedValue +
            Environment.NewLine + change.Rationale + Environment.NewLine +
            change.SupportStatus + "  writable=" + change.Writable).ToList();
    }

    private void SuggestTrigger(object sender, RoutedEventArgs e)
    {
        var signaled = TriggerSignals.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var decision = _triggers.Evaluate(signaled, _session.ManualLoadoutId, TriggersOptIn.IsChecked == true, DateTimeOffset.UtcNow);
        TriggerSuggestion.Text = decision.Action + ": " + decision.Reason + " A suggestion is not an applied plan.";
    }

    private void RecordMeasurement(object sender, RoutedEventArgs e)
    {
        var run = _measurements.Add(MeasurementLabel.Text, MeasurementConfounders.Text);
        ShowMeasurements();
        PersistState();
        StatusPerformance.Text = run.PerformanceEffect;
    }

    private void ClearMeasurements(object sender, RoutedEventArgs e)
    {
        _measurements.Clear();
        ShowMeasurements();
        PersistState();
        StatusPerformance.Text = "not-measured";
    }

    private void ShowMeasurements()
    {
        MeasurementList.ItemsSource = _measurements.Runs.Select(item =>
            item.Label + " — " + item.PerformanceEffect + Environment.NewLine + item.Confounders + Environment.NewLine + item.CaptureNote).ToList();
    }

    private void RecordProcessLassoPower(object sender, RoutedEventArgs e) => RecordPower(PowerOwnerKind.ProcessLasso);

    private void RecordWindowsPower(object sender, RoutedEventArgs e) => RecordPower(PowerOwnerKind.Windows);

    private void RecordPower(PowerOwnerKind requested)
    {
        var decision = PowerOwnership.Select(requested, _powerOwner, exportExists: false);
        _powerOwner = decision.Owner;
        PowerDecisionText.Text = decision.Detail;
        PersistState();
    }

    private void ClearRecordedOwner(object sender, RoutedEventArgs e)
    {
        var decision = PowerOwnership.Clear();
        _powerOwner = decision.Owner;
        PowerDecisionText.Text = decision.Detail;
        PersistState();
    }

    private void PersistState()
    {
        try
        {
            AppStateStore.Save(AppPaths.StateFile, new AppState
            {
                PowerOwner = _powerOwner.ToString(),
                Measurements = _measurements.Runs.ToList()
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void BeginSession(object sender, RoutedEventArgs e)
    {
        if (_session.Phase is SessionPhase.Completed or SessionPhase.Cancelled or SessionPhase.Failed)
        {
            _session.TryTransition(SessionPhase.Idle, out _);
        }

        SessionPhaseText.Text = _session.TryTransition(SessionPhase.Discovering, out var reason)
            ? _session.Phase.ToString()
            : reason;
    }

    private void ContinueSession(object sender, RoutedEventArgs e)
    {
        var next = _session.NextPhase(_plan is not null, out var blocked);
        if (next is null || !_session.TryTransition(next.Value, out var reason))
        {
            SessionPhaseText.Text = "Staying in " + _session.Phase + ". " + blocked;
            return;
        }

        SessionPhaseText.Text = _session.Phase + ". " + reason;
        if (_session.Phase == SessionPhase.Completed)
        {
            _status = _status with { PerformanceEffect = "not-measured" };
            ApplyStatus();
        }
    }

    private void LaunchConfirmed(object sender, RoutedEventArgs e)
    {
        if (_session.Phase is not (SessionPhase.Preparing or SessionPhase.Active))
        {
            LaunchResultText.Text = "Launch is available while the session is Preparing or Active. Nothing was started.";
            return;
        }

        var arguments = LaunchArguments.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = _session.Launch(new ConfirmedLaunch
        {
            PathOrUri = LaunchPath.Text.Trim(),
            PathConfirmed = PathConfirmed.IsChecked == true,
            ApprovedArguments = arguments,
            PromptText = PromptBox.Text,
            AlreadyRunning = AlreadyRunning.IsChecked == true
        }, new ShellProcessStarter(), readinessTimedOut: false);
        LaunchResultText.Text = result.Detail + Environment.NewLine + _session.RelaunchNote() +
                                Environment.NewLine + "Owned: " + _session.Owned.Count + ". Already running: " + _session.AlreadyRunning.Count + ".";
    }

    private void ApproveCurrentPlan(object sender, RoutedEventArgs e)
    {
        if (_plan is null)
        {
            ApprovePlan.IsChecked = false;
            ApplyResult.Text = "Compile a plan before approving it.";
            return;
        }

        var path = IsolatedPath.Text.Trim();
        if (!File.Exists(path))
        {
            ApprovePlan.IsChecked = false;
            ApplyResult.Text = "Choose an isolated copy that exists. Nothing was written.";
            return;
        }

        if (!IsolatedCopyHash.TryRead(path, out var approvedHash))
        {
            ApprovePlan.IsChecked = false;
            _approvedPlanHash = null;
            _approvedConfigHash = null;
            ApplyResult.Text = IsolatedCopyHash.UnreadableMessage;
            return;
        }

        _approvedPlanHash = ApprovalBinding.HashPlan(_plan);
        _approvedConfigHash = approvedHash;
        ApplyResult.Text = "Approval is bound to this plan and this file hash.";
    }

    private void ClearPlanApproval(object sender, RoutedEventArgs e)
    {
        _approvedPlanHash = null;
        _approvedConfigHash = null;
    }

    private void ApplyIsolated(object sender, RoutedEventArgs e)
    {
        if (_plan is null)
        {
            ApplyResult.Text = "There is no compiled plan.";
            return;
        }

        var path = IsolatedPath.Text.Trim();
        if (!File.Exists(path))
        {
            ApplyResult.Text = "The isolated copy is missing. Nothing was written.";
            return;
        }

        if (!IsolatedCopyHash.TryRead(path, out var configHash))
        {
            _approvedPlanHash = null;
            _approvedConfigHash = null;
            ApprovePlan.IsChecked = false;
            ApplyResult.Text = IsolatedCopyHash.UnreadableMessage;
            return;
        }

        if (_approvedPlanHash is null || _approvedConfigHash is null)
        {
            ApplyResult.Text = "Approve the plan before applying. Nothing was written.";
            return;
        }

        var planHash = ApprovalBinding.HashPlan(_plan);
        if (!ApprovalBinding.StillValid(_approvedPlanHash, _approvedConfigHash, planHash, configHash))
        {
            _approvedPlanHash = null;
            _approvedConfigHash = null;
            ApprovePlan.IsChecked = false;
            ApplyResult.Text = "The plan or file changed. Approval was invalidated. Nothing was written.";
            return;
        }

        var result = new TransactionCoordinator().Apply(new ApplyRequest
        {
            TargetPath = path,
            ExpectedBaselineHash = configHash,
            Approved = true,
            JournalDirectory = AppPaths.JournalDirectory,
            LiveCandidatePaths = LiveDiscovery.LiveConfigPaths(),
            PlanId = _plan.PlanId,
            Edits = []
        });
        _status = _status with
        {
            Persisted = result.PersistenceStatus,
            Governor = result.GovernorStatus,
            EffectiveSetting = result.EffectiveStatus,
            PerformanceEffect = result.PerformanceStatus
        };
        ApplyStatus();
        ApplyResult.Text = result.Status + ": " + CheckReport.Redact(result.Message);
    }

    private void ApplyStatus()
    {
        StatusCompiled.Text = _status.Compiled;
        StatusPersisted.Text = _status.Persisted;
        StatusGovernor.Text = _status.Governor;
        StatusEffective.Text = _status.EffectiveSetting;
        StatusPerformance.Text = _status.PerformanceEffect;
    }

    private sealed record ShellSnapshot(
        ProcessLassoInstallation Installation,
        HardwareInventory Hardware,
        LoadoutCatalog Catalog,
        IReadOnlyList<string> Recovery);
}

