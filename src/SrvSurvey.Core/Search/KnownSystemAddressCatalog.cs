using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SrvSurvey.Core.Search;

public sealed partial class KnownSystemAddressCatalog
{
    public const string LegacyFileName = "Boxel.Names.txt";

    private const long MaximumFileBytes = 16L * 1024 * 1024;
    private const int MaximumEntries = 250_000;
    private const int MaximumLineCharacters = 16_384;
    private readonly IReadOnlyDictionary<string, long> addresses;

    private KnownSystemAddressCatalog(
        IReadOnlyDictionary<string, long> addresses,
        string? sourcePath,
        IReadOnlyList<string> warnings)
    {
        this.addresses = addresses;
        SourcePath = sourcePath;
        Warnings = warnings;
    }

    public static KnownSystemAddressCatalog Empty { get; } = new(
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
        null,
        []);

    public string? SourcePath { get; }

    public IReadOnlyList<string> Warnings { get; }

    public int Count => addresses.Count;

    public bool HasData => Count > 0;

    public bool TryResolve(string? systemName, out long systemAddress)
    {
        systemAddress = 0;
        return !string.IsNullOrWhiteSpace(systemName)
            && addresses.TryGetValue(systemName.Trim(), out systemAddress);
    }

    public static KnownSystemAddressCatalog Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        var path = Path.Combine(
            Path.GetFullPath(dataDirectory),
            "pub",
            LegacyFileName);
        if (!File.Exists(path))
        {
            return Empty;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaximumFileBytes)
            {
                throw new InvalidDataException(
                    $"The known-system address catalog size is invalid: {info.Length:N0} bytes.");
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return Load(stream, path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or DecoderFallbackException
            or FormatException
            or OverflowException)
        {
            return new KnownSystemAddressCatalog(
                new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
                path,
                [$"Imported pub/{LegacyFileName} was preserved but ignored safely: {exception.Message}"]);
        }
    }

    internal static KnownSystemAddressCatalog Load(
        Stream stream,
        string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: true);
        var result = new Dictionary<string, long>(
            StringComparer.OrdinalIgnoreCase);
        var foundStart = false;
        var foundMissingStart = false;
        var foundEnd = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length > MaximumLineCharacters)
            {
                throw new InvalidDataException(
                    "The known-system address catalog contains an oversized line.");
            }

            var trimmed = line.Trim();
            if (!foundStart)
            {
                foundStart = string.Equals(
                    trimmed,
                    "known_systems = {",
                    StringComparison.Ordinal);
                continue;
            }

            if (string.Equals(
                    trimmed,
                    "known_missing = [",
                    StringComparison.Ordinal))
            {
                foundMissingStart = true;
                continue;
            }

            if (foundMissingStart)
            {
                if (string.Equals(trimmed, "]", StringComparison.Ordinal))
                {
                    foundEnd = true;
                    break;
                }

                continue;
            }

            var match = EntryPattern().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            if (name.Length == 0
                || !long.TryParse(
                    match.Groups["address"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var address)
                || address <= 0)
            {
                throw new InvalidDataException(
                    "The known-system address catalog contains an invalid entry.");
            }

            result.TryAdd(name, address);
            if (result.Count > MaximumEntries)
            {
                throw new InvalidDataException(
                    "The known-system address catalog contains too many entries.");
            }
        }

        // Reaching the closing bracket proves both section markers were seen;
        // entries cannot be collected before the first marker.
        if (!foundEnd || result.Count == 0)
        {
            throw new InvalidDataException(
                "The known-system address catalog is incomplete.");
        }

        return new KnownSystemAddressCatalog(result, sourcePath, []);
    }

    [GeneratedRegex(
        "^\\s*\"(?<name>[^\"]+)\"\\s*:\\s*(?:\\[\\s*)?(?<address>\\d+)\\s*,",
        RegexOptions.CultureInvariant)]
    private static partial Regex EntryPattern();
}
