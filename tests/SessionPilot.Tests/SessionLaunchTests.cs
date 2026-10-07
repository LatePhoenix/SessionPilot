namespace SessionPilot.Tests;

public class SessionLaunchTests
{
    [Fact]
    public void Session_FollowsTheHappyPath_AndBlocksASecondOne()
    {
        var session = new SessionCoordinator();
        Assert.True(session.TryTransition(SessionPhase.Discovering, out _));
        Assert.True(session.TryTransition(SessionPhase.Observing, out _));
        Assert.True(session.TryTransition(SessionPhase.Planning, out _));
        Assert.True(session.TryTransition(SessionPhase.AwaitingApproval, out _));
        Assert.True(session.TryTransition(SessionPhase.Preparing, out _));
        Assert.True(session.TryTransition(SessionPhase.Active, out _));
        Assert.True(session.BlocksAnotherSession);

        var second = new SessionCoordinator();
        second.TryTransition(SessionPhase.Discovering, out _);
        Assert.False(second.TryTransition(SessionPhase.Active, out var reason));
        Assert.Contains("Cannot move", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualChoice_BeatsATrigger()
    {
        var session = new SessionCoordinator();
        session.ChooseManually("vrchat-steamvr");
        Assert.False(session.TryApplyTrigger("vrchat-social", out var reason));
        Assert.Equal("vrchat-steamvr", session.ManualLoadoutId);
        Assert.Contains("manual", reason, StringComparison.OrdinalIgnoreCase);
        session.ClearManualSelection();
        Assert.Null(session.ManualLoadoutId);
    }

    [Fact]
    public void NextPhase_ExplainsWhyContinueCannotMove()
    {
        var idle = new SessionCoordinator();
        Assert.Null(idle.NextPhase(planCompiled: false, out var idleReason));
        Assert.Equal("Press Begin to start a session.", idleReason);

        var waiting = new SessionCoordinator();
        Assert.True(waiting.TryTransition(SessionPhase.Discovering, out _));
        Assert.True(waiting.TryTransition(SessionPhase.Observing, out _));
        Assert.True(waiting.TryTransition(SessionPhase.Planning, out _));
        Assert.True(waiting.TryTransition(SessionPhase.AwaitingApproval, out _));
        Assert.Null(waiting.NextPhase(planCompiled: false, out var waitingReason));
        Assert.Equal("Compile a plan before preparing.", waitingReason);
        Assert.Equal(SessionPhase.Preparing, waiting.NextPhase(planCompiled: true, out _));

        var finished = new SessionCoordinator();
        foreach (var phase in new[]
        {
            SessionPhase.Discovering, SessionPhase.Observing, SessionPhase.Planning, SessionPhase.AwaitingApproval,
            SessionPhase.Preparing, SessionPhase.Active, SessionPhase.Restoring, SessionPhase.Completed
        })
        {
            Assert.True(finished.TryTransition(phase, out _));
        }

        Assert.Null(finished.NextPhase(planCompiled: true, out var finishedReason));
        Assert.Equal("Press Begin to start again.", finishedReason);
    }

    [Fact]
    public void PromptText_NeverBecomesAnArgument()
    {
        var session = new SessionCoordinator();
        var starter = new FakeStarter();
        var result = session.Launch(new ConfirmedLaunch
        {
            PathOrUri = @"C:\apps\tool.exe",
            PathConfirmed = true,
            ApprovedArguments = ["--open", "close optional apps"],
            PromptText = "close optional apps"
        }, starter, readinessTimedOut: false);

        Assert.False(result.Started);
        Assert.False(result.Owned);
        Assert.Equal(0, starter.Calls);
        Assert.Contains("Prompt text", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void UnconfirmedPath_IsNotLaunched()
    {
        var session = new SessionCoordinator();
        var starter = new FakeStarter();
        var result = session.Launch(new ConfirmedLaunch
        {
            PathOrUri = @"C:\apps\tool.exe",
            PathConfirmed = false
        }, starter, false);

        Assert.False(result.Owned);
        Assert.Equal(0, starter.Calls);
    }

    [Fact]
    public void AlreadyRunning_IsNotOwned()
    {
        var session = new SessionCoordinator();
        var starter = new FakeStarter();
        var result = session.Launch(new ConfirmedLaunch
        {
            PathOrUri = @"C:\apps\vrchat.exe",
            PathConfirmed = true,
            AlreadyRunning = true
        }, starter, false);

        Assert.False(result.Owned);
        Assert.Equal(0, starter.Calls);
        Assert.Contains(@"C:\apps\vrchat.exe", session.AlreadyRunning);
        Assert.Empty(session.Owned);
    }

    [Fact]
    public void ReadinessTimeout_IsAFailedLaunch_NotOwnership()
    {
        var session = new SessionCoordinator();
        var starter = new FakeStarter();
        var result = session.Launch(new ConfirmedLaunch
        {
            PathOrUri = @"C:\apps\tool.exe",
            PathConfirmed = true,
            ApprovedArguments = ["--safe"]
        }, starter, readinessTimedOut: true);

        Assert.True(result.Started);
        Assert.False(result.Owned);
        Assert.Empty(session.Owned);
        Assert.Equal(1, starter.Calls);
        Assert.Equal(["--safe"], starter.LastArguments);
    }

    [Fact]
    public void ConfirmedLaunch_IsOwnedSeparately()
    {
        var session = new SessionCoordinator();
        var result = session.Launch(new ConfirmedLaunch
        {
            PathOrUri = "steam://launch/438100",
            PathConfirmed = true,
            PromptText = "play VRChat"
        }, new FakeStarter(), false);

        Assert.True(result.Owned);
        Assert.Single(session.Owned);
        Assert.DoesNotContain("play VRChat", session.Owned[0].Arguments);
        Assert.Contains("unsaved", session.RelaunchNote(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeStarter : IProcessStarter
    {
        public int Calls { get; private set; }
        public IReadOnlyList<string> LastArguments { get; private set; } = [];
        public bool Start(string pathOrUri, IReadOnlyList<string> arguments)
        {
            Calls++;
            LastArguments = arguments.ToList();
            return true;
        }
    }
}
