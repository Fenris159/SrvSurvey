using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.Configuration;

public static class CrossPlatformUiSettingsImporter
{
    internal const string CompletionSignalFileName = "current-profile-ui-import-completed.lock";

    public static async Task<CrossPlatformUiSettingsImportResult> ImportAsync(
        string sourcePath,
        string destinationPath,
        string backupDirectory,
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

        await File.WriteAllBytesAsync(
                Path.Combine(backupDirectory, "incoming-cross-platform-ui.json"),
                importedBytes,
                cancellationToken
            )
            .ConfigureAwait(false);
        string? backupPath = null;
        if (File.Exists(destination))
        {
            backupPath = Path.Combine(backupDirectory, LegacyUiSettingsMigrator.BackupFileName);
            File.Copy(destination, backupPath, overwrite: false);
            await VerifyEqualAsync(destination, backupPath, cancellationToken).ConfigureAwait(false);
            byte[] currentBytes = await File.ReadAllBytesAsync(backupPath, cancellationToken).ConfigureAwait(false);
            ValidateSettings(currentBytes);
            JsonObject current = JsonNode.Parse(currentBytes)!.AsObject();
            JsonObject incoming = JsonNode.Parse(importedBytes)!.AsObject();
            JsonObject merged = ProfileJsonMerge.MergeMissing(current, incoming);
            importedBytes = JsonNode.DeepEquals(current, merged)
                ? currentBytes
                : JsonSerializer.SerializeToUtf8Bytes(merged);
        }

        string stagedPath = Path.Combine(destinationDirectory, Path.GetRandomFileName());
        try
        {
            await File.WriteAllBytesAsync(stagedPath, importedBytes, cancellationToken).ConfigureAwait(false);
            await VerifyBytesAsync(stagedPath, importedBytes, cancellationToken).ConfigureAwait(false);
            File.Move(stagedPath, destination, overwrite: true);
            await VerifyBytesAsync(destination, importedBytes, cancellationToken).ConfigureAwait(false);
            await WriteCompletionSignalAsync(backupDirectory, importedBytes, cancellationToken).ConfigureAwait(false);

            return new CrossPlatformUiSettingsImportResult(true, backupPath);
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

    private static async Task WriteCompletionSignalAsync(
        string backupDirectory,
        byte[] importedBytes,
        CancellationToken cancellationToken
    )
    {
        string completionPath = Path.Combine(backupDirectory, CompletionSignalFileName);
        string stagedPath = Path.Combine(backupDirectory, Path.GetRandomFileName());
        byte[] content = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(importedBytes)));
        try
        {
            await using (
                var stream = new FileStream(
                    stagedPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4_096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough
                )
            )
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(stagedPath, completionPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(stagedPath))
            {
                File.Delete(stagedPath);
            }
        }
    }
}

public sealed record CrossPlatformUiSettingsImportResult(bool Imported, string? BackupPath);
