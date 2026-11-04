using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class VrOverlayCalibrationStore
{
    private readonly string dataDirectory;
    private readonly string plottersPath;
    private readonly string defaultPlottersPath;

    public VrOverlayCalibrationStore(string dataDirectory, string? defaultPlottersPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        plottersPath = Path.Combine(this.dataDirectory, "plotters.json");
        this.defaultPlottersPath = Path.GetFullPath(
            defaultPlottersPath ?? Path.Combine(AppContext.BaseDirectory, "plotters.json")
        );
    }

    /// <summary>Reads calibration after serialized layout recovery.</summary>
    public VrOverlayCalibrationCatalog Load(string? profileId = null)
    {
        using FileStream lease = OverlayLayoutFileAccess.Acquire(dataDirectory);
        OverlayLayoutFileAccess.Recover(dataDirectory);
        Dictionary<string, VrOverlayCalibration> defaults = LoadFile(
            File.Exists(plottersPath) ? plottersPath : defaultPlottersPath,
            allowDesktopPrefix: true
        );
        Dictionary<string, VrOverlayCalibration> factoryDefaults = LoadFile(
            defaultPlottersPath,
            allowDesktopPrefix: true
        );
        Dictionary<string, IReadOnlyDictionary<string, VrOverlayCalibration>> overrides = LoadModes(
            GetOverrideDirectory(null)
        );
        if (profileId == VrPlatformProfileCatalog.MetaVdxrProfileId)
        {
            ApplySeatedDefaults(defaults, factoryDefaults);
            Dictionary<string, IReadOnlyDictionary<string, VrOverlayCalibration>> routeModes = LoadModes(
                GetOverrideDirectory(profileId)
            );
            if (
                routeModes.Remove(
                    DefaultCalibrationMode,
                    out IReadOnlyDictionary<string, VrOverlayCalibration>? routeDefaults
                )
            )
            {
                foreach (KeyValuePair<string, VrOverlayCalibration> entry in routeDefaults)
                {
                    defaults[entry.Key] = entry.Value;
                }
            }
            foreach (KeyValuePair<string, IReadOnlyDictionary<string, VrOverlayCalibration>> mode in routeModes)
            {
                var merged = new Dictionary<string, VrOverlayCalibration>(
                    overrides.GetValueOrDefault(mode.Key) ?? new Dictionary<string, VrOverlayCalibration>(),
                    StringComparer.Ordinal
                );
                foreach (KeyValuePair<string, VrOverlayCalibration> entry in mode.Value)
                {
                    merged[entry.Key] = entry.Value;
                }
                overrides[mode.Key] = merged;
            }
        }

        return new VrOverlayCalibrationCatalog(defaults, factoryDefaults, overrides);
    }

    private static void ApplySeatedDefaults(
        Dictionary<string, VrOverlayCalibration> defaults,
        Dictionary<string, VrOverlayCalibration> factoryDefaults
    )
    {
        foreach (string name in factoryDefaults.Keys.ToArray())
        {
            VrOverlayCalibration legacyDefault = factoryDefaults[name];
            factoryDefaults[name] = VrSeatedCalibrationDefaults.Get(name);
            if (!defaults.TryGetValue(name, out VrOverlayCalibration? saved) || saved == legacyDefault)
            {
                defaults[name] = factoryDefaults[name];
            }
        }
    }

    private static Dictionary<string, IReadOnlyDictionary<string, VrOverlayCalibration>> LoadModes(string directory)
    {
        var modes = new Dictionary<string, IReadOnlyDictionary<string, VrOverlayCalibration>>(
            StringComparer.OrdinalIgnoreCase
        );
        if (Directory.Exists(directory))
        {
            foreach (string path in Directory.GetFiles(directory, "*.json"))
            {
                modes[Path.GetFileNameWithoutExtension(path)] = LoadFile(path, allowDesktopPrefix: false);
            }
        }
        return modes;
    }

    /// <summary>Updates calibration under the same folder lease as desktop layout writers.</summary>
    public VrOverlayCalibrationSaveResult Save(
        string plotterName,
        VrOverlayCalibration calibration,
        string? mode = null,
        string? profileId = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        ArgumentNullException.ThrowIfNull(calibration);
        calibration.Validate();
        using FileStream lease = OverlayLayoutFileAccess.Acquire(dataDirectory);
        OverlayLayoutFileAccess.Recover(dataDirectory);
        string? normalizedMode = NormalizeMode(mode);
        if (profileId == VrPlatformProfileCatalog.MetaVdxrProfileId)
        {
            return SaveOverride(plotterName, calibration, normalizedMode ?? DefaultCalibrationMode, profileId);
        }
        return normalizedMode is null
            ? SaveDefault(plotterName, calibration)
            : SaveOverride(plotterName, calibration, normalizedMode);
    }

    private VrOverlayCalibrationSaveResult SaveDefault(string plotterName, VrOverlayCalibration calibration)
    {
        string sourcePath = File.Exists(plottersPath) ? plottersPath : defaultPlottersPath;
        JsonObject root = ParseObject(sourcePath);
        if (root[plotterName] is not JsonValue value || !value.TryGetValue<string>(out string? existing))
        {
            throw new InvalidDataException($"Overlay calibration '{plotterName}' is not present in '{sourcePath}'.");
        }

        int brace = existing.IndexOf('{');
        string desktop = (brace >= 0 ? existing[..brace] : existing).TrimEnd();
        root[plotterName] = $"{desktop} {calibration}";

        Directory.CreateDirectory(dataDirectory);
        string? backupPath = File.Exists(plottersPath)
            ? CreateVerifiedBackup(plottersPath, "vr-calibration-backups")
            : null;
        WriteAtomic(
            root,
            plottersPath,
            temporaryPath =>
            {
                Dictionary<string, VrOverlayCalibration> pending = LoadFile(temporaryPath, allowDesktopPrefix: true);
                if (!pending.TryGetValue(plotterName, out VrOverlayCalibration? saved) || saved != calibration)
                {
                    throw new InvalidDataException(
                        $"Overlay calibration '{plotterName}' could not be verified before saving."
                    );
                }
            }
        );
        Dictionary<string, VrOverlayCalibration> verified = LoadFile(plottersPath, allowDesktopPrefix: true);
        if (!verified.TryGetValue(plotterName, out VrOverlayCalibration? saved) || saved != calibration)
        {
            throw new InvalidDataException($"Overlay calibration '{plotterName}' could not be verified after saving.");
        }

        return new VrOverlayCalibrationSaveResult(plottersPath, backupPath);
    }

    private VrOverlayCalibrationSaveResult SaveOverride(
        string plotterName,
        VrOverlayCalibration calibration,
        string mode,
        string? profileId = null
    )
    {
        string overrideDirectory = GetOverrideDirectory(profileId);
        string overridePath = Path.Combine(overrideDirectory, $"{mode}.json");
        JsonObject root = File.Exists(overridePath) ? ParseObject(overridePath) : [];
        root[plotterName] = calibration.ToString();

        Directory.CreateDirectory(overrideDirectory);
        string? backupPath = File.Exists(overridePath)
            ? CreateVerifiedBackup(overridePath, "vr-calibration-backups")
            : null;
        WriteAtomic(
            root,
            overridePath,
            temporaryPath =>
            {
                Dictionary<string, VrOverlayCalibration> pending = LoadFile(temporaryPath, allowDesktopPrefix: false);
                if (!pending.TryGetValue(plotterName, out VrOverlayCalibration? saved) || saved != calibration)
                {
                    throw new InvalidDataException(
                        $"VR override '{mode}/{plotterName}' could not be verified before saving."
                    );
                }
            }
        );

        return new VrOverlayCalibrationSaveResult(overridePath, backupPath);
    }

    private const string DefaultCalibrationMode = "Default";

    private string GetOverrideDirectory(string? profileId) =>
        profileId == VrPlatformProfileCatalog.MetaVdxrProfileId
            ? Path.Combine(dataDirectory, "vr", "vdxr")
            : Path.Combine(dataDirectory, "vr");

    private static Dictionary<string, VrOverlayCalibration> LoadFile(string path, bool allowDesktopPrefix)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, VrOverlayCalibration>(StringComparer.Ordinal);
        }

        JsonObject root = ParseObject(path);
        var result = new Dictionary<string, VrOverlayCalibration>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonNode?> entry in root)
        {
            if (entry.Value is not JsonValue value || !value.TryGetValue<string>(out string? text))
            {
                throw new InvalidDataException($"VR calibration '{entry.Key}' must be a string.");
            }

            var calibration = VrOverlayCalibration.Parse(text, allowDesktopPrefix);
            if (calibration is not null)
            {
                result[entry.Key] = calibration;
            }
        }

        return result;
    }

    private static JsonObject ParseObject(string path)
    {
        return JsonNode.Parse(
                File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                }
            ) as JsonObject
            ?? throw new InvalidDataException($"'{path}' is not a JSON object.");
    }

    private static string CreateVerifiedBackup(string path, string directoryName)
    {
        string directory = Path.Combine(
            Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("The calibration file has no parent directory."),
            directoryName
        );
        Directory.CreateDirectory(directory);
        string backupPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(path)}-"
                + $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffffZ}-"
                + $"{Guid.NewGuid():N}.json"
        );
        File.Copy(path, backupPath, false);
        if (
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(File.ReadAllBytes(path)),
                SHA256.HashData(File.ReadAllBytes(backupPath))
            )
        )
        {
            File.Delete(backupPath);
            throw new IOException("The VR calibration backup did not match its source.");
        }

        return backupPath;
    }

    private static void WriteAtomic(JsonObject root, string path, Action<string> verify)
    {
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (
                var writer = new Utf8JsonWriter(
                    stream,
                    new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = true }
                )
            )
            {
                root.WriteTo(writer);
            }

            verify(temporaryPath);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string? NormalizeMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        string normalized = mode.Trim();
        if (
            normalized is "." or ".."
            || !string.Equals(Path.GetFileName(normalized), normalized, StringComparison.Ordinal)
            || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
        )
        {
            throw new InvalidDataException($"'{mode}' is not a safe VR mode name.");
        }

        return normalized;
    }
}

