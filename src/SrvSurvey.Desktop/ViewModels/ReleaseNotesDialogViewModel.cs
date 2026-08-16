namespace SrvSurvey.Desktop.ViewModels;

public sealed record ReleaseNoteChangeViewModel(string Text);

public sealed record ReleaseNotesDialogViewModel(
    string Title,
    string Introduction,
    string ChangesHeading,
    IReadOnlyList<ReleaseNoteChangeViewModel> Changes)
{
    public bool HasIntroduction => !string.IsNullOrWhiteSpace(Introduction);

    public static ReleaseNotesDialogViewModel Create(
        string fallbackTitle,
        string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var title = fallbackTitle;
        var titleIndex = Array.FindIndex(lines, line =>
            line.TrimStart().StartsWith("# ", StringComparison.Ordinal));
        if (titleIndex >= 0)
        {
            title = RemoveInlineMarkdown(lines[titleIndex].Trim()[2..]);
        }

        var changesIndex = Array.FindIndex(lines, IsChangesHeading);
        if (changesIndex < 0)
        {
            return new ReleaseNotesDialogViewModel(
                title,
                string.Empty,
                "What's changed",
                [new ReleaseNoteChangeViewModel(RemoveInlineMarkdown(markdown.Trim()))]);
        }

        var introductionStart = titleIndex >= 0 ? titleIndex + 1 : 0;
        var introduction = JoinParagraphs(
            lines[introductionStart..changesIndex]);
        var heading = RemoveInlineMarkdown(lines[changesIndex].Trim()[3..]);
        var changes = ParseChanges(lines[(changesIndex + 1)..]);
        return new ReleaseNotesDialogViewModel(
            title,
            introduction,
            heading,
            changes);
    }

    private static IReadOnlyList<ReleaseNoteChangeViewModel> ParseChanges(
        IReadOnlyList<string> lines)
    {
        var changes = new List<ReleaseNoteChangeViewModel>();
        var current = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal)
                || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                AddChange(changes, current);
                current.Add(trimmed[2..]);
            }
            else if (trimmed.Length > 0)
            {
                current.Add(trimmed);
            }
        }

        AddChange(changes, current);
        return changes;
    }

    private static bool IsChangesHeading(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            return false;
        }

        var heading = trimmed[3..].Trim().Replace('\u2019', '\'');
        return heading.StartsWith("What's changed", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddChange(
        ICollection<ReleaseNoteChangeViewModel> changes,
        List<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        changes.Add(new ReleaseNoteChangeViewModel(
            RemoveInlineMarkdown(string.Join(' ', lines))));
        lines.Clear();
    }

    private static string JoinParagraphs(IReadOnlyList<string> lines)
    {
        var paragraphs = new List<string>();
        var current = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                if (current.Count > 0)
                {
                    paragraphs.Add(RemoveInlineMarkdown(string.Join(' ', current)));
                    current.Clear();
                }

                continue;
            }

            current.Add(trimmed);
        }

        if (current.Count > 0)
        {
            paragraphs.Add(RemoveInlineMarkdown(string.Join(' ', current)));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
    }

    private static string RemoveInlineMarkdown(string text)
    {
        return text.Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Trim();
    }
}
