namespace SessionPilot.Core;

public enum SessionPhase
{
    Idle,
    Discovering,
    Observing,
    Planning,
    AwaitingApproval,
    Preparing,
    Active,
    Restoring,
    Completed,
    Cancelled,
    Failed,
    PartiallyApplied,
    RecoveryRequired
}

public sealed record ConfirmedLaunch
{
    public required string PathOrUri { get; init; }
    public bool PathConfirmed { get; init; }
    public IReadOnlyList<string> ApprovedArguments { get; init; } = [];
    public string? PromptText { get; init; }
    public bool AlreadyRunning { get; init; }
}

public interface IProcessStarter
{
    bool Start(string pathOrUri, IReadOnlyList<string> arguments);
}

public sealed record LaunchResult
{
    public bool Started { get; init; }
    public bool Owned { get; init; }
    public required string Detail { get; init; }
}

public sealed record OwnedLaunch
{
    public required string PathOrUri { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = [];
}

public static class LaunchGuard
{
    public static string? Reject(ConfirmedLaunch launch)
    {
        if (!launch.PathConfirmed || string.IsNullOrWhiteSpace(launch.PathOrUri))
        {
            return "Launch requires a confirmed path or URI.";
        }

        if (launch.PathOrUri.Contains('\n') || launch.PathOrUri.Contains('\r'))
        {
            return "The path or URI is not a single confirmed value.";
        }

        var prompt = launch.PromptText?.Trim();
        if (!string.IsNullOrEmpty(prompt))
        {
            if (string.Equals(launch.PathOrUri.Trim(), prompt, StringComparison.Ordinal))
            {
                return "Prompt text cannot be a launch path.";
            }

            if (launch.ApprovedArguments.Any(argument => argument.Contains(prompt, StringComparison.Ordinal)))
            {
                return "Prompt text cannot become an argument.";
            }
        }

        return null;
    }
}

public sealed class SessionCoordinator
{
    private readonly List<OwnedLaunch> _owned = [];
    private readonly List<string> _alreadyRunning = [];
    private static readonly Dictionary<SessionPhase, SessionPhase[]> Allowed = new()
    {
        [SessionPhase.Idle] = [SessionPhase.Discovering],
        [SessionPhase.Discovering] = [SessionPhase.Observing, SessionPhase.Cancelled, SessionPhase.Failed],
        [SessionPhase.Observing] = [SessionPhase.Planning, SessionPhase.Cancelled],
        [SessionPhase.Planning] = [SessionPhase.AwaitingApproval, SessionPhase.Cancelled],
        [SessionPhase.AwaitingApproval] = [SessionPhase.Preparing, SessionPhase.Cancelled],
        [SessionPhase.Preparing] = [SessionPhase.Active, SessionPhase.Failed, SessionPhase.PartiallyApplied],
        [SessionPhase.Active] = [SessionPhase.Restoring, SessionPhase.Failed, SessionPhase.Cancelled],
        [SessionPhase.Restoring] = [SessionPhase.Completed, SessionPhase.Failed, SessionPhase.PartiallyApplied, SessionPhase.RecoveryRequired],
        [SessionPhase.Completed] = [SessionPhase.Idle],
        [SessionPhase.Cancelled] = [SessionPhase.Idle],
        [SessionPhase.Failed] = [SessionPhase.Idle],
        [SessionPhase.PartiallyApplied] = [SessionPhase.Restoring, SessionPhase.RecoveryRequired],
        [SessionPhase.RecoveryRequired] = [SessionPhase.Restoring]
    };

    public SessionPhase Phase { get; private set; } = SessionPhase.Idle;
    public string? ManualLoadoutId { get; private set; }
    public IReadOnlyList<OwnedLaunch> Owned => _owned;
    public IReadOnlyList<string> AlreadyRunning => _alreadyRunning;
    public bool BlocksAnotherSession => Phase is not (SessionPhase.Idle or SessionPhase.Completed or SessionPhase.Cancelled or SessionPhase.Failed);

    public bool TryTransition(SessionPhase next, out string reason)
    {
        if (Allowed.TryGetValue(Phase, out var options) && options.Contains(next))
        {
            Phase = next;
            reason = "Moved to " + next + ".";
            return true;
        }

        reason = "Cannot move from " + Phase + " to " + next + ".";
        return false;
    }

    public void ChooseManually(string loadoutId) => ManualLoadoutId = loadoutId;

    public bool TryApplyTrigger(string loadoutId, out string reason)
    {
        if (ManualLoadoutId is not null)
        {
            reason = "A manual selection overrides the trigger.";
            return false;
        }

        ManualLoadoutId = null;
        reason = "Trigger suggested '" + loadoutId + "'. It was not applied.";
        return true;
    }

    public LaunchResult Launch(ConfirmedLaunch launch, IProcessStarter starter, bool readinessTimedOut)
    {
        var rejected = LaunchGuard.Reject(launch);
        if (rejected is not null)
        {
            return new LaunchResult { Started = false, Owned = false, Detail = rejected };
        }

        if (launch.AlreadyRunning)
        {
            _alreadyRunning.Add(launch.PathOrUri);
            return new LaunchResult
            {
                Started = false,
                Owned = false,
                Detail = "An already-running application is not owned by the session."
            };
        }

        if (!starter.Start(launch.PathOrUri, launch.ApprovedArguments))
        {
            return new LaunchResult { Started = false, Owned = false, Detail = "The launch failed and is not owned." };
        }

        if (readinessTimedOut)
        {
            return new LaunchResult
            {
                Started = true,
                Owned = false,
                Detail = "Readiness timed out. The launch is a failure and is not owned."
            };
        }

        _owned.Add(new OwnedLaunch { PathOrUri = launch.PathOrUri, Arguments = launch.ApprovedArguments.ToList() });
        return new LaunchResult { Started = true, Owned = true, Detail = "The session started this application." };
    }

    public string RelaunchNote() =>
        "Relaunch does not restore unsaved documents, tabs, remote jobs, or exact application state.";
}
