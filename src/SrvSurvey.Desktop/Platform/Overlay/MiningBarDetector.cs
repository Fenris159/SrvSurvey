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
/// <summary>Only emits transitions after a baseline, never treats lost visibility as a removed bar.</summary>
public sealed class MiningBarConfirmation
{
    private readonly MiningBarState[] stable = new MiningBarState[6];
    private readonly MiningBarState[] candidate = new MiningBarState[6];
    private readonly int[] counts = new int[6];
    public IReadOnlyList<MiningBarState> States => stable;
    public IReadOnlyList<int> Disappeared { get; private set; } = [];

    public int[] Apply(MiningBarAnalysis analysis)
    {
        var appeared = new List<int>();
        var disappeared = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            var next = analysis.Slots[i];
            if (next == MiningBarState.Unknown)
            {
                counts[i] = 0;
                continue;
            }
            counts[i] = candidate[i] == next ? Math.Min(3, counts[i] + 1) : 1;
            candidate[i] = next;
            if (counts[i] < 3) continue;
            if (stable[i] == MiningBarState.Absent && next == MiningBarState.Present) appeared.Add(i + 1);
            if (stable[i] == MiningBarState.Present && next == MiningBarState.Absent) disappeared.Add(i + 1);
            stable[i] = next;
        }
        Disappeared = disappeared;
        return appeared.ToArray();
    }
}
