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

public static class StartupRecovery
{
    public static IReadOnlyList<string> DescribeIncomplete(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return ["No journal directory. Nothing was rolled back."];
        }

        var journals = JournalRecovery.FindIncomplete(directory);
        if (journals.Count == 0)
        {
            return ["No incomplete journal. Nothing was rolled back."];
        }

        return journals.Select(journal => CheckReport.Redact(JournalRecovery.Describe(journal) + " Nothing was rolled back.")).ToList();
    }
}
