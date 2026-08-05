using System.Text.Json;

namespace SrvSurvey.Core.Colonization;

public static class ColonizationSystemSiteReconciler
{
    public static ColonizationSystemSiteReconciliationPlan CreatePlan(
        IReadOnlyList<ColonizationSystemSite> baseline,
        IReadOnlyList<ColonizationSystemSite> latest,
        IReadOnlyList<ColonizationSystemSite> edited)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(edited);
        ValidateUniqueSites(baseline, "baseline");
        ValidateUniqueSites(latest, "latest Raven");
        ValidateUniqueSites(edited, "edited");

        var updates = new List<ColonizationSystemSite>();
        var deletes = new List<string>();
        var conflicts = new List<ColonizationSystemSiteConflict>();
        var unchanged = 0;

        foreach (var local in edited)
        {
            var original = FindMatch(baseline, local);
            if (original is null)
            {
                var concurrent = FindMatch(latest, local);
                if (concurrent is null)
                {
                    updates.Add(Clone(local));
                }
                else if (KnownFieldsEqual(concurrent, local))
                {
                    unchanged++;
                }
                else
                {
                    conflicts.Add(new ColonizationSystemSiteConflict(
                        DisplayIdentity(local),
                        "site",
                        "The site was also added remotely with different values."));
                }

                continue;
            }

            var remote = FindMatch(latest, original)
                ?? FindMatch(latest, local);
            if (remote is null)
            {
                conflicts.Add(new ColonizationSystemSiteConflict(
                    DisplayIdentity(local),
                    "site",
                    "The site was removed remotely after this workspace opened."));
                continue;
            }

            var merge = MergeChangedFields(original, remote, local);
            if (merge.Conflicts.Count > 0)
            {
                conflicts.AddRange(merge.Conflicts.Select(field =>
                    new ColonizationSystemSiteConflict(
                        DisplayIdentity(local),
                        field,
                        "Both the local workspace and Raven changed this field.")));
                continue;
            }

            if (!merge.HasLocalChanges || KnownFieldsEqual(remote, merge.Site))
            {
                unchanged++;
            }
            else
            {
                updates.Add(merge.Site);
            }
        }

