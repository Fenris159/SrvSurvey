using System.Text.Json;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Quests;

public static class QuestJournalPayloadResolver
{
    private static readonly HashSet<string> AuxiliaryEvents = new(
        [
            "Cargo",
            "Market",
            "NavRoute",
            "Backpack",
            "ModulesInfo",
            "Outfitting",
            "ShipLocker",
            "Shipyard",
            "FCMaterials",
        ],
        StringComparer.Ordinal);

    public static async Task<QuestJournalPayloadResult> ResolveAsync(
        string journalDirectory,
        JournalEventEnvelope journalEvent,
        bool allowCargoFile = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        ArgumentNullException.ThrowIfNull(journalEvent);

        if (!AuxiliaryEvents.Contains(journalEvent.EventName))
        {
            return new QuestJournalPayloadResult(
                journalEvent.Payload.Clone(),
                UsedAuxiliaryFile: false,
                Warning: null);
        }

        if (journalEvent.EventName == "Cargo" && !allowCargoFile)
        {
            return Fallback(
                journalEvent,
                "Cargo.json was ignored because multiple Elite windows prevent safe commander attribution.");
        }

        var path = Path.Combine(
            journalDirectory,
            $"{journalEvent.EventName}.json");
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Fallback(
                    journalEvent,
                    $"Quest payload file '{path}' did not contain a JSON object.");
            }

            return new QuestJournalPayloadResult(
                document.RootElement.Clone(),
                UsedAuxiliaryFile: true,
                Warning: null);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            return Fallback(
                journalEvent,
                $"Quest payload file '{path}' could not be read: {exception.Message}");
        }
    }

    private static QuestJournalPayloadResult Fallback(
        JournalEventEnvelope journalEvent,
        string warning)
    {
        return new QuestJournalPayloadResult(
            journalEvent.Payload.Clone(),
            UsedAuxiliaryFile: false,
            warning);
    }
}

public sealed record QuestJournalPayloadResult(
    JsonElement Payload,
    bool UsedAuxiliaryFile,
    string? Warning);
