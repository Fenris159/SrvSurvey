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

        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        int changesHeading = FindChangesHeading(lines);
        if (changesHeading < 0)
        {
            return string.Empty;
        }

        int introductionEnd = Array.FindIndex(lines, IsSecondLevelHeading);
        if (introductionEnd < 0)
        {
            introductionEnd = changesHeading;
        }

        int end = lines.Length;
        for (int index = changesHeading + 1; index < lines.Length; index++)
        {
            if (IsSecondLevelHeading(lines[index]))
            {
                end = index;
                break;
            }
        }

        string introduction = string.Join('\n', lines[..introductionEnd]).Trim();
        string changes = string.Join('\n', lines[changesHeading..end]).Trim();
        string excerpt = string.IsNullOrEmpty(introduction) ? changes : introduction + "\n\n" + changes;
        return excerpt.Length <= MaximumExcerptCharacters ? excerpt : excerpt[..MaximumExcerptCharacters].TrimEnd();
    }

    private static int FindChangesHeading(string[] lines)
    {
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (!IsSecondLevelHeading(line))
            {
                continue;
            }

            string heading = line[3..].Trim();
            if (
                heading.StartsWith("What's changed", StringComparison.OrdinalIgnoreCase)
                || heading.StartsWith("What’s changed", StringComparison.OrdinalIgnoreCase)
            )
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSecondLevelHeading(string line)
    {
        return line.TrimStart().StartsWith("## ", StringComparison.Ordinal);
    }
}
