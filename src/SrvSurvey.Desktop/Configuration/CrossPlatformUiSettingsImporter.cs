using System.Security.Cryptography;
using System.Text.Json;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Configuration;

public static class CrossPlatformUiSettingsImporter
{
    public static async Task<CrossPlatformUiSettingsImportResult> ImportAsync(
        string sourcePath,
        string destinationPath,
        string backupDirectory,
        string? overlayDataDirectory = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        string source = Path.GetFullPath(sourcePath);
        string destination = Path.GetFullPath(destinationPath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("The selected profile UI settings file does not exist.", source);
        }

        byte[] importedBytes = await File.ReadAllBytesAsync(source, cancellationToken).ConfigureAwait(false);
        ValidateSettings(importedBytes);

        string destinationDirectory =
            Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("The UI settings destination has no parent directory.");
        Directory.CreateDirectory(destinationDirectory);
        Directory.CreateDirectory(backupDirectory);

        string? backupPath = null;
        if (File.Exists(destination))
        {
            backupPath = Path.Combine(backupDirectory, LegacyUiSettingsMigrator.BackupFileName);
            File.Copy(destination, backupPath, overwrite: false);
            await VerifyEqualAsync(destination, backupPath, cancellationToken).ConfigureAwait(false);
        }

        string stagedPath = Path.Combine(destinationDirectory, Path.GetRandomFileName());
        try
        {
            await File.WriteAllBytesAsync(stagedPath, importedBytes, cancellationToken).ConfigureAwait(false);
            await VerifyBytesAsync(stagedPath, importedBytes, cancellationToken).ConfigureAwait(false);
            File.Move(stagedPath, destination, overwrite: true);
            await VerifyBytesAsync(destination, importedBytes, cancellationToken).ConfigureAwait(false);
            int referencedOverlayCount = 0;
            if (
                !string.IsNullOrWhiteSpace(overlayDataDirectory)
                && TryReadOverlayPositionReference(importedBytes, out OverlayPositionReference? reference)
                && reference is not null
            )
            {
                referencedOverlayCount = new LegacyOverlayLayoutStore(
                    overlayDataDirectory
                ).SaveImportedPositionReferences(reference);
            }

            return new CrossPlatformUiSettingsImportResult(true, backupPath, referencedOverlayCount);
        }
        finally
        {
            if (File.Exists(stagedPath))
            {
                File.Delete(stagedPath);
            }
        }
    }

    private static void ValidateSettings(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The selected profile UI settings must contain a JSON object.");
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The selected profile UI settings are not valid JSON.", exception);
        }
    }

    internal static bool TryReadOverlayPositionReference(byte[] settingsBytes, out OverlayPositionReference? reference)
    {
        ArgumentNullException.ThrowIfNull(settingsBytes);
        using var document = JsonDocument.Parse(settingsBytes);
        if (
            !document.RootElement.TryGetProperty("DesktopBehavior", out JsonElement desktopBehavior)
            || !desktopBehavior.TryGetProperty("ApplicationWindowPosition", out JsonElement position)
            || !position.TryGetProperty("Monitor", out JsonElement monitorElement)
            || monitorElement.ValueKind != JsonValueKind.String
        )
        {
            reference = null;
            return false;
        }

        string? monitor = monitorElement.GetString();
        if (monitor is null || !monitor.StartsWith("bounds:", StringComparison.OrdinalIgnoreCase))
        {
            reference = null;
            return false;
        }

        string[] parts = monitor["bounds:".Length..].Split(',', StringSplitOptions.TrimEntries);
        if (
            parts.Length != 4
            || !int.TryParse(parts[2], out int width)
            || !int.TryParse(parts[3], out int height)
            || width <= 0
            || height <= 0
        )
        {
            reference = null;
            return false;
        }

        reference = new OverlayPositionReference(width, height);
        return true;
    }

    private static async Task VerifyEqualAsync(
        string expectedPath,
        string actualPath,
        CancellationToken cancellationToken
    )
    {
        byte[] expected = await File.ReadAllBytesAsync(expectedPath, cancellationToken).ConfigureAwait(false);
        await VerifyBytesAsync(actualPath, expected, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyBytesAsync(string path, byte[] expected, CancellationToken cancellationToken)
    {
        byte[] actual = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(expected), SHA256.HashData(actual)))
        {
            throw new IOException("A copied UI settings file did not match its source.");
        }
    }
}

public sealed record CrossPlatformUiSettingsImportResult(
    bool Imported,
    string? BackupPath,
    int ReferencedOverlayCount = 0
);
