using System.Text;

namespace SessionPilot.Core;

public static class ApprovalBinding
{
    public static string HashPlan(CompiledPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append(plan.LoadoutId).Append('\n').Append(plan.Summary).Append('\n');
        foreach (var change in plan.Changes)
        {
            builder.Append(change.ChangeId).Append('|').Append(change.ProposedValue).Append('|').Append(change.Writable).Append('\n');
        }

        return ContentHashing.Sha256(Encoding.UTF8.GetBytes(builder.ToString()));
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
            lines.Add(name + " It was not opened or rolled back.");
        }

        return lines;
    }
}
