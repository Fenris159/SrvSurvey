using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Colonization;

public sealed partial class ColonizationDeliveryRecovery
{
    private static readonly TimeSpan ProjectLocationCacheTtl = TimeSpan.FromSeconds(4);

    private bool pendingContributionRemainingSync;
    private string? lastDepotPatchPayloadSignature;
    private (long SystemAddress, long MarketId, ColonizationProject Project, long MonotonicTicks)? projectLocationCache;

    /// <summary>Loads a docked construction project and repairs reliable body metadata independently of faction changes.</summary>
    private async Task<IReadOnlyList<ColonizationDeliveryNotice>> SynchronizeDockedProjectAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken = default
    )
    {
        var parser = new ColonizationConstructionState();
        if (!parser.Apply(journalEvent) || parser.CurrentDock is not { IsConstructionSite: true } dock)
        {
            return [];
        }

        int version = profileVersion;
        ColonizationProjectLookup lookup = await FindOrLoadProjectAsync(
            dock.SystemAddress,
            dock.MarketId,
            cancellationToken: cancellationToken
        );
        ColonizationProject? project = lookup.Project;
        if (project is null || version != profileVersion)
        {
            return [];
        }

        bool isUntracked = !lookup.LinkedCommander && localUntrackedProject?.BuildId == project.BuildId;
        ColonizationDeliveryNotice? loadNotice = null;
        if (lookup.LinkedCommander)
        {
            loadNotice = new(ColonizationDeliveryNoticeKind.ProjectLinked, project.BuildName);
        }
        else if (isUntracked)
        {
            loadNotice = new(ColonizationDeliveryNoticeKind.UntrackedProjectLoaded, project.BuildName);
        }

        ColonizationProjectUpdate? update = isUntracked ? null : CreateDockMetadataUpdate(project, dock, journalEvent);
        if (update is null)
        {
            return Notices(loadNotice);
        }
        ColonizationProject updated = await client.UpdateProjectAsync(update, cancellationToken);
        if (version != profileVersion)
        {
            return [];
        }
        updated = await ClearPhantomCommoditiesAsync(updated, cancellationToken: cancellationToken);
        if (version != profileVersion)
        {
            return [];
        }
        UpsertProject(updated);
        return Notices(loadNotice, new(ColonizationDeliveryNoticeKind.ProjectMetadataUpdated, updated.BuildName));
    }

    /// <summary>Builds a minimal faction/body patch from reliable dock metadata, preserving unknown fields.</summary>
    private ColonizationProjectUpdate? CreateDockMetadataUpdate(
        ColonizationProject project,
        ColonizationDockingSnapshot dock,
        JournalEventEnvelope journalEvent
    )
    {
        bool factionChanged = !string.IsNullOrWhiteSpace(dock.FactionName) && dock.FactionName != project.FactionName;
        (int? bodyId, string? bodyName) = ResolveDockBody(dock, journalEvent);
        bool bodyChanged =
            bodyId is >= 0
            && (project.BodyNumber != bodyId || (!string.IsNullOrWhiteSpace(bodyName) && project.BodyName != bodyName));
        if (!factionChanged && !bodyChanged)
        {
            return null;
        }
        return new ColonizationProjectUpdate
        {
            BuildId = project.BuildId,
            FactionName = factionChanged ? dock.FactionName : null,
            BodyNumber = bodyChanged ? bodyId : null,
            BodyName = bodyChanged && !string.IsNullOrWhiteSpace(bodyName) ? bodyName : null,
        };
    }

    /// <summary>Uses an event's own body first, falling back only to a body known for the current commander and system.</summary>
    private (int? BodyId, string? BodyName) ResolveDockBody(
        ColonizationDockingSnapshot dock,
        JournalEventEnvelope journalEvent
    )
    {
        if (currentSystemAddress != dock.SystemAddress)
        {
            return (null, null);
        }
        if (ColonizationBodyJournal.ReadBodyId(journalEvent.Payload) is >= 0 and var bodyId)
        {
            return (bodyId, ColonizationBodyJournal.ReadBodyName(journalEvent.Payload));
        }
        return string.Equals(currentBodyCommanderName, commanderName, StringComparison.OrdinalIgnoreCase)
            ? (currentBodyId, currentBodyName)
            : (null, null);
    }

    /// <summary>Retains the originating delivery before uploading and reconciles remaining requirements after acknowledgement.</summary>
    private async Task<IReadOnlyList<ColonizationDeliveryNotice>> SynchronizeContributionAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken = default
    )
    {
        long? marketId = GetJournalInt64(journalEvent.Payload, "MarketID");
        Dictionary<string, int> contributions = ReadJournalContributions(journalEvent.Payload);
        if (marketId is not > 0 || contributions.Count == 0)
        {
            return [];
        }

        int version = profileVersion;
        ColonizationDockingSnapshot? dock = GetJournalDock(journalEvent);
        ColonizationProject? project = (
            await FindOrLoadProjectAsync(
                dock?.MarketId == marketId ? dock.SystemAddress : currentSystemAddress,
                marketId.Value,
                cancellationToken: cancellationToken
            )
        ).Project;
        if (version != profileVersion)
        {
            return [];
        }
        if (project is null)
        {
            return [new(ColonizationDeliveryNoticeKind.ContributionProjectUnknown)];
        }

        string eventId = journalEvent.RawJson;
        string owner = RecoveryOwner;
        if (pendingContributions.Exists(item => item.Owner == owner && item.EventId == eventId))
        {
            return [new(ColonizationDeliveryNoticeKind.ContributionAlreadyRetained)];
        }
        var pending = new ColonizationPendingContribution(
            owner,
            project.BuildId,
            commanderName!,
            contributions,
            eventId,
            true
        );
        pendingContributions.Add(pending);
        contributionsInFlight.Add(eventId);
        SavePendingContributions();
        try
        {
            await client.ContributeToProjectAsync(project.BuildId, pending.Commander, contributions, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            pendingContributions[pendingContributions.IndexOf(pending)] = pending with
            {
                OutcomeUnknown = !IsDefiniteRejection(exception),
            };
            SavePendingContributions();
            nextWriteRetry = utcNow().Add(WriteRetryDelay);
            throw;
        }
        finally
        {
            contributionsInFlight.Remove(eventId);
            observer.PendingContributionsChanged();
        }
        pendingContributions.Remove(pending);
        SavePendingContributions();
        if (version != profileVersion)
        {
            return [];
        }
        // Contribute only credits history. Remaining need rows stay stale until an absolute
        // depot/commodity update lands — force that publish here so Raven cannot track a
        // delivery without updating what is still required.
        pendingContributionRemainingSync = true;
        ColonizationDeliveryNotice? remainingNotice = await PublishRemainingAfterContributionAsync(
            project,
            marketId.Value,
            contributions,
            cancellationToken: cancellationToken
        );
        return Notices(
            new(
                ColonizationDeliveryNoticeKind.ContributionPublished,
                project.BuildName,
                Count: contributions.Values.Sum(value => (long)value)
            ),
            remainingNotice
        );
    }

    /// <summary>Publishes absolute remaining requirements after delivery acknowledgement without duplicating credit.</summary>
    private async Task<ColonizationDeliveryNotice?> PublishRemainingAfterContributionAsync(
        ColonizationProject project,
        long marketId,
        IReadOnlyDictionary<string, int> contributions,
        CancellationToken cancellationToken = default
    )
    {
        ColonizationConstructionDepotSnapshot? depot = constructionState.CurrentDepot;
        if (depot is not null && depot.MarketId == marketId)
        {
            Dictionary<string, int> depotRemaining = ToRemainingCommodities(depot);
            if (!DictionariesEqual(project.Commodities, depotRemaining))
            {
                // Depot already moved ahead of the cached project (depot before contribute).
                return await PublishProjectRemainingAsync(
                    project,
                    depot,
                    depotRemaining,
                    force: true,
                    cancellationToken: cancellationToken
                );
            }
        }

        Dictionary<string, int> remaining = ApplyContributionToRemaining(project.Commodities, contributions);
        return await PublishProjectRemainingAsync(
            project,
            depot is not null && depot.MarketId == marketId ? depot : null,
            remaining,
            force: true,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>
    /// Synchronizes live depot events, handling completion independently of remaining-cargo updates.
    /// </summary>
    private async Task<ColonizationDeliveryNotice?> SynchronizeDepotAsync(
        JournalEventEnvelope journalEvent,
        CancellationToken cancellationToken = default
    )
    {
        var parser = new ColonizationConstructionState();
        if (!parser.Apply(journalEvent) || parser.CurrentDepot is not { } depot)
        {
            return null;
        }

        int version = profileVersion;
        ColonizationDockingSnapshot? dock = constructionState.CurrentDock;
        ColonizationProject? project = (
            await FindOrLoadProjectAsync(
                dock?.MarketId == depot.MarketId ? dock.SystemAddress : currentSystemAddress,
                depot.MarketId,
                cancellationToken: cancellationToken
            )
        ).Project;
        if (version != profileVersion)
        {
            return null;
        }
        if (project is null)
        {
            return new(ColonizationDeliveryNoticeKind.DepotProjectUnknown);
        }

        if (depot.IsComplete)
        {
            return await MarkProjectCompleteAsync(project, cancellationToken);
        }

        Dictionary<string, int> remaining = ToRemainingCommodities(depot);
        long maximumRequiredLong = depot.Resources.Sum(resource => (long)resource.RequiredAmount);
        if (maximumRequiredLong > int.MaxValue)
        {
            return new(ColonizationDeliveryNoticeKind.RequirementsAboveSupportedTotal);
        }

        int maximumRequired = (int)maximumRequiredLong;
        bool force = pendingContributionRemainingSync;
        bool requiresUpdate =
            force
            || project.MaximumRequired != maximumRequired
            || !DictionariesEqual(project.Commodities, remaining)
            || depot.IsFailed;
        if (!requiresUpdate)
        {
            return null;
        }

        return await PublishProjectRemainingAsync(
            project,
            depot,
            remaining,
            force,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>Marks a completed depot's project complete once, leaving later depot events free to retry a failure.</summary>
    private async Task<ColonizationDeliveryNotice?> MarkProjectCompleteAsync(
        ColonizationProject project,
        CancellationToken cancellationToken
    )
    {
        if (project.IsComplete)
        {
            return null;
        }

        int version = profileVersion;
        await client.MarkProjectCompleteAsync(project.BuildId, cancellationToken);
        if (version != profileVersion)
        {
            return null;
        }
        UpsertProject(
            project with
            {
                IsComplete = true,
                RemainingRequired = 0,
                Commodities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            }
        );
        pendingContributionRemainingSync = false;
        InvalidateProjectLocationCache();
        return new(ColonizationDeliveryNoticeKind.ProjectMarkedComplete, project.BuildName);
    }

    /// <summary>Patches only remaining commodity requirements using the initiating cancellation token.</summary>
    private async Task<ColonizationDeliveryNotice?> PublishProjectRemainingAsync(
        ColonizationProject project,
        ColonizationConstructionDepotSnapshot? depot,
        Dictionary<string, int> remaining,
        bool force,
        CancellationToken cancellationToken = default
    )
    {
        remaining = ColonizationCommodityMaps.NormalizeNeedMap(remaining);
        int? maximumRequired = depot is null
            ? project.MaximumRequired
            : checked((int)depot.Resources.Sum(resource => (long)resource.RequiredAmount));
        if (
            !force
            && project.MaximumRequired == maximumRequired
            && DictionariesEqual(project.Commodities, remaining)
            && depot is not { IsFailed: true }
        )
        {
            return null;
        }

        string signature = ColonizationCommodityMaps.CreateDepotUpdateSignature(
            project.BuildId,
            maximumRequired,
            remaining,
            includeDepot: depot is not null,
            depotFailed: depot?.IsFailed == true
        );
        if (!force && string.Equals(signature, lastDepotPatchPayloadSignature, StringComparison.Ordinal))
        {
            return null;
        }

        int version = profileVersion;
        await ClearPhantomCommoditiesAsync(project, cancellationToken: cancellationToken);
        if (version != profileVersion)
        {
            return null;
        }

        ColonizationProject updated = await client.UpdateProjectAsync(
            new ColonizationProjectUpdate
            {
                BuildId = project.BuildId,
                MaximumRequired = maximumRequired,
                Commodities = remaining,
                ConstructionDepot = depot is null ? null : ColonizationConstructionDepotPayload.FromSnapshot(depot),
            },
            cancellationToken
        );
        if (version != profileVersion)
        {
            return null;
        }
        updated = await ClearPhantomCommoditiesAsync(updated, cancellationToken: cancellationToken);
        if (version != profileVersion)
        {
            return null;
        }
        UpsertProject(updated);
        lastDepotPatchPayloadSignature = signature;
        InvalidateProjectLocationCache();
        pendingContributionRemainingSync = false;
        return new(
            force
                ? ColonizationDeliveryNoticeKind.RemainingUpdatedAfterContribution
                : ColonizationDeliveryNoticeKind.RequirementsUpdated,
            updated.BuildName
        );
    }

    /// <summary>Removes completed phantom requirements from a project without changing unrelated fields.</summary>
    private async Task<ColonizationProject> ClearPhantomCommoditiesAsync(
        ColonizationProject project,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<string, int> zeroes = ColonizationCommodityMaps.PhantomZeroPatchMap(project.Commodities);
        if (zeroes.Count == 0)
        {
            return project;
        }

        Dictionary<string, int> cleared = ColonizationCommodityMaps.ApplyPhantomZeros(project.Commodities);
        ColonizationProject updated = await client.UpdateProjectAsync(
            new ColonizationProjectUpdate { BuildId = project.BuildId, Commodities = zeroes },
            cancellationToken
        );
        // Prefer the cleared need map when the API echoes a partial merge response.
        if (ColonizationCommodityMaps.PhantomZeroPatchMap(updated.Commodities).Count > 0)
        {
            return updated with { Commodities = cleared, RemainingRequired = cleared.Values.Sum() };
        }

        return updated with
        {
            Commodities = ColonizationCommodityMaps.ApplyPhantomZeros(updated.Commodities),
        };
    }

    private static Dictionary<string, int> ToRemainingCommodities(ColonizationConstructionDepotSnapshot depot)
    {
        return depot.Resources.ToDictionary(
            resource => resource.Name,
            resource => resource.RemainingAmount,
            StringComparer.OrdinalIgnoreCase
        );
    }

    private static Dictionary<string, int> ApplyContributionToRemaining(
        IReadOnlyDictionary<string, int> commodities,
        IReadOnlyDictionary<string, int> contributions
    )
    {
        var remaining = new Dictionary<string, int>(commodities, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, int> contribution in contributions)
        {
            string name = ColonizationConstructionState.NormalizeCommodityName(contribution.Key);
            if (name.Length == 0 || contribution.Value <= 0)
            {
                continue;
            }

            int current = remaining.GetValueOrDefault(name);
            int next = Math.Max(0, current - contribution.Value);
            remaining[name] = next;
        }

        return remaining;
    }

    /// <summary>Resolves a project by system and market with cancellation-aware cache recovery.</summary>
    private async Task<ColonizationProjectLookup> FindOrLoadProjectAsync(
        long? systemAddress,
        long marketId,
        CancellationToken cancellationToken = default
    )
    {
        ColonizationProject? project =
            projects.FirstOrDefault(candidate => candidate.MarketId == marketId)
            ?? (localUntrackedProject?.MarketId == marketId ? localUntrackedProject : null);
        if (project is not null || systemAddress is not > 0)
        {
            return new ColonizationProjectLookup(project, LinkedCommander: false);
        }

        int version = profileVersion;
        if (TryGetCachedProject(systemAddress.Value, marketId, out ColonizationProject? cached))
        {
            project = cached;
        }
        else
        {
            project = await client.GetProjectAsync(systemAddress.Value, marketId, cancellationToken);
            if (version != profileVersion)
            {
                return new ColonizationProjectLookup(null, LinkedCommander: false);
            }
            if (project is not null)
            {
                RememberProjectLocation(systemAddress.Value, marketId, project);
            }
        }

        if (project is null)
        {
            return new ColonizationProjectLookup(null, LinkedCommander: false);
        }

        project = await ClearPhantomCommoditiesAsync(project, cancellationToken: cancellationToken);
        if (version != profileVersion)
        {
            return new ColonizationProjectLookup(null, LinkedCommander: false);
        }

        bool linkedCommander = false;
        if (ShouldAutoLinkDockedProject(project))
        {
            await client.LinkCommanderAsync(project.BuildId, commanderName!, cancellationToken);
            if (version != profileVersion)
            {
                return new ColonizationProjectLookup(null, LinkedCommander: false);
            }
            localUntrackedProject = null;
            linkedCommander = true;
            InvalidateProjectLocationCache();
        }
        else
        {
            localUntrackedProject = project;
        }

        UpsertProject(project);
        return new ColonizationProjectLookup(project, linkedCommander);
    }

    private bool ShouldAutoLinkDockedProject(ColonizationProject project)
    {
        return !string.IsNullOrWhiteSpace(commanderName)
            && !string.IsNullOrWhiteSpace(project.ArchitectName)
            && string.Equals(commanderName.Trim(), project.ArchitectName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetCachedProject(long systemAddress, long marketId, out ColonizationProject? project)
    {
        project = null;
        if (projectLocationCache is not { } cache || cache.SystemAddress != systemAddress || cache.MarketId != marketId)
        {
            return false;
        }

        long ageMilliseconds = Environment.TickCount64 - cache.MonotonicTicks;
        if (ageMilliseconds < 0 || ageMilliseconds > ProjectLocationCacheTtl.TotalMilliseconds)
        {
            projectLocationCache = null;
            return false;
        }

        project = cache.Project;
        return true;
    }

    private void RememberProjectLocation(long systemAddress, long marketId, ColonizationProject project)
    {
        projectLocationCache = (systemAddress, marketId, project, Environment.TickCount64);
    }

    private void InvalidateProjectLocationCache()
    {
        projectLocationCache = null;
    }

    private readonly record struct ColonizationProjectLookup(ColonizationProject? Project, bool LinkedCommander);

    private void UpsertProject(ColonizationProject project)
    {
        if (localUntrackedProject?.BuildId == project.BuildId)
        {
            localUntrackedProject = project;
        }

        projects = Sort(
            projects
                .Where(candidate =>
                    !string.Equals(candidate.BuildId, project.BuildId, StringComparison.OrdinalIgnoreCase)
                )
                .Append(project)
        );
        observer.ProjectsChanged();
    }

    private static Dictionary<string, int> ReadJournalContributions(JsonElement root)
    {
        if (!root.TryGetProperty("Contributions", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement row in rows.EnumerateArray())
        {
            string name = ColonizationConstructionState.NormalizeCommodityName(GetJournalString(row, "Name"));
            int? amount = GetJournalInt32(row, "Amount");
            if (name.Length > 0 && amount is > 0)
            {
                int existing = result.GetValueOrDefault(name);
                if (existing > int.MaxValue - amount.Value)
                {
                    return [];
                }

                result[name] = existing + amount.Value;
            }
        }

        return result;
    }

    private static bool DictionariesEqual(Dictionary<string, int> left, Dictionary<string, int> right)
    {
        return left.Count == right.Count
            && left.All(pair => right.TryGetValue(pair.Key, out int value) && value == pair.Value);
    }
}
