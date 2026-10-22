using System.Text.Json;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Configuration;

/// <summary>Retains source placement provenance until imported display references can be assigned.</summary>
internal sealed class ImportedOverlayReferenceRecovery(AppDataPaths paths)
{
    private const string FileName = "pending-overlay-import-references.json";
    private readonly string directory = Path.GetDirectoryName(paths.UiSettingsPath)!;

    public IReadOnlySet<string> Remember(
        ProfileImportResult result,
        JsonObject incoming,
        JsonObject current,
        IReadOnlySet<string> added,
        ICollection<ProfileImportWarning>? warnings = null
    )
    {
        var eligible = added.ToHashSet(StringComparer.Ordinal);
        try
        {
            using FileStream lease = OverlayLayoutFileAccess.Acquire(directory);
            Backup(result.BackupDirectory);
            Dictionary<string, PendingReference> pending = Read(warnings);
            foreach (string name in added)
            {
                string? position = ReadPosition(current, name) ?? ReadPosition(incoming, name);
                if (position is not null)
                {
                    pending[name] = new PendingReference(result.Manifest.SourceDirectory, position);
                    eligible.Add(name);
                }
            }
            foreach ((string name, PendingReference reference) in pending)
            {
                if (
                    string.Equals(
                        reference.SourceDirectory,
                        result.Manifest.SourceDirectory,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
                    )
                    && ReadPosition(incoming, name) == reference.Position
                    && ReadPosition(current, name) == reference.Position
                )
                {
                    eligible.Add(name);
                }
            }
            Save(pending, result.BackupDirectory);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            warnings?.Add(new ProfileImportWarning(FileName, exception.Message));
        }
        return eligible;
    }

    public void Complete(ProfileImportResult result, ICollection<ProfileImportWarning>? warnings = null)
    {
        IReadOnlyDictionary<string, LegacyOverlayPlacement> placements = new LegacyOverlayLayoutStore(
            paths.OverlaySettingsDirectory
        )
            .Load()
            .Placements;
        using FileStream lease = OverlayLayoutFileAccess.Acquire(directory);
        Backup(result.BackupDirectory);
        Dictionary<string, PendingReference> pending = Read(warnings);
        foreach (
            string name in pending
                .Keys.Where(name =>
                    placements.TryGetValue(name, out LegacyOverlayPlacement? placement)
                    && placement.PositionReference is not null
                )
                .ToArray()
        )
        {
            pending.Remove(name);
        }
        Save(pending, result.BackupDirectory);
    }

    private static string? ReadPosition(JsonObject positions, string name) =>
        positions[name] is JsonValue value
        && value.TryGetValue(out string? position)
        && !string.IsNullOrWhiteSpace(position)
            ? position
            : null;

    private Dictionary<string, PendingReference> Read(ICollection<ProfileImportWarning>? warnings)
    {
        string path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, PendingReference>(StringComparer.Ordinal);
        }
        try
        {
            Dictionary<string, PendingReference> pending =
                JsonSerializer.Deserialize<Dictionary<string, PendingReference>>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Pending overlay reference recovery data is invalid.");
            if (
                pending.Values.Any(reference =>
                    reference is null
                    || string.IsNullOrEmpty(reference.SourceDirectory)
                    || string.IsNullOrEmpty(reference.Position)
                )
            )
            {
                throw new InvalidDataException("Pending overlay reference recovery data is invalid.");
            }
            return pending;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            warnings?.Add(new ProfileImportWarning(FileName, exception.Message));
            return new Dictionary<string, PendingReference>(StringComparer.Ordinal);
        }
    }

    private void Backup(string backupDirectory)
    {
        string path = Path.Combine(directory, FileName);
        string backup = Path.Combine(backupDirectory, FileName);
        if (File.Exists(path) && !File.Exists(backup))
        {
            File.Copy(path, backup);
        }
    }

    private void Save(Dictionary<string, PendingReference> pending, string backupDirectory)
    {
        string path = Path.Combine(directory, FileName);
        Backup(backupDirectory);
        if (pending.Count == 0)
        {
            File.Delete(path);
            return;
        }
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(pending));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private sealed record PendingReference(string SourceDirectory, string Position);
}
