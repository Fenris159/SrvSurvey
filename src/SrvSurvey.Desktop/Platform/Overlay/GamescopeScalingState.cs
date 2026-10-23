using System.Globalization;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal readonly record struct GamescopeScalingRequest(uint Timestamp, uint Mode, string Display = "");

/// <summary>Joins verified startup configuration with observed compositor commands, without treating commands as published state.</summary>
internal sealed class GamescopeScalingState(uint? startupMode)
{
    private readonly uint? startup = startupMode;
    private uint? latestTimestamp;
    private bool needsNewerTimestamp;
    private readonly Dictionary<string, uint> latestModes = new(StringComparer.Ordinal);
    internal const string ScalerAtom = "GAMESCOPE_NEW_SCALING_SCALER";
    internal const string LegacyAtom = "GAMESCOPE_SCALING_FILTER";

    public uint? Mode { get; private set; } = startupMode;

    public void Initialize(IEnumerable<uint> existingRequests)
    {
        latestTimestamp = null;
        needsNewerTimestamp = false;
        latestModes.Clear();
        uint[] modes = existingRequests.Distinct().ToArray();
        Mode = modes.Length switch
        {
            0 => startup,
            1 => modes[0],
            _ => null, // X11 does not retain the ordering of commands issued before subscription.
        };
    }

    public void ReconcileSubscription(
        IEnumerable<uint> existingRequests,
        IEnumerable<GamescopeScalingRequest>? observedRequests = null
    )
    {
        Observe(observedRequests ?? []);
        if (existingRequests.Any(mode => mode != Mode))
        {
            Mode = null;
        }
        needsNewerTimestamp = Mode is null && latestTimestamp is not null;
    }

    public void Observe(IEnumerable<GamescopeScalingRequest> requests)
    {
        GamescopeScalingRequest[] ordered = requests
            .OrderBy(request => request.Timestamp, Comparer<uint>.Create(CompareTimestamps))
            .ToArray();
        if (ordered.Length == 0)
        {
            return;
        }
        uint last = ordered[^1].Timestamp;
        if (
            latestTimestamp is uint previous
            && (CompareTimestamps(last, previous) < 0 || (needsNewerTimestamp && last == previous))
        )
        {
            return;
        }
        needsNewerTimestamp = false;
        if (latestTimestamp != last)
        {
            latestTimestamp = last;
            latestModes.Clear();
        }
        foreach (GamescopeScalingRequest request in ordered.Where(request => request.Timestamp == last))
        {
            // Retain cross-server ties across polls; only one server has a reliable event order.
            latestModes[request.Display] = request.Mode;
        }
        uint[] modes = latestModes.Values.Distinct().ToArray();
        Mode = modes.Length == 1 ? modes[0] : null;
    }

    private static int CompareTimestamps(uint left, uint right) => unchecked((int)(left - right));

    internal static uint? DecodeRequest(string atom, uint value) =>
        atom switch
        {
            ScalerAtom => value <= 4 ? value : null, // Unknown new values are ignored by Gamescope.
            LegacyAtom => value == 2 ? 1u : 0u,
            _ => null,
        };

    internal static uint? ReadStartupMode(string procDirectory, uint? processId)
    {
        if (processId is null or 0)
        {
            return null;
        }
        try
        {
            string directory = Path.Combine(procDirectory, processId.Value.ToString(CultureInfo.InvariantCulture));
            string? owner = ReadOwner(directory);
            if (
                owner is null
                || owner != ReadOwner(Path.Combine(procDirectory, "self"))
                || File.ReadAllText(Path.Combine(directory, "comm")).Trim() is not ("gamescope" or "gamescope-wl")
            )
            {
                return null;
            }
            string[] arguments = File.ReadAllText(Path.Combine(directory, "cmdline"))
                .Split('\0', StringSplitOptions.RemoveEmptyEntries);
            // Custom builds may use another executable name; compositor identity
            // and the process's same-user comm were verified before reading argv.
            return arguments.Length > 0 ? ParseArguments(arguments) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static uint? ParseArguments(IReadOnlyList<string> arguments)
    {
        uint mode = 0;
        int index = 1;
        while (index < arguments.Count)
        {
            string argument = arguments[index++];
            if (argument == "--")
            {
                break;
            }
            string? scaler = ReadScalerArgument(argument, arguments, ref index);
            if (scaler is not null)
            {
                uint? parsed = scaler switch
                {
                    "auto" => 0u,
                    "integer" => 1u,
                    "fit" => 2u,
                    "fill" => 3u,
                    "stretch" => 4u,
                    _ => null,
                };
                if (parsed is null)
                {
                    return null;
                }
                mode = parsed.Value;
            }
        }
        return mode;
    }

    private static string? ReadScalerArgument(string argument, IReadOnlyList<string> arguments, ref int index)
    {
        if (argument == "--scaler")
        {
            return index < arguments.Count ? arguments[index++] : string.Empty;
        }
        if (argument.StartsWith("--scaler=", StringComparison.Ordinal))
        {
            return argument[9..];
        }
        if (argument.Length < 2 || argument[0] != '-' || argument[1] == '-')
        {
            return null;
        }
        return ReadShortScalerArgument(argument, arguments, ref index);
    }

    private static string? ReadShortScalerArgument(string argument, IReadOnlyList<string> arguments, ref int index)
    {
        // GNU short-option clusters allow -efSinteger. Other options taking
        // a value consume the rest of the cluster or the next argument.
        for (int offset = 1; offset < argument.Length; offset++)
        {
            char option = argument[offset];
            if ("whrmSFWHosORTC".Contains(option))
            {
                string value = argument[(offset + 1)..];
                if (value.Length == 0 && index < arguments.Count)
                {
                    value = arguments[index++];
                }
                return option == 'S' ? value : null;
            }
        }
        return null;
    }

    private static string? ReadOwner(string directory) =>
        File.ReadLines(Path.Combine(directory, "status"))
            .FirstOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal))
            ?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ElementAtOrDefault(1);
}
