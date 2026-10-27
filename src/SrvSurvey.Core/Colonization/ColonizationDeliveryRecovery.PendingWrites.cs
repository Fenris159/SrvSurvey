namespace SrvSurvey.Core.Colonization;

public sealed partial class ColonizationDeliveryRecovery
{
    private static readonly TimeSpan WriteRetryDelay = TimeSpan.FromSeconds(5);

    private readonly List<ColonizationPendingCargoAdjustment> failedCargoAdjustments = [];
    private readonly List<ColonizationPendingContribution> pendingContributions = [];
    private readonly HashSet<(string Owner, long MarketId)> cargoWritesInFlight = [];
    private readonly HashSet<(string Owner, string EventId)> contributionsInFlight = [];
    private readonly DateTimeOffset retainedCargoLoadedAt;
    private DateTimeOffset nextWriteRetry;
    private bool retryingWrites;
    private string? contributionSaveFailure;

    /// <summary>Deliveries still awaiting credit acknowledgement for the active profile, in retention order.</summary>
    public IReadOnlyList<ColonizationPendingContribution> GetPendingContributions()
    {
        string owner = RecoveryOwner;
        return pendingContributions.Where(item => item.Owner == owner && !item.CreditAcknowledged).ToArray();
    }

    /// <summary>Whether reconciliation decisions are allowed; false while a delivery upload or retry is active.</summary>
    public bool CanReconcileContributions => !retryingWrites && contributionsInFlight.Count == 0;

    /// <summary>Retries retained work on idle polls while protecting ownership and preventing overlapping recovery passes.</summary>
    public async Task<IReadOnlyList<ColonizationDeliveryNotice>> RetryPendingWritesAsync(
        CancellationToken cancellationToken = default
    ) => await RetryPendingWritesAsync([], cancellationToken);

    /// <summary>Retries retained work while excluding newly captured cargo and preserving commander ownership across awaits.</summary>
    private async Task<IReadOnlyList<ColonizationDeliveryNotice>> RetryPendingWritesAsync(
        IReadOnlyList<ColonizationPendingCargoAdjustment> capturedCargo,
        CancellationToken cancellationToken
    )
    {
        if (!isEnabled || commanderName is null || retryingWrites || utcNow() < nextWriteRetry)
        {
            return [];
        }
        retryingWrites = true;
        observer.PendingContributionsChanged();
        try
        {
            string owner = RecoveryOwner;
            int version = profileVersion;
            nextWriteRetry = utcNow().Add(WriteRetryDelay);
            IReadOnlyList<ColonizationDeliveryNotice> cargoNotices = await RetryCargoAdjustmentsAsync(
                owner,
                version,
                capturedCargo,
                cancellationToken
            );
            if (version != profileVersion)
            {
                return [];
            }
            var notices = new List<ColonizationDeliveryNotice>(cargoNotices);
            foreach (
                ColonizationPendingContribution pending in pendingContributions
                    .Where(item => item.Owner == owner)
                    .ToArray()
            )
            {
                if (contributionsInFlight.Contains((pending.Owner, pending.EventId)))
                {
                    continue;
                }
                ColonizationDeliveryNotice? notice = await RetryContributionAsync(pending, cancellationToken);
                if (version != profileVersion)
                {
                    return [];
                }
                AddNotice(notices, notice);
            }
            return notices.Distinct().ToArray();
        }
        finally
        {
            retryingWrites = false;
            observer.PendingContributionsChanged();
        }
    }

