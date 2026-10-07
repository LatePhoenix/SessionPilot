namespace SessionPilot.Tests;

public class GracefulCloseTests
{
    private static readonly DateTimeOffset Created = DateTimeOffset.Parse("2026-10-07T15:00:00Z");

    [Fact]
    public void UnapprovedClose_DoesNotTouchTheProcess()
    {
        var closer = new FakeCloser(hasWindow: true, closeAccepted: true);
        var outcome = GracefulClosePolicy.Request(10, Created, 10, Created, userApproved: false, optionalApp: true, closer, unsavedPromptVisible: false);

        Assert.Equal(CloseOutcomeKind.NotApproved, outcome.Kind);
        Assert.Equal(0, closer.CloseCalls);
        Assert.False(outcome.ProcessExited);
    }

    [Fact]
    public void PidReuse_InvalidatesApprovalBeforeAnyRequest()
    {
        var closer = new FakeCloser(hasWindow: true, closeAccepted: true);
        var outcome = GracefulClosePolicy.Request(10, Created, 10, Created.AddSeconds(1), userApproved: true, optionalApp: true, closer, false);

        Assert.Equal(CloseOutcomeKind.IdentityMismatch, outcome.Kind);
        Assert.False(outcome.ApprovalStillValid);
        Assert.Equal(0, closer.CloseCalls);
        Assert.False(outcome.ProcessExited);
    }

    [Fact]
    public void MissingWindow_IsReportedWithoutARequest()
    {
        var closer = new FakeCloser(hasWindow: false, closeAccepted: true);
        var outcome = Request(closer, unsaved: false);

        Assert.Equal(CloseOutcomeKind.NoWindow, outcome.Kind);
        Assert.Equal(0, closer.CloseCalls);
    }

    [Fact]
    public void RefusedRequest_DoesNotEscalate()
    {
        var closer = new FakeCloser(hasWindow: true, closeAccepted: false);
        var outcome = Request(closer, unsaved: false);

        Assert.Equal(CloseOutcomeKind.Refused, outcome.Kind);
        Assert.Equal(1, closer.CloseCalls);
        Assert.False(outcome.ProcessExited);
    }

    [Fact]
    public void UnsavedPrompt_StaysVisible()
    {
        var closer = new FakeCloser(hasWindow: true, closeAccepted: true);
        var outcome = Request(closer, unsaved: true);

        Assert.Equal(CloseOutcomeKind.UnsavedWorkPrompt, outcome.Kind);
        Assert.False(outcome.ProcessExited);
    }

    [Fact]
    public void AcceptedRequest_IsNotAnExit()
    {
        var closer = new FakeCloser(hasWindow: true, closeAccepted: true);
        var outcome = Request(closer, unsaved: false);

        Assert.Equal(CloseOutcomeKind.Requested, outcome.Kind);
        Assert.True(outcome.ApprovalStillValid);
        Assert.False(outcome.ProcessExited);
        Assert.Equal(1, closer.CloseCalls);
    }

    private static CloseOutcome Request(FakeCloser closer, bool unsaved) =>
        GracefulClosePolicy.Request(10, Created, 10, Created, true, true, closer, unsaved);

    private sealed class FakeCloser(bool hasWindow, bool closeAccepted) : ICloseRequest
    {
        public int CloseCalls { get; private set; }
        public bool HasMainWindow => hasWindow;
        public bool TryCloseMainWindow()
        {
            CloseCalls++;
            return closeAccepted;
        }
    }
}
