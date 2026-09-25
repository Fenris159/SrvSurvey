using System.Text.Json;

namespace SrvSurvey.Core.Journal;

/// <summary>
/// Reads the latest pledged power from journal files. Status.json only carries it while the game is running.
/// </summary>
public static class JournalPowerplayPledge
{
    public static string ReadLatest(
        IEnumerable<string> journalDirectories,
        string frontierId,
        string? commanderName = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        string power = "";
        foreach (
            FileInfo journal in journalDirectories
                .Where(Directory.Exists)
                .SelectMany(static directory => Directory.EnumerateFiles(directory, "Journal*.log"))
                .Select(static path => new FileInfo(path))
                .OrderByDescending(static file => file.LastWriteTimeUtc)
                .Take(6)
                .OrderBy(static file => file.LastWriteTimeUtc)
        )
        {
            power = ReadJournal(journal, power, frontierId, commanderName);
        }

        return power;
    }

    private static string ReadJournal(FileInfo journal, string power, string frontierId, string? commanderName)
    {
        string currentFrontierId = "";
        string currentCommanderName = "";
        try
        {
            using var stream = new FileStream(
                journal.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                if (
                    line.Length == 0
                    || !line.Contains("Powerplay", StringComparison.Ordinal)
                        && !line.Contains("\"Commander\"", StringComparison.Ordinal)
                        && !line.Contains("\"LoadGame\"", StringComparison.Ordinal)
                )
                {
                    continue;
                }

                ApplyLine(line, frontierId, commanderName, ref currentFrontierId, ref currentCommanderName, ref power);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A journal can be replaced or locked while the game is writing it.
        }

        return power;
    }

    private static void ApplyLine(
        string line,
        string frontierId,
        string? commanderName,
        ref string currentFrontierId,
        ref string currentCommanderName,
        ref string power
    )
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            JsonElement entry = document.RootElement;
            if (entry.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            switch (Text(entry, "event"))
            {
                case "Commander":
                    currentFrontierId = Text(entry, "FID");
                    currentCommanderName = Text(entry, "Name");
                    break;
                case "LoadGame":
                    currentFrontierId = Text(entry, "FID");
                    currentCommanderName = Text(entry, "Commander");
                    break;
                default:
                    if (
                        currentFrontierId.Equals(frontierId, StringComparison.OrdinalIgnoreCase)
                        || currentFrontierId.Length == 0
                            && commanderName is not null
                            && currentCommanderName.Equals(commanderName, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        power = ApplyEvent(entry, power);
                    }

                    break;
            }
        }
        catch (JsonException)
        {
            // A damaged journal line does not erase a pledge already read.
        }
    }

    private static string ApplyEvent(JsonElement element, string previousPower) =>
        Text(element, "event") switch
        {
            "PowerplayLeave" or "PowerplayDefect" => "",
            "Powerplay" or "PowerplayJoin" or "PowerplayMerits" or "PowerplayRank" => PledgedPower(
                element,
                previousPower
            ),
            _ => previousPower,
        };

    private static string PledgedPower(JsonElement element, string previousPower)
    {
        string pledged = Text(element, "Power");
        return pledged.Length > 0 ? pledged : previousPower;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
