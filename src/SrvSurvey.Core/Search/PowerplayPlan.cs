namespace SrvSurvey.Core.Search;

public readonly record struct PowerplaySpanshQuery(string IndexedState, string RequiredState);

/// <summary>
/// Decides which Spansh power index to read, how a row's standing is inferred, and which systems match a mining objective.
/// </summary>
public static class PowerplayPlan
{
    public const string Reinforce = "Reinforce";
    public const string Undermine = "Undermine";
    public const string Acquire = "Acquire";
    public const string AnyPower = "Any";
    public const string NoPower = "None";
    public const double FortifiedReachLy = 20;
    public const double StrongholdReachLy = 30;

    public static PowerplaySpanshQuery SpanshFilter(string objective, string requestedState)
    {
        bool openAcquisition = IsAcquire(objective) && IsUnspecified(requestedState);
        if (openAcquisition || IsLiveState(requestedState))
        {
            return new PowerplaySpanshQuery(PowerplayStanding.Unoccupied, openAcquisition ? "" : requestedState);
        }

        return new PowerplaySpanshQuery(IsUnspecified(requestedState) ? "" : requestedState, "");
    }

    public static string Infer(
        string controllingPower,
        string reportedState,
        IReadOnlyList<PowerplayProgress> progress
    ) => PowerplayStanding.Infer(controllingPower, reportedState, progress);

    public static bool UsesLiveConflict(string objective, string requestedState) =>
        SpanshFilter(objective, requestedState).IndexedState == PowerplayStanding.Unoccupied;

    public static double AcquisitionReachLy(string supporterState) =>
        supporterState.Equals("Stronghold", StringComparison.OrdinalIgnoreCase) ? StrongholdReachLy : FortifiedReachLy;

    public static bool IsAcquisitionTarget(MiningSystemResult candidate, string requestedState) =>
        candidate.Power.Length == 0
        && PowerplayStanding.IsAcquisitionState(candidate.PowerState)
        && (IsUnspecified(requestedState) || Same(candidate.PowerState, requestedState));

    public static bool Matches(string objective, MiningSystemResult system, string pledgedPower, string opposingPower)
    {
        if (IsAcquire(objective))
        {
            return PowerplayStanding.IsAcquisitionState(system.PowerState);
        }

        if (objective.Equals(Reinforce, StringComparison.OrdinalIgnoreCase))
        {
            return !IsExpansion(system) && MatchesPledge(system, pledgedPower);
        }

        if (objective.Equals(Undermine, StringComparison.OrdinalIgnoreCase))
        {
            return !IsExpansion(system) && MatchesOpposition(system, pledgedPower, opposingPower);
        }

        return true;
    }

    private static bool IsAcquire(string objective) => objective.Equals(Acquire, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnspecified(string state) =>
        state.Length == 0 || state.Equals(AnyPower, StringComparison.OrdinalIgnoreCase);

    private static bool IsLiveState(string state) =>
        state is PowerplayStanding.Unoccupied or PowerplayStanding.Expansion or PowerplayStanding.Contested;

    private static bool IsExpansion(MiningSystemResult system) => Same(system.PowerState, PowerplayStanding.Expansion);

    private static bool MatchesPledge(MiningSystemResult system, string pledgedPower)
    {
        if (pledgedPower.Equals(NoPower, StringComparison.Ordinal))
        {
            return system.Power.Length == 0;
        }

        return system.Power.Length > 0 && (IsUnspecified(pledgedPower) || Same(system.Power, pledgedPower));
    }

    private static bool MatchesOpposition(MiningSystemResult system, string pledgedPower, string opposingPower)
    {
        if (opposingPower.Equals(NoPower, StringComparison.Ordinal))
        {
            return system.Power.Length == 0;
        }

        return system.Power.Length > 0
            && (IsUnspecified(opposingPower) || Same(system.Power, opposingPower))
            && (IsUnspecified(pledgedPower) || !Same(system.Power, pledgedPower));
    }

    private static bool Same(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
