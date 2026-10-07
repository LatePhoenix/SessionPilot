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
