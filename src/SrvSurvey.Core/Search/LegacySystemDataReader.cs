using System.Text.Json;

namespace SrvSurvey.Core.Search;

public sealed class LegacySystemDataReader(string dataDirectory)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string dataDirectory = GetFullPath(dataDirectory);

    public async Task<LegacySystemDataReadResult> ReadAsync(
        string frontierId,
        BoxelAddress boxel,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        ArgumentNullException.ThrowIfNull(boxel);
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

        var systemDirectory = Path.Combine(dataDirectory, "systems", frontierId);
        if (!Directory.Exists(systemDirectory))
        {
            return LegacySystemDataReadResult.Empty;
        }

        var systems = new List<BoxelSystemObservation>();
        var errors = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     systemDirectory,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            LegacySystemData? data;
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                data = await JsonSerializer.DeserializeAsync<LegacySystemData>(
                        stream,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException)
            {
                errors.Add($"Could not read {path}: {exception.Message}");
                continue;
            }

            var resolved = data?.Address > 0
                ? BoxelAddress.TryFromSystemAddress(
                    data.Address,
                    data.Name,
                    out var systemBoxel)
                : BoxelAddress.TryParse(data?.Name, out systemBoxel);
            if (data is null
                || !resolved
                || systemBoxel is null
                || !string.Equals(
                    systemBoxel.Prefix,
                    boxel.Prefix,
                    StringComparison.Ordinal))
            {
                continue;
            }

            systems.Add(new BoxelSystemObservation(
                systemBoxel,
                GetCoordinate(data.StarPos),
                data.LastVisited,
                null,
                false,
                data.FssAllBodies));
        }

        return new LegacySystemDataReadResult(
            systems
                .OrderBy(system => system.Boxel.N2)
                .ToArray(),
            errors);
    }

    private static string GetFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private static GalacticCoordinate? GetCoordinate(IReadOnlyList<double>? position)
    {
        return position is { Count: >= 3 }
            && double.IsFinite(position[0])
            && double.IsFinite(position[1])
            && double.IsFinite(position[2])
                ? new GalacticCoordinate(position[0], position[1], position[2])
                : null;
    }

    private sealed record LegacySystemData(
        string? Name,
        long Address,
        IReadOnlyList<double>? StarPos,
        DateTimeOffset? LastVisited,
        bool FssAllBodies);
}

public sealed record LegacySystemDataReadResult(
    IReadOnlyList<BoxelSystemObservation> Systems,
    IReadOnlyList<string> Errors)
{
    public static LegacySystemDataReadResult Empty { get; } = new([], []);
}
