using System.Text.Json;
using Microsoft.Data.Sqlite;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Exploration.History;

public sealed partial class ExplorationHistoryCatalog
{
    private static readonly Lazy<ExobiologyReferenceCatalog> NotableCodex = new(
        ExobiologyReferenceCatalog.LoadEmbedded
    );

    private bool BelongsToCommander(SourceContext context) =>
        string.Equals(context.FrontierId, frontierId, StringComparison.OrdinalIgnoreCase) && context.IsLive == isLive;

    private void BackfillNotables(
        SqliteConnection connection,
        JournalSource source,
        long through,
        CancellationToken token
    )
    {
        SourceContext context = ReadContinuationContext(connection, source);
        context.JournalPartNumber = source.FilePart?.Number;
        foreach (JournalLine line in ReadLines(source.Path, 0, token, through))
        {
            if (
                line.Content is not { } text
                || !JournalEventEnvelope.TryParse(text, out JournalEventEnvelope? entry, out _)
                || entry is null
            )
            {
                continue;
            }
            ApplyContext(context, entry);
            if (BelongsToCommander(context))
            {
                ApplyNotableDiscovery(connection, context, entry);
            }
        }
    }

    private static void ApplyNotableDiscovery(
        SqliteConnection connection,
        SourceContext context,
        JournalEventEnvelope entry
    )
    {
        ExplorationNotables flags = ClassifyNotable(entry);
        if (flags == ExplorationNotables.None || EnsureSystem(connection, context, entry.Payload) is not { } system)
        {
            return;
        }
        Execute(
            connection,
            """
            INSERT INTO notable_systems(system_key,flags) VALUES(@system,@flags)
            ON CONFLICT(system_key) DO UPDATE SET flags=notable_systems.flags | excluded.flags;
            """,
            ("@system", system),
            ("@flags", (int)flags)
        );
    }

    private static ExplorationNotables ClassifyNotable(JournalEventEnvelope entry) =>
        entry.EventName switch
        {
            "Scan" => ClassifyNotableScan(entry.Payload),
            "CodexEntry" => ClassifyNotableCodex(entry.Payload),
            "FSSSignalDiscovered" when Text(entry.Payload, "SignalName") == "$Fixed_Event_Life_Cloud;" =>
                ExplorationNotables.StellarPhenomena,
            _ => ExplorationNotables.None,
        };

    private static ExplorationNotables ClassifyNotableScan(JsonElement root)
    {
        if (string.Equals(Text(root, "ScanType"), "NavBeaconDetail", StringComparison.OrdinalIgnoreCase))
        {
            return ExplorationNotables.None;
        }
        ExplorationNotables flags = Text(root, "PlanetClass") switch
        {
            "Earthlike body" => ExplorationNotables.EarthLikeWorld,
            "Water world" => ExplorationNotables.WaterWorld,
            "Ammonia world" => ExplorationNotables.AmmoniaWorld,
            _ => ExplorationNotables.None,
        };
        if (Text(root, "TerraformState") == "Terraformable")
        {
            flags |= ExplorationNotables.TerraformableWorld;
        }
        return flags
            | (
                Text(root, "StarType") switch
                {
                    "N" or "NS" => ExplorationNotables.NeutronStar,
                    "H" or "BH" or "SupermassiveBlackHole" => ExplorationNotables.BlackHole,
                    _ => ExplorationNotables.None,
                }
            );
    }

    private static ExplorationNotables ClassifyNotableCodex(JsonElement root)
    {
        ExobiologyReference? reference =
            NotableCodex.Value.FindByVariant(Text(root, "Name"))
            ?? NotableCodex.Value.FindByEntryId(Integer(root, "EntryID") ?? 0);
        if (reference?.HudCategory is "Cloud" or "Anomaly")
        {
            return ExplorationNotables.StellarPhenomena;
        }
        return
            reference?.HudCategory == "Tourist"
            && reference.VariantName.StartsWith("$Codex_Ent_Green_", StringComparison.Ordinal)
            ? ExplorationNotables.GreenGasGiant
            : ExplorationNotables.None;
    }
}
