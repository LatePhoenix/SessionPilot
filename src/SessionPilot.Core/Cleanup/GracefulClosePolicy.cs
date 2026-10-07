namespace SessionPilot.Core;

public enum CloseOutcomeKind
{
    NotApproved,
    IdentityMismatch,
    NoWindow,
    Refused,
    UnsavedWorkPrompt,
    Requested
}

public sealed record CloseOutcome
{
    public required CloseOutcomeKind Kind { get; init; }
    public required string Detail { get; init; }
    public bool ProcessExited { get; init; }
    public bool ApprovalStillValid { get; init; }
}

public interface ICloseRequest
{
    bool HasMainWindow { get; }
    bool TryCloseMainWindow();
}

public static class GracefulClosePolicy
{
    public static CloseOutcome Request(
        int approvedProcessId,
        DateTimeOffset approvedCreation,
        int currentProcessId,
        DateTimeOffset currentCreation,
        bool userApproved,
        bool optionalApp,
        ICloseRequest closer,
        bool unsavedPromptVisible)
    {
        if (!userApproved || !optionalApp)
        {
            return Result(CloseOutcomeKind.NotApproved, "Graceful close was not approved for one optional application.", approvalValid: false);
        }

        if (!ProcessIdentity.IsSame(approvedProcessId, approvedCreation, currentProcessId, currentCreation))
        {
            return Result(CloseOutcomeKind.IdentityMismatch, "PID and creation time no longer match. The approval was invalidated.", approvalValid: false);
        }

        if (!closer.HasMainWindow)
        {
            return Result(CloseOutcomeKind.NoWindow, "The process has no main window, so no close request was sent.", approvalValid: true);
        }

        if (!closer.TryCloseMainWindow())
        {
            return Result(CloseOutcomeKind.Refused, "CloseMainWindow was refused. The process was not forced to exit.", approvalValid: true);
        }

        if (unsavedPromptVisible)
        {
            return Result(CloseOutcomeKind.UnsavedWorkPrompt, "The application is showing a prompt. SessionPilot will not bypass it.", approvalValid: true);
        }

        return Result(CloseOutcomeKind.Requested, "A main-window close was requested. That request does not mean the process exited.", approvalValid: true);
    }

    private static CloseOutcome Result(CloseOutcomeKind kind, string detail, bool approvalValid) => new()
    {
        Kind = kind,
        Detail = detail,
        ProcessExited = false,
        ApprovalStillValid = approvalValid
    };
}
