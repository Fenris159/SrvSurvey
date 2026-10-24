using System.Text.Json;
using System.Text.Json.Nodes;

namespace SrvSurvey.Core.Storage;

internal static class ProfileImportMerger
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static async Task<ProfileInventory> BuildPayloadAsync(
        ProfileInventory incoming,
        ProfileInventory current,
        string payloadDirectory,
        ICollection<ProfileImportWarning> warnings,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(payloadDirectory);
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var currentFiles = current.Entries.ToDictionary(entry => entry.RelativePath, comparer);
        var currentDirectories = current.RelativeDirectories.ToHashSet(comparer);
        foreach (
            string directory in incoming.RelativeDirectories.Where(directory =>
                !HasFileAncestor(directory, currentFiles)
            )
        )
        {
            Directory.CreateDirectory(ProfileInventory.ResolveEntryPath(payloadDirectory, directory));
        }
        foreach (ProfileInventoryEntry entry in incoming.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                currentDirectories.Contains(entry.RelativePath)
                || HasFileAncestor(entry.RelativePath, currentFiles, includeSelf: false)
            )
            {
                warnings.Add(
                    new ProfileImportWarning(
                        entry.RelativePath,
                        "Kept the current file or folder; the incoming path conflicts with it. The source is retained in the backup."
                    )
                );
                continue;
            }
            if (
                currentFiles.TryGetValue(entry.RelativePath, out ProfileInventoryEntry? previous)
                && entry.Sha256 == previous.Sha256
            )
            {
                continue;
            }
            await WritePayloadFileAsync(
                    incoming,
                    current,
                    payloadDirectory,
                    entry.RelativePath,
                    previous is not null,
                    warnings,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        return await ProfileInventory.CreateAsync(payloadDirectory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WritePayloadFileAsync(
        ProfileInventory incoming,
        ProfileInventory current,
        string payloadDirectory,
        string relativePath,
        bool hasCurrent,
        ICollection<ProfileImportWarning> warnings,
        CancellationToken cancellationToken
    )
    {
        string sourcePath = ProfileInventory.ResolveEntryPath(incoming.RootPath, relativePath);
        string target = ProfileInventory.ResolveEntryPath(payloadDirectory, relativePath);
        if (!hasCurrent)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (string.Equals(relativePath, "settings.json", StringComparison.OrdinalIgnoreCase))
            {
                byte[] sourceBytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(
                        target,
                        ExcludeIncomingOverlayOpacity(sourceBytes, relativePath),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            else
            {
                File.Copy(sourcePath, target);
            }
            return;
        }
        byte[]? merged = await MergeFileAsync(
                sourcePath,
                ProfileInventory.ResolveEntryPath(current.RootPath, relativePath),
                relativePath,
                warnings,
                cancellationToken
            )
            .ConfigureAwait(false);
        if (merged is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, merged, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Keeps legacy opacity out of activation without changing the verified raw source backup.</summary>
    private static byte[] ExcludeIncomingOverlayOpacity(byte[] sourceBytes, string relativePath)
    {
        if (!string.Equals(relativePath, "settings.json", StringComparison.OrdinalIgnoreCase))
        {
            return sourceBytes;
        }
        try
        {
            if (
                JsonNode.Parse(sourceBytes, documentOptions: DocumentOptions) is JsonObject settings
                && settings.Remove("plotterOpacity")
            )
            {
                return JsonSerializer.SerializeToUtf8Bytes(settings, IndentedJson);
            }
        }
        catch (JsonException)
        {
            // Preserve unreadable shared data under the existing compatibility policy.
        }
        return sourceBytes;
    }

    private static bool HasFileAncestor(
        string path,
        Dictionary<string, ProfileInventoryEntry> files,
        bool includeSelf = true
    )
    {
        if (includeSelf && files.ContainsKey(path))
        {
            return true;
        }
        int separator = path.LastIndexOf('/');
        while (separator >= 0)
        {
            if (files.ContainsKey(path[..separator]))
            {
                return true;
            }
            separator = path.LastIndexOf('/', separator - 1);
        }
        return false;
    }

    private static async Task<byte[]?> MergeFileAsync(
        string sourcePath,
        string currentPath,
        string relativePath,
        ICollection<ProfileImportWarning> warnings,
        CancellationToken cancellationToken
    )
    {
        if (!relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        try
        {
            byte[] sourceBytes = ExcludeIncomingOverlayOpacity(
                await File.ReadAllBytesAsync(sourcePath, cancellationToken).ConfigureAwait(false),
                relativePath
            );
            byte[] currentBytes = await File.ReadAllBytesAsync(currentPath, cancellationToken).ConfigureAwait(false);
            if (
                JsonNode.Parse(currentBytes, documentOptions: DocumentOptions) is not JsonObject current
                || JsonNode.Parse(sourceBytes, documentOptions: DocumentOptions) is not JsonObject incoming
            )
            {
                throw new InvalidDataException("Only JSON objects can be merged safely.");
            }
            JsonObject merged = ProfileJsonMerge.MergeMissing(current, incoming);
            if (JsonNode.DeepEquals(merged, current))
            {
                return null;
            }
            return JsonNode.DeepEquals(merged, incoming)
                ? sourceBytes
                : JsonSerializer.SerializeToUtf8Bytes(merged, IndentedJson);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        {
            warnings.Add(
                new ProfileImportWarning(
                    relativePath,
                    "Kept the current file because the two versions could not be merged. The source is retained in the backup. "
                        + exception.Message
                )
            );
            return null;
        }
    }
}
