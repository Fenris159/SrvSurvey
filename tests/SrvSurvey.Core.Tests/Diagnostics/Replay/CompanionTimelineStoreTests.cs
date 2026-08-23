using System.IO.Compression;
using System.Text.Json;
using SrvSurvey.Core.Diagnostics.Replay;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Diagnostics.Replay;

public sealed class CompanionTimelineStoreTests
{
    [Fact]
    public async Task RollingHistoryKeepsOneDayAndSuppressesTimestampOnlyChanges()
    {
        using var temp = new TemporaryDirectory();
        var earlyTime = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-22T10:00:00Z"));
        using (var earlyStore = new CompanionTimelineStore(temp.Path, earlyTime))
        {
            await earlyStore.AppendAsync(CreateUpdate(new EliteStatus
            {
                Timestamp = earlyTime.GetUtcNow(),
                EventName = "Status",
                Flags = StatusFlags.InSrv,
                Heading = 42,
            }));
        }

        var currentTime = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-23T12:00:00Z"));
        using (var store = new CompanionTimelineStore(temp.Path, currentTime))
        {
            await store.AppendAsync(CreateUpdate(new EliteStatus
            {
                Timestamp = currentTime.GetUtcNow(),
                EventName = "Status",
                Flags = StatusFlags.InSrv,
                Heading = 90,
            }));
            currentTime.Advance(TimeSpan.FromSeconds(1));
            await store.AppendAsync(CreateUpdate(new EliteStatus
            {
                Timestamp = currentTime.GetUtcNow(),
                EventName = "Status",
                Flags = StatusFlags.InSrv,
                Heading = 90,
            }));
        }

        var entries = new List<CompanionTimelineEntry>();
        await foreach (var entry in CompanionTimelineStore.StreamAsync(
                           temp.Path,
                           from: null,
                           to: null,
                           CancellationToken.None))
        {
            entries.Add(entry);
        }

        var status = Assert.Single(entries);
        Assert.Equal(ReplayInputKind.Status, status.Kind);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-23T12:00:00Z"),
            status.Timestamp);
        Assert.Contains(
            "2026-08-23T12:00:00.000Z",
            CompanionTimelineCodec.SerializeEntry(status),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageSynchronizesAndPreloadsEveryCompanionTimeline()
    {
        using var temp = new TemporaryDirectory();
        var journals = Path.Combine(temp.Path, "journals");
        var history = Path.Combine(temp.Path, "history");
        Directory.CreateDirectory(journals);
        await File.WriteAllLinesAsync(
            Path.Combine(journals, "Journal.2026-08-23T095000.01.log"),
            [
                "{\"timestamp\":\"2026-08-23T09:50:00Z\",\"event\":\"Fileheader\",\"Odyssey\":true}",
                "{\"timestamp\":\"2026-08-23T09:50:01Z\",\"event\":\"Commander\",\"Name\":\"Private Cmdr\",\"FID\":\"F123456\"}",
                "{\"timestamp\":\"2026-08-23T09:50:02Z\",\"event\":\"LoadGame\",\"Commander\":\"Private Cmdr\",\"FID\":\"F123456\",\"Odyssey\":true}",
                "{\"timestamp\":\"2026-08-23T09:50:03Z\",\"event\":\"Location\",\"StarSystem\":\"Private System\",\"SystemAddress\":123,\"StarPos\":[1,2,3]}",
                "{\"timestamp\":\"2026-08-23T10:00:00Z\",\"event\":\"Music\",\"MusicTrack\":\"Exploration\"}",
            ]);
        var time = new MutableTimeProvider(
            DateTimeOffset.Parse("2026-08-23T10:05:00Z"));
        using (var store = new CompanionTimelineStore(history, time))
        {
            await store.AppendAsync(CreateUpdate(new EliteStatus
            {
                Timestamp = DateTimeOffset.Parse("2026-08-23T09:59:00Z"),
                EventName = "Status",
                Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
                Latitude = 12.3,
                Longitude = 45.6,
                Heading = 90,
                BodyName = "Private Body",
            }));
            await store.AppendAsync(CreateCompleteUpdate(
                DateTimeOffset.Parse("2026-08-23T10:01:00Z")));
        }

        var packagePath = Path.Combine(temp.Path, "incident.srvreplay");
        var result = await new JournalReplayExporter().ExportAsync(
            journals,
            history,
            packagePath,
            new JournalReplayExportRequest(
                DateTimeOffset.Parse("2026-08-23T10:00:00Z"),
                DateTimeOffset.Parse("2026-08-23T10:02:00Z"),
                ReplayPrivacyMode.Redacted,
                "test"),
            CancellationToken.None);

        Assert.Equal(6, result.CompanionEventCount);
        using (var archive = ZipFile.OpenRead(packagePath))
        {
            Assert.NotNull(archive.GetEntry("companions.jsonl"));
        }

        var session = await new ReplaySessionManager().ImportAsync(
            packagePath,
            Path.Combine(temp.Path, "managed"),
            CancellationToken.None);
        Assert.Empty(session.MissingCompanionTimelines);
        Assert.Equal(5, session.BootstrapInputCount);
        Assert.Contains(session.Events, item => item.Kind == ReplayInputKind.Cargo);

        var player = new JournalReplayPlayer(session);
        await player.SeekAsync(
            session.BootstrapInputCount,
            CancellationToken.None);
        var playback = Path.GetDirectoryName(session.PlaybackJournalPath)!;
        var status = await StatusFileReader.ReadAsync(
            Path.Combine(playback, StatusFileReader.FileName));
        Assert.NotNull(status.Status);
        Assert.Equal(0, status.Status.Latitude);
        Assert.Equal(0, status.Status.Longitude);
        Assert.Equal("Replay Body", status.Status.BodyName);

        while (!session.Events
                   .Take(player.Position)
                   .Any(item => item.Kind == ReplayInputKind.Cargo))
        {
            Assert.True(await player.StepAsync(CancellationToken.None));
        }

        var cargo = await CargoFileReader.ReadAsync(
            Path.Combine(playback, CargoFileReader.FileName));
        Assert.Equal(2, cargo.Snapshot?.GetCount("ancientorb"));
        var routeEntry = Assert.Single(
            session.Events,
            item => item.Kind == ReplayInputKind.NavRoute);
        using var routeDocument = JsonDocument.Parse(routeEntry.RawJson);
        Assert.Equal(
            "Replay Route 001",
            routeDocument.RootElement.GetProperty("Route")[0]
                .GetProperty("StarSystem").GetString());
    }

    private static JournalMonitorUpdate CreateUpdate(EliteStatus status) => new(
        null,
        [],
        status,
        null,
        null,
        null,
        [],
        IsBootstrapRead: false);

    private static JournalMonitorUpdate CreateCompleteUpdate(
        DateTimeOffset timestamp) => new(
        null,
        [],
        new EliteStatus
        {
            Timestamp = timestamp,
            EventName = "Status",
            Flags = StatusFlags.InSrv | StatusFlags.HasLatLong,
            Latitude = 13.3,
            Longitude = 46.6,
            Heading = 91,
            BodyName = "Private Body",
        },
        new NavRouteSnapshot(
            timestamp,
            "NavRoute",
            [new NavRouteEntry(
                "Private Destination",
                456,
                new GalacticCoordinate(4, 5, 6),
                "G")]),
        new CargoSnapshot(
            timestamp,
            "Cargo",
            "Ship",
            2,
            [new CargoItem("ancientorb", "Ancient Orb", 2, 0)]),
        new MarketSnapshot(
            timestamp,
            "Market",
            789,
            "Private Station",
            "Coriolis",
            "all",
            "Private System",
            []),
        [],
        IsBootstrapRead: false,
        new ShipLockerSnapshot(
            timestamp,
            "ShipLocker",
            [new ShipLockerItem("Items", "healthpack", "Health Pack", 3)]));

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan amount) => now += amount;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SrvSurvey-companion-timeline-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
