namespace SessionPilot.Core;

public static class OllamaSessionGate
{
    public const string LoopbackEndpoint = "http://127.0.0.1:11434";

    public const string ActiveSessionNote = "A session became Active. The plan was not recompiled.";

    public static bool Allow(SessionPhase phase) => phase != SessionPhase.Active;

    public static bool CompileAfterRequest(SessionPhase phase) => Allow(phase);
}
