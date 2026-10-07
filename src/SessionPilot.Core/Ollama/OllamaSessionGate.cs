namespace SessionPilot.Core;

public static class OllamaSessionGate
{
    public const string LoopbackEndpoint = "http://127.0.0.1:11434";

    public static bool Allow(SessionPhase phase) => phase != SessionPhase.Active;
}
