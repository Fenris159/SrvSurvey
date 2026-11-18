using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>The session reducers and retained snapshots shared with shell display and profile workflows.</summary>
internal sealed class JournalProjectionState
{
    internal JournalSessionState Session { get; } = new();

    internal ExplorationState Exploration { get; } = new();

    internal Dictionary<string, ExplorationSurveySourceCheckpoint> ConsumedExplorationRecords { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    internal ExobiologyState Exobiology { get; set; } = null!;

    internal CargoInventoryState CargoInventory { get; } = new();

    /// <summary>Verified provider cargo remains independent of local consumers that still suppress shared companion data.</summary>
    internal CargoInventoryState ProviderCargoInventory { get; } = new();

    internal bool AwaitFreshProviderCargoSnapshot { get; set; } = true;
    internal string? ProviderCargoFrontierId { get; set; }
    internal string? ProviderCargoJournalPath { get; set; }
    internal bool ProviderCargoSessionEnded { get; set; }
    internal DateTimeOffset? ProviderCargoSessionEndedAt { get; set; }

    internal string? LatestStatusSource { get; set; }

    internal EliteStatus? LatestStatus { get; set; }

    internal CargoSnapshot? LatestCargo { get; set; }

    internal ShipLockerSnapshot? LatestShipLocker { get; set; }

    internal bool AwaitFreshCargoSnapshot { get; set; }

    internal bool IsAwaitingCommanderIdentity { get; set; }

    internal DateTimeOffset? CompanionIdentityChangedAt { get; set; }

    internal DateTimeOffset LastIdleHousekeepingAt { get; set; }

    internal string? ProfileFrontierId { get; set; }

    internal string? ProfileCommanderName { get; set; }

    internal string? ProfileRavenApiKey { get; set; }

    internal bool ProfileRestoredFromDisk { get; set; }

    internal bool ProfileHasExplorationTotals { get; set; }

    internal bool ProfileIsOdyssey { get; set; } = true;

    internal void RememberConsumedExplorationRecords(IReadOnlyList<JournalEventEnvelope> entries)
    {
        foreach (JournalEventEnvelope entry in entries)
        {
            if (ExplorationSurveySourceCheckpoint.FromEntry(entry) is { } boundary)
            {
                ConsumedExplorationRecords[boundary.SourceName] = boundary;
            }
        }
    }
}
