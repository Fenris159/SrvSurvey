namespace SrvSurvey.Core.Exploration;

internal readonly record struct OrganismIdentity(
    string? Genus,
    long? EntryId,
    string? Variant,
    string? Species);

internal static class OrganismIdentityMatcher
{
    public static T? FindBestMatch<T>(
        IEnumerable<T> candidates,
        OrganismIdentity incoming,
        Func<T, OrganismIdentity> identitySelector)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(identitySelector);
        var materialized = candidates.ToArray();

        if (incoming.EntryId is > 0
            && FindBy(materialized, identitySelector, identity =>
                identity.EntryId == incoming.EntryId) is { } entryMatch)
        {
            return entryMatch;
        }

        if (!string.IsNullOrWhiteSpace(incoming.Variant)
            && FindBy(materialized, identitySelector, identity => string.Equals(
                identity.Variant,
                incoming.Variant,
                StringComparison.Ordinal)) is { } variantMatch)
        {
            return variantMatch;
        }

        var sameGenus = materialized
            .Where(candidate => string.Equals(
                identitySelector(candidate).Genus,
                incoming.Genus,
                StringComparison.Ordinal))
            .ToArray();

        if (string.IsNullOrWhiteSpace(incoming.Variant)
            && !string.IsNullOrWhiteSpace(incoming.Species)
            && FindBy(sameGenus, identitySelector, identity => string.Equals(
                identity.Species,
                incoming.Species,
                StringComparison.Ordinal)) is { } speciesMatch)
        {
            return speciesMatch;
        }

        var placeholder = FindBy(sameGenus, identitySelector, identity =>
            identity.EntryId is not > 0
            && string.IsNullOrWhiteSpace(identity.Variant)
            && (string.IsNullOrWhiteSpace(identity.Species)
                || string.Equals(
                    identity.Species,
                    incoming.Species,
                    StringComparison.Ordinal)));
        if (placeholder is not null)
        {
            return placeholder;
        }

        return HasIdentity(incoming) ? null : sameGenus.FirstOrDefault();
    }

    private static T? FindBy<T>(
        IEnumerable<T> candidates,
        Func<T, OrganismIdentity> identitySelector,
        Func<OrganismIdentity, bool> predicate)
        where T : class => candidates.FirstOrDefault(candidate =>
            predicate(identitySelector(candidate)));

    private static bool HasIdentity(OrganismIdentity identity) =>
        identity.EntryId is > 0
        || !string.IsNullOrWhiteSpace(identity.Variant)
        || !string.IsNullOrWhiteSpace(identity.Species);
}
