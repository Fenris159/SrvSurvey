using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed partial class SteamScreenshotSourceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("\"users\" \"\"")]
    [InlineData("\"users\" {}")]
    [InlineData("\"users\" { \"76561197960265770\" { \"MostRecent\" \"0\" } }")]
    public void ExistingUnusableLoginMetadataNeverSelectsAnOlderAccount(string metadata)
    {
        string root = Path.Combine(directory, "Steam");
        CreateAccount(root, "42");
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "loginusers.vdf"), metadata);

        Assert.Null(new SteamScreenshotSource([root]).FindFolder(null));
    }

    [Theory]
    [InlineData("flatpak")]
    [InlineData("ambiguous")]
    [InlineData("unlisted")]
    [InlineData("invalid-index")]
    public void ExternalLibrariesMustIdentifyTheirOwningSteamClient(string ownership)
    {
        string native = Path.Combine(directory, "native");
        string flatpak = Path.Combine(directory, "flatpak");
        CreateAccount(native, "99");
        string expected = CreateAccount(flatpak, "42");
        string library = Path.Combine(directory, "external-library");
        string journal = Path.Combine(library, "steamapps", "compatdata", "359320", "pfx", "journals");
        Directory.CreateDirectory(journal);
        foreach (string root in new[] { native, flatpak })
        {
            Directory.CreateDirectory(Path.Combine(root, "steamapps"));
            bool owns =
                ownership == "ambiguous" || (ownership == "flatpak" || ownership == "invalid-index") && root == flatpak;
            string path = owns ? library : Path.Combine(directory, "unrelated");
            string escaped = path.Replace("\\", "\\\\", StringComparison.Ordinal);
            File.WriteAllText(
                Path.Combine(root, "steamapps", "libraryfolders.vdf"),
                ownership == "invalid-index" && root == native
                    ? string.Empty
                    : "\"libraryfolders\" { \"1\" { \"path\" \"" + escaped + "\" } }"
            );
        }

        Assert.Equal(
            ownership == "flatpak" ? expected : null,
            new SteamScreenshotSource([native, flatpak]).FindFolder(journal)
        );
    }

    [Theory]
    [InlineData("{")]
    [InlineData("}")]
    public async Task QuotedBracesRemainLegalAccountAndScreenshotMetadata(string caption)
    {
        string root = Path.Combine(directory, "Steam");
        string folder = CreateAccount(root, "42");
        Directory.CreateDirectory(Path.Combine(root, "config"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "config", "loginusers.vdf"),
            "\"users\" { \"76561197960265770\" { \"MostRecent\" \"1\" \"PersonaName\" \"" + caption + "\" } }"
        );
        Assert.Equal(folder, new SteamScreenshotSource([root]).FindFolder(null));
        await File.WriteAllBytesAsync(Path.Combine(folder, "indexed.jpg"), [1]);
        await File.WriteAllTextAsync(
            IndexPath(folder),
            "\"screenshots\" { \"359320\" { \"0\" { \"caption\" \""
                + caption
                + "\" \"filename\" \"359320/screenshots/indexed.jpg\" \"creation\" \"18413\" } } }"
        );
        SteamScreenshotBatch batch = await new SteamScreenshotSource([], TimeZoneInfo.Utc).ReadAsync(
            folder,
            CreateJournal(),
            "F123",
            CancellationToken.None
        );
        Assert.Empty(batch.Warnings);
        Assert.Equal("Sol", Assert.Single(batch.Captures).Entry.Payload.GetProperty("System").GetString());
    }

    [Fact]
    public async Task LinkedScreenshotStorageKeepsTheLogicalSteamIndexAndCanonicalCapturePath()
    {
        string folder = CreateAccount(Path.Combine(directory, "Steam"), "42");
        string photos = Path.Combine(directory, "photo-storage");
        Directory.CreateDirectory(photos);
        Directory.Delete(folder);
        try
        {
            Directory.CreateSymbolicLink(folder, photos);
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            Assert.Skip("Creating directory aliases requires Windows developer mode or symbolic-link privilege.");
        }
        string capture = Path.Combine(photos, "indexed.jpg");
        await File.WriteAllBytesAsync(capture, [1]);
        await File.WriteAllTextAsync(
            IndexPath(folder),
            "\"screenshots\" { \"359320\" { \"0\" { \"filename\" \"359320/screenshots/indexed.jpg\" \"creation\" \"18413\" } } }"
        );

        SteamScreenshotBatch batch = await new SteamScreenshotSource([], TimeZoneInfo.Utc).ReadAsync(
            folder,
            CreateJournal(),
            "F123",
            CancellationToken.None
        );

        Assert.Empty(batch.Warnings);
        Assert.Equal(capture, Assert.Single(batch.Captures).Entry.Payload.GetProperty("Filename").GetString());
    }

    [Fact]
    public async Task ContinuationCapturesInheritOwnershipAndRecomputeItWhenEarlierJournalDataChanges()
    {
        string folder = CreateAccount(Path.Combine(directory, "Steam"), "42");
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        string first = Path.Combine(journals, "Journal.1970-01-01T050000.01.log");
        const string initial = """
            {"timestamp":"1970-01-01T05:00:00Z","event":"Fileheader","part":1}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:00:02Z","event":"Location","StarSystem":"Sol","Body":"Earth","BodyType":"Planet"}
            {"timestamp":"1970-01-01T05:06:00Z","event":"Continued","Part":2}
            """;
        await File.WriteAllTextAsync(first, initial);
        File.SetLastWriteTimeUtc(first, new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc));
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.1970-01-01T050000.02.log"),
            """
            {"timestamp":"1970-01-01T05:06:01Z","event":"Fileheader","part":2}
            {"timestamp":"1970-01-01T05:06:10Z","event":"FSDJump","StarSystem":"Lave"}
            """
        );
        await File.WriteAllBytesAsync(Path.Combine(folder, "19700101050653_1.jpg"), [1]);
        var source = new SteamScreenshotSource([], TimeZoneInfo.Utc);
        SteamScreenshotBatch before = await source.ReadAsync(folder, journals, "F123", CancellationToken.None);
        Assert.Equal("Lave", Assert.Single(before.Captures).Entry.Payload.GetProperty("System").GetString());
        await File.WriteAllTextAsync(first, initial.Replace("F123", "F456", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(first, new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc));

        Assert.Empty((await source.ReadAsync(folder, journals, "F123", CancellationToken.None)).Captures);
        Assert.Equal(
            "Test",
            Assert
                .Single((await source.ReadAsync(folder, journals, "F456", CancellationToken.None)).Captures)
                .CommanderName
        );
    }

    [Theory]
    [InlineData("Journal.1970-01-01T050000.03.log")]
    [InlineData("Journal.1970-01-01T050000.02.log")]
    [InlineData("Journal.1970-01-01T050100.01.log")]
    public async Task UnrelatedOrMissingJournalPartsCannotInheritAnotherSessionsOwnership(string nextFile)
    {
        string folder = CreateAccount(Path.Combine(directory, "Steam"), "42");
        string journals = CreateJournal();
        string first = Path.Combine(journals, "Journal.1970-01-01T050000.01.log");
        string opening = await File.ReadAllTextAsync(first);
        await File.WriteAllTextAsync(
            first,
            opening[..opening.IndexOf("{\"timestamp\":\"1970-01-01T05:07:00Z\"", StringComparison.Ordinal)]
        );

        await File.WriteAllTextAsync(
            Path.Combine(journals, nextFile),
            """
            {"timestamp":"1970-01-01T05:06:01Z","event":"Fileheader","part":3}
            {"timestamp":"1970-01-01T05:06:10Z","event":"FSDJump","StarSystem":"Lave"}
            """
        );
        await File.WriteAllBytesAsync(Path.Combine(folder, "19700101050653_1.jpg"), [1]);
        Assert.Empty(
            (
                await new SteamScreenshotSource([], TimeZoneInfo.Utc).ReadAsync(
                    folder,
                    journals,
                    "F123",
                    CancellationToken.None
                )
            ).Captures
        );
    }

    [Fact]
    public async Task DockingAfterLeavingAPlanetLabelsTheStationInsteadOfTheDepartedBody()
    {
        string folder = CreateAccount(Path.Combine(directory, "Steam"), "42");
        string journals = CreateJournal();
        string path = Path.Combine(journals, "Journal.1970-01-01T050000.01.log");
        string history = await File.ReadAllTextAsync(path);
        history = history[..history.IndexOf("{\"timestamp\":\"1970-01-01T05:07:00Z\"", StringComparison.Ordinal)];
        await File.WriteAllTextAsync(
            path,
            history
                + """
                {"timestamp":"1970-01-01T05:06:00Z","event":"LeaveBody","Body":"Earth"}
                {"timestamp":"1970-01-01T05:06:10Z","event":"Docked","StarSystem":"Sol","StationName":"Test Dock"}
                """
        );
        await File.WriteAllBytesAsync(Path.Combine(folder, "19700101050653_1.jpg"), [1]);
        SteamScreenshotBatch batch = await new SteamScreenshotSource([], TimeZoneInfo.Utc).ReadAsync(
            folder,
            journals,
            "F123",
            CancellationToken.None
        );
        Assert.Equal("Test Dock", Assert.Single(batch.Captures).Entry.Payload.GetProperty("Body").GetString());
    }
}
