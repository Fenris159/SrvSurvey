using System.Text.Json;

namespace SrvSurvey.Core.Storage;

public sealed class CommanderProfileCatalog(string profileDirectory)
{
    public string ProfileDirectory { get; } = Path.GetFullPath(profileDirectory);

    public async Task<CommanderProfileCatalogResult> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(ProfileDirectory))
        {
            return new CommanderProfileCatalogResult([], []);
        }

        var candidates = new List<ProfileCandidate>();
        var warnings = new List<string>();
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var paths = Directory.EnumerateFiles(
                ProfileDirectory,
                "F*-live.json",
                SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(
                ProfileDirectory,
                "F*-legacy.json",
                SearchOption.TopDirectoryOnly))
            .Distinct(pathComparer)
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var candidate = await ReadCandidateAsync(path, cancellationToken)
                    .ConfigureAwait(false);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
                else
                {
                    warnings.Add(
                        $"Ignored {Path.GetFileName(path)} because it has no valid commander identity.");
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or JsonException)
            {
                warnings.Add(
                    $"Could not read {Path.GetFileName(path)}: {exception.Message}");
            }
        }

        var profiles = candidates
            .GroupBy(candidate => candidate.FrontierId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var preferred = group
                    .OrderByDescending(candidate => candidate.IsOdyssey)
                    .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
                    .First();
                return new CommanderProfileIdentity(
                    preferred.FrontierId,
                    preferred.CommanderName,
                    group.Any(candidate => candidate.IsOdyssey),
                    group.Any(candidate => !candidate.IsOdyssey));
            })
            .OrderBy(profile => profile.CommanderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.FrontierId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new CommanderProfileCatalogResult(profiles, warnings);
    }

    private static async Task<ProfileCandidate?> ReadCandidateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var fileName = Path.GetFileName(path);
        var suffix = fileName.EndsWith(
            "-live.json",
            StringComparison.OrdinalIgnoreCase)
                ? "-live.json"
                : "-legacy.json";
        var fileFrontierId = fileName[..^suffix.Length];
        var frontierId = GetString(root, "fid") ?? fileFrontierId;
        var commanderName = GetString(root, "commander");
        if (!IsFrontierId(frontierId) || string.IsNullOrWhiteSpace(commanderName))
        {
            return null;
        }

        var isOdyssey = GetBoolean(root, "isOdyssey")
            ?? suffix.Equals("-live.json", StringComparison.OrdinalIgnoreCase);
        return new ProfileCandidate(
            frontierId.ToUpperInvariant(),
            commanderName.Trim(),
            isOdyssey,
            File.GetLastWriteTimeUtc(path));
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static bool IsFrontierId(string? value)
    {
        return value is not null
            && value.Length > 1
            && value[0] is 'F' or 'f'
            && value[1..].All(char.IsAsciiDigit);
    }

    private sealed record ProfileCandidate(
        string FrontierId,
        string CommanderName,
        bool IsOdyssey,
        DateTime LastWriteTimeUtc);
}

public sealed record CommanderProfileCatalogResult(
    IReadOnlyList<CommanderProfileIdentity> Profiles,
    IReadOnlyList<string> Warnings);

public sealed record CommanderProfileIdentity(
    string FrontierId,
    string CommanderName,
    bool HasLiveProfile,
    bool HasLegacyProfile);
