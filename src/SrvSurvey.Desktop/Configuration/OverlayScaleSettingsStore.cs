using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Configuration;

public sealed class OverlayScaleSettingsStore
{
    private readonly UiSettingsDocumentStore documentStore;

    public OverlayScaleSettingsStore(string path)
    {
        documentStore = new UiSettingsDocumentStore(path);
    }

    public OverlayScalePreferences Load()
    {
        var settings = documentStore.Load()["OverlayScale"] as JsonObject;
        return new OverlayScalePreferences(OverlayScaleCatalog.NormalizeIndex(GetIndex(settings?["Index"])));
    }

    public void Save(OverlayScalePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!OverlayScaleCatalog.IsSupported(preferences.Index))
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferences),
                $"Overlay scale index {preferences.Index} is not supported."
            );
        }

        documentStore.Update(root =>
        {
            var settings = root["OverlayScale"] as JsonObject;
            if (settings is null)
            {
                settings = [];
                root["OverlayScale"] = settings;
            }

            root["Version"] = 1;
            settings["Index"] = preferences.Index;
        });
    }

    public OverlayScaleMigrationResult MigrateLegacyScale(double renderScaling)
    {
        OverlayScalePreferences current = Load();
        if (!OverlayScaleCatalog.IsLegacyIndex(current.Index))
        {
            return OverlayScaleMigrationResult.NotRequired;
        }

        int migratedIndex = OverlayScaleCatalog.ConvertToRelativeIndex(current.Index, renderScaling);
        string backupPath = CreateVerifiedBackup();
        try
        {
            Save(new OverlayScalePreferences(migratedIndex));
            if (Load().Index != migratedIndex)
            {
                throw new InvalidDataException("The migrated global overlay scale could not be verified.");
            }

            return new OverlayScaleMigrationResult(true, current.Index, migratedIndex, backupPath);
        }
        catch (Exception migrationException)
            when (migrationException is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            try
            {
                RestoreBackup(backupPath);
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    "The global overlay scale migration failed and its settings rollback also failed.",
                    new AggregateException(migrationException, rollbackException)
                );
            }

            throw new IOException(
                "The global overlay scale migration failed; the original settings were restored.",
                migrationException
            );
        }
    }

    private string CreateVerifiedBackup()
    {
        string sourcePath = documentStore.Path;
        if (!File.Exists(sourcePath))
        {
            throw new InvalidDataException("The legacy global overlay scale has no settings file to back up.");
        }

        string directory = Path.Combine(
            Path.GetDirectoryName(sourcePath)
                ?? throw new InvalidOperationException("The UI settings path has no directory."),
            "overlay-scale-backups"
        );
        Directory.CreateDirectory(directory);
        string backupPath = Path.Combine(
            directory,
            $"cross-platform-ui-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}.json"
        );
        File.Copy(sourcePath, backupPath, overwrite: false);
        if (
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(File.ReadAllBytes(sourcePath)),
                SHA256.HashData(File.ReadAllBytes(backupPath))
            )
        )
        {
            throw new InvalidDataException("The global overlay scale settings backup could not be verified.");
        }

        return backupPath;
    }

    private void RestoreBackup(string backupPath)
    {
        string temporaryPath = string.Concat(
            documentStore.Path,
            ".",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            ".rollback"
        );
        try
        {
            File.Copy(backupPath, temporaryPath, overwrite: false);
            File.Move(temporaryPath, documentStore.Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static int? GetIndex(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out int integer))
        {
            return integer;
        }

        if (
            value.TryGetValue<double>(out double number)
            && double.IsFinite(number)
            && double.IsInteger(number)
            && number is >= int.MinValue and <= int.MaxValue
        )
        {
            return Convert.ToInt32(number, CultureInfo.InvariantCulture);
        }

        return null;
    }
}

public sealed record OverlayScalePreferences(int Index)
{
    public static OverlayScalePreferences Default { get; } = new(OverlayScaleCatalog.BaselineIndex);
}

public sealed record OverlayScaleMigrationResult(
    bool Migrated,
    int PreviousIndex,
    int MigratedIndex,
    string? BackupPath
)
{
    public static OverlayScaleMigrationResult NotRequired { get; } = new(false, 0, 0, null);
}

