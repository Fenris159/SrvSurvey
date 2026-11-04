using System.Text.Json;

namespace SrvSurvey.Core.Journal;

public sealed partial class JournalDirectoryMonitor
{
    private bool currentJournalIsContinuation;

    private sealed record ResolvedJournalIdentity(string FrontierId, IReadOnlyList<FileInfo> Parts);

    private async Task<ResolvedJournalIdentity?> ResolveJournalIdentityAsync(
        FileInfo file,
        CancellationToken cancellationToken
    )
    {
        var parts = new List<FileInfo>();
        bool requirePart = false;
        while (file.Exists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JournalIdentityCacheEntry? identity = await ReadJournalIdentityAsync(file, cancellationToken)
                .ConfigureAwait(false);
            if (identity is null || (requirePart && identity.Part is null))
            {
                return null;
            }
            parts.Add(file);
            if (!string.IsNullOrWhiteSpace(identity.FrontierId))
            {
                parts.Reverse();
                return new(identity.FrontierId, parts);
            }
            if (identity.Part?.PreviousPath is not { } previous)
            {
                return null;
            }
            file = new FileInfo(previous);
            requirePart = true;
        }
        return null;
    }

    private async Task<string?> ReadFrontierIdAsync(FileInfo file, CancellationToken cancellationToken) =>
        (await ResolveJournalIdentityAsync(file, cancellationToken).ConfigureAwait(false))?.FrontierId;

    private async Task ReadSelectedPartsAsync(
        FileInfo latest,
        List<JournalEventEnvelope> events,
        List<string> errors,
        CancellationToken cancellationToken
    )
    {
        ResolvedJournalIdentity? identity = await ResolveJournalIdentityAsync(latest, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<FileInfo> parts = identity?.Parts ?? [latest];
        int start = 0;
        for (int index = 0; index < parts.Count; index++)
        {
            if (PathsEqual(parts[index].FullName, currentJournalPath))
            {
                start = index;
                break;
            }
        }
        for (int index = start; index < parts.Count; index++)
        {
            FileInfo part = parts[index];
            if (!PathsEqual(part.FullName, currentJournalPath))
            {
                FlushPendingLine(events, errors);
                SetActiveJournalDirectory(part.DirectoryName);
                currentJournalPath = part.FullName;
                currentJournalOffset = 0;
                pendingJournalBytes = [];
                // Only verified predecessors share a running session. A continuation
                // header must not reset commander, vehicle or workspace reducers.
                currentJournalIsContinuation = index > 0;
            }
            await ReadJournalAppendAsync(events, errors, cancellationToken).ConfigureAwait(false);
        }
    }

    // A temporarily missing continuation must not make its already-consumed
    // predecessor look like a new journal. Keep the cursor until it returns.
    private async Task<bool> IsEarlierSessionPartAsync(FileInfo candidate, CancellationToken cancellationToken)
    {
        if (
            currentJournalPath is null
            || !PathsEqual(Path.GetDirectoryName(currentJournalPath)!, candidate.DirectoryName)
            || !journalIdentityCache.TryGetValue(currentJournalPath, out JournalIdentityCacheEntry? current)
            || current.Part is not { } currentPart
        )
        {
            return false;
        }
        JournalIdentityCacheEntry? identity = await ReadJournalIdentityAsync(candidate, cancellationToken)
            .ConfigureAwait(false);
        return identity?.Part is { } candidatePart
            && string.Equals(currentPart.Series, candidatePart.Series, StringComparison.Ordinal)
            && candidatePart.Number < currentPart.Number;
    }

    private static string? ReadDeclaredFrontierId(JournalEventEnvelope entry) =>
        entry.EventName is "Commander" or "LoadGame"
        && entry.Payload.TryGetProperty("FID", out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
