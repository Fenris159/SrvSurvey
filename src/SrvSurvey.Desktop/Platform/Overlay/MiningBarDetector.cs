using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

public enum MiningBarState { Unknown, Absent, Present }

public sealed record MiningBarAnalysis(MiningBarState[] Slots, double OffsetX, double OffsetY)
{
    internal bool HasAnchor { get; init; }
    // Identity evidence survives an unreadable image; Slots describes only the current image.
    internal int AnchorSlots { get; init; }
    internal double[] BarScores { get; init; } = [];
    public static MiningBarAnalysis Unknown() => new(new MiningBarState[6], 0, 0);
}

/// <summary>Groups bars of the selected color and preserves their calibrated rig identities.</summary>
public static class MiningBarDetector
{
    public static MiningBarAnalysis Analyze(IFssPixelSource pixels, MiningDetectionSettings settings,
        MiningBarAnalysis? previous = null)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(settings);
        return MiningColorBarDetector.Analyze(pixels, settings.Normalize(), previous);
    }
}
/// <summary>Adds immediately, but requires uninterrupted empty readings for three seconds before removal.</summary>
public sealed class MiningBarConfirmation(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private readonly MiningBarState[] stable = new MiningBarState[6];
    private readonly long?[] emptySince = new long?[6];
    private long? lastReading;
    private (double X, double Y)? settledPosition;
    private long? settlingSince;
    public bool IsSettling { get; private set; }
    public IReadOnlyList<MiningBarState> States => stable;

    public void Apply(MiningBarAnalysis analysis)
    {
        var now = time.GetTimestamp();
        // A capture stall does not prove the HUD stayed empty throughout the gap.
        if (lastReading is { } last && time.GetElapsedTime(last, now) > TimeSpan.FromSeconds(1.5))
        {
            Array.Clear(emptySince);
            if (settledPosition is not null) { IsSettling = true; settlingSince = null; }
        }
        lastReading = now;
        if (WaitForSteadyHud(analysis, now))
        {
            Array.Clear(emptySince);
            Array.Clear(stable);
            return;
        }
        for (var i = 0; i < 6; i++)
        {
            switch (analysis.Slots[i])
            {
                case MiningBarState.Present:
                    emptySince[i] = null;
                    stable[i] = MiningBarState.Present;
                    break;
                case MiningBarState.Absent:
                    emptySince[i] ??= now;
                    stable[i] = time.GetElapsedTime(emptySince[i]!.Value, now) >= TimeSpan.FromSeconds(3)
                        ? MiningBarState.Absent : MiningBarState.Unknown;
                    break;
                default:
                    emptySince[i] = null;
                    stable[i] = MiningBarState.Unknown;
                    break;
            }
        }
    }

    private bool WaitForSteadyHud(MiningBarAnalysis analysis, long now)
    {
        if (!analysis.HasAnchor) return false;
        if (analysis.Slots.All(state => state == MiningBarState.Unknown))
        {
            if (settledPosition is not null) { IsSettling = true; settlingSince = null; }
            return IsSettling;
        }
        if (settledPosition is { } origin
            && Math.Pow(analysis.OffsetX - origin.X, 2) + Math.Pow(analysis.OffsetY - origin.Y, 2) > 16)
        {
            // Coordinates are normalized to a roughly 44-pixel circle diameter by MiningHudImage.
            // Compare against the settling origin so slow accumulated drift also pauses writes.
            IsSettling = true;
            settlingSince = now;
            settledPosition = (analysis.OffsetX, analysis.OffsetY);
        }
        settledPosition ??= (analysis.OffsetX, analysis.OffsetY);
        if (!IsSettling) return false;
        settlingSince ??= now;
        if (time.GetElapsedTime(settlingSince.Value, now) < TimeSpan.FromSeconds(1)) return true;
        IsSettling = false;
        return false;
    }
}
