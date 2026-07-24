namespace SrvSurvey.Desktop;

internal static class StartupOptions
{
    private const string JournalDirectoryOption = "--journal-directory";

    public static string? GetJournalDirectory(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (argument.Equals(JournalDirectoryOption, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Count ? args[index + 1] : null;
            }

            var prefix = $"{JournalDirectoryOption}=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return argument[prefix.Length..];
            }
        }

        return null;
    }
}
