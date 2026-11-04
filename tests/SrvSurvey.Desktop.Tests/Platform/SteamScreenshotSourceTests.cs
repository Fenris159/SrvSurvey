using System.Globalization;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed partial class SteamScreenshotSourceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-steam-shots-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("Steam")]
    [InlineData(".local/share/Steam")]
    [InlineData(".var/app/com.valvesoftware.Steam/.local/share/Steam")]
    public void DetectionUsesTheMostRecentSteamAccountInsteadOfAnotherAccountsScreenshots(string relativeRoot)
    {
        string root = Path.Combine(directory, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
        string expected = CreateAccount(root, "42");
        CreateAccount(root, "99");
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(
            Path.Combine(root, "config", "loginusers.vdf"),
            "\"users\" { \"76561197960265770\" { \"MostRecent\" \"1\" } \"76561197960265827\" { \"MostRecent\" \"0\" } }"
        );

        Assert.Equal(expected, new SteamScreenshotSource([root]).FindFolder(null));
    }

    [Fact]
    public void AmbiguousAccountsAreNotCombinedAndAMissingScreenshotFolderCanBeDetected()
    {
        string root = Path.Combine(directory, "Steam");
        string expected = CreateAccount(root, "42");
        Directory.Delete(expected, true);
        var source = new SteamScreenshotSource([root]);
        Assert.Equal(expected, source.FindFolder(null));

        CreateAccount(root, "99");
        Assert.Null(source.FindFolder(null));
    }

    [Fact]
    public void AProtonJournalSelectsItsSteamInstallationBeforeFallbackRoots()
    {
        string root = Path.Combine(directory, "custom-Steam");
        string expected = CreateAccount(root, "42");
        string journal = Path.Combine(
            root,
            "steamapps",
            "compatdata",
            "359320",
            "pfx",
            "drive_c",
            "users",
            "steamuser",
            "Saved Games",
            "Frontier Developments",
            "Elite Dangerous"
        );
        string fallback = Path.Combine(directory, "other-Steam");
        CreateAccount(fallback, "99");

        Assert.Equal(expected, new SteamScreenshotSource([fallback]).FindFolder(journal));
        Assert.Null(new SteamScreenshotSource([Path.Combine(directory, "not-installed")]).FindFolder(null));
    }

    [Fact]
    public async Task ATruncatedSteamIndexFallsBackToCaptureNamesAndAnotherGamesFolderIsRejected()
    {
        string root = Path.Combine(directory, "Steam");
        string folder = CreateAccount(root, "42");
        string journals = CreateJournal();
        await File.WriteAllBytesAsync(Path.Combine(folder, "19700101050653_1.jpg"), [1]);
        await File.WriteAllTextAsync(IndexPath(folder), "\"screenshots\" { \"359320\" {");
        var source = new SteamScreenshotSource([], TimeZoneInfo.Utc);

        SteamScreenshotBatch fallback = await source.ReadAsync(folder, journals, "F123", CancellationToken.None);
        Assert.Equal("Sol", Assert.Single(fallback.Captures).Entry.Payload.GetProperty("System").GetString());
        Assert.Single(fallback.Warnings);

        string anotherGame = Path.Combine(root, "userdata", "42", "760", "remote", "123", "screenshots");
        Directory.CreateDirectory(anotherGame);
        await File.WriteAllBytesAsync(Path.Combine(anotherGame, "19700101050653_1.jpg"), [1]);
        SteamScreenshotBatch rejected = await source.ReadAsync(anotherGame, journals, "F123", CancellationToken.None);
        Assert.Empty(rejected.Captures);
        Assert.Single(rejected.Warnings);
        Assert.True(File.Exists(Path.Combine(anotherGame, "19700101050653_1.jpg")));
    }

    [Theory]
    [InlineData("Planet", "Earth")]
    [InlineData("Station", "Sylvester Dock")]
    public async Task CaptureTimesUseTheJournalCommanderAndLocationAndExcludeOtherGamesAndUnsafePaths(
        string bodyType,
        string bodyName
    )
    {
        string folder = CreateAccount(Path.Combine(directory, "Steam"), "42");
        string journals = CreateJournal(bodyType, bodyName);
        await File.WriteAllBytesAsync(Path.Combine(folder, "first.jpg"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(folder, "second.jpg"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(folder, "other.jpg"), [1]);
        await File.WriteAllBytesAsync(
            Path.Combine(Directory.GetParent(folder)!.Parent!.Parent!.FullName, "outside.jpg"),
            [1]
        );
        await File.WriteAllTextAsync(
            IndexPath(folder),
            """
            "screenshots" {
              "359320" {
                "0" { "filename" "359320/screenshots/first.jpg" "creation" "18413" }
                "1" { "filename" "359320/screenshots/second.jpg" "creation" "18500" }
                "2" { "filename" "359320/screenshots/other.jpg" "creation" "18550" }
                "3" { "filename" "../outside.jpg" "creation" "18413" }
              }
              "123" { "0" { "filename" "359320/screenshots/other.jpg" "creation" "18413" } }
            }
            """
        );

        SteamScreenshotBatch batch = await new SteamScreenshotSource([], TimeZoneInfo.Utc).ReadAsync(
            folder,
            journals,
            "F123",
            CancellationToken.None
        );

        Assert.Empty(batch.Warnings);
        Assert.Collection(
            batch.Captures,
            first =>
            {
                Assert.Equal("Test", first.CommanderName);
                Assert.Equal("Sol", first.Entry.Payload.GetProperty("System").GetString());
                Assert.Equal(bodyName, first.Entry.Payload.GetProperty("Body").GetString());
                Assert.Equal(
                    DateTimeOffset.Parse("1970-01-01T05:06:53Z", CultureInfo.InvariantCulture),
                    first.Entry.Timestamp
                );
            },
            second =>
            {
                Assert.Equal("Lave", second.Entry.Payload.GetProperty("System").GetString());
                Assert.False(second.Entry.Payload.TryGetProperty("Body", out _));
            }
        );
    }

    [Fact]
    public async Task NewFilesCanBeMatchedBeforeSteamWritesItsIndexButUnmatchedAndMenuCapturesAreRetained()
    {
        string folder = CreateAccount(Path.Combine(directory, "Steam"), "42");
        string journals = CreateJournal();
        foreach (
            string name in new[]
            {
                "19700101050653_1.jpg",
                "19700101051000_1.jpg",
                "19691231000000_1.jpg",
                "unknown.jpg",
            }
        )
        {
            await File.WriteAllBytesAsync(Path.Combine(folder, name), [1]);
        }
        var source = new SteamScreenshotSource([], TimeZoneInfo.Utc);

        SteamScreenshotBatch batch = await source.ReadAsync(folder, journals, "F123", CancellationToken.None);

        Assert.Equal("Sol", Assert.Single(batch.Captures).Entry.Payload.GetProperty("System").GetString());
        Assert.Equal(4, Directory.GetFiles(folder, "*.jpg").Length);
    }

    [Fact]
    public async Task BodyEvidenceOnlyAppliesFromItsRecordedTimeAndIsClearedOnDeparture()
    {
        string folder = CreateAccount(Path.Combine(directory, "Steam"), "42");
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        await File.WriteAllTextAsync(
            Path.Combine(journals, "Journal.1970-01-01T050000.01.log"),
            """
            {"timestamp":"1970-01-01T05:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:00:02Z","event":"Location","StarSystem":"Sol"}
            {"timestamp":"1970-01-01T05:07:00Z","event":"Screenshot","System":"Sol","Body":"Earth"}
            {"timestamp":"1970-01-01T05:08:10Z","event":"Screenshot","System":"Lave","Body":"Wrong system"}
            {"timestamp":"1970-01-01T05:08:30Z","event":"LeaveBody","StarSystem":"Sol","Body":"Earth"}
            """ + "\n"
        );
        foreach (string name in new[] { "before.jpg", "at-body.jpg", "after.jpg" })
        {
            await File.WriteAllBytesAsync(Path.Combine(folder, name), [1]);
        }
        await File.WriteAllTextAsync(
            IndexPath(folder),
            """
            "screenshots" { "359320" {
              "0" { "filename" "359320/screenshots/before.jpg" "creation" "18413" }
              "1" { "filename" "359320/screenshots/at-body.jpg" "creation" "18500" }
              "2" { "filename" "359320/screenshots/after.jpg" "creation" "18520" }
            } }
            """
        );
        SteamScreenshotBatch batch = await new SteamScreenshotSource([], TimeZoneInfo.Utc).ReadAsync(
            folder,
            journals,
            "F123",
            CancellationToken.None
        );
        Assert.Empty(batch.Warnings);
        Assert.Collection(
            batch.Captures,
            before => Assert.False(before.Entry.Payload.TryGetProperty("Body", out _)),
            known => Assert.Equal("Earth", known.Entry.Payload.GetProperty("Body").GetString()),
            departed => Assert.False(departed.Entry.Payload.TryGetProperty("Body", out _))
        );
    }

    private string CreateJournal(string bodyType = "Planet", string bodyName = "Earth")
    {
        string journals = Path.Combine(directory, "journals");
        Directory.CreateDirectory(journals);
        File.WriteAllText(
            Path.Combine(journals, "Journal.1970-01-01T050000.01.log"),
            $$"""
            {"timestamp":"1970-01-01T05:00:00Z","event":"Fileheader","Odyssey":true}
            {"timestamp":"1970-01-01T05:00:01Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:00:02Z","event":"Location","StarSystem":"Sol","Body":"{{bodyName}}","BodyType":"{{bodyType}}"}
            {"timestamp":"1970-01-01T05:07:00Z","event":"FSDJump","StarSystem":"Lave"}
            {"timestamp":"1970-01-01T05:09:00Z","event":"Commander","FID":"F456","Name":"Other"}
            {"timestamp":"1970-01-01T05:09:01Z","event":"Location","StarSystem":"Achenar"}
            {"timestamp":"1970-01-01T05:09:30Z","event":"Commander","FID":"F123","Name":"Test"}
            {"timestamp":"1970-01-01T05:09:31Z","event":"Location","StarSystem":"Alioth"}
            {"timestamp":"1970-01-01T05:10:00Z","event":"Music","MusicTrack":"MainMenu"}
            """ + "\n"
        );
        return journals;
    }

    internal static string CreateAccount(string root, string account)
    {
        string folder = Path.Combine(root, "userdata", account, "760", "remote", "359320", "screenshots");
        Directory.CreateDirectory(folder);
        return folder;
    }

    internal static string IndexPath(string folder) =>
        Path.Combine(Directory.GetParent(folder)!.Parent!.Parent!.FullName, "screenshots.vdf");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
