namespace SrvSurvey.Desktop;

internal static class StartupOptions
{
    private const string JournalDirectoryOption = "--journal-directory";
    private const string DiagnosticReplayOption = "--diagnostic-replay";
    private const string FrontierIdOption = "--frontier-id";
    private const string LegacyFrontierIdOption = "-fid";
    internal const string MultiCommanderInstanceOption = "--multi-commander-instance";

    public static bool AllowsConcurrentInstance(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return HasDiagnosticReplayOption(args)
            || args.Any(argument => argument.Equals(MultiCommanderInstanceOption, StringComparison.OrdinalIgnoreCase));
    }

    public static string? GetJournalDirectory(IReadOnlyList<string> args)
    {
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument.Equals(JournalDirectoryOption, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Count ? args[index + 1] : null;
            }

            string prefix = $"{JournalDirectoryOption}=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return argument[prefix.Length..];
            }
        }

        return null;
    }

    public static string? GetFrontierId(IReadOnlyList<string> args)
    {
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (
                argument.Equals(FrontierIdOption, StringComparison.OrdinalIgnoreCase)
                || argument.Equals(LegacyFrontierIdOption, StringComparison.OrdinalIgnoreCase)
            )
            {
                return NormalizeFrontierId(index + 1 < args.Count ? args[index + 1] : null);
            }

            string prefix = $"{FrontierIdOption}=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeFrontierId(argument[prefix.Length..]);
            }
        }

        return null;
    }

    public static string? GetDiagnosticReplayManifest(IReadOnlyList<string> args)
    {
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument.Equals(DiagnosticReplayOption, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1 < args.Count ? NormalizePath(args[index + 1]) : null;
            }

            string prefix = $"{DiagnosticReplayOption}=";
            if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizePath(argument[prefix.Length..]);
            }
        }

        return null;
    }

    public static bool HasDiagnosticReplayOption(IReadOnlyList<string> args)
    {
        return args.Any(argument =>
            argument.Equals(DiagnosticReplayOption, StringComparison.OrdinalIgnoreCase)
            || argument.StartsWith($"{DiagnosticReplayOption}=", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static string? NormalizeFrontierId(string? value)
    {
        string? normalized = value?.Trim();
        return
            normalized is not null
            && normalized.Length > 1
            && (normalized[0] is 'F' or 'f')
            && normalized[1..].All(char.IsAsciiDigit)
            ? normalized.ToUpperInvariant()
            : null;
    }

    private static string? NormalizePath(string? value)
    {
        string? normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
