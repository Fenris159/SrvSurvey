using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public sealed partial class ColonizationDeliveryRecovery
{
    private readonly Dictionary<
        JournalEventEnvelope,
        (ColonizationConstructionDepotSnapshot Depot, bool Follows)
    > contributionDepots = new(ReferenceEqualityComparer.Instance);

    /// <summary>Retains a later depot in the same poll even when docking subsequently moves to another market.</summary>
    private void CaptureFollowingContributionDepots(IReadOnlyList<JournalEventEnvelope> events)
    {
        var following = new Dictionary<long, ColonizationConstructionDepotSnapshot>();
        for (int index = events.Count - 1; index >= 0; index--)
        {
            JournalEventEnvelope journalEvent = events[index];
            if (journalEvent.EventName == "ColonisationConstructionDepot")
            {
                var parser = new ColonizationConstructionState();
                if (parser.Apply(journalEvent) && parser.CurrentDepot is { } depot)
                {
                    following.TryAdd(depot.MarketId, depot);
                }
            }
            else if (
                journalEvent.EventName == "ColonisationContribution"
                && GetJournalInt64(journalEvent.Payload, "MarketID") is { } marketId
                && following.TryGetValue(marketId, out ColonizationConstructionDepotSnapshot? depot)
            )
            {
                contributionDepots[journalEvent] = (depot, true);
            }
        }
    }

    /// <summary>Computes the absolute journal target once, before Raven can accept credit, including earlier retained deliveries.</summary>
    private ColonizationPendingContributionRequirements CreateContributionRequirements(
        ColonizationProject project,
        IReadOnlyDictionary<string, int> cargo,
        JournalEventEnvelope? journalEvent,
        bool alreadyCredited = false
    )
    {
        long marketId = journalEvent is null
            ? project.MarketId
            : GetJournalInt64(journalEvent.Payload, "MarketID") ?? project.MarketId;
        marketId = Math.Max(0, marketId);
        DateTimeOffset? recordedAt = journalEvent?.Timestamp;
        Dictionary<string, int> baseline = ContributionBaseline(project, recordedAt, alreadyCredited);
        (ColonizationConstructionDepotSnapshot? depot, bool follows) = ContributionDepot(journalEvent, marketId);
        Dictionary<string, int>? depotRemaining = depot is null ? null : ToRemainingCommodities(depot);
        bool authoritative = IsAuthoritativeContributionDepot(depot, follows, recordedAt, baseline, depotRemaining);
        Dictionary<string, int> remaining = baseline;
        if (authoritative)
        {
            remaining = depotRemaining!;
        }
        else if (!alreadyCredited)
        {
            remaining = ApplyContributionToRemaining(
                depotRemaining is null
                    ? ColonizationCommodityMaps.NormalizeNeedMap(baseline)
                    : PreserveLowerRequirements(baseline, depotRemaining),
                cargo
            );
        }
        return new ColonizationPendingContributionRequirements(
            marketId,
            authoritative && depot!.Timestamp > recordedAt ? depot.Timestamp : recordedAt,
            ColonizationCommodityMaps.NormalizeNeedMap(remaining),
            authoritative ? depot : null
        );
    }

    private Dictionary<string, int> ContributionBaseline(
        ColonizationProject project,
        DateTimeOffset? recordedAt,
        bool alreadyCredited
    )
    {
        Dictionary<string, int> baseline = new(project.Commodities, StringComparer.OrdinalIgnoreCase);
        if (!alreadyCredited)
        {
            foreach (
                ColonizationPendingContribution retained in pendingContributions.Where(item =>
                    item.Owner == RecoveryOwner
                    && item.BuildId == project.BuildId
                    && item.Requirements is not null
                    && !(item.Requirements.RecordedAt > recordedAt)
                )
            )
            {
                baseline = PreserveLowerRequirements(baseline, retained.Requirements!.Commodities);
            }
        }
        return baseline;
    }

    private (ColonizationConstructionDepotSnapshot? Depot, bool Follows) ContributionDepot(
        JournalEventEnvelope? journalEvent,
        long marketId
    )
    {
        ColonizationConstructionDepotSnapshot? depot = constructionState.CurrentDepot;
        bool follows = false;
        if (
            journalEvent is not null
            && contributionDepots.TryGetValue(
                journalEvent,
                out (ColonizationConstructionDepotSnapshot Depot, bool Follows) context
            )
        )
        {
            depot = context.Depot;
            follows = context.Follows;
        }
        if (depot?.MarketId != marketId)
        {
            depot = null;
        }
        return (depot, follows);
    }

    private static bool IsAuthoritativeContributionDepot(
        ColonizationConstructionDepotSnapshot? depot,
        bool follows,
        DateTimeOffset? recordedAt,
        Dictionary<string, int> baseline,
        Dictionary<string, int>? depotRemaining
    ) =>
        depot is not null
        && (
            (follows && !(depot.Timestamp < recordedAt))
            || depot.Timestamp > recordedAt
            || (depot.Timestamp == recordedAt && DepotReflectsUnpublishedProgress(depot, baseline, depotRemaining!))
        );

    /// <summary>Explicit completion is progress even when Raven's remaining commodity amounts are already zero.</summary>
    private static bool DepotReflectsUnpublishedProgress(
        ColonizationConstructionDepotSnapshot snapshot,
        Dictionary<string, int> baseline,
        Dictionary<string, int> depot
    )
    {
        if (snapshot.IsComplete)
        {
            return true;
        }

        Dictionary<string, int> known = ColonizationCommodityMaps.NormalizeNeedMap(
            baseline.Where(commodity => commodity.Value >= 0)
        );
        return known.Any(commodity =>
            depot.TryGetValue(commodity.Key, out int remaining) && remaining != commodity.Value
        );
    }

    /// <summary>Real journal requirements must initialize template slots directly, rather than first converting them to misleading zero progress.</summary>
    private void PreserveJournalRequiredTemplateSlots(ColonizationProject project, Dictionary<string, int> zeroes)
    {
        IEnumerable<ColonizationConstructionDepotSnapshot?> depots = contributionDepots
            .Values.Select(context => context.Depot)
            .Append(constructionState.CurrentDepot);
        foreach (
            ColonizationConstructionDepotSnapshot? depot in depots.Where(depot => depot?.MarketId == project.MarketId)
        )
        {
            foreach (
                ColonizationResourceRequirement resource in depot!.Resources.Where(resource =>
                    resource.RemainingAmount > 0
                )
            )
            {
                zeroes.Remove(resource.Name);
            }
        }
    }

    /// <summary>Migrates legacy recovery entries before another credit attempt; an already-credited legacy entry uses current absolute state.</summary>
    private async Task<ColonizationPendingContribution> PrepareContributionRequirementsAsync(
        ColonizationPendingContribution pending,
        CancellationToken cancellationToken
    )
    {
        if (pending.Requirements is not null)
        {
            return pending;
        }
        int version = profileVersion;
        ColonizationProject project =
            await client.GetProjectAsync(pending.BuildId, cancellationToken)
            ?? throw new InvalidDataException("The retained delivery's Raven project is unavailable.");
        if (version != profileVersion)
        {
            return pending;
        }
        if (!JournalEventEnvelope.TryParse(pending.EventId, out JournalEventEnvelope? journalEvent, out _))
        {
            journalEvent = null;
        }
        ColonizationPendingContribution prepared = pending with
        {
            Requirements = CreateContributionRequirements(
                project,
                pending.Cargo,
                journalEvent,
                pending.CreditAcknowledged
            ),
        };
        pendingContributions[pendingContributions.IndexOf(pending)] = prepared;
        return prepared;
    }

    /// <summary>Writes the uncertain marker and absolute target before credit, then durably changes phase on acknowledgement.</summary>
    private async Task<ColonizationPendingContribution?> SendContributionCreditAsync(
        ColonizationPendingContribution pending,
        CancellationToken cancellationToken
    )
    {
        ColonizationPendingContribution sending = pending with { OutcomeUnknown = true };
        int index = pendingContributions.IndexOf(pending);
        pendingContributions[index] = sending;
        if (!SavePendingContributions())
        {
            pendingContributions[index] = pending;
            nextWriteRetry = utcNow().Add(WriteRetryDelay);
            return null;
        }
        try
        {
            await client.ContributeToProjectAsync(pending.BuildId, pending.Commander, pending.Cargo, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            pendingContributions[pendingContributions.IndexOf(sending)] = sending with
            {
                OutcomeUnknown = !IsDefiniteRejection(exception),
            };
            SavePendingContributions();
            nextWriteRetry = utcNow().Add(WriteRetryDelay);
            throw;
        }
        ColonizationPendingContribution acknowledged = sending with
        {
            CreditAcknowledged = true,
            OutcomeUnknown = true,
        };
        pendingContributions[pendingContributions.IndexOf(sending)] = acknowledged;
        SavePendingContributions();
        return acknowledged;
    }

    /// <summary>Replays only absolute requirements after credit, retaining them until acknowledgement and respecting later depot/remote progress.</summary>
    private async Task<ColonizationDeliveryNotice?> RecoverContributionRequirementsAsync(
        ColonizationPendingContribution pending,
        CancellationToken cancellationToken
    )
    {
        int version = profileVersion;
        pending = await PrepareContributionRequirementsAsync(pending, cancellationToken);
        if (version != profileVersion || pending.Owner != RecoveryOwner)
        {
            return null;
        }
        ColonizationPendingContribution? checkpoint = CheckpointContributionRequirements(pending);
        if (checkpoint is null)
        {
            return new(ColonizationDeliveryNoticeKind.ContributionRecoveryNotSaved, Detail: contributionSaveFailure);
        }
        pending = checkpoint;
        ColonizationProject remote =
            await client.GetProjectAsync(pending.BuildId, cancellationToken)
            ?? throw new InvalidDataException("The acknowledged delivery's Raven project is unavailable.");
        if (version != profileVersion || !isEnabled)
        {
            return null;
        }
        checkpoint = CheckpointContributionRequirements(pending, onlyWhenChanged: true);
        if (checkpoint is null)
        {
            return new(ColonizationDeliveryNoticeKind.ContributionRecoveryNotSaved, Detail: contributionSaveFailure);
        }
        pending = checkpoint;
        ColonizationPendingContributionRequirements requirements = pending.Requirements!;
        ColonizationConstructionDepotSnapshot? depot = requirements.Depot;
        if (depot is { IsComplete: false, TotalRequired: > int.MaxValue })
        {
            return new(ColonizationDeliveryNoticeKind.RequirementsAboveSupportedTotal);
        }
        ColonizationDeliveryNotice? notice = await PublishRecoveredRequirementsAsync(
            remote,
            requirements,
            cancellationToken
        );
        if (version != profileVersion)
        {
            return null;
        }
        pendingContributions.Remove(pending);
        return SavePendingContributions()
            ? notice
            : new(ColonizationDeliveryNoticeKind.ContributionRecoveryNotSaved, Detail: contributionSaveFailure);
    }

    private ColonizationPendingContribution? CheckpointContributionRequirements(
        ColonizationPendingContribution pending,
        bool onlyWhenChanged = false
    )
    {
        ColonizationPendingContribution refreshed = RefreshContributionRequirements(pending);
        if (onlyWhenChanged && ReferenceEquals(refreshed, pending))
        {
            return pending;
        }
        return SavePendingContributions() ? refreshed : null;
    }

    private async Task<ColonizationDeliveryNotice?> PublishRecoveredRequirementsAsync(
        ColonizationProject remote,
        ColonizationPendingContributionRequirements requirements,
        CancellationToken cancellationToken
    )
    {
        if (remote.IsComplete)
        {
            return null;
        }
        ColonizationConstructionDepotSnapshot? depot = requirements.Depot;
        if (depot?.IsComplete == true)
        {
            return await MarkProjectCompleteAsync(remote, cancellationToken);
        }
        Dictionary<string, int> target = depot is null
            ? requirements.Commodities
            : PreserveLowerRequirements(requirements.Commodities, ToRemainingCommodities(depot));
        target = PreserveLowerRequirements(remote.Commodities, target);
        // When Raven has moved further ahead, an old depot payload must not replace its newer progress.
        ColonizationConstructionDepotSnapshot? payload =
            depot is not null && DepotMatchesRecoveredRequirements(target, depot) ? depot : null;
        return await PublishProjectRemainingAsync(remote, payload, target, force: true, cancellationToken);
    }

    /// <summary>Checkpoints later depot progress and completion before recovery can send either absolute write.</summary>
    private ColonizationPendingContribution RefreshContributionRequirements(ColonizationPendingContribution pending)
    {
        ColonizationPendingContributionRequirements requirements = pending.Requirements!;
        if (
            constructionState.CurrentDepot is not { } current
            || current.MarketId != requirements.MarketId
            || current.Timestamp < requirements.RecordedAt
            || current.Timestamp is null
            || (requirements.Depot?.IsComplete == true && !current.IsComplete)
            || ReferenceEquals(current, requirements.Depot)
        )
        {
            return pending;
        }
        ColonizationPendingContribution refreshed = pending with
        {
            Requirements = requirements with
            {
                RecordedAt = current.Timestamp,
                Commodities = PreserveLowerRequirements(requirements.Commodities, ToRemainingCommodities(current)),
                Depot = current,
            },
        };
        pendingContributions[pendingContributions.IndexOf(pending)] = refreshed;
        return refreshed;
    }

    /// <summary>Preserves known nonnegative progress while allowing journal targets to initialize absent or negative template slots.</summary>
    private static Dictionary<string, int> PreserveLowerRequirements(
        Dictionary<string, int> current,
        IReadOnlyDictionary<string, int> target
    )
    {
        Dictionary<string, int> remaining = ColonizationCommodityMaps.NormalizeNeedMap(current);
        Dictionary<string, int> known = ColonizationCommodityMaps.NormalizeNeedMap(
            current.Where(commodity => commodity.Value >= 0)
        );
        foreach (KeyValuePair<string, int> commodity in ColonizationCommodityMaps.NormalizeNeedMap(target))
        {
            remaining[commodity.Key] = known.TryGetValue(commodity.Key, out int value)
                ? Math.Min(value, commodity.Value)
                : commodity.Value;
        }
        return remaining;
    }

    private static bool DepotMatchesRecoveredRequirements(
        Dictionary<string, int> remaining,
        ColonizationConstructionDepotSnapshot depot
    )
    {
        Dictionary<string, int> resources = ToRemainingCommodities(depot);
        return resources.All(resource => remaining.GetValueOrDefault(resource.Key) == resource.Value)
            && remaining.All(resource => resource.Value == 0 || resources.ContainsKey(resource.Key));
    }
}
