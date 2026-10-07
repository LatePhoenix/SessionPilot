namespace SessionPilot.Core;

public sealed record TriggerOptions
{
    public bool OptedIn { get; init; }
    public TimeSpan Debounce { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MinimumDwell { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan ExitGrace { get; init; } = TimeSpan.FromSeconds(45);
}

public sealed record TriggerSnapshot
{
    public bool ManualOverrideActive { get; init; }
    public string? ManualLoadoutId { get; init; }
    public string? ActiveLoadoutId { get; init; }
    public string? PendingLoadoutId { get; init; }
    public DateTimeOffset? PendingSince { get; init; }
    public DateTimeOffset? SessionEndedAt { get; init; }
    public IReadOnlyList<string> SignaledLoadouts { get; init; } = [];
}

public sealed record TriggerDecision
{
    public required string Action { get; init; }
    public string? LoadoutId { get; init; }
    public string? PendingLoadoutId { get; init; }
    public DateTimeOffset? PendingSince { get; init; }
    public required string Reason { get; init; }
}

public static class TriggerEngine
{
    private static readonly string[] Precedence =
    [
        "vrchat-diagnostic",
        "vrchat-social",
        "vrchat-steamvr",
        "desktop-gaming",
        "development-local-ai",
        "development-build-heavy",
        "development-interactive",
        "media-playback",
        "background-batch",
        "balanced"
    ];

    public static TriggerDecision Evaluate(TriggerSnapshot snapshot, TriggerOptions options, DateTimeOffset now)
    {
        if (!options.OptedIn)
        {
            return Hold("Triggers are off. Detection does not apply a loadout or request elevation.");
        }

        if (snapshot.ManualOverrideActive)
        {
            return new TriggerDecision
            {
                Action = "hold",
                LoadoutId = snapshot.ManualLoadoutId ?? snapshot.ActiveLoadoutId,
                Reason = "A manual selection overrides automatic signals."
            };
        }

        var desired = Highest(snapshot.SignaledLoadouts);
        if (desired is null)
        {
            if (snapshot.SessionEndedAt is null || snapshot.ActiveLoadoutId is null || snapshot.ActiveLoadoutId == "balanced")
            {
                return Hold("No workload signal.");
            }

            if (now - snapshot.SessionEndedAt.Value < options.ExitGrace)
            {
                return Hold("Session exit grace is still running.");
            }

            return new TriggerDecision
            {
                Action = "suggest",
                LoadoutId = "balanced",
                Reason = "The session grace period elapsed. Restoration still requires an approved plan."
            };
        }

        if (string.Equals(desired, snapshot.ActiveLoadoutId, StringComparison.OrdinalIgnoreCase))
        {
            return Hold("The highest-precedence signal is already active.");
        }

        if (!string.Equals(desired, snapshot.PendingLoadoutId, StringComparison.OrdinalIgnoreCase) || snapshot.PendingSince is null)
        {
            return new TriggerDecision
            {
                Action = "wait",
                PendingLoadoutId = desired,
                PendingSince = now,
                Reason = "The signal must stay stable through debounce and minimum dwell."
            };
        }

        var elapsed = now - snapshot.PendingSince.Value;
        if (elapsed < options.Debounce || elapsed < options.MinimumDwell)
        {
            return new TriggerDecision
            {
                Action = "wait",
                PendingLoadoutId = desired,
                PendingSince = snapshot.PendingSince,
                Reason = "Debounce or minimum dwell has not elapsed."
            };
        }

        return new TriggerDecision
        {
            Action = "suggest",
            LoadoutId = desired,
            Reason = "The signal outranked other workloads and remained stable. Application still requires an approved plan."
        };
    }

    private static TriggerDecision Hold(string reason) => new() { Action = "hold", Reason = reason };

    private static string? Highest(IReadOnlyList<string> signaled)
    {
        foreach (var id in Precedence)
        {
            if (signaled.Any(item => string.Equals(item, id, StringComparison.OrdinalIgnoreCase)))
            {
                return id;
            }
        }

        return null;
    }
}
