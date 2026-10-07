namespace SessionPilot.Core;

public enum CleanupClassification
{
    ProtectedParticipant,
    SystemOrSecurity,
    PossibleUnsavedWork,
    Unknown
}

public sealed record CleanupPreview
{
    public required string ExecutableName { get; init; }
    public required CleanupClassification Classification { get; init; }
    public required string Reason { get; init; }
    public bool CloseAuthorized { get; init; }
}

public static class CleanupClassifier
{
    private static readonly HashSet<string> UnsavedWork = new(StringComparer.OrdinalIgnoreCase)
    {
        "winword.exe", "excel.exe", "powerpnt.exe", "notepad.exe", "notepad++.exe",
        "code.exe", "devenv.exe", "cursor.exe", "rider64.exe", "idea64.exe"
    };

    private static readonly HashSet<string> NotBlanketTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "python.exe", "pythonw.exe", "node.exe",
        "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe",
        "windowsterminal.exe", "wt.exe", "cmd.exe", "powershell.exe", "pwsh.exe",
        "code.exe", "devenv.exe", "cursor.exe"
    };

    public static CleanupPreview Classify(string executableName, bool protectedParticipant, double? cpuFractionOfCapacity)
    {
        var name = Normalize(executableName);
        if (protectedParticipant)
        {
            return Preview(name, CleanupClassification.ProtectedParticipant, "Required workload participant. CPU use does not make it disposable.", false);
        }

        if (ProtectedProcesses.IsProtected(name))
        {
            return Preview(name, CleanupClassification.SystemOrSecurity, "System or security process. Excluded from cleanup.", false);
        }

        if (UnsavedWork.Contains(name))
        {
            return Preview(name, CleanupClassification.PossibleUnsavedWork, "This application may hold unsaved work. It is not a blanket close target.", false);
        }

        var reason = NotBlanketTargets.Contains(name)
            ? "This executable is not a blanket cleanup target."
            : "Unknown process. Inspect it before any action.";
        if (cpuFractionOfCapacity is not null)
        {
            reason += " Observed CPU does not authorize a close.";
        }

        return Preview(name, CleanupClassification.Unknown, reason, false);
    }

    private static CleanupPreview Preview(string name, CleanupClassification classification, string reason, bool closeAuthorized) => new()
    {
        ExecutableName = name,
        Classification = classification,
        Reason = reason,
        CloseAuthorized = closeAuthorized
    };

    private static string Normalize(string executableName)
    {
        var name = Path.GetFileName(executableName.Trim());
        if (name.Length == 0)
        {
            return "";
        }

        return name.Contains('.', StringComparison.Ordinal) ? name : name + ".exe";
    }
}
