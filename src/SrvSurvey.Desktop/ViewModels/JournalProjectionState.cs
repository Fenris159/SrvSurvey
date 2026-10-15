using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>The session reducers and retained snapshots shared with shell display and profile workflows.</summary>
internal sealed class JournalProjectionState
{
    internal JournalSessionState Session { get; } = new();

    internal ExplorationState Exploration { get; } = new();

    internal ExobiologyState Exobiology { get; set; } = null!;

    internal CargoInventoryState CargoInventory { get; } = new();

    /// <summary>Verified provider cargo remains independent of local consumers that still suppress shared companion data.</summary>
    internal CargoInventoryState ProviderCargoInventory { get; } = new();

    internal bool AwaitFreshProviderCargoSnapshot { get; set; } = true;
    internal string? ProviderCargoFrontierId { get; set; }
    internal string? ProviderCargoJournalPath { get; set; }

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

    internal bool ProfileIsOdyssey { get; set; } = true;
}
