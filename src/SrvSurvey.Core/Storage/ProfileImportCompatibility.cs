using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Exobiology;

namespace SrvSurvey.Core.Storage;

internal static class ProfileImportCompatibility
{
    public static async Task<ProfileInventory> SelectReadableDataAsync(
        ProfileInventory inventory,
        string snapshotDirectory,
        ICollection<ProfileImportWarning> warnings,
        CancellationToken cancellationToken
    )
    {
        var accepted = new List<ProfileInventoryEntry>();
        foreach (ProfileInventoryEntry entry in inventory.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                await CanUseFileAsync(snapshotDirectory, entry.RelativePath, warnings, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                accepted.Add(entry);
            }
        }
        return inventory with { Entries = accepted };
    }

    private static async Task<bool> CanUseFileAsync(
        string snapshotDirectory,
        string relativePath,
        ICollection<ProfileImportWarning> warnings,
        CancellationToken cancellationToken
    )
    {
        if (!relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        bool player = IsPlayerFile(relativePath);
        bool codex = TryGetCodexFile(relativePath, out string frontierId, out int regionId);
        try
        {
            await using FileStream input = File.OpenRead(
                ProfileInventory.ResolveEntryPath(snapshotDirectory, relativePath)
            );
            JsonDocumentOptions options =
                player || codex
                    ? default
                    : new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                    };
            using JsonDocument document = await JsonDocument
                .ParseAsync(input, options, cancellationToken)
                .ConfigureAwait(false);
            if ((player || codex) && document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The player or Codex file must contain a JSON object.");
            }
            if (codex)
            {
                return await CheckCodexAsync(
                        snapshotDirectory,
                        relativePath,
                        frontierId,
                        regionId,
                        document.RootElement,
                        warnings,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            return true;
        }
        catch (Exception exception) when (ProfileInventory.IsRecoverable(exception) || exception is JsonException)
        {
            string disposition =
                player || codex
                    ? "Skipped incompatible player/Codex file; the original is retained in the backup. "
                    : "Preserved the file without conversion; its JSON could not be read. ";
            warnings.Add(new ProfileImportWarning(relativePath, disposition + exception.Message));
            return !player && !codex;
        }
    }

    private static async Task<bool> CheckCodexAsync(
        string directory,
        string relativePath,
        string frontierId,
        int regionId,
        JsonElement root,
        ICollection<ProfileImportWarning> warnings,
        CancellationToken cancellationToken
    )
    {
        if (
            root.TryGetProperty("codexFirsts", out JsonElement firsts)
            && firsts.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)
        )
        {
            throw new InvalidDataException("codexFirsts must be an object containing discoveries.");
        }
        CommanderCodexLoadResult loaded = await new CommanderCodexStore(directory)
            .LoadAsync(frontierId, null, regionId, cancellationToken)
            .ConfigureAwait(false);
        foreach (string warning in loaded.Warnings)
        {
            warnings.Add(new ProfileImportWarning(relativePath, warning));
        }
        return loaded.IsSuccess;
    }

    private static bool IsPlayerFile(string path) =>
        Path.GetFileName(path) == path
        && path.StartsWith('F')
        && (
            path.EndsWith("-live.json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("-legacy.json", StringComparison.OrdinalIgnoreCase)
        );

    private static bool TryGetCodexFile(string path, out string frontierId, out int regionId)
    {
        frontierId = string.Empty;
        regionId = 0;
        if (Path.GetFileName(path) != path || !path.StartsWith('F'))
        {
            return false;
        }
        string[] parts = Path.GetFileNameWithoutExtension(path).Split('-');
        if (parts.Length is not (2 or 3) || !parts[1].Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        frontierId = parts[0];
        return parts.Length == 2
            || int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out regionId) && regionId > 0;
    }
}
