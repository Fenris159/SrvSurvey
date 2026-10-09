using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Words colonization delivery recovery outcomes for the colonization workspace status lines.</summary>
public static class ColonizationDeliveryMessages
{
    /// <summary>Joins outcomes one per line, in the order they occurred.</summary>
    public static string Describe(IEnumerable<ColonizationDeliveryNotice> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        return string.Join(Environment.NewLine, notices.Select(Describe));
    }

    /// <summary>Words one outcome.</summary>
    public static string Describe(ColonizationDeliveryNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        return DescribeProject(notice)
            ?? DescribeCarrierCargo(notice)
            ?? DescribeCarrierSync(notice)
            ?? throw new ArgumentOutOfRangeException(nameof(notice), notice.Kind, "Unknown delivery notice.");
    }

    /// <summary>Words the docking repair warning for the current system.</summary>
    public static string Describe(ColonizationBuildSiteRepairWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);
        return $"Raven project sync skipped {warning.EventName} in {warning.SystemLabel}: "
            + (
                warning.Detail
                ?? "The matched Raven site has no persisted ID; its market information could not be repaired."
            );
    }

    private static string? DescribeProject(ColonizationDeliveryNotice notice) =>
        notice.Kind switch
        {
            ColonizationDeliveryNoticeKind.LiveEventSkipped => $"Raven project sync skipped {notice.Name}: "
                + notice.Detail,
            ColonizationDeliveryNoticeKind.ArchitectNeedsApiKey =>
                "Raven architect update was not sent because this commander has no saved API key.",
            ColonizationDeliveryNoticeKind.ArchitectNeedsSystem =>
                "Raven architect update was not sent because the current system is unknown.",
            ColonizationDeliveryNoticeKind.ArchitectRegistered =>
                $"Registered {notice.Name} as the Raven architect for {notice.Detail}.",
            ColonizationDeliveryNoticeKind.ProjectLinked =>
                $"Linked Raven project {notice.Name} into the active list for this construction site.",
            ColonizationDeliveryNoticeKind.UntrackedProjectLoaded =>
                $"Loaded untracked Raven project {notice.Name} for this construction site.",
            ColonizationDeliveryNoticeKind.ProjectMetadataUpdated =>
                $"Updated Raven project metadata for {notice.Name}.",
            ColonizationDeliveryNoticeKind.BuildSiteMarketRepaired => $"Repaired Raven Market Info for {notice.Name}.",
            ColonizationDeliveryNoticeKind.BuildSiteNameRepaired => $"Repaired the Raven site name for {notice.Name}.",
            ColonizationDeliveryNoticeKind.ContributionProjectUnknown =>
                "Raven did not identify a project for the recorded construction contribution.",
            ColonizationDeliveryNoticeKind.ContributionAlreadyRetained =>
                "This construction delivery is already retained for reconciliation.",
            ColonizationDeliveryNoticeKind.ContributionPublished =>
                $"Published {notice.Count:N0} contributed cargo units to {notice.Name}.",
            ColonizationDeliveryNoticeKind.DepotProjectUnknown =>
                "Raven did not identify a project for the current construction depot.",
            ColonizationDeliveryNoticeKind.ProjectMarkedComplete => $"Marked Raven project {notice.Name} complete.",
            ColonizationDeliveryNoticeKind.RequirementsAboveSupportedTotal =>
                "Raven project sync rejected construction requirements above the supported total.",
            ColonizationDeliveryNoticeKind.RemainingUpdatedAfterContribution =>
                $"Updated Raven remaining cargo after contribution for {notice.Name}.",
            ColonizationDeliveryNoticeKind.RequirementsUpdated =>
                $"Updated Raven construction requirements for {notice.Name}.",
            ColonizationDeliveryNoticeKind.ContributionOutcomeUncertain =>
                "A construction contribution has an uncertain server outcome and is retained. Verify its credit on Raven before resubmitting.",
            ColonizationDeliveryNoticeKind.ContributionPendingFailed => "Construction contributions remain pending: "
                + notice.Detail,
            ColonizationDeliveryNoticeKind.ContributionRequirementsPendingFailed =>
                "Delivery credit is recorded; remaining construction requirements will retry: " + notice.Detail,
            ColonizationDeliveryNoticeKind.ContributionRecoveryNotSaved =>
                "Construction deliveries are retained in memory, but their recovery file could not be saved: "
                    + notice.Detail,
            _ => null,
        };

    private static string? DescribeCarrierCargo(ColonizationDeliveryNotice notice) =>
        notice.Kind switch
        {
            ColonizationDeliveryNoticeKind.SquadronCargoDiffSkipped =>
                "Raven project sync skipped squadron cargo diff: " + notice.Detail,
            ColonizationDeliveryNoticeKind.SquadronCargoQueued =>
                $"Queued {notice.Count:N0} squadron Fleet Carrier cargo update(s) until dock baseline finishes.",
            ColonizationDeliveryNoticeKind.CarrierCargoQueued =>
                $"Queued {notice.Count:N0} Fleet Carrier cargo update(s) until dock baseline finishes.",
            ColonizationDeliveryNoticeKind.CarrierCargoQueuedBehindUnconfirmed =>
                "Fleet Carrier cargo update queued behind an unconfirmed adjustment.",
            ColonizationDeliveryNoticeKind.CarrierCargoAdjusted =>
                $"Updated {notice.Count:N0} linked Fleet Carrier cargo entry(s) from {notice.Name}.",
            ColonizationDeliveryNoticeKind.CarrierBaselineLoaded =>
                $"Loaded Raven Colonial cargo baseline for {notice.Name}.",
            ColonizationDeliveryNoticeKind.CarrierBaselineNotLoaded => "Fleet Carrier dock baseline was not loaded: "
                + notice.Detail,
            ColonizationDeliveryNoticeKind.CarrierCargoPendingUncertain =>
                "Fleet Carrier updates remain pending; refresh its market to reconcile an uncertain write.",
            ColonizationDeliveryNoticeKind.CarrierCargoPendingConcurrentChange =>
                "Fleet Carrier updates remain pending because Raven cargo changed concurrently. Refresh its market to reconcile.",
            ColonizationDeliveryNoticeKind.CarrierCargoPendingFailed => "Fleet Carrier updates remain pending: "
                + notice.Detail,
            ColonizationDeliveryNoticeKind.CarrierCargoRecoveryNotSaved =>
                "Cargo updates are retained in memory, but their recovery file could not be saved: " + notice.Detail,
            ColonizationDeliveryNoticeKind.QueuedCarrierCargoRetained =>
                "Queued Fleet Carrier updates are retained for reconciliation: " + notice.Detail,
            _ => null,
        };

    private static string? DescribeCarrierSync(ColonizationDeliveryNotice notice) =>
        notice.Kind switch
        {
            ColonizationDeliveryNoticeKind.CarrierPublishing => $"Publishing {notice.Name} to Raven Colonial...",
            ColonizationDeliveryNoticeKind.CarrierPublishedAwaitingMarket => $"Published and linked {notice.Name}. "
                + "Open its commodity market to synchronize cargo.",
            ColonizationDeliveryNoticeKind.CarrierPublishedCargoCurrent => $"Published and linked {notice.Name}; "
                + "its cargo is already current.",
            ColonizationDeliveryNoticeKind.CarrierPublishedCargoUpdated => $"Published and linked {notice.Name} and "
                + $"updated {notice.Count:N0} cargo entries.",
            ColonizationDeliveryNoticeKind.CarrierLinkedCargoNotUpdated =>
                "The Fleet Carrier was linked, but its current cargo was not updated: " + notice.Detail,
            ColonizationDeliveryNoticeKind.CarrierNotPublished => "The Fleet Carrier was not published: "
                + notice.Detail,
            ColonizationDeliveryNoticeKind.CapiCargoSeeded =>
                $"Seeded linked carrier {notice.Count} cargo from Frontier CAPI ({notice.Detail}).",
            ColonizationDeliveryNoticeKind.CapiCargoSeedNotApplied => "Frontier CAPI cargo seed was not applied: "
                + notice.Detail,
            ColonizationDeliveryNoticeKind.CarrierCargoChecking => $"Checking {notice.Name} market cargo...",
            ColonizationDeliveryNoticeKind.CarrierCargoNotUpdated => "Fleet Carrier cargo was not updated: "
                + notice.Detail,
            ColonizationDeliveryNoticeKind.CarrierNotOnRaven => "Raven Colonial does not have this Fleet Carrier.",
            ColonizationDeliveryNoticeKind.CarrierCargoCurrent => $"{notice.Name} cargo is already current.",
            ColonizationDeliveryNoticeKind.CarrierCargoUpdating => $"Updating {notice.Count:N0} cargo entries for "
                + notice.Name
                + "...",
            ColonizationDeliveryNoticeKind.CarrierCargoUpdated => $"Updated {notice.Count:N0} cargo entries for "
                + notice.Name
                + ".",
            _ => null,
        };
}
