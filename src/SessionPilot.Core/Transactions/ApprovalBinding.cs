using System.Text;

namespace SessionPilot.Core;

public static class ApprovalBinding
{
    public static string HashPlan(CompiledPlan plan)
    {
        var builder = new StringBuilder();
        Field(builder, plan.LoadoutId);
        Field(builder, plan.Summary);
        Field(builder, plan.Intent.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Field(builder, plan.Intent.LoadoutId);
        Field(builder, plan.Intent.Objective);
        Field(builder, plan.Intent.SessionMode);
        Field(builder, plan.Intent.PowerPreference);
        Field(builder, plan.Intent.BackgroundPolicy);
        foreach (var application in plan.Intent.RequestedApplications)
        {
            Field(builder, application);
        }

        foreach (var change in plan.Changes)
        {
            Field(builder, change.TargetIdentity);
            Field(builder, change.Section);
            Field(builder, change.Key);
            Field(builder, change.ExistingValue);
            Field(builder, change.ProposedValue);
            Field(builder, change.Writable ? "true" : "false");
            Field(builder, change.SupportStatus.ToString());
        }

        return ContentHashing.Sha256(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static void Field(StringBuilder builder, string? value)
    {
        value ??= "";
        builder.Append(value.Length).Append(':').Append(value);
    }

    public static bool StillValid(string? approvedPlanHash, string? approvedConfigHash, string planHash, string configHash) =>
        approvedPlanHash is not null &&
        approvedConfigHash is not null &&
        string.Equals(approvedPlanHash, planHash, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(approvedConfigHash, configHash, StringComparison.OrdinalIgnoreCase);
}

public static class IsolatedCopyHash
{
    public const string UnreadableMessage = "The isolated copy could not be read. Nothing was written.";

    public static bool TryRead(string path, out string hash)
    {
        try
        {
            hash = ContentHashing.Sha256(File.ReadAllBytes(path));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            hash = "";
            return false;
        }
    }
}

public static class StartupRecovery
{
    public static IReadOnlyList<string> DescribeIncomplete(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return ["No journal directory. Nothing was rolled back."];
        }

        var scan = JournalRecovery.Scan(directory);
        var lines = new List<string>();
        if (scan.Incomplete.Count == 0)
        {
            lines.Add("No incomplete journal. Nothing was rolled back.");
        }
        else
        {
            lines.AddRange(scan.Incomplete.Select(journal => CheckReport.Redact(JournalRecovery.Describe(journal) + " Nothing was rolled back.")));
        }

        foreach (var name in scan.UnreadableFileNames)
        {
            lines.Add(name + ": It was not opened or rolled back.");
        }

        return lines;
    }
}
