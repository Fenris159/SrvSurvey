using System.Text;
using System.Text.Json;

namespace SrvSurvey.Core.Journal;

public static class JournalSnapshotReader
{
    public static async Task<JournalSnapshot> ReadLatestAsync(
        string journalFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalFolder);

        var directory = new DirectoryInfo(journalFolder);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException(
                $"The journal folder does not exist: {journalFolder}");
        }

        var latestJournal = directory
            .EnumerateFiles("Journal.*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (latestJournal is null)
        {
            throw new FileNotFoundException(
                $"No Journal.*.log files were found in: {journalFolder}");
        }

        await using var stream = new FileStream(
            latestJournal.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        return await ReadAsync(reader, latestJournal.FullName, cancellationToken);
    }

    public static async Task<JournalSnapshot> ReadAsync(
        TextReader reader,
        string? sourcePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        string? gameVersion = null;
        string? gameBuild = null;
        bool? isOdyssey = null;
        string? commanderName = null;
        string? frontierId = null;
        string? gameMode = null;
        string? systemName = null;
        long? systemAddress = null;
        string? bodyName = null;
        var isShutdown = false;
        DateTimeOffset? lastEventTimestamp = null;
        var validLineCount = 0;
        var recognizedEventCount = 0;
        var malformedLineCount = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                malformedLineCount++;
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    malformedLineCount++;
                    continue;
                }

                validLineCount++;
                if (TryGetDateTimeOffset(root, "timestamp", out var timestamp))
                {
                    lastEventTimestamp = timestamp;
                }

                var eventName = GetString(root, "event");
                switch (eventName)
                {
                    case "Fileheader":
                        gameVersion = GetString(root, "gameversion") ?? gameVersion;
                        gameBuild = GetString(root, "build") ?? gameBuild;
                        isOdyssey = GetBoolean(root, "Odyssey") ?? isOdyssey;
                        isShutdown = false;
                        recognizedEventCount++;
                        break;

                    case "Commander":
                        commanderName = GetString(root, "Name") ?? commanderName;
                        frontierId = GetString(root, "FID") ?? frontierId;
                        recognizedEventCount++;
                        break;

                    case "LoadGame":
                        commanderName = GetString(root, "Commander") ?? commanderName;
                        frontierId = GetString(root, "FID") ?? frontierId;
                        gameMode = GetString(root, "GameMode") ?? gameMode;
                        gameVersion = GetString(root, "gameversion") ?? gameVersion;
                        gameBuild = GetString(root, "build") ?? gameBuild;
                        isOdyssey = GetBoolean(root, "Odyssey") ?? isOdyssey;
                        isShutdown = false;
                        recognizedEventCount++;
                        break;

                    case "Location":
                    case "SupercruiseExit":
                        systemName = GetString(root, "StarSystem") ?? systemName;
                        systemAddress = GetInt64(root, "SystemAddress") ?? systemAddress;
                        bodyName = GetCurrentPlanetName(root);
                        isShutdown = false;
                        recognizedEventCount++;
                        break;

                    case "FSDJump":
                    case "CarrierJump":
                        systemName = GetString(root, "StarSystem") ?? systemName;
                        systemAddress = GetInt64(root, "SystemAddress") ?? systemAddress;
                        bodyName = GetCurrentPlanetName(root);
                        isShutdown = false;
                        recognizedEventCount++;
                        break;

                    case "ApproachBody":
                        bodyName = GetString(root, "Body") ?? bodyName;
                        recognizedEventCount++;
                        break;

                    case "LeaveBody":
                        // The legacy application clears touchdown/SRV coordinates
                        // but retains the current planet until another location event.
                        recognizedEventCount++;
                        break;

                    case "Shutdown":
                        isShutdown = true;
                        recognizedEventCount++;
                        break;
                }
            }
        }

        return new JournalSnapshot(
            sourcePath,
            gameVersion,
            gameBuild,
            isOdyssey,
            commanderName,
            frontierId,
            gameMode,
            systemName,
            systemAddress,
            bodyName,
            isShutdown,
            lastEventTimestamp,
            validLineCount,
            recognizedEventCount,
            malformedLineCount);
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
        return root.TryGetProperty(propertyName, out var value)
            && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean()
                : null;
    }

    private static string? GetCurrentPlanetName(JsonElement root)
    {
        return GetString(root, "BodyType") == "Planet"
            ? GetString(root, "Body")
            : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), out number)
                ? number
                : null;
    }

    private static bool TryGetDateTimeOffset(
        JsonElement root,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && property.TryGetDateTimeOffset(out value);
    }
}
