using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Core.Updates;

public sealed record ReferenceCatalogSource(
    string Catalog,
    string? LocalPath,
    string? Warning)
{
    public bool IsLocal => LocalPath is not null && Warning is null;
}

public sealed record LegacyReferenceCatalogLoadResult(
    ExobiologyReferenceCatalog Exobiology,
    BiologyCriteriaCatalog BiologyCriteria,
    GuardianSiteCatalog GuardianSites,
    GuardianPublishedSiteCatalog GuardianPublishedSites,
    GuardianSiteTemplateCatalog GuardianTemplates,
    HumanSiteTemplateCatalog HumanSiteTemplates,
    GreenGasGiantCriteriaCatalog GreenGasGiants,
    IReadOnlyList<ReferenceCatalogSource> Sources)
{
    public int LocalCatalogCount => Sources.Count(source => source.IsLocal);

    public IReadOnlyList<string> Warnings { get; } = Sources
        .Where(source => source.Warning is not null)
        .Select(source => source.Warning!)
        .ToArray();
}

public static class LegacyReferenceCatalogLoader
{
    public static LegacyReferenceCatalogLoadResult Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var root = Path.GetFullPath(dataDirectory);
        var published = Path.Combine(root, "pub");
        var sources = new List<ReferenceCatalogSource>();

        var exobiology = LoadCandidate(
            "Codex reference",
            Path.Combine(root, "codexRef.json"),
            ExobiologyReferenceCatalog.LoadEmbedded,
            path => LoadFile(path, ExobiologyReferenceCatalog.Load),
            candidate => candidate.Count,
            sources);
        var biologyCriteria = LoadCandidate(
            "biology criteria",
            Path.Combine(published, "bio-criteria"),
            BiologyCriteriaCatalog.LoadEmbedded,
            BiologyCriteriaCatalog.LoadDirectory,
            candidate => candidate.Roots.Count,
            sources,
            Directory.Exists);
        var guardianSites = LoadCandidate(
            "Guardian site index",
            published,
            GuardianSiteCatalog.LoadEmbedded,
            GuardianSiteCatalog.LoadPublishedDirectory,
            candidate => candidate.Count,
            sources,
            path => File.Exists(Path.Combine(path, "allRuins.json"))
                && File.Exists(Path.Combine(path, "allStructures.json")));
        var guardianPublishedSites = LoadCandidate(
            "Guardian published surveys",
            Path.Combine(published, "guardian.zip"),
            GuardianPublishedSiteCatalog.LoadEmbedded,
            path => LoadFile(path, GuardianPublishedSiteCatalog.LoadZip),
            candidate => candidate.Count,
            sources);
        var guardianTemplates = LoadCandidate(
            "Guardian site templates",
            Path.Combine(published, "guardianSiteTemplates.json"),
            GuardianSiteTemplateCatalog.LoadEmbedded,
            path => LoadFile(path, GuardianSiteTemplateCatalog.Load),
            candidate => candidate.Count,
            sources);
        var humanSiteTemplates = LoadCandidate(
            "human settlement templates",
            Path.Combine(published, "settlements", "humanSiteTemplates.json"),
            HumanSiteTemplateCatalog.LoadEmbedded,
            path => LoadFile(path, HumanSiteTemplateCatalog.Load),
            candidate => candidate.Count,
            sources);
        var greenGasGiants = LoadCandidate(
            "Green Gas Giant criteria",
            Path.Combine(published, "ggg.json"),
            GreenGasGiantCriteriaCatalog.LoadEmbedded,
            path => LoadFile(path, GreenGasGiantCriteriaCatalog.Load),
            candidate => candidate.TemperatureCount,
            sources);

        return new LegacyReferenceCatalogLoadResult(
            exobiology,
            biologyCriteria,
            guardianSites,
            guardianPublishedSites,
            guardianTemplates,
            humanSiteTemplates,
            greenGasGiants,
            sources);
    }

    private static T LoadCandidate<T>(
        string catalogName,
        string candidatePath,
        Func<T> loadEmbedded,
        Func<string, T> loadCandidate,
        Func<T, int> getCoverage,
        ICollection<ReferenceCatalogSource> sources,
        Func<string, bool>? exists = null)
    {
        var embedded = loadEmbedded();
        exists ??= File.Exists;
        if (!exists(candidatePath))
        {
            sources.Add(new ReferenceCatalogSource(catalogName, null, null));
            return embedded;
        }

        try
        {
            var candidate = loadCandidate(candidatePath);
            var embeddedCoverage = getCoverage(embedded);
            var candidateCoverage = getCoverage(candidate);
            if (candidateCoverage < embeddedCoverage)
            {
                throw new InvalidDataException(
                    $"coverage {candidateCoverage:N0} is below the embedded baseline "
                    + $"of {embeddedCoverage:N0}");
            }

            sources.Add(new ReferenceCatalogSource(
                catalogName,
                candidatePath,
                null));
            return candidate;
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            sources.Add(new ReferenceCatalogSource(
                catalogName,
                null,
                $"Ignored legacy {catalogName} at {candidatePath}: "
                    + exception.Message
                    + " Embedded reference data remains active."));
            return embedded;
        }
    }

    private static T LoadFile<T>(string path, Func<Stream, T> load)
    {
        using var stream = File.OpenRead(path);
        return load(stream);
    }

    private static bool IsRecoverable(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or System.Text.Json.JsonException
            or ArgumentException
            or InvalidOperationException;
    }
}
