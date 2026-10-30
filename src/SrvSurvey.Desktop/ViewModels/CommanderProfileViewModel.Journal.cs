using System.Globalization;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class CommanderProfileViewModel
{
    private FrontierJournalShipState journalShip = new();
    private FrontierShipSnapshot? currentShipProjection;
    public FrontierShipSnapshot? CurrentShip => currentShipProjection ??= ReadCurrentShip();

    /// <summary>Attributes every profile reducer to the account active at each journal record.</summary>
    public void UpdateJournalState(
        string? initialFrontierId,
        IReadOnlyList<JournalEventEnvelope> entries,
        string? initialCommanderName = null
    )
    {
        if (detectedFrontierId is null)
        {
            return;
        }
        JournalEventEnvelope[] attributed = JournalCommanderEvents
            .ForCommander(entries, detectedFrontierId, initialFrontierId, initialCommanderName)
            .ToArray();
        UpdateJournalPowerplay(initialFrontierId, entries, initialCommanderName);
        UpdateJournalReputation(detectedCommanderName, attributed);
        UpdateJournalCommunityGoals(detectedCommanderName, attributed);
        UpdateJournalCarrierJump(detectedCommanderName, attributed);
        bool shipChanged = false;
        foreach (JournalEventEnvelope entry in attributed)
        {
            shipChanged |= journalShip.Apply(entry);
        }
        if (shipChanged)
        {
            ResetSnapshotProjectionCache();
            RebuildCurrentShipModuleGroups();
            RaiseSnapshotProperties();
        }
    }

    private FrontierShipSnapshot? ReadCurrentShip() =>
        IsViewingJournalCommander ? journalShip.Project(Snapshot) : Snapshot?.CurrentShip;

    /// <summary>Tracks jump state by carrier ID, so one carrier's requests and cancellations cannot affect another.</summary>
    public void UpdateJournalCarrierJump(string? commanderName, IReadOnlyList<JournalEventEnvelope> journalEvents)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        string? name = commanderName?.Trim();
        bool changed = !string.Equals(journalCarrierJumpCommanderName, name, StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            journalCarrierJumpCommanderName = name;
            journalCarrierJumps.Clear();
        }
        foreach (JournalEventEnvelope entry in journalEvents)
        {
            changed |= ApplyCarrierJournalEntry(entry);
        }
        if (changed)
        {
            carrierOperationRows = null;
            OnPropertyChanged(nameof(CarrierOperations));
        }
    }

    /// <summary>Ignores older observations and retires only the identified carrier's pending jump.</summary>
    private bool ApplyCarrierJournalEntry(JournalEventEnvelope entry)
    {
        if (
            entry.EventName
            is not ("CarrierJumpRequest" or "CarrierJumpCancelled" or "CarrierJump" or "CarrierLocation")
        )
        {
            return false;
        }
        string id = FirstNonEmpty(
            ReadJournalIdentifier(entry.Payload, "CarrierID"),
            ReadJournalIdentifier(entry.Payload, "MarketID")
        );
        if (id.Length == 0 && journalCarrierJumps.Count == 1)
        {
            id = journalCarrierJumps.Keys.Single();
        }
        if (id.Length == 0)
        {
            return false;
        }
        DateTimeOffset at = entry.Timestamp ?? now().ToUniversalTime();
        journalCarrierJumps.TryGetValue(id, out JournalCarrierJumpState? previous);
        if (previous is not null && at < previous.UpdatedAt)
        {
            return false;
        }
        if (entry.EventName == "CarrierJumpRequest")
        {
            string type = ReadJournalString(entry.Payload, "CarrierType");
            bool? squadron = type.Length == 0 ? previous?.Squadron : type == "SquadronCarrier";
            journalCarrierJumps[id] = new(
                FirstNonEmpty(
                    ReadJournalString(entry.Payload, "SystemName"),
                    ReadJournalString(entry.Payload, "StarSystem"),
                    ReadJournalString(entry.Payload, "Body")
                ),
                ReadJournalDateTimeOffset(entry.Payload, "DepartureTime"),
                at,
                squadron
            );
            return true;
        }
        if (
            entry.EventName != "CarrierJumpCancelled"
            && (
                previous is not { Destination.Length: > 0 }
                || entry.EventName == "CarrierLocation" && previous.DepartureAt is { } departure && at < departure
            )
        )
        {
            return false;
        }
        journalCarrierJumps[id] = new("", null, at, previous?.Squadron);
        return true;
    }

    private string EffectiveCarrierCurrentJump()
    {
        if (
            Snapshot is null
            || !string.Equals(
                Snapshot.CommanderName,
                journalCarrierJumpCommanderName,
                StringComparison.OrdinalIgnoreCase
            )
            || manuallySelectedFrontierId is not null
                && !string.Equals(manuallySelectedFrontierId, detectedFrontierId, StringComparison.OrdinalIgnoreCase)
        )
        {
            return NormalizeCurrentJump(Carrier?.CurrentJump);
        }
        JournalCarrierJumpState? state = SelectedJournalCarrierJump();
        DateTimeOffset? fetchedAt =
            carrierWorkspaceKind == CarrierWorkspaceKind.Personal
                ? Snapshot.CarrierFetchedAt
                : Snapshot.SquadronCarrierFetchedAt;
        return state is not null && (fetchedAt is null || state.UpdatedAt >= fetchedAt)
            ? state.Destination
            : NormalizeCurrentJump(Carrier?.CurrentJump);
    }

    /// <summary>Uses explicit carrier identities; legacy missing IDs are accepted only when the candidate is unambiguous.</summary>
    private JournalCarrierJumpState? SelectedJournalCarrierJump()
    {
        string? id = CarrierIdentity(Carrier);
        if (id is not null)
        {
            return journalCarrierJumps.GetValueOrDefault(id);
        }
        bool squadron = carrierWorkspaceKind == CarrierWorkspaceKind.Squadron;
        string? otherId = CarrierIdentity(squadron ? PersonalCarrier : SquadronCarrier);
        JournalCarrierJumpState[] candidates = journalCarrierJumps
            .Where(pair =>
                pair.Key != otherId && (squadron ? pair.Value.Squadron == true : pair.Value.Squadron != true)
            )
            .Select(pair => pair.Value)
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static string? CarrierIdentity(FrontierCarrierSnapshot? carrier)
    {
        if (carrier?.FuelData?.CarrierId is { } id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
        return carrier
            ?.DataPoints?.FirstOrDefault(point =>
                point.Path.EndsWith(".market.id", StringComparison.OrdinalIgnoreCase)
                || point.Path.EndsWith(".carrierId", StringComparison.OrdinalIgnoreCase)
                || point.Path.EndsWith(".marketId", StringComparison.OrdinalIgnoreCase)
                || point.Path.EndsWith("fleetcarrier.id", StringComparison.OrdinalIgnoreCase)
                || point.Path.EndsWith(".squadronCarrier.id", StringComparison.OrdinalIgnoreCase)
            )
            ?.Value;
    }

    private sealed record JournalCarrierJumpState(
        string Destination,
        DateTimeOffset? DepartureAt,
        DateTimeOffset UpdatedAt,
        bool? Squadron
    );
}
