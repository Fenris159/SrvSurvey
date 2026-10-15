namespace SrvSurvey.Desktop;

internal static class StartupOptions
{
    private const string JournalDirectoryOption = "--journal-directory";
    private const string DiagnosticReplayOption = "--diagnostic-replay";
    private const string FrontierIdOption = "--frontier-id";
    private const string LegacyFrontierIdOption = "-fid";
    internal const string MultiCommanderInstanceOption = "--multi-commander-instance";
    private const string InstanceFrontierIdOption = "--instance-frontier-id";

    /// <summary>Builds replacement-process arguments for the current commander assignment.</summary>
    internal static string[] ForRestart(
        IReadOnlyList<string> arguments,
        string? frontierId,
        bool preserveCommander = true
    )
    {
        var retained = new List<string>();
        int index = 0;
        while (index < arguments.Count)
        {
            string argument = arguments[index++];
            if (argument.Equals(InstanceFrontierIdOption, StringComparison.OrdinalIgnoreCase))
            {
                index++;
            }
            else if (!argument.StartsWith(InstanceFrontierIdOption + "=", StringComparison.OrdinalIgnoreCase))
            {
                retained.Add(argument);
            }
        }

        if (
            preserveCommander
            && NormalizeFrontierId(frontierId) is { } commander
            && !HasDiagnosticReplayOption(arguments)
        )
        {
            retained.Add(InstanceFrontierIdOption);
            retained.Add(commander);
            if (!AllowsConcurrentInstance(retained))
            {
                retained.Add(MultiCommanderInstanceOption);
            }
        }
        return retained.ToArray();
    }

    /// <summary>Identifies launches authorized to coexist with another instance, including diagnostic replay.</summary>
    public static bool AllowsConcurrentInstance(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return HasDiagnosticReplayOption(args)
            || args.Any(argument => argument.Equals(MultiCommanderInstanceOption, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads an optional journal directory supplied with a separate or inline command-line value.</summary>
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

    /// <summary>Reads the explicit commander override, accepting the current and legacy option names.</summary>
    public static string? GetFrontierId(IReadOnlyList<string> args)
    {
        return ReadFrontierId(args, FrontierIdOption, LegacyFrontierIdOption);
    }

    /// <summary>Reads a replacement process's retained commander without treating it as a user preference override.</summary>
    internal static string? GetInstanceFrontierId(IReadOnlyList<string> args)
    {
        return ReadFrontierId(args, InstanceFrontierIdOption, null);
    }

    /// <summary>Reads and normalizes a commander ID from either separate-value or inline option syntax.</summary>
    private static string? ReadFrontierId(IReadOnlyList<string> args, string option, string? legacyOption)
    {
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            if (
                argument.Equals(option, StringComparison.OrdinalIgnoreCase)
                || argument.Equals(legacyOption, StringComparison.OrdinalIgnoreCase)
            )
            {
                return NormalizeFrontierId(index + 1 < args.Count ? args[index + 1] : null);
            }

            string prefix = $"{option}=";
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
