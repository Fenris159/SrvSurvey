using System.Text.Json.Nodes;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Storage;

public sealed class SystemNoteStore
{
    private readonly LegacySystemDataFileStore fileStore;

    public SystemNoteStore(string dataDirectory)
    {
        fileStore = new LegacySystemDataFileStore(dataDirectory);
    }

    public async Task<SystemNoteLoadResult> LoadAsync(
        string frontierId,
        string systemName,
        long systemAddress,
        CancellationToken cancellationToken = default)
    {
        var result = await fileStore.LoadAsync(
                new LegacySystemDataFileContext(
                    frontierId,
                    null,
                    systemName,
                    systemAddress,
                    null),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Root is null)
        {
            return result.Exists
                ? new SystemNoteLoadResult(
                    result.Path,
                    true,
                    null,
                    result.Error)
                : new SystemNoteLoadResult(
                    result.Path,
                    false,
                    string.Empty,
                    null);
        }

        return new SystemNoteLoadResult(
            result.Path,
            true,
            GetString(result.Root, "notes") ?? string.Empty,
            null);
    }

    public Task<string> SaveAsync(
        SystemNoteContext context,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return fileStore.UpdateAsync(
            new LegacySystemDataFileContext(
                context.FrontierId,
                context.CommanderName,
                context.SystemName,
                context.SystemAddress,
                context.StarPosition),
            root => root["notes"] = notes ?? string.Empty,
            cancellationToken);
    }

    public static string MakeSafeFileName(string value)
    {
        return LegacySystemDataFileStore.MakeSafeFileName(value);
    }

    private static string? GetString(JsonObject root, string propertyName)
    {
        return root[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var result)
                ? result
                : null;
    }
}

public sealed record SystemNoteContext(
    string FrontierId,
    string? CommanderName,
    string SystemName,
    long SystemAddress,
    GalacticCoordinate? StarPosition);

public sealed record SystemNoteLoadResult(
    string Path,
    bool Exists,
    string? Notes,
    string? Error)
{
    public bool IsSuccess => Notes is not null;
}
