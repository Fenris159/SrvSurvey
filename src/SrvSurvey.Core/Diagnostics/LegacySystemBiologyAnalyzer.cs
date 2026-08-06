using System.Text.Json;

namespace SrvSurvey.Core.Diagnostics;

public sealed class LegacySystemBiologyAnalyzer
{
    private readonly string dataDirectory;

    public LegacySystemBiologyAnalyzer(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
    }

    public async Task<LegacySystemBiologyAnalysisResult> AnalyzeAsync(
        string frontierId,
        IProgress<LegacySystemBiologyAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFrontierId(frontierId);
        var systemDirectory = Path.Combine(
            dataDirectory,
            "systems",
            frontierId);
        if (!Directory.Exists(systemDirectory))
        {
            return LegacySystemBiologyAnalysisResult.Empty;
        }

        var files = new DirectoryInfo(systemDirectory)
            .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        var warnings = new List<string>();
        var summaries = new Dictionary<string, MutableSpeciesSummary>(
            StringComparer.Ordinal);
        var processedFiles = 0;
        var bodyCount = 0;
        var organismCount = 0;
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            var analyzed = await TryAnalyzeFileAsync(
                    file,
                    summaries,
                    cancellationToken)
                .ConfigureAwait(false);
            if (analyzed.Warning is not null)
            {
                warnings.Add(analyzed.Warning);
            }
            else
            {
                processedFiles++;
                bodyCount += analyzed.BodyCount;
                organismCount += analyzed.OrganismCount;
            }

            Report(
                progress,
                index,
                files,
                file,
                bodyCount,
                organismCount);
        }

        return new LegacySystemBiologyAnalysisResult(
            files.Length,
            processedFiles,
            bodyCount,
            organismCount,
            summaries.Values
                .OrderBy(summary => summary.Name, StringComparer.Ordinal)
                .Select(summary => summary.Create())
                .ToArray(),
            warnings);
    }

    private static async Task<FileAnalysisCounts> TryAnalyzeFileAsync(
        FileInfo file,
        IDictionary<string, MutableSpeciesSummary> summaries,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return FileAnalysisCounts.Failed(
                    $"{file.Name}: the root value is not an object.");
            }

            var bodyCount = 0;
            var organismCount = 0;
            AnalyzeBodies(
                document.RootElement,
                summaries,
                ref bodyCount,
                ref organismCount,
                cancellationToken);
            return new FileAnalysisCounts(bodyCount, organismCount, null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return FileAnalysisCounts.Failed($"{file.Name}: {exception.Message}");
        }
    }

    private readonly record struct FileAnalysisCounts(
        int BodyCount,
        int OrganismCount,
        string? Warning)
    {
        public static FileAnalysisCounts Failed(string warning) =>
            new(0, 0, warning);
    }

    private static void AnalyzeBodies(
        JsonElement root,
        IDictionary<string, MutableSpeciesSummary> summaries,
        ref int bodyCount,
        ref int organismCount,
        CancellationToken cancellationToken)
    {
        if (!TryGetProperty(root, "bodies", out var bodies)
            || bodies.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var body in bodies.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (body.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            bodyCount++;
            AnalyzeBody(body, summaries, ref organismCount);
        }
    }

    private static void AnalyzeBody(
        JsonElement body,
        IDictionary<string, MutableSpeciesSummary> summaries,
        ref int organismCount)
    {
        if (!TryGetProperty(body, "organisms", out var organisms)
            || organisms.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        string? atmosphereComponents = null;
        if (TryGetProperty(
                body,
                "atmosphereComposition",
                out var composition)
            && composition.ValueKind == JsonValueKind.Object)
        {
            atmosphereComponents = string.Join(
                ',',
                composition.EnumerateObject().Select(property => property.Name));
        }

        foreach (var organism in organisms.EnumerateArray())
        {
            organismCount++;
            if (organism.ValueKind != JsonValueKind.Object
                || !TryGetProperty(
                    organism,
                    "speciesLocalized",
                    out var species)
                || species.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(species.GetString()))
            {
                continue;
            }

            var name = species.GetString()!;
            if (!summaries.TryGetValue(name, out var summary))
            {
                summary = new MutableSpeciesSummary(name);
                summaries.Add(name, summary);
            }

            summary.Count++;
            if (atmosphereComponents is not null)
            {
                summary.AtmosphereCounts[atmosphereComponents] =
                    summary.AtmosphereCounts.GetValueOrDefault(
                        atmosphereComponents) + 1;
            }
        }
    }

    private static bool TryGetProperty(
        JsonElement root,
        string name,
        out JsonElement value)
    {
        if (root.TryGetProperty(name, out value))
        {
            return true;
        }

        var matchedValue = root.EnumerateObject()
            .Where(property => string.Equals(
                property.Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            .Select(property => (JsonElement?)property.Value)
            .FirstOrDefault();
        if (matchedValue is { } found)
        {
            value = found;
            return true;
        }

        value = default;
        return false;
    }

    private static void Report(
        IProgress<LegacySystemBiologyAnalysisProgress>? progress,
        int index,
        FileInfo[] files,
        FileInfo file,
        int bodyCount,
        int organismCount)
    {
        progress?.Report(new LegacySystemBiologyAnalysisProgress(
            index + 1,
            files.Length,
            file.Name,
            bodyCount,
            organismCount));
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

    private sealed class MutableSpeciesSummary(string name)
    {
        public string Name { get; } = name;

        public int Count { get; set; }

        public Dictionary<string, int> AtmosphereCounts { get; } =
            new(StringComparer.Ordinal);

        public LegacySystemBiologySpeciesSummary Create()
        {
            return new LegacySystemBiologySpeciesSummary(
                Name,
                Count,
                AtmosphereCounts
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new LegacyAtmosphereCompositionSummary(
                        pair.Key,
                        pair.Value))
                    .ToArray());
        }
    }
}

public sealed record LegacySystemBiologyAnalysisProgress(
    int ProcessedFileCount,
    int TotalFileCount,
    string CurrentFile,
    int BodyCount,
    int OrganismCount);

public sealed record LegacySystemBiologyAnalysisResult(
    int CandidateFileCount,
    int ProcessedFileCount,
    int BodyCount,
    int OrganismCount,
    IReadOnlyList<LegacySystemBiologySpeciesSummary> Species,
    IReadOnlyList<string> Warnings)
{
    public static LegacySystemBiologyAnalysisResult Empty { get; } =
        new(0, 0, 0, 0, [], []);
}

public sealed record LegacySystemBiologySpeciesSummary(
    string Name,
    int Count,
    IReadOnlyList<LegacyAtmosphereCompositionSummary> AtmosphereCompositions);

public sealed record LegacyAtmosphereCompositionSummary(
    string Components,
    int Count);