public static class OverlayScaleCatalog
{
    public const int MinimumPercent = -100;
    public const int MaximumPercent = 200;
    public const int PercentStep = 5;

    private const int RelativeIndexBase = 1000;
    private static readonly double?[] LegacyAbsoluteScales =
    [
        null,
        1d,
        1.1d,
        1.2d,
        1.25d,
        1.3d,
        1.4d,
        1.5d,
        1.6d,
        1.7d,
        1.75d,
        1.8d,
        1.9d,
        2d,
        2.1d,
        2.2d,
        2.25d,
        2.3d,
        2.4d,
        2.5d,
        0.9d,
        0.8d,
        0.75d,
        0.7d,
        0.6d,
        0.5d,
    ];

    public static int BaselineIndex => GetIndex(0);

    public static IReadOnlyList<OverlayScaleOption> Options { get; } =
        Enumerable
            .Range(0, ((MaximumPercent - MinimumPercent) / PercentStep) + 1)
            .Select(ordinal => MinimumPercent + (ordinal * PercentStep))
            .Select(percent => new OverlayScaleOption(
                GetIndex(percent),
                percent,
                FormatPercent(percent),
                1d + (percent / 100d)
            ))
            .ToArray();

    public static bool IsSupported(int index)
    {
        return IsRelativeIndex(index) || IsLegacyIndex(index);
    }

    public static int NormalizeIndex(int? index)
    {
        if (index is not { } value)
        {
            return BaselineIndex;
        }

        return IsSupported(value) ? value : BaselineIndex;
    }

    public static int GetIndex(int percent)
    {
        int normalized = NormalizePercent(percent);
        return RelativeIndexBase + ((normalized - MinimumPercent) / PercentStep);
    }

    public static int GetPercent(int index) => GetPercent(index, 1d);

    public static int GetPercent(int index, double renderScaling)
    {
        int relativeIndex = ConvertToRelativeIndex(index, renderScaling);
        return MinimumPercent + ((relativeIndex - RelativeIndexBase) * PercentStep);
    }

    public static int ConvertToRelativeIndex(int index, double renderScaling)
    {
        int normalized = NormalizeIndex(index);
        if (IsRelativeIndex(normalized))
        {
            return normalized;
        }

        double? absoluteScale = LegacyAbsoluteScales[normalized];
        if (absoluteScale is null)
        {
            return BaselineIndex;
        }

        double safeRenderScaling = NormalizeRenderScaling(renderScaling);
        return GetIndex(NormalizePercent(((absoluteScale.Value / safeRenderScaling) - 1d) * 100d));
    }

    public static int NormalizePercent(double percent)
    {
        if (!double.IsFinite(percent))
        {
            return 0;
        }

        double clamped = Math.Clamp(percent, MinimumPercent, MaximumPercent);
        return (int)(Math.Round(clamped / PercentStep, MidpointRounding.AwayFromZero) * PercentStep);
    }

    public static double GetRelativeScale(int index, double renderScaling)
    {
        int normalized = NormalizeIndex(index);
        if (IsRelativeIndex(normalized))
        {
            return 1d + (GetPercent(normalized) / 100d);
        }

        double? absoluteScale = LegacyAbsoluteScales[normalized];
        return absoluteScale is null ? 1d : absoluteScale.Value / NormalizeRenderScaling(renderScaling);
    }

    public static string FormatPercent(int percent) => percent.ToString("+0;-0;0", CultureInfo.CurrentCulture) + "%";

    private static bool IsRelativeIndex(int index) =>
        index >= RelativeIndexBase && index < RelativeIndexBase + Options.Count;

    public static bool IsLegacyIndex(int index) => index >= 0 && index < LegacyAbsoluteScales.Length;

    private static double NormalizeRenderScaling(double renderScaling) =>
        double.IsFinite(renderScaling) && renderScaling > 0 ? renderScaling : 1d;
}

public sealed record OverlayScaleOption(int Index, int Percent, string DisplayName, double RelativeScale)
{
    public override string ToString() => DisplayName;
}
