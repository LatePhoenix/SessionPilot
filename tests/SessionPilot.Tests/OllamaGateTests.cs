namespace SessionPilot.Tests;

public class OllamaGateTests
{
    [Theory]
    [InlineData(SessionPhase.Idle, true)]
    [InlineData(SessionPhase.Planning, true)]
    [InlineData(SessionPhase.Active, false)]
    public void Inference_IsBlockedOnlyWhileActive(SessionPhase phase, bool allowed)
    {
        Assert.Equal(allowed, OllamaSessionGate.Allow(phase));
        Assert.Equal(allowed, OllamaSessionGate.CompileAfterRequest(phase));
        Assert.StartsWith("http://127.0.0.1", OllamaSessionGate.LoopbackEndpoint, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveSession_BlocksRecompile()
    {
        Assert.False(OllamaSessionGate.CompileAfterRequest(SessionPhase.Active));
        Assert.Equal("A session became Active. The plan was not recompiled.", OllamaSessionGate.ActiveSessionNote);
    }
}