public sealed class VrOverlayCalibrationCatalog
{
    public VrOverlayCalibrationCatalog(
        IReadOnlyDictionary<string, VrOverlayCalibration> defaults,
        IReadOnlyDictionary<string, VrOverlayCalibration> factoryDefaults,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, VrOverlayCalibration>> overrides
    )
    {
        Defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        FactoryDefaults = factoryDefaults ?? throw new ArgumentNullException(nameof(factoryDefaults));
        Overrides = overrides ?? throw new ArgumentNullException(nameof(overrides));
    }

    public IReadOnlyDictionary<string, VrOverlayCalibration> Defaults { get; }

    public IReadOnlyDictionary<string, VrOverlayCalibration> FactoryDefaults { get; }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, VrOverlayCalibration>> Overrides { get; }

    public VrOverlayCalibration? Resolve(string plotterName, string? mode)
    {
        if (
            !string.IsNullOrWhiteSpace(mode)
            && Overrides.TryGetValue(mode, out IReadOnlyDictionary<string, VrOverlayCalibration>? modeOverrides)
            && modeOverrides.TryGetValue(plotterName, out VrOverlayCalibration? modeCalibration)
        )
        {
            return modeCalibration;
        }

        return Defaults.GetValueOrDefault(plotterName);
    }
}

