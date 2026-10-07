namespace SessionPilot.Core;

public static class TriggerSuggestions
{
    public static TriggerDecision Suggest(TriggerSnapshot snapshot, TriggerOptions options, DateTimeOffset now)
    {
        var decision = TriggerEngine.Evaluate(snapshot, options, now);
        if (decision.Action is not ("hold" or "wait" or "suggest"))
        {
            return new TriggerDecision
            {
                Action = "hold",
                Reason = "Triggers can suggest a plan. They cannot apply one or request elevation."
            };
        }

        return decision;
    }
}

public sealed class TriggerTracker
{
    public string? PendingLoadoutId { get; private set; }

    public DateTimeOffset? PendingSince { get; private set; }

    public TriggerDecision Evaluate(IReadOnlyList<string> signals, string? manualLoadoutId, bool optedIn, DateTimeOffset now)
    {
        var decision = TriggerSuggestions.Suggest(new TriggerSnapshot
        {
            ManualOverrideActive = manualLoadoutId is not null,
            ManualLoadoutId = manualLoadoutId,
            SignaledLoadouts = signals,
            PendingLoadoutId = PendingLoadoutId,
            PendingSince = PendingSince
        }, new TriggerOptions { OptedIn = optedIn }, now);
        PendingLoadoutId = decision.PendingLoadoutId;
        PendingSince = decision.PendingSince;
        return decision;
    }
}
