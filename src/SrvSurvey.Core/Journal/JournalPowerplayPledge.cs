using System.Text.Json;

namespace SrvSurvey.Core.Journal;

/// <summary>
/// Reads the latest pledged power from journal files. Status.json only carries it while the game is running.
/// </summary>
public static class JournalPowerplayPledge
{
    public static string ReadLatest(IEnumerable<string> journalDirectories)
    {
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
            foreach (string line in File.ReadLines(journal.FullName))
            {
                if (line.Length == 0 || !line.Contains("Powerplay", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    string eventName = Text(document.RootElement, "event");
                    if (eventName is "PowerplayLeave" or "PowerplayDefect")
                    {
                        power = "";
                    }
                    else if (eventName is "Powerplay" or "PowerplayJoin" or "PowerplayMerits" or "PowerplayRank")
                    {
                        string pledged = Text(document.RootElement, "Power");
                        if (pledged.Length > 0)
                        {
                            power = pledged;
                        }
                    }
                }
                catch (JsonException)
                {
                    // A damaged journal line does not erase a pledge already read.
                }
            }
        }

        return power;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
