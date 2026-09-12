using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Storage;

public sealed class CommanderProfileCatalog
{
    private readonly IReadOnlyList<string> journalDirectories;

    public CommanderProfileCatalog(string profileDirectory, IReadOnlyList<string>? journalDirectories = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        ProfileDirectory = Path.GetFullPath(profileDirectory);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        this.journalDirectories = (journalDirectories ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(pathComparer)
            .ToArray();
    }

    public string ProfileDirectory { get; }

    public async Task<CommanderProfileCatalogResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<ProfileCandidate>();
        var warnings = new List<string>();
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = Directory.Exists(ProfileDirectory)
            ? Directory
                .EnumerateFiles(ProfileDirectory, "F*-live.json", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(ProfileDirectory, "F*-legacy.json", SearchOption.TopDirectoryOnly))
                .Distinct(pathComparer)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var candidate = await ReadCandidateAsync(path, cancellationToken).ConfigureAwait(false);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
                else
                {
                    warnings.Add($"Ignored {Path.GetFileName(path)} because it has no valid commander identity.");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                warnings.Add($"Could not read {Path.GetFileName(path)}: {exception.Message}");
            }
        }

        foreach (var journalDirectory in journalDirectories.Where(Directory.Exists))
        {
            foreach (var journalPath in EnumerateJournals(journalDirectory, warnings))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var candidate = await ReadJournalCandidateAsync(journalPath, cancellationToken)
                        .ConfigureAwait(false);
                    if (candidate is not null)
                    {
                        candidates.Add(candidate);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Could not read {Path.GetFileName(journalPath)}: {exception.Message}");
                }
            }
        }

        var profiles = candidates
            .GroupBy(candidate => candidate.FrontierId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var preferred = group
                    .OrderByDescending(candidate => candidate.JournalDirectory is not null)
                    .ThenByDescending(candidate => candidate.IsOdyssey)
                    .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
                    .First();
                var source = group
                    .Where(candidate => candidate.JournalDirectory is not null)
                    .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
                    .FirstOrDefault();
                return new CommanderProfileIdentity(
                    preferred.FrontierId,
                    source?.CommanderName ?? preferred.CommanderName,
                    group.Any(candidate => candidate.IsOdyssey),
                    group.Any(candidate => !candidate.IsOdyssey),
                    source?.JournalDirectory
                );
            })
            .OrderBy(profile => profile.CommanderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.FrontierId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new CommanderProfileCatalogResult(profiles, warnings);
    }

    private static string[] EnumerateJournals(string journalDirectory, List<string> warnings)
    {
        try
        {
            return Directory
                .EnumerateFiles(journalDirectory, "Journal.*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenByDescending(Path.GetFileName, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not inspect {journalDirectory}: {exception.Message}");
            return [];
        }
    }

    private static async Task<ProfileCandidate?> ReadCandidateAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        using var document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var fileName = Path.GetFileName(path);
        var suffix = fileName.EndsWith("-live.json", StringComparison.OrdinalIgnoreCase)
            ? "-live.json"
            : "-legacy.json";
        var fileFrontierId = fileName[..^suffix.Length];
        var frontierId = GetString(root, "fid") ?? fileFrontierId;
        var commanderName = GetString(root, "commander");
        if (!IsFrontierId(frontierId) || string.IsNullOrWhiteSpace(commanderName))
        {
            return null;
        }

        var isOdyssey =
            GetBoolean(root, "isOdyssey") ?? suffix.Equals("-live.json", StringComparison.OrdinalIgnoreCase);
        return new ProfileCandidate(
            frontierId.ToUpperInvariant(),
            commanderName.Trim(),
            isOdyssey,
            File.GetLastWriteTimeUtc(path),
            JournalDirectory: null
        );
    }

    private static async Task<ProfileCandidate?> ReadJournalCandidateAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        var isOdyssey = true;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!JournalEventEnvelope.TryParse(line, out var journalEvent, out _) || journalEvent is null)
            {
                continue;
            }

            if (
                journalEvent.EventName == "Fileheader"
                && journalEvent.Payload.TryGetProperty("Odyssey", out var odysseyValue)
                && odysseyValue.ValueKind is JsonValueKind.True or JsonValueKind.False
            )
            {
                isOdyssey = odysseyValue.GetBoolean();
                continue;
            }

            if (journalEvent.EventName is not ("Commander" or "LoadGame"))
            {
                continue;
            }

            var frontierId = GetString(journalEvent.Payload, "FID");
            var commanderName = GetString(
                journalEvent.Payload,
                journalEvent.EventName == "Commander" ? "Name" : "Commander"
            );
            if (!IsFrontierId(frontierId) || string.IsNullOrWhiteSpace(commanderName))
            {
                continue;
            }

            return new ProfileCandidate(
                frontierId!.ToUpperInvariant(),
                commanderName.Trim(),
                isOdyssey,
                File.GetLastWriteTimeUtc(path),
                Path.GetDirectoryName(path)
            );
        }

        return null;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
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
        return value is not null && value.Length > 1 && value[0] is 'F' or 'f' && value[1..].All(char.IsAsciiDigit);
    }

    private sealed record ProfileCandidate(
        string FrontierId,
        string CommanderName,
        bool IsOdyssey,
        DateTime LastWriteTimeUtc,
        string? JournalDirectory
    );
}

public sealed record CommanderProfileCatalogResult(
    IReadOnlyList<CommanderProfileIdentity> Profiles,
    IReadOnlyList<string> Warnings
);

public sealed record CommanderProfileIdentity(
    string FrontierId,
    string CommanderName,
    bool HasLiveProfile,
    bool HasLegacyProfile,
    string? JournalDirectory = null
);
