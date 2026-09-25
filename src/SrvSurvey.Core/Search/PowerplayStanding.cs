namespace SrvSurvey.Core.Search;

public readonly record struct PowerplayProgress(string Power, double Progress);

/// <summary>
/// Spansh keeps <c>power_state</c> at the last settled value and leaves <c>controlling_power</c> empty
/// until the Thursday tick. Live acquisition position is <c>power_conflict_progress</c>, where 1.0 is the
/// 120,000-point acquire line and 0.30 is the 36,000-point conflict line.
/// </summary>
public static class PowerplayStanding
{
    public const double ConflictThreshold = 0.30;
    public const double AcquireThreshold = 1.0;
    public const string Unoccupied = "Unoccupied";
    public const string Expansion = "Expansion";
    public const string Contested = "Contested";

    public static string Infer(string controllingPower, string reportedState, IReadOnlyList<PowerplayProgress> progress)
    {
        if (controllingPower.Length > 0)
        {
            return reportedState.Length > 0 ? reportedState : "Exploited";
        }

        if (progress.Count == 0)
        {
            return reportedState;
        }

        int pastConflict = progress.Count(entry => entry.Progress >= ConflictThreshold);
        if (pastConflict >= 2)
        {
            return Contested;
        }

        return pastConflict == 1 ? Expansion : Unoccupied;
    }

    public static bool IsAcquisitionState(string state) =>
        state.Equals(Unoccupied, StringComparison.OrdinalIgnoreCase)
        || state.Equals(Expansion, StringComparison.OrdinalIgnoreCase)
        || state.Equals(Contested, StringComparison.OrdinalIgnoreCase);
}
