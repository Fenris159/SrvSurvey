namespace SrvSurvey.Core.Colonization;

public sealed class ColonizationProjectPublisher(
    IRavenColonialClient client)
{
    private const int MaximumOrderCorrectionAttempts = 2;

    private readonly IRavenColonialClient client = client
        ?? throw new ArgumentNullException(nameof(client));

    public async Task<ColonizationProjectPublishResult> CreateAsync(
        ColonizationProjectCreate project,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var systemKey = project.SystemAddress.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var sitesBeforeCreation = await client.GetSystemSitesAsync(
                systemKey,
                cancellationToken)
            .ConfigureAwait(false);
        var primarySiteId = GetPrimarySiteId(sitesBeforeCreation);

        if (sitesBeforeCreation.Count > 0 && primarySiteId is null)
        {
            throw new InvalidDataException(
                "Raven returned an existing primary site without a persisted ID. "
                + "The project was not created because its order could not be protected.");
        }

        if (primarySiteId is not null && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Save a Raven API key before creating a project in a system that "
                + "already has a primary port. The project was not created because "
                + "SRV Survey could not guarantee the primary port would remain first.");
        }

        var created = await client.CreateProjectAsync(project, cancellationToken)
            .ConfigureAwait(false);
        if (created is null)
        {
            return new ColonizationProjectPublishResult(
                null,
                ColonizationPrimarySiteOrderStatus.NotRequired,
                null);
        }

        if (primarySiteId is null)
        {
            return new ColonizationProjectPublishResult(
                created,
                ColonizationPrimarySiteOrderStatus.NotRequired,
                null);
        }

        try
        {
            var status = await PreservePrimarySiteOrderAsync(
                    systemKey,
                    primarySiteId,
                    created,
                    project,
                    apiKey!.Trim(),
                    cancellationToken)
                .ConfigureAwait(false);
            return new ColonizationProjectPublishResult(created, status, null);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidDataException)
        {
            return CreateUnverifiedResult(created, exception);
        }
        catch (TaskCanceledException exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            return CreateUnverifiedResult(created, exception);
        }
    }

    private async Task<ColonizationPrimarySiteOrderStatus>
        PreservePrimarySiteOrderAsync(
            string systemKey,
            string primarySiteId,
            ColonizationProject created,
            ColonizationProjectCreate request,
            string apiKey,
            CancellationToken cancellationToken)
    {
        var correctionSent = false;
        var latest = await client.GetSystemSitesAsync(systemKey, cancellationToken)
            .ConfigureAwait(false);

        for (var attempt = 0;
             attempt < MaximumOrderCorrectionAttempts;
             attempt++)
        {
            var orderedSiteIds = GetOrderedSiteIds(latest);
            if (!orderedSiteIds.Contains(primarySiteId, StringComparer.Ordinal))
            {
                throw new InvalidDataException(
                    $"Raven no longer returned primary site '{primarySiteId}' after "
                    + $"creating project '{created.BuildId}'.");
            }

            if (!ContainsCreatedSite(latest, created, request))
            {
                throw new InvalidDataException(
                    "Raven did not return a system site for newly created project "
                    + $"'{created.BuildId}', so its primary-port order could not be verified.");
            }

            if (string.Equals(
                    orderedSiteIds[0],
                    primarySiteId,
                    StringComparison.Ordinal))
            {
                return correctionSent
                    ? ColonizationPrimarySiteOrderStatus.Restored
                    : ColonizationPrimarySiteOrderStatus.Preserved;
            }

            var correctedOrder = new List<string>(orderedSiteIds.Count)
            {
                primarySiteId,
            };
            correctedOrder.AddRange(orderedSiteIds.Where(siteId =>
                !string.Equals(siteId, primarySiteId, StringComparison.Ordinal)));

            await client.UpdateSystemSitesAsync(
                    systemKey,
                    new ColonizationSystemSiteUpdate
                    {
                        OrderedSiteIds = correctedOrder,
                    },
                    apiKey,
                    cancellationToken)
                .ConfigureAwait(false);
            correctionSent = true;
            latest = await client.GetSystemSitesAsync(systemKey, cancellationToken)
                .ConfigureAwait(false);
        }

        throw new InvalidDataException(
            $"Raven did not retain primary site '{primarySiteId}' as the first site "
            + $"after {MaximumOrderCorrectionAttempts} order-only corrections.");
    }

    private static string? GetPrimarySiteId(
        IReadOnlyList<ColonizationSystemSite> sites)
    {
        if (sites.Count == 0)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(sites[0].Id)
            ? null
            : sites[0].Id.Trim();
    }

    private static List<string> GetOrderedSiteIds(
        IReadOnlyList<ColonizationSystemSite> sites)
    {
        if (sites.Count == 0)
        {
            throw new InvalidDataException(
                "Raven returned no system sites after creating the project.");
        }

        var ids = new List<string>(sites.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var persistedId in sites.Select(site => site.Id))
        {
            if (string.IsNullOrWhiteSpace(persistedId))
            {
                throw new InvalidDataException(
                    "Raven returned a system site without a persisted ID after "
                    + "creating the project.");
            }

            var id = persistedId.Trim();
            if (!seen.Add(id))
            {
                throw new InvalidDataException(
                    $"Raven returned duplicate system site ID '{id}' after creating "
                    + "the project.");
            }

            ids.Add(id);
        }

        return ids;
    }

    private static bool ContainsCreatedSite(
        IReadOnlyList<ColonizationSystemSite> sites,
        ColonizationProject created,
        ColonizationProjectCreate request)
    {
        return sites.Any(site =>
            (!string.IsNullOrWhiteSpace(created.BuildId)
                && string.Equals(
                    site.BuildId,
                    created.BuildId,
                    StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(request.SystemSiteId)
                && string.Equals(
                    site.Id,
                    request.SystemSiteId,
                    StringComparison.Ordinal))
            || (created.MarketId > 0 && site.MarketId == created.MarketId));
    }

    private static ColonizationProjectPublishResult CreateUnverifiedResult(
        ColonizationProject created,
        Exception exception)
    {
        return new ColonizationProjectPublishResult(
            created,
            ColonizationPrimarySiteOrderStatus.Unverified,
            $"Created {created.BuildName}, but SRV Survey could not verify that Raven "
            + $"kept the existing primary port first: {exception.Message}");
    }
}

public sealed record ColonizationProjectPublishResult(
    ColonizationProject? Project,
    ColonizationPrimarySiteOrderStatus PrimarySiteOrderStatus,
    string? Warning);

public enum ColonizationPrimarySiteOrderStatus
{
    NotRequired,
    Preserved,
    Restored,
    Unverified,
}
