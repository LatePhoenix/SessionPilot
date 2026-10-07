using System.Text;
using System.Text.RegularExpressions;

namespace SessionPilot.Core;

public sealed record CheckSnapshot
{
    public string Product { get; init; } = "SessionPilot";
    public bool LiveWritesEnabled { get; init; }
    public string? InstallationVersion { get; init; }
    public int ConfigCandidateCount { get; init; }
    public int PresentCandidates { get; init; }
    public int MissingCandidates { get; init; }
    public int AccessDeniedCandidates { get; init; }
    public bool ActiveConfigAssumed { get; init; }
    public string HardwareSummary { get; init; } = "unavailable";
    public int ProcessCount { get; init; }
    public int ProcessAccessDenied { get; init; }
    public string Compiled { get; init; } = "not compiled";
    public string Persisted { get; init; } = "not-written";
    public string Governor { get; init; } = "not-verified";
    public string EffectiveSetting { get; init; } = "not-observed";
    public string PerformanceEffect { get; init; } = "not-measured";
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public static class CheckReport
{
    public static string Format(CheckSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SessionPilot check");
        builder.AppendLine("Live Process Lasso writes: " + (snapshot.LiveWritesEnabled ? "enabled" : "disabled"));
        builder.AppendLine("Installation version: " + (string.IsNullOrWhiteSpace(snapshot.InstallationVersion) ? "not observed" : snapshot.InstallationVersion));
        builder.AppendLine($"Config candidates: {snapshot.ConfigCandidateCount} listed, {snapshot.PresentCandidates} present, {snapshot.MissingCandidates} missing, {snapshot.AccessDeniedCandidates} access-denied");
        builder.AppendLine("Active configuration assumed: " + (snapshot.ActiveConfigAssumed ? "yes" : "no"));
        builder.AppendLine("Hardware: " + snapshot.HardwareSummary);
        builder.AppendLine($"Processes: {snapshot.ProcessCount} observed, {snapshot.ProcessAccessDenied} access-denied");
        builder.AppendLine("Compiled: " + snapshot.Compiled);
        builder.AppendLine("Persisted: " + snapshot.Persisted);
        builder.AppendLine("Governor: " + snapshot.Governor);
        builder.AppendLine("Effective setting: " + snapshot.EffectiveSetting);
        builder.AppendLine("Performance effect: " + snapshot.PerformanceEffect);
        foreach (var note in snapshot.Notes)
        {
            builder.AppendLine("Note: " + note);
        }

        builder.AppendLine("Sampling loop: not started");
        return Redact(builder.ToString());
    }

    public static string Redact(string text)
    {
        var redacted = Regex.Replace(text, @"[A-Za-z]:\\[^\s""']+", "[path]");
        redacted = Regex.Replace(redacted, @"\\\\[^\s""']+", "[path]");
        return redacted;
    }
}

public static class FailureText
{
    public static string ForDialog(string message)
    {
        var detail = CheckReport.Redact(message);
        return string.IsNullOrEmpty(detail)
            ? "Nothing was written."
            : detail + Environment.NewLine + "Nothing was written.";
    }

    public static string ForCheck(string message)
    {
        var redacted = CheckReport.Redact(message);
        return string.Join(' ', redacted.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }
}
