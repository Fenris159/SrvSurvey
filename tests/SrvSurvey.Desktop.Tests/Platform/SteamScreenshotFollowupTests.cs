using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed partial class SteamScreenshotSourceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedCapturesAcceptSourcesWithOrWithoutATrailingSeparator(bool trailing)
    {
        string folder = CreateAccount(Path.Combine(directory, "Steam"), "42");
        await File.WriteAllBytesAsync(Path.Combine(folder, "indexed.jpg"), [1]);
        await File.WriteAllTextAsync(
            IndexPath(folder),
            "\"screenshots\" { \"359320\" { \"0\" { \"filename\" \"359320/screenshots/indexed.jpg\" \"creation\" \"18413\" } } }"
        );
        SteamScreenshotBatch batch = await new SteamScreenshotSource([], TimeZoneInfo.Utc).ReadAsync(
            trailing ? folder + Path.DirectorySeparatorChar : folder,
            CreateJournal(),
            "F123",
            CancellationToken.None
        );
        Assert.Equal("Sol", Assert.Single(batch.Captures).Entry.Payload.GetProperty("System").GetString());
    }

    [Fact]
    public void ADetectedClientCannotClaimAnExternalLibraryAbsentFromItsOwnershipIndex()
    {
        string root = Path.Combine(directory, "detected-client");
        CreateAccount(root, "42");
        Directory.CreateDirectory(Path.Combine(root, "steamapps"));
        File.WriteAllText(
            Path.Combine(root, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"unrelated\" } }"
        );
        string journals = Path.Combine(
            directory,
            "unknown-owner",
            "steamapps",
            "compatdata",
            "359320",
            "pfx",
            "journals"
        );
        Directory.CreateDirectory(journals);
        Assert.Null(new SteamScreenshotSource([root]).FindFolder(journals));
    }

    [Theory]
    [InlineData("Station", "Old carrier", "New carrier")]
    [InlineData("Star", "Sol", "New carrier")]
    [InlineData("Planet", "Earth", "Earth")]
    public async Task FreshDockingReplacesOldNamedLocationsButRetainsValidPlanetContext(
        string type,
        string body,
        string expected
    )
    {
        string folder = CreateAccount(Path.Combine(directory, "Steam"), "42");
        string journals = CreateJournal(type, body);
        string journal = Path.Combine(journals, "Journal.1970-01-01T050000.01.log");
        string text = await File.ReadAllTextAsync(journal);
        text = text[..text.IndexOf("{\"timestamp\":\"1970-01-01T05:07:00Z\"", StringComparison.Ordinal)];
        await File.WriteAllTextAsync(
            journal,
            text
                + """
                {"timestamp":"1970-01-01T05:01:00Z","event":"Undocked"}
                {"timestamp":"1970-01-01T05:02:00Z","event":"Docked","StarSystem":"Sol","StationName":"New carrier"}
                {"timestamp":"1970-01-01T05:07:00Z","event":"FuelScoop","Total":10}
                """
        );
        await File.WriteAllBytesAsync(Path.Combine(folder, "19700101050653_1.jpg"), [1]);
        SteamScreenshotBatch batch = await new SteamScreenshotSource([], TimeZoneInfo.Utc).ReadAsync(
            folder,
            journals,
            "F123",
            CancellationToken.None
        );
        Assert.Equal(expected, Assert.Single(batch.Captures).Entry.Payload.GetProperty("Body").GetString());
    }

    [Fact]
    public async Task AMalformedIndexPathCannotDiscardLaterValidCaptureMetadata()
    {
        string folder = CreateAccount(Path.Combine(directory, "Steam"), "42");
        await File.WriteAllBytesAsync(Path.Combine(folder, "indexed.jpg"), [1]);
        await File.WriteAllTextAsync(
            IndexPath(folder),
            "\"screenshots\" { \"359320\" { \"0\" { \"filename\" \"bad\0.jpg\" \"creation\" \"18413\" } \"1\" { \"filename\" \"359320/screenshots/indexed.jpg\" \"creation\" \"18413\" } } }"
        );
        SteamScreenshotBatch batch = await new SteamScreenshotSource([], TimeZoneInfo.Utc).ReadAsync(
            folder,
            CreateJournal(),
            "F123",
            CancellationToken.None
        );
        Assert.Equal("Sol", Assert.Single(batch.Captures).Entry.Payload.GetProperty("System").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalCapturesNeedCoverageWhileTheBoundLiveSessionCanRemainQuiet(bool boundLive)
    {
        string folder = CreateAccount(Path.Combine(directory, "Steam"), "42");
        string journals = Path.Combine(directory, "coverage-journals");
        Directory.CreateDirectory(journals);
        string journal = Path.Combine(journals, "Journal.1970-01-01T050000.01.log");
        await File.WriteAllTextAsync(
            journal,
            """
            {"timestamp":"1970-01-01T05:00:00Z","event":"Fileheader","part":1}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:00:02Z","event":"Location","StarSystem":"Sol","Body":"Earth","BodyType":"Planet"}
            """
        );
        await File.WriteAllBytesAsync(Path.Combine(folder, "19700101060653_1.jpg"), [1]);
        var source = new SteamScreenshotSource([], TimeZoneInfo.Utc);
        var through = new DateTimeOffset(1970, 1, 1, 7, 0, 0, TimeSpan.Zero);
        SteamScreenshotLiveSession? live = boundLive ? new(journal, "F123", through) : null;
        SteamScreenshotBatch batch = await source.ReadAsync(folder, journals, "F123", CancellationToken.None, live);
        if (boundLive)
        {
            Assert.Equal("Earth", Assert.Single(batch.Captures).Entry.Payload.GetProperty("Body").GetString());
            Assert.Empty(
                (
                    await source.ReadAsync(
                        folder,
                        journals,
                        "F123",
                        CancellationToken.None,
                        new(journal, "F456", through)
                    )
                ).Captures
            );
            Assert.Empty(
                (
                    await source.ReadAsync(
                        folder,
                        journals,
                        "F123",
                        CancellationToken.None,
                        new(Path.Combine(journals, "another.log"), "F123", through)
                    )
                ).Captures
            );
        }
        else
        {
            Assert.Empty(batch.Captures);
        }
        await File.AppendAllTextAsync(
            journal,
            "\n{\"timestamp\":\"1970-01-01T06:07:00Z\",\"event\":\"FuelScoop\",\"Total\":10}\n"
        );
        Assert.Equal(
            "Sol",
            Assert
                .Single((await source.ReadAsync(folder, journals, "F123", CancellationToken.None)).Captures)
                .Entry.Payload.GetProperty("System")
                .GetString()
        );
        await File.AppendAllTextAsync(journal, "{\"timestamp\":\"1970-01-01T06:08:00Z\",\"event\":\"Shutdown\"}\n");
        await File.WriteAllBytesAsync(Path.Combine(folder, "19700101060900_1.jpg"), [1]);
        Assert.Single((await source.ReadAsync(folder, journals, "F123", CancellationToken.None, live)).Captures);
    }
}