    /// <summary>
    /// Retries checked deliveries after the commander verifies Raven did not record them, leaving every other
    /// delivery unchanged. Returns null when integration is disabled, reconciliation is busy, or the profile changes.
    /// </summary>
    public async Task<IReadOnlyList<ColonizationDeliveryNotice>?> RetryVerifiedContributionsAsync(
        IReadOnlyCollection<string> eventIds
    )
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        if (!isEnabled || commanderName is null || !CanReconcileContributions)
        {
            return null;
        }
        ColonizationPendingContribution[] selected = GetUncertainContributions(eventIds);
        int version = profileVersion;
        retryingWrites = true;
        nextWriteRetry = utcNow().Add(WriteRetryDelay);
        observer.PendingContributionsChanged();
        try
        {
            var notices = new List<ColonizationDeliveryNotice>();
            foreach (ColonizationPendingContribution pending in selected)
            {
                if (version != profileVersion || !isEnabled)
                {
                    return null;
                }
                AddNotice(
                    notices,
                    await RetryContributionAsync(pending, CancellationToken.None, verifiedNotRecorded: true)
                );
            }
            return version == profileVersion ? notices.Distinct().ToArray() : null;
        }
        finally
        {
            retryingWrites = false;
            observer.PendingContributionsChanged();
        }
    }

    /// <summary>Acknowledges checked deliveries verified credited on Raven, retaining requirements-only recovery without sending a server request.</summary>
    public void DismissVerifiedContributions(IReadOnlyCollection<string> eventIds)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        if (!CanReconcileContributions)
        {
            return;
        }
        foreach (ColonizationPendingContribution pending in GetUncertainContributions(eventIds))
        {
            pendingContributions[pendingContributions.IndexOf(pending)] = pending with
            {
                CreditAcknowledged = true,
                OutcomeUnknown = true,
            };
        }
        SavePendingContributions();
    }

    /// <summary>Captures selected journal identities from only the active commander's uncertain deliveries.</summary>
    private ColonizationPendingContribution[] GetUncertainContributions(IReadOnlyCollection<string> eventIds)
    {
        var selectedIds = eventIds.ToHashSet();
        string owner = RecoveryOwner;
        return pendingContributions
            .Where(item =>
                item.Owner == owner
                && !item.CreditAcknowledged
                && item.OutcomeUnknown
                && selectedIds.Contains(item.EventId)
            )
            .ToArray();
    }

    /// <summary>Reconciles failed cargo writes in order, holding dependent writes while allowing other carriers to recover.</summary>
    private async Task<IReadOnlyList<ColonizationDeliveryNotice>> RetryCargoAdjustmentsAsync(
        string owner,
        int version,
        IReadOnlyList<ColonizationPendingCargoAdjustment> capturedCargo,
        CancellationToken cancellationToken
    )
    {
        var blocked = new HashSet<long>();
        var notices = new List<ColonizationDeliveryNotice>();
        foreach (
            ColonizationPendingCargoAdjustment pending in failedCargoAdjustments
                .Where(item => item.Owner == owner)
                .ToArray()
        )
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            if (pending.Delta.Count == 0)
            {
                SavePendingCargoAdjustments();
                continue;
            }
            if (
                apiKey is null
                || !FleetCarrierCargoSyncEnabled
                || capturedCargo.Contains(pending)
                || blocked.Contains(pending.MarketId)
                || IsCargoBaselinePending(pending.MarketId)
                || cargoWritesInFlight.Contains((owner, pending.MarketId))
            )
            {
                continue;
            }
            ColonizationDeliveryNotice? notice = await ReconcileCargoAdjustmentAsync(
                pending,
                version,
                cancellationToken
            );
            if (version != profileVersion)
            {
                return [];
            }
            if (notice is not null)
            {
                blocked.Add(pending.MarketId);
                notices.Add(notice);
            }
        }
        return notices.Distinct().ToArray();
    }

    /// <summary>Checks a relative write against its prior baseline, recognizing applied results and refusing conflicting remote state.</summary>
    private async Task<ColonizationDeliveryNotice?> ReconcileCargoAdjustmentAsync(
        ColonizationPendingCargoAdjustment pending,
        int version,
        CancellationToken cancellationToken
    )
    {
        if (pending.AwaitingBaseline)
        {
            // A cached dock baseline cannot release a dependency retained across restart.
            if (!pendingCargoBaselineReleases.Contains(pending))
            {
                return new(ColonizationDeliveryNoticeKind.CarrierCargoPendingUncertain);
            }
            if (!ReleasePendingCargoBaseline(pending))
            {
                return new(ColonizationDeliveryNoticeKind.CarrierCargoRecoveryNotSaved);
            }
            pending = pending with { AwaitingBaseline = false };
        }
        cargoWritesInFlight.Add((pending.Owner, pending.MarketId));
        try
        {
            ColonizationFleetCarrier? remote = await client.GetFleetCarrierAsync(pending.MarketId, cancellationToken);
            if (
                version != profileVersion
                || !isEnabled
                || !FleetCarrierCargoSyncEnabled
                || !failedCargoAdjustments.Contains(pending)
                || IsCargoBaselinePending(pending.MarketId)
            )
            {
                return null;
            }
            if (remote is null || (pending.Attempted && pending.Before is null))
            {
                return new(ColonizationDeliveryNoticeKind.CarrierCargoPendingUncertain);
            }
            Dictionary<string, int> before = pending.Before ?? remote.Cargo;
            Dictionary<string, int> beforeStock = ColonizationCarrierInventory.NormalizeStock(before);
            Dictionary<string, int> remoteStock = ColonizationCarrierInventory.NormalizeStock(remote.Cargo);
            bool applied =
                pending.Attempted
                && pending.Delta.All(pair =>
                    remoteStock.GetValueOrDefault(ColonizationCarrierInventory.CommodityKey(pair.Key))
                    == Math.Max(
                        0,
                        beforeStock.GetValueOrDefault(ColonizationCarrierInventory.CommodityKey(pair.Key)) + pair.Value
                    )
                );
            ColonizationDeliveryNoticeKind? blockedReason = GetCargoReplayBlockReason(pending, remote, applied);
            if (blockedReason is { } reason)
            {
                return new(reason);
            }
            return await ReplayCargoAdjustmentAsync(pending, remote, before, applied, version, cancellationToken)
                ? null
                : new(ColonizationDeliveryNoticeKind.CarrierCargoRecoveryNotSaved);
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or OperationCanceledException)
        {
            RetainFailedCargoAdjustment(pending, exception);
            return new(ColonizationDeliveryNoticeKind.CarrierCargoPendingFailed, Detail: exception.Message);
        }
        finally
        {
            cargoWritesInFlight.Remove((pending.Owner, pending.MarketId));
        }
    }

    /// <summary>Marks a reconcilable write as attempted, sends it unless already applied, and installs Raven's result.</summary>
    private async Task<bool> ReplayCargoAdjustmentAsync(
        ColonizationPendingCargoAdjustment pending,
        ColonizationFleetCarrier remote,
        Dictionary<string, int> before,
        bool applied,
        int version,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ColonizationPendingCargoAdjustment attempted = pending with
        {
            Attempted = true,
            OutcomeUnknown = true,
            Before = pending.Attempted ? before : CopyCargo(remote.Cargo),
        };
        failedCargoAdjustments[failedCargoAdjustments.IndexOf(pending)] = attempted;
        if (!SavePendingCargoAdjustments())
        {
            failedCargoAdjustments[failedCargoAdjustments.IndexOf(attempted)] = pending;
            return false;
        }
        IReadOnlyDictionary<string, int> updated = applied
            ? remote.Cargo
            : await client.AdjustFleetCarrierCargoAsync(
                pending.MarketId,
                ColonizationCarrierInventory.FromRaven(remote).CreateRavenAdjustment(pending.Delta),
                apiKey!,
                cancellationToken
            );
        failedCargoAdjustments.Remove(attempted);
        SavePendingCargoAdjustments();
        if (version == profileVersion)
        {
            ReplaceLocalFleetCarrier(remote with { Cargo = CopyCargo(updated) });
        }
        return true;
    }

    /// <summary>Rejects concurrent or uncertain relative-write replay until a fresh market supplies an authoritative count.</summary>
    private static ColonizationDeliveryNoticeKind? GetCargoReplayBlockReason(
        ColonizationPendingCargoAdjustment pending,
        ColonizationFleetCarrier remote,
        bool applied
    )
    {
        Dictionary<string, int> before = ColonizationCarrierInventory.NormalizeStock(pending.Before ?? remote.Cargo);
        Dictionary<string, int> remoteStock = ColonizationCarrierInventory.NormalizeStock(remote.Cargo);
        if (
            pending.Attempted
            && !applied
            && !pending.Delta.All(pair =>
                remoteStock.GetValueOrDefault(ColonizationCarrierInventory.CommodityKey(pair.Key))
                == before.GetValueOrDefault(ColonizationCarrierInventory.CommodityKey(pair.Key))
            )
        )
        {
            return ColonizationDeliveryNoticeKind.CarrierCargoPendingConcurrentChange;
        }
        return pending.Attempted && pending.OutcomeUnknown && !applied
            ? ColonizationDeliveryNoticeKind.CarrierCargoPendingUncertain
            : null;
    }

    /// <summary>Retries a definitely rejected delivery and preserves ambiguous outcomes for explicit user reconciliation.</summary>
    private async Task<ColonizationDeliveryNotice?> RetryContributionAsync(
        ColonizationPendingContribution pending,
        CancellationToken cancellationToken,
        bool verifiedNotRecorded = false
    )
    {
        if (!pending.CreditAcknowledged && pending.OutcomeUnknown && !verifiedNotRecorded)
        {
            return new(ColonizationDeliveryNoticeKind.ContributionOutcomeUncertain);
        }
        int version = profileVersion;
        try
        {
            if (!pending.CreditAcknowledged)
            {
                pending = await PrepareContributionRequirementsAsync(pending, cancellationToken);
                if (version != profileVersion)
                {
                    return null;
                }
                ColonizationPendingContribution? acknowledged = await SendContributionCreditAsync(
                    pending,
                    cancellationToken
                );
                if (acknowledged is null)
                {
                    return new(
                        ColonizationDeliveryNoticeKind.ContributionRecoveryNotSaved,
                        Detail: contributionSaveFailure
                    );
                }
                pending = acknowledged;
            }
            return version == profileVersion
                ? await RecoverContributionRequirementsAsync(pending, cancellationToken)
                : null;
        }
        catch (Exception exception)
            when (exception is HttpRequestException or InvalidDataException or OperationCanceledException)
        {
            return new(
                pendingContributions.Any(item =>
                    item.Owner == pending.Owner && item.EventId == pending.EventId && item.CreditAcknowledged
                )
                    ? ColonizationDeliveryNoticeKind.ContributionRequirementsPendingFailed
                    : ColonizationDeliveryNoticeKind.ContributionPendingFailed,
                Detail: exception.Message
            );
        }
    }

    /// <summary>Preserves ordered relative writes across restart without persisting credentials.</summary>
    private bool SavePendingCargoAdjustments()
    {
        try
        {
            // Keep retired entries in memory until their removal has been committed to disk.
            store.SavePendingCargoAdjustments(failedCargoAdjustments.Where(item => item.Delta.Count > 0).ToArray());
            failedCargoAdjustments.RemoveAll(item => item.Delta.Count == 0);
            pendingCargoBaselineReleases.RemoveWhere(item => item.Delta.Count == 0);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            observer.FleetCarrierStatusChanged(
                new(ColonizationDeliveryNoticeKind.CarrierCargoRecoveryNotSaved, Detail: exception.Message)
            );
            return false;
        }
    }

    /// <summary>Persists delivery recovery state without credentials and notifies the recovery controls.</summary>
    private bool SavePendingContributions()
    {
        try
        {
            store.SavePendingContributions(pendingContributions);
            contributionSaveFailure = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            contributionSaveFailure = exception.Message;
            observer.StatusChanged(
                new(ColonizationDeliveryNoticeKind.ContributionRecoveryNotSaved, Detail: exception.Message)
            );
            observer.PendingContributionsChanged();
            return false;
        }
        observer.PendingContributionsChanged();
        return true;
    }
}
