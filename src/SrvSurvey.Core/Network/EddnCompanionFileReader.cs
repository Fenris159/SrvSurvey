using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SrvSurvey.Core.Network
{
    internal sealed record EddnCompanionReadResult(JObject? content, string? error)
    {
        internal bool isSuccess => content != null;
    }

    internal static class EddnCompanionFileReader
    {
        private static readonly TimeSpan[] retryDelays =
        [
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(400),
        ];

        internal static async Task<EddnCompanionReadResult> read(
            string journalFolder,
            JObject journalEvent,
            IReadOnlyList<TimeSpan>? retrySchedule = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(journalFolder);
            ArgumentNullException.ThrowIfNull(journalEvent);

            var eventName = journalEvent.Value<string>("event");
            if (!EddnMessageSanitizer.isCompanionEvent(eventName))
            {
                return new EddnCompanionReadResult(
                    null,
                    "the journal event does not identify a supported companion file");
            }

            return await readWithRetries(
                    Path.Combine(journalFolder, eventName + ".json"),
                    eventName!,
                    journalEvent,
                    retrySchedule ?? retryDelays,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<EddnCompanionReadResult> readWithRetries(
            string filepath,
            string eventName,
            JObject journalEvent,
            IReadOnlyList<TimeSpan> delays,
            CancellationToken cancellationToken)
        {
            string? lastError = null;
            for (var attempt = 0; attempt <= delays.Count; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attemptResult = await tryReadAttempt(
                        filepath,
                        eventName,
                        journalEvent,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (attemptResult.isSuccess)
                {
                    return attemptResult;
                }

                lastError = attemptResult.error;
                if (attempt < delays.Count)
                {
                    await Task.Delay(delays[attempt], cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return new EddnCompanionReadResult(
                null,
                lastError ?? $"{eventName}.json could not be read");
        }

        private static async Task<EddnCompanionReadResult> tryReadAttempt(
            string filepath,
            string eventName,
            JObject journalEvent,
            CancellationToken cancellationToken)
        {
            try
            {
                return await tryReadCompanionFile(
                        filepath,
                        eventName,
                        journalEvent,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return new EddnCompanionReadResult(
                    null,
                    $"{eventName}.json could not be read: {ex.Message}");
            }
        }

        private static async Task<EddnCompanionReadResult> tryReadCompanionFile(
            string filepath,
            string eventName,
            JObject journalEvent,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(filepath))
            {
                return new EddnCompanionReadResult(
                    null,
                    $"{eventName}.json was not found");
            }

            using var stream = new FileStream(
                filepath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(reader);
            var content = await JObject.LoadAsync(
                jsonReader,
                cancellationToken).ConfigureAwait(false);
            if (content.Value<string>("event") != eventName)
            {
                return new EddnCompanionReadResult(
                    null,
                    $"{eventName}.json contained a different event");
            }

            if (!matchesMarket(journalEvent, content))
            {
                return new EddnCompanionReadResult(
                    null,
                    $"{eventName}.json did not match the event's MarketID");
            }

            if (!isCurrent(journalEvent, content))
            {
                return new EddnCompanionReadResult(
                    null,
                    $"{eventName}.json was older than the journal event");
            }

            return new EddnCompanionReadResult(content, null);
        }

        private static bool matchesMarket(JObject journalEvent, JObject content)
        {
            var expected = journalEvent.Value<long?>("MarketID");
            return !expected.HasValue
                || expected <= 0
                || content.Value<long?>("MarketID") == expected;
        }

        private static bool isCurrent(JObject journalEvent, JObject content)
        {
            if (!DateTimeOffset.TryParse(
                    journalEvent.Value<string>("timestamp"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var eventTimestamp)
                || !DateTimeOffset.TryParse(
                    content.Value<string>("timestamp"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var fileTimestamp))
            {
                return true;
            }

            return fileTimestamp >= eventTimestamp;
        }
    }
}