public sealed record VrOverlayCalibration(float Scale, Vector3 Position, Vector3 Rotation)
{
    public static VrOverlayCalibration? Parse(string text, bool allowDesktopPrefix = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        int start = text.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        if (!allowDesktopPrefix && !string.IsNullOrWhiteSpace(text[..start]))
        {
            throw new InvalidDataException("A VR override must contain only a calibration block.");
        }

        string[] parts = text[start..]
            .Split(
                ['{', '}', ',', ':', '<', '>'],
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
            );
        if (
            parts.Length != 10
            || !string.Equals(parts[0], "s", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], "p", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[6], "r", StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException("The VR calibration format is invalid.");
        }

        var calibration = new VrOverlayCalibration(
            ParseSingle(parts[1]),
            new Vector3(ParseSingle(parts[3]), ParseSingle(parts[4]), ParseSingle(parts[5])),
            new Vector3(ParseSingle(parts[7]), ParseSingle(parts[8]), ParseSingle(parts[9]))
        );
        calibration.Validate();
        return calibration;
    }

    public void Validate()
    {
        if (!float.IsFinite(Scale) || Scale is < 0.1f or > 50f || !IsFinite(Position) || !IsFinite(Rotation))
        {
            throw new InvalidDataException("VR scale must be from 0.1 to 50 and all coordinates must be finite.");
        }
    }

    public override string ToString()
    {
        return "{ s: "
            + Format(Scale)
            + ", p: <"
            + Format(Position.X)
            + ", "
            + Format(Position.Y)
            + ", "
            + Format(Position.Z)
            + ">, r: <"
            + Format(Rotation.X)
            + ", "
            + Format(Rotation.Y)
            + ", "
            + Format(Rotation.Z)
            + ">}";
    }

    private static float ParseSingle(string value)
    {
        return float.Parse(value, CultureInfo.InvariantCulture);
    }

    private static bool IsFinite(Vector3 vector)
    {
        return float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);
    }

    private static string Format(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}

public sealed record VrOverlayCalibrationSaveResult(string Path, string? BackupPath);
