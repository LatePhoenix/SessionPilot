using System.Text.RegularExpressions;

namespace SessionPilot.Core;

public enum PowerOwnerKind
{
    Unset,
    ProcessLasso,
    Windows
}

public sealed record PowerPlanInfo
{
    public required string Guid { get; init; }
    public required string Name { get; init; }
    public bool Active { get; init; }
}

public sealed record PowerDecision
{
    public required PowerOwnerKind Owner { get; init; }
    public bool Switched { get; init; }
    public required string Detail { get; init; }
}

public static class PowerPlanParser
{
    private static readonly Regex GuidPattern = new(@"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<PowerPlanInfo> Parse(string listing)
    {
        var plans = new List<PowerPlanInfo>();
        foreach (var raw in listing.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = GuidPattern.Match(raw);
            if (!match.Success)
            {
                continue;
            }

            var after = raw[(match.Index + match.Length)..];
            var open = after.LastIndexOf('(');
            var close = after.LastIndexOf(')');
            var name = open >= 0 && close > open ? after[(open + 1)..close].Trim() : "";
            plans.Add(new PowerPlanInfo
            {
                Guid = match.Value,
                Name = name,
                Active = raw.TrimEnd().EndsWith('*')
            });
        }

        return plans;
    }
}

public static class PowerOwnership
{
    public static PowerDecision Select(PowerOwnerKind requested, PowerOwnerKind recorded, bool exportExists)
    {
        if (requested == PowerOwnerKind.Unset)
        {
            return new PowerDecision { Owner = recorded, Switched = false, Detail = "No power owner is selected. Nothing was switched." };
        }

        if (recorded != PowerOwnerKind.Unset && recorded != requested)
        {
            return new PowerDecision
            {
                Owner = recorded,
                Switched = false,
                Detail = "A second power writer was refused. The recorded owner stays " + recorded + "."
            };
        }

        if (!exportExists)
        {
            return new PowerDecision
            {
                Owner = requested,
                Switched = false,
                Detail = "Owner " + requested + " is recorded. No power-rule export exists, so nothing was switched."
            };
        }

        return new PowerDecision
        {
            Owner = requested,
            Switched = false,
            Detail = "An export is noted, but this build does not switch power plans."
        };
    }

    public static PowerDecision Clear() => new()
    {
        Owner = PowerOwnerKind.Unset,
        Switched = false,
        Detail = "The recorded owner was cleared. Nothing was switched."
    };
}
