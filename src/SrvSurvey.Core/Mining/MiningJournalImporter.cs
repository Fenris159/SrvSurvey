using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Mining;

public static class MiningJournalImporter
{
    public static async Task<MiningCommanderData> ReadAsync(
        IEnumerable<string> paths,
        string frontierId,
        CancellationToken cancellationToken = default
    )
    {
        var result = new MiningWorkspaceState(new MiningCommanderData());
        foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase))
        {
            var context = new JournalSessionState();
            await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
            {
                if (!JournalEventEnvelope.TryParse(line, out var entry, out _) || entry is null)
                {
                    continue;
                }

                context.Apply(entry);
                if (
                    context.FrontierId != frontierId
                    || entry.EventName
                        is not (
                            "Scan"
                            or "SAASignalsFound"
                            or "MissionAccepted"
                            or "MissionCompleted"
                            or "MissionAbandoned"
                            or "MissionFailed"
                            or "CargoDepot"
                        )
                )
                {
                    continue;
                }

                result.Apply(
                    entry,
                    true,
                    context.SystemName ?? "",
                    context.BodyName ?? "",
                    context.ShipType ?? "",
                    context.StarPosition
                );
            }
        }
        result.Synchronize();
        return result.Data;
    }
}
