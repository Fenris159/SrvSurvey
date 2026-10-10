using System.Globalization;

namespace SrvSurvey.Core.Colonization;

public static partial class ColonizationProjectCsvExporter
{
    /// <summary>Lists each included build with its system and readable Raven type for the report's project card.</summary>
    public static IEnumerable<KeyValuePair<string, string>> BuildDetails(ColonizationProjectPreview preview) =>
        preview.Projects.Select(project =>
            KeyValuePair.Create(project.BuildName, $"{project.SystemName} · {BuildTypeName(project)}")
        );

    /// <summary>Uses the reference type name when known and preserves unrecognized public build types.</summary>
    private static string BuildTypeName(ColonizationProject project) =>
        ColonizationProjectPreview.ResolveEffects(project.BuildType)?.Name ?? project.BuildType;

    /// <summary>Flattens grouped effects with their system identity for the compact CSV export.</summary>
    public static IEnumerable<KeyValuePair<string, string>> CombinedEffectDetails(ColonizationProjectPreview preview) =>
        SystemEffectGroups(preview)
            .SelectMany(system =>
                system.Fields.Select(field => KeyValuePair.Create($"{system.Name} · {field.Key}", field.Value))
            );

    /// <summary>Keeps a system heading separate from its short effect labels in the native report.</summary>
    public static IEnumerable<ColonizationPreviewSystemEffects> SystemEffectGroups(
        ColonizationProjectPreview preview
    ) =>
        preview
            .Projects.GroupBy(project => project.SystemName, StringComparer.OrdinalIgnoreCase)
            .Select(system => new ColonizationPreviewSystemEffects(
                string.IsNullOrWhiteSpace(system.Key) ? "Unknown system" : system.Key,
                SystemEffectDetails(system).ToArray()
            ));

    /// <summary>Combines one system's numeric effects while preserving prerequisites and missing reference data.</summary>
    private static IEnumerable<KeyValuePair<string, string>> SystemEffectDetails(
        IEnumerable<ColonizationProject> projects
    )
    {
        (ColonizationProject Project, ColonizationBuildEffects? Effects)[] builds = projects
            .Select(project =>
                (Project: project, Effects: ColonizationProjectPreview.ResolveEffects(project.BuildType))
            )
            .ToArray();
        ColonizationBuildEffects[] known = builds
            .Where(build => build.Effects is not null)
            .Select(build => build.Effects!)
            .ToArray();
        yield return KeyValuePair.Create(
            "System score",
            builds.All(build => build.Effects?.Score is not null)
                ? Number(known.Sum(effects => (long)effects.Score!.Value))
                : "Unknown"
        );
        foreach (
            IGrouping<string, KeyValuePair<string, int>> effect in known
                .SelectMany(effects => effects.Effects)
                .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        )
        {
            yield return KeyValuePair.Create(
                effect.Key,
                effect.Sum(pair => (long)pair.Value).ToString("+0;-0;0", CultureInfo.InvariantCulture)
            );
        }
        foreach (
            IGrouping<int, ColonizationBuildEffects> tier in known
                .Where(effects => effects.NeedsCount > 0)
                .GroupBy(effects => effects.NeedsTier)
                .OrderBy(group => group.Key)
        )
        {
            yield return KeyValuePair.Create(
                $"Needs Tier {tier.Key} points",
                Number(tier.Sum(effects => (long)effects.NeedsCount))
            );
        }
        foreach (
            IGrouping<int, ColonizationBuildEffects> tier in known
                .Where(effects => effects.GivesCount > 0)
                .GroupBy(effects => effects.GivesTier)
                .OrderBy(group => group.Key)
        )
        {
            yield return KeyValuePair.Create(
                $"Provides Tier {tier.Key} points",
                Number(tier.Sum(effects => (long)effects.GivesCount))
            );
        }
        int[][] pads = builds
            .Where(build => build.Effects is not null)
            .Select(build =>
                build.Effects!.LandingPads.GetValueOrDefault(
                    ColonizationBuildCatalog.NormalizeSiteBuildTypeKey(build.Project.BuildType).ToLowerInvariant()
                ) ?? new int[3]
            )
            .ToArray();
        yield return KeyValuePair.Create(
            "Landing pads",
            known.Length == builds.Length
                ? $"Small: {pads.Sum(pad => (long)pad[0])}, Medium: {pads.Sum(pad => (long)pad[1])}, Large: {pads.Sum(pad => (long)pad[2])}"
                : "Unknown"
        );
        var contextual = new Dictionary<string, string>
        {
            ["Economic influence"] = string.Join(
                "; ",
                known
                    .Select(effects => effects.Economy)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ),
            ["Requires"] = string.Join(
                "; ",
                builds
                    .Where(build => !string.IsNullOrWhiteSpace(build.Effects?.Prerequisite))
                    .Select(build => $"{build.Project.BuildName}: {build.Effects!.Prerequisite}")
            ),
            ["Unlocks"] = string.Join(
                "; ",
                known.SelectMany(effects => effects.Unlocks).Distinct(StringComparer.OrdinalIgnoreCase)
            ),
        };
        foreach (KeyValuePair<string, string> field in contextual.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)))
        {
            yield return field;
        }
        if (known.Length < builds.Length)
        {
            yield return KeyValuePair.Create(
                "Reference data missing",
                string.Join("; ", builds.Where(build => build.Effects is null).Select(build => build.Project.BuildName))
            );
        }
    }
}

/// <summary>A system heading and its combined reference effects, shared by native reporting and CSV export.</summary>
public sealed record ColonizationPreviewSystemEffects(string Name, IReadOnlyList<KeyValuePair<string, string>> Fields);
