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
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
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
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        string[] paths = Directory.Exists(ProfileDirectory)
            ? Directory
                .EnumerateFiles(ProfileDirectory, "F*-live.json", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(ProfileDirectory, "F*-legacy.json", SearchOption.TopDirectoryOnly))
                .Distinct(pathComparer)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];

        await AddStoredProfilesAsync(paths, candidates, warnings, cancellationToken).ConfigureAwait(false);
        await AddJournalProfilesAsync(candidates, warnings, cancellationToken).ConfigureAwait(false);

        CommanderProfileIdentity[] profiles = candidates
            .GroupBy(candidate => candidate.FrontierId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                ProfileCandidate preferred = group
                    .OrderByDescending(candidate => candidate.JournalDirectory is not null)
                    .ThenByDescending(candidate => candidate.IsOdyssey)
                    .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
                    .First();
                ProfileCandidate? source = group
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

    private static async Task AddStoredProfilesAsync(
        IEnumerable<string> paths,
        List<ProfileCandidate> candidates,
        List<string> warnings,
        CancellationToken cancellationToken
    )
    {
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ProfileCandidate? candidate = await ReadCandidateAsync(path, cancellationToken).ConfigureAwait(false);
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
    }

    private async Task AddJournalProfilesAsync(
        List<ProfileCandidate> candidates,
        List<string> warnings,
        CancellationToken cancellationToken
    )
    {
        foreach (string journalDirectory in journalDirectories.Where(Directory.Exists))
        {
            foreach (string journalPath in EnumerateJournals(journalDirectory, warnings))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    ProfileCandidate? candidate = await ReadJournalCandidateAsync(journalPath, cancellationToken)
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
        using JsonDocument document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string fileName = Path.GetFileName(path);
        string suffix = fileName.EndsWith("-live.json", StringComparison.OrdinalIgnoreCase)
            ? "-live.json"
            : "-legacy.json";
        string fileFrontierId = fileName[..^suffix.Length];
        string frontierId = GetString(root, "fid") ?? fileFrontierId;
        string? commanderName = GetString(root, "commander");
        if (!IsFrontierId(frontierId) || string.IsNullOrWhiteSpace(commanderName))
        {
            return null;
        }

        bool isOdyssey =
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
        bool isOdyssey = true;
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
            if (
                !JournalEventEnvelope.TryParse(line, out JournalEventEnvelope? journalEvent, out _)
                || journalEvent is null
            )
            {
                continue;
            }

            if (TryReadOdysseyMode(journalEvent, out bool currentOdysseyMode))
            {
                isOdyssey = currentOdysseyMode;
                continue;
            }

            ProfileCandidate? candidate = CreateJournalCandidate(journalEvent, path, isOdyssey);
            if (candidate is null)
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static bool TryReadOdysseyMode(JournalEventEnvelope journalEvent, out bool isOdyssey)
    {
        if (
            journalEvent.EventName == "Fileheader"
            && journalEvent.Payload.TryGetProperty("Odyssey", out JsonElement odysseyValue)
            && odysseyValue.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            isOdyssey = odysseyValue.GetBoolean();
            return true;
        }

        isOdyssey = true;
        return false;
    }

    private static ProfileCandidate? CreateJournalCandidate(
        JournalEventEnvelope journalEvent,
        string path,
        bool isOdyssey
    )
    {
        if (journalEvent.EventName is not ("Commander" or "LoadGame"))
        {
            return null;
        }

        string? frontierId = GetString(journalEvent.Payload, "FID");
        string? commanderName = GetString(
            journalEvent.Payload,
            journalEvent.EventName == "Commander" ? "Name" : "Commander"
        );
        if (!IsFrontierId(frontierId) || string.IsNullOrWhiteSpace(commanderName))
        {
            return null;
        }

        return new ProfileCandidate(
            frontierId!.ToUpperInvariant(),
            commanderName.Trim(),
            isOdyssey,
            File.GetLastWriteTimeUtc(path),
            Path.GetDirectoryName(path)
        );
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? GetBoolean(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
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
