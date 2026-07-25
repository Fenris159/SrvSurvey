using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Colonization;

public sealed class LegacyColonizationProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly string dataDirectory;

    public LegacyColonizationProfileStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
    }

    public async Task<LegacyColonizationProfileLoadResult> LoadAsync(
        string frontierId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        var normalizedFrontierId = frontierId.Trim();
        var path = Path.Combine(
            dataDirectory,
            normalizedFrontierId + "-colony.json");
        if (!File.Exists(path))
        {
            return new LegacyColonizationProfileLoadResult(
                path,
                false,
                null,
                null,
                []);
        }

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<LegacyDocument>(
                    stream,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    "The legacy colonisation profile is empty.");
            var warnings = new List<string>();
            var projects = (document.Projects ?? [])
                .Where(project =>
                {
                    if (!string.IsNullOrWhiteSpace(project.BuildId))
                    {
                        return true;
                    }

                    warnings.Add(
                        "A cached colonisation project without a build ID was ignored.");
                    return false;
                })
                .Select(NormalizeProject)
                .ToArray();
            var hidden = (document.HiddenProjectIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var carriers = (document.LinkedFleetCarriers
                    ?? new Dictionary<long, ColonizationFleetCarrier>())
                .Values
                .Where(carrier => carrier is not null)
                .Select(NormalizeFleetCarrier)
                .GroupBy(carrier => carrier.MarketId)
                .Select(group => group.Last())
                .ToArray();
            var snapshot = new LegacyColonizationProfileSnapshot(
                document.FrontierId ?? normalizedFrontierId,
                document.CommanderName,
                projects,
                hidden,
                document.PrimaryProjectId,
                carriers);
            return new LegacyColonizationProfileLoadResult(
                path,
                true,
                snapshot,
                null,
                warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException)
        {
            return new LegacyColonizationProfileLoadResult(
                path,
                true,
                null,
                exception.Message,
                []);
        }
    }

    private static ColonizationProject NormalizeProject(
        ColonizationProject project)
    {
        return project with
        {
            BuildId = project.BuildId.Trim(),
            BuildType = project.BuildType ?? string.Empty,
            BuildName = project.BuildName ?? string.Empty,
            SystemName = project.SystemName ?? string.Empty,
            StarPosition = project.StarPosition ?? [],
            Commanders = project.Commanders
                ?? new Dictionary<string, HashSet<string>>(
                    StringComparer.OrdinalIgnoreCase),
            Commodities = project.Commodities
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
            Ready = project.Ready
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            LinkedFleetCarriers = (project.LinkedFleetCarriers ?? [])
                .Where(carrier => carrier is not null)
                .Select(carrier => carrier with
                {
                    Name = carrier.Name ?? string.Empty,
                    DisplayName = carrier.DisplayName ?? string.Empty,
                    AssignedCommodities = carrier.AssignedCommodities
                        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                })
                .ToList(),
        };
    }

    private static ColonizationFleetCarrier NormalizeFleetCarrier(
        ColonizationFleetCarrier carrier)
    {
        return carrier with
        {
            Name = carrier.Name ?? string.Empty,
            DisplayName = carrier.DisplayName ?? string.Empty,
            Cargo = carrier.Cargo
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        };
    }

    private sealed record LegacyDocument
    {
        [JsonPropertyName("fid")]
        public string? FrontierId { get; init; }

        [JsonPropertyName("cmdr")]
        public string? CommanderName { get; init; }

        [JsonPropertyName("projects")]
        public ColonizationProject[]? Projects { get; init; }

        [JsonPropertyName("hiddenIDs")]
        public string[]? HiddenProjectIds { get; init; }

        [JsonPropertyName("primaryBuildId")]
        public string? PrimaryProjectId { get; init; }

        [JsonPropertyName("linkedFCs")]
        public Dictionary<long, ColonizationFleetCarrier>?
            LinkedFleetCarriers
        { get; init; }
    }
}

public sealed record LegacyColonizationProfileSnapshot(
    string FrontierId,
    string? CommanderName,
    IReadOnlyList<ColonizationProject> Projects,
    IReadOnlyList<string> HiddenProjectIds,
    string? PrimaryProjectId,
    IReadOnlyList<ColonizationFleetCarrier> FleetCarriers);

public sealed record LegacyColonizationProfileLoadResult(
    string Path,
    bool Exists,
    LegacyColonizationProfileSnapshot? Snapshot,
    string? Error,
    IReadOnlyList<string> Warnings);
