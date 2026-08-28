using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSurveyShareService
{
    private readonly string dataDirectory;
    private readonly GuardianPublishedSiteCatalog publishedSites;

    public GuardianSurveyShareService(
        string dataDirectory,
        GuardianPublishedSiteCatalog? publishedSites = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        this.publishedSites = publishedSites
            ?? GuardianPublishedSiteCatalog.LoadEmbedded();
    }

    public IReadOnlyList<string> GetDiscoveryReasons(
        GuardianCommanderSiteSurvey survey)
    {
        ArgumentNullException.ThrowIfNull(survey);
        var reasons = new List<string>();
        AddPortableSurveyReasons(survey, reasons);

        var kind = GetKind(survey);
        var published = publishedSites.Find(
            kind,
            survey.BodyName,
            survey.Index);
        if (published is null)
        {
            if (HasSurveyData(survey))
            {
                reasons.Add("No published survey");
            }

            return reasons;
        }

        AddPublishedDifferenceReasons(survey, kind, published, reasons);
        return reasons;
    }

    private static void AddPortableSurveyReasons(
        GuardianCommanderSiteSurvey survey,
        List<string> reasons)
    {
        if (survey.Survey.RawPointsOfInterest?.Count > 0)
        {
            reasons.Add("Raw points of interest");
        }

        if (survey.Survey.ComponentMaterials.Count > 0)
        {
            reasons.Add("Component materials");
        }

        if (survey.MapMarkerOffset != default)
        {
            reasons.Add("Map alignment offset");
        }
    }

    private static void AddPublishedDifferenceReasons(
        GuardianCommanderSiteSurvey survey,
        GuardianSiteKind kind,
        GuardianPublishedSite published,
        List<string> reasons)
    {
        if (published.SiteHeading == -1 && survey.Survey.SiteHeading != -1)
        {
            reasons.Add("Site heading");
        }

        if (kind == GuardianSiteKind.Ruins
            && published.RelicTowerHeading == -1
            && survey.Survey.RelicTowerHeading != -1)
        {
            reasons.Add("Relic tower heading");
        }

        if (published.Location is null && survey.Survey.Location is not null)
        {
            reasons.Add("Surface location");
        }

        if (survey.Survey.PoiStatuses.Any(pair =>
                !published.PoiStatuses.TryGetValue(pair.Key, out var status)
                || status != pair.Value))
        {
            reasons.Add("Point-of-interest status");
        }

        if (survey.Survey.RelicHeadings.Keys.Any(
                name => !published.RelicHeadings.ContainsKey(name)))
        {
            reasons.Add("Relic heading");
        }

        var groups = string.Concat(survey.ObeliskGroups.Order());
        if (!string.Equals(
                published.ObeliskGroups,
                groups,
                StringComparison.Ordinal))
        {
            reasons.Add("Obelisk groups");
        }
    }

    public async Task<GuardianSurveyShareBundle> PrepareAsync(
        string frontierId,
        bool isOdyssey,
        GuardianCommanderDataReadResult commanderData,
        CancellationToken cancellationToken = default)
    {
        ValidateFrontierId(frontierId);
        ArgumentNullException.ThrowIfNull(commanderData);
        var sourceRoot = Path.Combine(
            dataDirectory,
            "guardian",
            frontierId,
            isOdyssey ? string.Empty : "legacy");
        sourceRoot = Path.GetFullPath(sourceRoot);
        var sites = commanderData.Surveys
            .Select(survey => new GuardianSurveyShareSite(
                GetDisplayName(survey),
                ValidateSourcePath(sourceRoot, survey.Path),
                GetDiscoveryReasons(survey)))
            .Where(site => site.Reasons.Count > 0)
            .OrderBy(site => site.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(site => site.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var shareDirectory = Path.Combine(dataDirectory, "share");
        Directory.CreateDirectory(shareDirectory);
        var hash = await ComputeHashAsync(sites, cancellationToken)
            .ConfigureAwait(false);
        var archivePath = Path.Combine(
            shareDirectory,
            $"surveys-{frontierId}-{hash}.zip");
        if (!File.Exists(archivePath))
        {
            await WriteArchiveAsync(archivePath, sites, cancellationToken)
                .ConfigureAwait(false);
        }

        return new GuardianSurveyShareBundle(archivePath, sites);
    }

    private static async Task<string> ComputeHashAsync(
        IReadOnlyList<GuardianSurveyShareSite> sites,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var sourcePath in sites.Select(site => site.SourcePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(Encoding.UTF8.GetBytes(
                Path.GetFileName(sourcePath)));
            await using var stream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer.AsSpan(0, read));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant()[..16];
    }

    private static async Task WriteArchiveAsync(
        string archivePath,
        IReadOnlyList<GuardianSurveyShareSite> sites,
        CancellationToken cancellationToken)
    {
        var temporaryPath = archivePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous))
            using (var archive = new ZipArchive(
                       output,
                       ZipArchiveMode.Create,
                       leaveOpen: false))
            {
                var entryNames = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var sourcePath in sites.Select(site => site.SourcePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entryName = Path.GetFileName(sourcePath);
                    if (!entryNames.Add(entryName))
                    {
                        throw new InvalidDataException(
                            $"Multiple Guardian surveys use the filename {entryName}.");
                    }

                    var entry = archive.CreateEntry(
                        entryName,
                        CompressionLevel.SmallestSize);
                    await using var input = new FileStream(
                        sourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete,
                        16 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using var entryStream = await entry.OpenAsync(
                        cancellationToken);
                    await input.CopyToAsync(entryStream, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            try
            {
                File.Move(temporaryPath, archivePath, false);
            }
            catch (IOException) when (File.Exists(archivePath))
            {
                // Another instance prepared the same content-addressed bundle.
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ValidateSourcePath(string sourceRoot, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(sourceRoot, fullPath);
        if (relative == ".."
            || relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException(
                $"Guardian survey path is outside the commander folder: {path}");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "The Guardian survey file no longer exists.",
                fullPath);
        }

        return fullPath;
    }

    private static bool HasSurveyData(GuardianCommanderSiteSurvey survey)
    {
        return survey.Survey.SiteHeading != -1
            || survey.Survey.RelicTowerHeading != -1
            || survey.Survey.Location is not null
            || survey.Survey.PoiStatuses.Count > 0
            || survey.Survey.RelicHeadings.Count > 0
            || survey.Survey.ComponentMaterials.Count > 0
            || survey.Survey.RawPointsOfInterest?.Count > 0
            || survey.MapMarkerOffset != default
            || survey.ObeliskGroups.Count > 0;
    }

    private static GuardianSiteKind GetKind(
        GuardianCommanderSiteSurvey survey)
    {
        return survey.SiteType.Equals("Alpha", StringComparison.OrdinalIgnoreCase)
            || survey.SiteType.Equals("Beta", StringComparison.OrdinalIgnoreCase)
            || survey.SiteType.Equals("Gamma", StringComparison.OrdinalIgnoreCase)
                ? GuardianSiteKind.Ruins
                : GuardianSiteKind.Structure;
    }

    private static string GetDisplayName(GuardianCommanderSiteSurvey survey)
    {
        if (!string.IsNullOrWhiteSpace(survey.LocalizedName))
        {
            return survey.LocalizedName;
        }

        if (!string.IsNullOrWhiteSpace(survey.Name))
        {
            return survey.Name;
        }

        return $"{survey.BodyName} #{survey.Index}";
    }

    private static void ValidateFrontierId(string frontierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        if (frontierId is "." or ".."
            || !string.Equals(
                Path.GetFileName(frontierId),
                frontierId,
                StringComparison.Ordinal)
            || frontierId.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException(
                "The Frontier ID must be a folder name, not a path.",
                nameof(frontierId));
        }
    }
}

public sealed record GuardianSurveyShareBundle(
    string ArchivePath,
    IReadOnlyList<GuardianSurveyShareSite> Sites);

public sealed record GuardianSurveyShareSite(
    string DisplayName,
    string SourcePath,
    IReadOnlyList<string> Reasons);
