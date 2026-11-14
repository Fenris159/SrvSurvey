using System.Collections.Frozen;
using System.Globalization;

namespace SrvSurvey.Core.Updates;

/// <summary>Versioned graphics-source identity; decoding remains limited to layouts supported by the installed bridge.</summary>
public sealed record CockpitCaptureProfile(
    int ProfileVersion,
    int ProtocolVersion,
    bool Validated,
    string VertexShaderHash,
    int RigidLayoutVersion,
    int DrawKind
)
{
    public const int MaximumBytes = 8 * 1024;

    private static readonly string[] Keys =
    [
        "ProfileVersion",
        "ProtocolVersion",
        "Validated",
        "VertexShaderHash",
        "RigidLayoutVersion",
        "DrawKind",
    ];

    private static readonly Lazy<CockpitCaptureProfile> EmbeddedProfile = new(LoadEmbedded);

    public static CockpitCaptureProfile Embedded => EmbeddedProfile.Value;

    // The bridge owns decoder compatibility. Keeping declarative fields opaque here
    // lets a newer DLL accept a new layout without rebuilding the desktop app.
    public bool IsCompatible => Validated && ProtocolVersion == 1;

    public IReadOnlyDictionary<string, string> LayoutFields { get; private init; } =
        FrozenDictionary<string, string>.Empty;

    /// <summary>Accepts a small declarative capture profile, rejecting duplicate settings and unrelated INI sections.</summary>
    public static CockpitCaptureProfile? Parse(string text)
    {
        if (text.Length > MaximumBytes)
        {
            return null;
        }
        var layout = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? values = ReadFields(text, layout);
        if (values is null || values.Count != Keys.Length || Keys.Any(key => !values.ContainsKey(key)))
        {
            return null;
        }
        string hash = values["VertexShaderHash"];
        if (hash.Length != 16 || !hash.All(char.IsAsciiHexDigit) || hash.All(character => character == '0'))
        {
            return null;
        }
        int[] numbers = new int[5];
        string[] numericKeys = ["ProfileVersion", "ProtocolVersion", "Validated", "RigidLayoutVersion", "DrawKind"];
        for (int index = 0; index < numericKeys.Length; index++)
        {
            if (
                !int.TryParse(
                    values[numericKeys[index]],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out numbers[index]
                )
            )
            {
                return null;
            }
        }
        if (
            numbers[0] < 1
            || numbers[1] < 1
            || numbers[2] is < 0 or > 1
            || numbers[3] < 1
            || numbers[4] < 1
            || (numbers[3] == 1 ? layout.Count != 0 : layout.Count == 0)
        )
        {
            return null;
        }
        return new CockpitCaptureProfile(
            numbers[0],
            numbers[1],
            numbers[2] == 1,
            hash.ToLowerInvariant(),
            numbers[3],
            numbers[4]
        )
        {
            LayoutFields = layout.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>Writes a canonical data-only profile; downloaded text cannot inject file paths or commands.</summary>
    public string Serialize() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"[Capture]\nProfileVersion={ProfileVersion}\nProtocolVersion={ProtocolVersion}\nValidated={(Validated ? 1 : 0)}\nVertexShaderHash={VertexShaderHash}\nRigidLayoutVersion={RigidLayoutVersion}\nDrawKind={DrawKind}\n"
        )
        + (
            LayoutFields.Count == 0
                ? string.Empty
                : "\n[Layout]\n"
                    + string.Concat(
                        LayoutFields
                            .OrderBy(field => field.Key, StringComparer.Ordinal)
                            .Select(field => $"{field.Key}={field.Value}\n")
                    )
        );

    private static Dictionary<string, string>? ReadFields(string text, Dictionary<string, string> layout)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? target = null;
        foreach (string raw in text.TrimStart('\uFEFF').Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }
            if (line.Equals("[Capture]", StringComparison.OrdinalIgnoreCase) && target is null)
            {
                target = values;
                continue;
            }
            if (line.Equals("[Layout]", StringComparison.OrdinalIgnoreCase) && target == values)
            {
                target = layout;
                continue;
            }
            if (target is null || !AddField(target, line, target == layout))
            {
                return null;
            }
        }
        return values;
    }

    private static bool AddField(Dictionary<string, string> target, string line, bool layout)
    {
        int separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return false;
        }
        string key = line[..separator].Trim();
        string value = line[(separator + 1)..].Trim();
        if (layout)
        {
            if (!IsLayoutToken(key, true) || !IsLayoutToken(value, false))
            {
                return false;
            }
            key = key.ToLowerInvariant();
            value = value.ToLowerInvariant();
        }
        return target.TryAdd(key, value);
    }

    private static bool IsLayoutToken(string text, bool key) =>
        text.Length is > 0 and <= 64
        && (!key || char.IsAsciiLetter(text[0]))
        && text.All(character =>
            char.IsAsciiLetterOrDigit(character) || character == '_' || (!key && character is '.' or '+' or '-')
        );

    private static CockpitCaptureProfile LoadEmbedded()
    {
        using Stream stream =
            typeof(CockpitCaptureProfile).Assembly.GetManifestResourceStream(
                "SrvSurvey.Core.Resources.cockpit-camera-profile.ini"
            ) ?? throw new InvalidDataException("The bundled cockpit capture profile is missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd()) is { IsCompatible: true } profile
            ? profile
            : throw new InvalidDataException("The bundled cockpit capture profile is incompatible.");
    }
}
