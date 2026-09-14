namespace SrvSurvey.Desktop.ViewModels;

public sealed record ReleaseNoteChangeViewModel(string Text);

public sealed record ReleaseNotesDialogViewModel(
    string Title,
    string Introduction,
    string ChangesHeading,
    IReadOnlyList<ReleaseNoteChangeViewModel> Changes
)
{
    public bool HasIntroduction => !string.IsNullOrWhiteSpace(Introduction);

    public static ReleaseNotesDialogViewModel Create(string fallbackTitle, string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);

        string[] lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        string title = fallbackTitle;
        int titleIndex = Array.FindIndex(lines, line => line.TrimStart().StartsWith("# ", StringComparison.Ordinal));
        if (titleIndex >= 0)
        {
            title = RemoveInlineMarkdown(lines[titleIndex].Trim()[2..]);
        }

        int changesIndex = Array.FindIndex(lines, IsChangesHeading);
        if (changesIndex < 0)
        {
            return new ReleaseNotesDialogViewModel(
                title,
                string.Empty,
                "What's changed",
                [new ReleaseNoteChangeViewModel(RemoveInlineMarkdown(markdown.Trim()))]
            );
        }

        int introductionStart = titleIndex switch
        {
            < 0 => 0,
            _ when titleIndex < changesIndex => titleIndex + 1,
            _ => changesIndex,
        };
        string introduction = JoinParagraphs(lines[introductionStart..changesIndex]);
        string heading = RemoveInlineMarkdown(lines[changesIndex].Trim()[3..]);
        List<ReleaseNoteChangeViewModel> changes = ParseChanges(lines[(changesIndex + 1)..]);
        return new ReleaseNotesDialogViewModel(title, introduction, heading, changes);
    }

    private static List<ReleaseNoteChangeViewModel> ParseChanges(IReadOnlyList<string> lines)
    {
        var changes = new List<ReleaseNoteChangeViewModel>();
        var current = new List<string>();
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (
                trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal)
            )
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
        string trimmed = line.TrimStart();
        if (!trimmed.StartsWith("## ", StringComparison.Ordinal))
        {
            return false;
        }

        string heading = trimmed[3..].Trim().Replace('\u2019', '\'');
        return heading.StartsWith("What's changed", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddChange(List<ReleaseNoteChangeViewModel> changes, List<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        changes.Add(new ReleaseNoteChangeViewModel(RemoveInlineMarkdown(string.Join(' ', lines))));
        lines.Clear();
    }

    private static string JoinParagraphs(IReadOnlyList<string> lines)
    {
        var paragraphs = new List<string>();
        var current = new List<string>();
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
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
