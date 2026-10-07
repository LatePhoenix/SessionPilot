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
        Assert.StartsWith("http://127.0.0.1", OllamaSessionGate.LoopbackEndpoint, StringComparison.Ordinal);
    }
}
