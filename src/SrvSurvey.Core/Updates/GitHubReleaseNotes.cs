namespace SrvSurvey.Core.Updates;

public static class GitHubReleaseNotes
{
    private const int MaximumExcerptCharacters = 128 * 1024;

    public static string ExtractChanges(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var changesHeading = FindChangesHeading(lines);
        if (changesHeading < 0)
        {
            return string.Empty;
        }

        var end = lines.Length;
        for (var index = changesHeading + 1; index < lines.Length; index++)
        {
            if (IsSecondLevelHeading(lines[index]))
            {
                end = index;
                break;
            }
        }

        var excerpt = string.Join('\n', lines[..end]).Trim();
        return excerpt.Length <= MaximumExcerptCharacters
            ? excerpt
            : excerpt[..MaximumExcerptCharacters].TrimEnd();
    }

    private static int FindChangesHeading(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (!IsSecondLevelHeading(line))
            {
                continue;
            }

            var heading = line[3..].Trim();
            if (heading.StartsWith("What's changed", StringComparison.OrdinalIgnoreCase)
                || heading.StartsWith("What’s changed", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSecondLevelHeading(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("## ", StringComparison.Ordinal)
            && !trimmed.StartsWith("### ", StringComparison.Ordinal);
    }
}
