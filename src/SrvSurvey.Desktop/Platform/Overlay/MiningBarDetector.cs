using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

public enum MiningBarState { Unknown, Absent, Present }

public sealed record MiningBarAnalysis(MiningBarState[] Slots, double OffsetX, double OffsetY)
{
    internal bool HasAnchor { get; init; }
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
    public IReadOnlyList<MiningBarState> States => stable;

    public void Apply(MiningBarAnalysis analysis)
    {
        var now = time.GetTimestamp();
        // A capture stall does not prove the HUD stayed empty throughout the gap.
        if (lastReading is { } last && time.GetElapsedTime(last, now) > TimeSpan.FromSeconds(1.5))
            Array.Clear(emptySince);
        lastReading = now;
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
}
