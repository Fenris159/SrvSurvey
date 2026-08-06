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

    public VrOverlayCalibrationStore(
        string dataDirectory,
        string? defaultPlottersPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        plottersPath = Path.Combine(this.dataDirectory, "plotters.json");
        this.defaultPlottersPath = Path.GetFullPath(
            defaultPlottersPath
                ?? Path.Combine(AppContext.BaseDirectory, "plotters.json"));
    }

    public VrOverlayCalibrationCatalog Load()
    {
        var defaults = LoadFile(
            File.Exists(plottersPath) ? plottersPath : defaultPlottersPath,
            allowDesktopPrefix: true);
        var factoryDefaults = LoadFile(
            defaultPlottersPath,
            allowDesktopPrefix: true);
        var overrides = new Dictionary<
            string,
            IReadOnlyDictionary<string, VrOverlayCalibration>>(
                StringComparer.OrdinalIgnoreCase);
        var overrideDirectory = Path.Combine(dataDirectory, "vr");
        if (Directory.Exists(overrideDirectory))
        {
            foreach (var path in Directory.GetFiles(overrideDirectory, "*.json"))
            {
                overrides[Path.GetFileNameWithoutExtension(path)] =
                    LoadFile(path, allowDesktopPrefix: false);
            }
        }

        return new VrOverlayCalibrationCatalog(
            defaults,
            factoryDefaults,
            overrides);
    }

    public VrOverlayCalibrationSaveResult Save(
        string plotterName,
        VrOverlayCalibration calibration,
        string? mode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        ArgumentNullException.ThrowIfNull(calibration);
        calibration.Validate();
        var normalizedMode = NormalizeMode(mode);
        return normalizedMode is null
            ? SaveDefault(plotterName, calibration)
            : SaveOverride(plotterName, calibration, normalizedMode);
    }

    private VrOverlayCalibrationSaveResult SaveDefault(
        string plotterName,
        VrOverlayCalibration calibration)
    {
        var sourcePath = File.Exists(plottersPath)
            ? plottersPath
            : defaultPlottersPath;
        var root = ParseObject(sourcePath);
        if (root[plotterName] is not JsonValue value
            || !value.TryGetValue<string>(out var existing))
        {
            throw new InvalidDataException(
                $"Overlay calibration '{plotterName}' is not present in '{sourcePath}'.");
        }

        var brace = existing.IndexOf('{');
        var desktop = (brace >= 0 ? existing[..brace] : existing).TrimEnd();
        root[plotterName] = $"{desktop} {calibration}";

        Directory.CreateDirectory(dataDirectory);
        var backupPath = File.Exists(plottersPath)
            ? CreateVerifiedBackup(plottersPath, "vr-calibration-backups")
            : null;
        WriteAtomic(
            root,
            plottersPath,
            temporaryPath =>
            {
                var pending = LoadFile(
                    temporaryPath,
                    allowDesktopPrefix: true);
                if (!pending.TryGetValue(plotterName, out var saved)
                    || saved != calibration)
                {
                    throw new InvalidDataException(
                        $"Overlay calibration '{plotterName}' could not be verified before saving.");
                }
            });
        var verified = LoadFile(plottersPath, allowDesktopPrefix: true);
        if (!verified.TryGetValue(plotterName, out var saved)
            || saved != calibration)
        {
            throw new InvalidDataException(
                $"Overlay calibration '{plotterName}' could not be verified after saving.");
        }

        return new VrOverlayCalibrationSaveResult(plottersPath, backupPath);
    }

    private VrOverlayCalibrationSaveResult SaveOverride(
        string plotterName,
        VrOverlayCalibration calibration,
        string mode)
    {
        var overrideDirectory = Path.Combine(dataDirectory, "vr");
        var overridePath = Path.Combine(overrideDirectory, $"{mode}.json");
        var root = File.Exists(overridePath)
            ? ParseObject(overridePath)
            : new JsonObject();
        root[plotterName] = calibration.ToString();

        Directory.CreateDirectory(overrideDirectory);
        var backupPath = File.Exists(overridePath)
            ? CreateVerifiedBackup(overridePath, "vr-calibration-backups")
            : null;
        WriteAtomic(
            root,
            overridePath,
            temporaryPath =>
            {
                var pending = LoadFile(
                    temporaryPath,
                    allowDesktopPrefix: false);
                if (!pending.TryGetValue(plotterName, out var saved)
                    || saved != calibration)
                {
                    throw new InvalidDataException(
                        $"VR override '{mode}/{plotterName}' could not be verified before saving.");
                }
            });

        return new VrOverlayCalibrationSaveResult(overridePath, backupPath);
    }

    private static Dictionary<string, VrOverlayCalibration> LoadFile(
        string path,
        bool allowDesktopPrefix)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, VrOverlayCalibration>(
                StringComparer.Ordinal);
        }

        var root = ParseObject(path);
        var result = new Dictionary<string, VrOverlayCalibration>(
            StringComparer.Ordinal);
        foreach (var entry in root)
        {
            if (entry.Value is not JsonValue value
                || !value.TryGetValue<string>(out var text))
            {
                throw new InvalidDataException(
                    $"VR calibration '{entry.Key}' must be a string.");
            }

            var calibration = VrOverlayCalibration.Parse(
                text,
                allowDesktopPrefix);
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
                })
            as JsonObject
            ?? throw new InvalidDataException($"'{path}' is not a JSON object.");
    }

    private static string CreateVerifiedBackup(
        string path,
        string directoryName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException(
                    "The calibration file has no parent directory."),
            directoryName);
        Directory.CreateDirectory(directory);
        var backupPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(path)}-"
            + $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffffZ}-"
            + $"{Guid.NewGuid():N}.json");
        File.Copy(path, backupPath, false);
        if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(File.ReadAllBytes(path)),
                SHA256.HashData(File.ReadAllBytes(backupPath))))
        {
            File.Delete(backupPath);
            throw new IOException("The VR calibration backup did not match its source.");
        }

        return backupPath;
    }

    private static void WriteAtomic(
        JsonObject root,
        string path,
        Action<string> verify)
    {
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new Utf8JsonWriter(
                       stream,
                       new JsonWriterOptions
                       {
                           Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                           Indented = true,
                       }))
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

        var normalized = mode.Trim();
        if (normalized is "." or ".."
            || !string.Equals(
                Path.GetFileName(normalized),
                normalized,
                StringComparison.Ordinal)
            || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException(
                $"'{mode}' is not a safe VR mode name.");
        }

        return normalized;
    }
}

