using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public sealed partial class ColonizationDeliveryRecovery
{
    private const int MaximumBuildSiteRepairVisits = 50;

    private readonly Lock buildSiteRepairLock = new();
    private readonly Queue<ColonizationBuildSiteRepairVisit> buildSiteRepairVisits = new();
    private readonly HashSet<ColonizationBuildSiteRepairVisit> buildSiteRepairVisitSet = [];
    private readonly HashSet<(long SystemAddress, long MarketId, string StationKey)> buildSiteRepairsInFlight = [];
    private ColonizationBuildSiteRepairWarning? buildSiteRepairWarning;
    private int buildSiteRepairContextVersion;

    /// <summary>The current system's docking repair failure, independent of other synchronization outcomes.</summary>
    public ColonizationBuildSiteRepairWarning? BuildSiteRepairWarning => buildSiteRepairWarning;

    /// <summary>Discards the repair warning and any repair results still in flight for the previous context.</summary>
    private void InvalidateBuildSiteRepairContext()
    {
        buildSiteRepairContextVersion++;
        SetBuildSiteRepairWarning(null);
    }

    /// <summary>Expires the repair warning when a docking batch moves to another known system.</summary>
    private void ExpireBuildSiteRepairWarningAfterSystemChange(ColonizationDockingSnapshot? dockBefore)
    {
        long? previousSystemAddress =
            dockBefore?.SystemAddress ?? buildSiteRepairWarning?.SystemAddress ?? currentSystemAddress;
        if (
            previousSystemAddress is > 0
            && constructionState.CurrentDock is { SystemAddress: > 0 } nextDock
            && previousSystemAddress != nextDock.SystemAddress
        )
        {
            InvalidateBuildSiteRepairContext();
        }
    }

    /// <summary>
    /// Repairs eligible docked sites and keeps failures scoped to the active system and commander context.
    /// </summary>
    private async Task<ColonizationDeliveryNotice?> SynchronizeBuildSiteRepairAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken = default
    )
    {
        if (apiKey is not { } repairApiKey)
        {
            return null;
        }

        JsonElement root = journalEvent.Payload;
        string? stationName = GetJournalString(root, "StationName");
        string? stationType = GetJournalString(root, "StationType");
        long? systemAddress = GetJournalInt64(root, "SystemAddress");
        long? marketId = GetJournalInt64(root, "MarketID");
        bool isConstructionShip = stationName?.Contains("ColonisationShip", StringComparison.Ordinal) == true;
        if (
            systemAddress is not > 0
            || marketId is not > 0
            || string.IsNullOrWhiteSpace(stationName)
            || !ColonizationBuildSiteRepair.IsPlayerColonyMarketId(marketId.Value)
            || ColonizationBuildSiteRepair.ShouldSkipDockContext(stationType, stationName, isConstructionShip)
        )
        {
            return null;
        }

        string stationKey = ColonizationBuildSiteRepair.NormalizeDockStationName(stationName).ToLowerInvariant();
        if (stationKey.Length == 0)
        {
            return null;
        }

        var visit = new ColonizationBuildSiteRepairVisit(marketId.Value, stationKey);
        (long, long, string stationKey) inFlight = (systemAddress.Value, marketId.Value, stationKey);
        lock (buildSiteRepairLock)
        {
            if (buildSiteRepairVisitSet.Contains(visit) || !buildSiteRepairsInFlight.Add(inFlight))
            {
                return null;
            }
        }

        int contextVersion = buildSiteRepairContextVersion;
        try
        {
            return await RepairBuildSiteAsync(
                journalEvent,
                visit,
                systemAddress.Value,
                stationName,
                repairApiKey,
                contextVersion,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or TaskCanceledException
                        or ArgumentException
            )
        {
            ReportBuildSiteRepairFailure(journalEvent, systemAddress.Value, contextVersion, exception.Message);
            return null;
        }
        finally
        {
            lock (buildSiteRepairLock)
            {
                buildSiteRepairsInFlight.Remove(inFlight);
            }
        }
    }

    /// <summary>
    /// Looks up and optionally patches a site, clearing its warning after a successful repair or no-op lookup.
    /// </summary>
    private async Task<ColonizationDeliveryNotice?> RepairBuildSiteAsync(
        JournalEventEnvelope journalEvent,
        ColonizationBuildSiteRepairVisit visit,
        long systemAddress,
        string stationName,
        string repairApiKey,
        int contextVersion,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<ColonizationSystemSite> sites = await GetSystemSitesForRepairAsync(
            systemAddress,
            cancellationToken: cancellationToken
        );
        ColonizationBuildSiteRepairPlan? plan = ColonizationBuildSiteRepair.CreatePlan(
            sites,
            stationName,
            visit.MarketId
        );
        if (plan is null)
        {
            ClearBuildSiteRepairWarning(systemAddress, contextVersion);
            return null;
        }

        if (string.IsNullOrWhiteSpace(plan.Site.Id))
        {
            ReportBuildSiteRepairFailure(journalEvent, systemAddress, contextVersion, detail: null);
            return null;
        }

        await client.PatchSystemSiteAsync(
            systemAddress.ToString(CultureInfo.InvariantCulture),
            plan.Site.Id,
            plan.CreatePatch(),
            repairApiKey,
            cancellationToken
        );
        RememberBuildSiteRepairVisit(visit);
        ClearBuildSiteRepairWarning(systemAddress, contextVersion);
        if (contextVersion != buildSiteRepairContextVersion)
        {
            return null;
        }

        return new(
            plan.Field == ColonizationBuildSiteRepairField.MarketId
                ? ColonizationDeliveryNoticeKind.BuildSiteMarketRepaired
                : ColonizationDeliveryNoticeKind.BuildSiteNameRepaired,
            plan.NormalizedStationName
        );
    }

    /// <summary>
    /// Shows a repair failure only while the request's system and context are still active.
    /// A null detail means the matched site has no persisted ID.
    /// </summary>
    private void ReportBuildSiteRepairFailure(
        JournalEventEnvelope journalEvent,
        long systemAddress,
        int contextVersion,
        string? detail
    )
    {
        string? systemName = GetJournalString(journalEvent.Payload, "StarSystem");
        bool differentSystem;
        if (currentSystemAddress is > 0)
        {
            differentSystem = currentSystemAddress != systemAddress;
        }
        else if (currentSystemName is not null && systemName is not null)
        {
            differentSystem = !string.Equals(currentSystemName, systemName, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            differentSystem =
                constructionState.CurrentDock is { SystemAddress: > 0 } activeDock
                && activeDock.SystemAddress != systemAddress;
        }
        if (contextVersion != buildSiteRepairContextVersion || differentSystem)
        {
            return;
        }

        string systemLabel = systemName ?? systemAddress.ToString(CultureInfo.InvariantCulture);
        SetBuildSiteRepairWarning(
            new ColonizationBuildSiteRepairWarning(systemAddress, journalEvent.EventName, systemLabel, detail)
        );
    }

    /// <summary>
    /// Clears only the recovered system's warning, leaving unrelated Raven status untouched.
    /// </summary>
    private void ClearBuildSiteRepairWarning(long systemAddress, int contextVersion)
    {
        if (contextVersion == buildSiteRepairContextVersion && buildSiteRepairWarning?.SystemAddress == systemAddress)
        {
            SetBuildSiteRepairWarning(null);
        }
    }

    /// <summary>
    /// Updates the independent repair warning and notifies the observer when it changes.
    /// </summary>
    private void SetBuildSiteRepairWarning(ColonizationBuildSiteRepairWarning? warning)
    {
        if (buildSiteRepairWarning == warning)
        {
            return;
        }

        buildSiteRepairWarning = warning;
        observer.BuildSiteRepairWarningChanged();
    }

    /// <summary>Loads planned sites with bounded retries and caller cancellation.</summary>
    private async Task<IReadOnlyList<ColonizationSystemSite>> GetSystemSitesForRepairAsync(
        long systemAddress,
        CancellationToken cancellationToken = default
    )
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await client.GetSystemSitesAsync(
                    systemAddress.ToString(CultureInfo.InvariantCulture),
                    cancellationToken
                );
            }
            catch (Exception exception) when (attempt < 2 && exception is HttpRequestException or TaskCanceledException)
            {
                await delayAsync(TimeSpan.FromSeconds(1.5 * (attempt + 1)), cancellationToken);
                attempt++;
            }
        }
    }

    private void RememberBuildSiteRepairVisit(ColonizationBuildSiteRepairVisit visit)
    {
        lock (buildSiteRepairLock)
        {
            if (!buildSiteRepairVisitSet.Add(visit))
            {
                return;
            }

            if (buildSiteRepairVisits.Count == MaximumBuildSiteRepairVisits)
            {
                buildSiteRepairVisitSet.Remove(buildSiteRepairVisits.Dequeue());
            }

            buildSiteRepairVisits.Enqueue(visit);
            try
            {
                store.SaveBuildSiteRepairVisits(buildSiteRepairVisits);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The server repair succeeded; a cache write failure must not
                // report that successful remote change as failed.
            }
        }
    }
}
