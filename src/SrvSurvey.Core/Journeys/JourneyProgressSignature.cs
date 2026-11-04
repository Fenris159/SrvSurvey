using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace SrvSurvey.Core.Journeys;

internal static class JourneyProgressSignature
{
    internal static bool IsTrusted(JourneyDocument journey) =>
        journey.ObservationHistory is { } history
        && history.Through == journey.Watermark
        && string.Equals(history.ProjectionSignature, Compute(journey), StringComparison.Ordinal);

    // Metadata and note saves may change independently of journal progress.
    internal static string Compute(JourneyDocument journey)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                Watermark = journey.Watermark.UtcTicks,
                Visits = journey.VisitedSystems.Select(visit => new
                {
                    visit.StarSystem.Name,
                    visit.StarSystem.SystemAddress,
                    X = visit.StarSystem.Position.X.ToString("R", CultureInfo.InvariantCulture),
                    Y = visit.StarSystem.Position.Y.ToString("R", CultureInfo.InvariantCulture),
                    Z = visit.StarSystem.Position.Z.ToString("R", CultureInfo.InvariantCulture),
                    Arrived = visit.Arrived.UtcTicks,
                    Departed = visit.Departed?.UtcTicks,
                    visit.Counts.BodyScans,
                    visit.Counts.DetailedSurfaceScans,
                    visit.Counts.NewCodexEntries,
                    visit.Counts.Organisms,
                    visit.Counts.Touchdowns,
                    visit.Counts.BodyCount,
                    visit.Counts.Screenshots,
                    visit.Counts.ExobiologyRewards,
                    visit.Counts.ExplorationRewards,
                    visit.Counts.Stars,
                    BodyCountObservedAt = visit.BodyCountObservedAt?.UtcTicks,
                    visit.BodyCountIsFinal,
                    LandedOn = Ordered(visit.LandedOn),
                    SubCategories = Ordered(visit.SubCategories),
                    SurfaceSignals = Ordered(visit.SurfaceSignals),
                    FssSignals = Ordered(visit.FssSignals),
                    CodexScanned = visit.CodexScanned?.Order().ToArray() ?? [],
                    BodiesScanned = visit.BodiesScanned?.Order().ToArray() ?? [],
                    CodexNew = visit.CodexNew?.Order(StringComparer.Ordinal).ToArray() ?? [],
                }),
            }
        );
        return "v1:" + Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static KeyValuePair<string, int>[] Ordered(IReadOnlyDictionary<string, int>? values) =>
        values?.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray() ?? [];
}