public sealed class VrOverlayCalibrationCatalog
{
    public VrOverlayCalibrationCatalog(
        IReadOnlyDictionary<string, VrOverlayCalibration> defaults,
        IReadOnlyDictionary<string, VrOverlayCalibration> factoryDefaults,
        IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, VrOverlayCalibration>> overrides)
    {
        Defaults = defaults
            ?? throw new ArgumentNullException(nameof(defaults));
        FactoryDefaults = factoryDefaults
            ?? throw new ArgumentNullException(nameof(factoryDefaults));
        Overrides = overrides
            ?? throw new ArgumentNullException(nameof(overrides));
    }

    public IReadOnlyDictionary<string, VrOverlayCalibration> Defaults { get; }

    public IReadOnlyDictionary<string, VrOverlayCalibration> FactoryDefaults
    {
        get;
    }

    public IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, VrOverlayCalibration>> Overrides
    { get; }

    public VrOverlayCalibration? Resolve(string plotterName, string? mode)
    {
        if (!string.IsNullOrWhiteSpace(mode)
            && Overrides.TryGetValue(mode, out var modeOverrides)
            && modeOverrides.TryGetValue(plotterName, out var modeCalibration))
        {
            return modeCalibration;
        }

        return Defaults.GetValueOrDefault(plotterName);
    }
}

public sealed record VrOverlayCalibration(
    float Scale,
    Vector3 Position,
    Vector3 Rotation)
{
    public static VrOverlayCalibration? Parse(
        string text,
        bool allowDesktopPrefix = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        var start = text.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        if (!allowDesktopPrefix && !string.IsNullOrWhiteSpace(text[..start]))
        {
            throw new InvalidDataException(
                "A VR override must contain only a calibration block.");
        }

        var parts = text[start..].Split(
            ['{', '}', ',', ':', '<', '>'],
            StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 10
            || !string.Equals(parts[0], "s", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], "p", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[6], "r", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The VR calibration format is invalid.");
        }

        var calibration = new VrOverlayCalibration(
            ParseSingle(parts[1]),
            new Vector3(
                ParseSingle(parts[3]),
                ParseSingle(parts[4]),
                ParseSingle(parts[5])),
            new Vector3(
                ParseSingle(parts[7]),
                ParseSingle(parts[8]),
                ParseSingle(parts[9])));
        calibration.Validate();
        return calibration;
    }

    public void Validate()
    {
        if (!float.IsFinite(Scale) || Scale is < 0.1f or > 50f
            || !IsFinite(Position)
            || !IsFinite(Rotation))
        {
            throw new InvalidDataException(
                "VR scale must be from 0.1 to 50 and all coordinates must be finite.");
        }
    }

    public override string ToString()
    {
        return "{ s: " + Format(Scale)
            + ", p: <" + Format(Position.X) + ", "
            + Format(Position.Y) + ", " + Format(Position.Z)
            + ">, r: <" + Format(Rotation.X) + ", "
            + Format(Rotation.Y) + ", " + Format(Rotation.Z) + ">}";
    }

    private static float ParseSingle(string value)
    {
        return float.Parse(value, CultureInfo.InvariantCulture);
    }

    private static bool IsFinite(Vector3 vector)
    {
        return float.IsFinite(vector.X)
            && float.IsFinite(vector.Y)
            && float.IsFinite(vector.Z);
    }

    private static string Format(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }
}

public sealed record VrOverlayCalibrationSaveResult(
    string Path,
    string? BackupPath);
