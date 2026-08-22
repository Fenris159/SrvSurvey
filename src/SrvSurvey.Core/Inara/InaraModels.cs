using Newtonsoft.Json.Linq;

// Behavioral reference:
// https://github.com/EDCD/EDMarketConnector/blob/2b6a0ce1ee3ba60c21f3f4e9fa093046da8825e4/plugins/inara.py
// Copyright (c) EDCD, licensed under GNU GPL v2 or later.

namespace SrvSurvey.Core.Inara
{
    internal sealed record InaraCredentials(
        string Commander,
        string FrontierId,
        string ApiKey)
    {
        public override string ToString() =>
            $"InaraCredentials {{ Commander = {Commander}, FrontierId = {FrontierId} }}";
    }

    /// <summary>
    /// Stable identity and eligibility for one initialized journal session.
    /// </summary>
    internal sealed record InaraSession(
        string Commander,
        string FrontierId,
        string? JournalPath,
        bool IsLive,
        bool IsBeta)
    {
        public static InaraSession? Create(
            InaraPublicationOptions options,
            string? journalPath)
        {
            ArgumentNullException.ThrowIfNull(options);
            var commander = options.CommanderName?.Trim();
            var frontierId = options.FrontierId?.Trim();
            var version = options.GameVersion?.Trim();
            if (string.IsNullOrWhiteSpace(commander)
                || string.IsNullOrWhiteSpace(frontierId)
                || string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            return new InaraSession(
                commander,
                frontierId,
                string.IsNullOrWhiteSpace(journalPath)
                    ? null
                    : journalPath,
                InaraPublisher.IsLiveVersion(version, options.IsOdyssey),
                InaraPublisher.IsBetaVersion(version));
        }

        public bool Matches(InaraSession other)
        {
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                    Commander,
                    other.Commander,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    FrontierId,
                    other.FrontierId,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    JournalPath,
                    other.JournalPath,
                    pathComparison)
                && IsLive == other.IsLive
                && IsBeta == other.IsBeta;
        }

        public InaraCredentials? GetCredentials(string? apiKey)
        {
            var normalized = apiKey?.Trim();
            return string.IsNullOrWhiteSpace(normalized)
                ? null
                : new InaraCredentials(Commander, FrontierId, normalized);
        }
    }

    internal sealed record InaraContext(
        string? Commander,
        string? FrontierId,
        string? SystemName,
        string? StationName,
        string? BodyName,
        string? ShipType,
        long? ShipId,
        string? ShipName,
        string? ShipIdent,
        bool? IsTaxi);

    internal sealed record InaraEvent(
        string Name,
        string Timestamp,
        JToken Data,
        string? ReplaceKey = null);

    internal sealed record InaraQueuedEvent(string ApiKey, InaraEvent Event)
    {
        public override string ToString() =>
            $"InaraQueuedEvent {{ Event = {Event.Name} }}";
    }

    internal static class InaraPayloadBuilder
    {
        public static JObject Build(
            string appVersion,
            InaraCredentials credentials,
            IReadOnlyCollection<InaraEvent> events)
        {
            var header = new JObject
            {
                ["appName"] = "SrvSurvey",
                ["appVersion"] = appVersion,
                ["isBeingDeveloped"] = true,
                ["APIkey"] = credentials.ApiKey,
                ["commanderName"] = credentials.Commander,
            };

            if (!string.IsNullOrWhiteSpace(credentials.FrontierId))
                header["commanderFrontierID"] = credentials.FrontierId;

            return new JObject
            {
                ["header"] = header,
                ["events"] = new JArray(events.Select(entry => new JObject
                {
                    ["eventName"] = entry.Name,
                    ["eventTimestamp"] = entry.Timestamp,
                    ["eventData"] = entry.Data.DeepClone(),
                })),
            };
        }
    }

    internal sealed class InaraEventQueue
    {
        public const int DefaultMaximumCount = 4096;
        private readonly object sync = new();
        private readonly List<InaraQueuedEvent> pending = new();

        public int Count
        {
            get
            {
                lock (sync)
                    return pending.Count;
            }
        }

        public int Enqueue(
            string apiKey,
            IEnumerable<InaraEvent> events,
            int maximumCount = DefaultMaximumCount)
        {
            lock (sync)
            {
                foreach (var entry in events)
                {
                    if (!string.IsNullOrWhiteSpace(entry.ReplaceKey))
                    {
                        pending.RemoveAll(item =>
                            item.ApiKey == apiKey
                            && item.Event.ReplaceKey == entry.ReplaceKey);
                    }

                    pending.Add(new InaraQueuedEvent(apiKey, entry));
                }

                return trimToMaximum(maximumCount);
            }
        }

        public List<InaraQueuedEvent> TakeBatch(
            string? apiKey,
            int maximumCount,
            out int discarded)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
            lock (sync)
            {
                discarded = pending.RemoveAll(item => item.ApiKey != apiKey);
                if (pending.Count == 0)
                    return [];

                var count = Math.Min(pending.Count, maximumCount);
                var batch = pending.GetRange(0, count);
                pending.RemoveRange(0, count);
                return batch;
            }
        }

        public List<InaraQueuedEvent> TakeAll()
        {
            lock (sync)
            {
                var copy = pending.ToList();
                pending.Clear();
                return copy;
            }
        }

        public int Requeue(
            IEnumerable<InaraQueuedEvent> events,
            int maximumCount = DefaultMaximumCount)
        {
            lock (sync)
            {
                var retained = events
                    .Where(item => string.IsNullOrWhiteSpace(item.Event.ReplaceKey)
                        || !pending.Any(current => current.ApiKey == item.ApiKey
                            && current.Event.ReplaceKey == item.Event.ReplaceKey))
                    .ToList();
                pending.InsertRange(0, retained);
                return trimToMaximum(maximumCount);
            }
        }

        public int DiscardExcept(string? apiKey)
        {
            lock (sync)
            {
                return pending.RemoveAll(item => item.ApiKey != apiKey);
            }
        }

        private int trimToMaximum(int maximumCount)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
            var dropped = Math.Max(0, pending.Count - maximumCount);
            if (dropped > 0)
                pending.RemoveRange(0, dropped);
            return dropped;
        }
    }
}