        foreach (var original in baseline)
        {
            if (FindMatch(edited, original) is not null)
            {
                continue;
            }

            var remote = FindMatch(latest, original);
            if (remote is null)
            {
                unchanged++;
                continue;
            }

            if (!AllFieldsEqual(original, remote))
            {
                conflicts.Add(new ColonizationSystemSiteConflict(
                    DisplayIdentity(original),
                    "delete",
                    "The site changed remotely and was not scheduled for deletion."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(original.Id))
            {
                conflicts.Add(new ColonizationSystemSiteConflict(
                    DisplayIdentity(original),
                    "delete",
                    "A persisted Raven site ID is required for deletion."));
                continue;
            }

            deletes.Add(original.Id);
        }

        return new ColonizationSystemSiteReconciliationPlan(
            new ColonizationSystemSiteUpdate
            {
                UpdatedSites = updates,
                DeletedSiteIds = deletes,
            },
            conflicts,
            unchanged);
    }

    private static MergeResult MergeChangedFields(
        ColonizationSystemSite original,
        ColonizationSystemSite remote,
        ColonizationSystemSite local)
    {
        var conflicts = new List<string>();
        var changed = false;
        var id = Merge(
            "id",
            original.Id,
            remote.Id,
            local.Id,
            StringComparer.Ordinal,
            conflicts,
            ref changed);
        var name = Merge(
            "name",
            original.Name,
            remote.Name,
            local.Name,
            StringComparer.Ordinal,
            conflicts,
            ref changed);
        var body = Merge(
            "bodyNum",
            original.BodyNumber,
            remote.BodyNumber,
            local.BodyNumber,
            EqualityComparer<int>.Default,
            conflicts,
            ref changed);
        var buildType = Merge(
            "buildType",
            original.BuildType,
            remote.BuildType,
            local.BuildType,
            StringComparer.Ordinal,
            conflicts,
            ref changed);
        var buildId = Merge(
            "buildId",
            original.BuildId,
            remote.BuildId,
            local.BuildId,
            StringComparer.Ordinal,
            conflicts,
            ref changed);
        var marketId = Merge(
            "marketId",
            original.MarketId,
            remote.MarketId,
            local.MarketId,
            EqualityComparer<long?>.Default,
            conflicts,
            ref changed);
        var status = Merge(
            "status",
            original.Status,
            remote.Status,
            local.Status,
            EqualityComparer<ColonizationSystemSiteStatus>.Default,
            conflicts,
            ref changed);
        return new MergeResult(
            remote with
            {
                Id = id,
                Name = name,
                BodyNumber = body,
                BuildType = buildType,
                BuildId = buildId,
                MarketId = marketId,
                Status = status,
                ExtensionData = CloneJsonMap(remote.ExtensionData),
            },
            changed,
            conflicts);
    }

    private static T Merge<T>(
        string field,
        T original,
        T remote,
        T local,
        IEqualityComparer<T> comparer,
        ICollection<string> conflicts,
        ref bool changed)
    {
        var localChanged = !comparer.Equals(original, local);
        if (!localChanged)
        {
            return remote;
        }

        changed = true;
        if (!comparer.Equals(original, remote)
            && !comparer.Equals(remote, local))
        {
            conflicts.Add(field);
        }

        return local;
    }

    private static ColonizationSystemSite? FindMatch(
        IReadOnlyList<ColonizationSystemSite> sites,
        ColonizationSystemSite target)
    {
        if (!string.IsNullOrWhiteSpace(target.Id))
        {
            var byId = sites.FirstOrDefault(site => string.Equals(
                site.Id,
                target.Id,
                StringComparison.Ordinal));
            if (byId is not null)
            {
                return byId;
            }
        }

        return string.IsNullOrWhiteSpace(target.Name)
            ? null
            : sites.FirstOrDefault(site => string.Equals(
                site.Name,
                target.Name,
                StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateUniqueSites(
        IReadOnlyList<ColonizationSystemSite> sites,
        string source)
    {
        var unnamedSite = sites.FirstOrDefault(site =>
            string.IsNullOrWhiteSpace(site.Name));
        if (unnamedSite is not null)
        {
            throw new InvalidDataException(
                $"A {source} colonisation site has no name.");
        }

        var duplicateId = sites
            .Where(site => !string.IsNullOrWhiteSpace(site.Id))
            .GroupBy(site => site.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvalidDataException(
                $"The {source} sites contain duplicate ID '{duplicateId.Key}'.");
        }

        var duplicateName = sites
            .GroupBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null)
        {
            throw new InvalidDataException(
                $"The {source} sites contain duplicate name '{duplicateName.Key}'.");
        }
    }

    private static bool KnownFieldsEqual(
        ColonizationSystemSite left,
        ColonizationSystemSite right)
    {
        return string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && left.BodyNumber == right.BodyNumber
            && string.Equals(
                left.BuildType,
                right.BuildType,
                StringComparison.Ordinal)
            && string.Equals(
                left.BuildId,
                right.BuildId,
                StringComparison.Ordinal)
            && left.MarketId == right.MarketId
            && left.Status == right.Status;
    }

    private static bool AllFieldsEqual(
        ColonizationSystemSite left,
        ColonizationSystemSite right)
    {
        return KnownFieldsEqual(left, right)
            && JsonMapsEqual(left.ExtensionData, right.ExtensionData);
    }

    private static bool JsonMapsEqual(
        IReadOnlyDictionary<string, JsonElement> left,
        IReadOnlyDictionary<string, JsonElement> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value)
                || !JsonElement.DeepEquals(pair.Value, value))
            {
                return false;
            }
        }

        return true;
    }

    private static ColonizationSystemSite Clone(ColonizationSystemSite site)
    {
        return site with { ExtensionData = CloneJsonMap(site.ExtensionData) };
    }

    private static Dictionary<string, JsonElement> CloneJsonMap(
        IReadOnlyDictionary<string, JsonElement> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal);
    }

    private static string DisplayIdentity(ColonizationSystemSite site)
    {
        return string.IsNullOrWhiteSpace(site.Id)
            ? site.Name
            : $"{site.Name} ({site.Id})";
    }

    private sealed record MergeResult(
        ColonizationSystemSite Site,
        bool HasLocalChanges,
        IReadOnlyList<string> Conflicts);
}

public sealed record ColonizationSystemSiteReconciliationPlan(
    ColonizationSystemSiteUpdate Update,
    IReadOnlyList<ColonizationSystemSiteConflict> Conflicts,
    int UnchangedCount)
{
    public bool HasChanges => Update.UpdatedSites.Count > 0
        || Update.DeletedSiteIds.Count > 0;

    public bool CanPublish => Conflicts.Count == 0 && HasChanges;
}

public sealed record ColonizationSystemSiteConflict(
    string Site,
    string Field,
    string Message);
