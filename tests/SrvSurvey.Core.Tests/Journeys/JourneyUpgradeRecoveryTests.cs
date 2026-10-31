using System.Text.Json.Nodes;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed class JourneyUpgradeRecoveryTests : IDisposable
{
    private const string LegacyName = "2026-07-01T000500";
    private const string StartingJournal = "Journal.2026-07-01T000000.01.log";
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journey-upgrade-{Guid.NewGuid():N}"
    );
    private string DataDirectory => Path.Combine(directory, "data");
    private string JournalDirectory => Path.Combine(directory, "journals");
    private CommanderProfileStore Profiles => new(DataDirectory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletedActiveJourneyDoesNotPreventStartingAnother(bool initializeFirst)
    {
        await WriteJournalAsync("F123");
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, "deleted-journey");
        JourneyService service = CreateService();
        if (initializeFirst)
        {
            JourneyServiceResult initialized = await service.InitializeActiveAsync("F123", true);
            Assert.Null(initialized.Journey);
            Assert.NotEmpty(initialized.Errors);
            Assert.Null((await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        }
        JourneyJournalSystemSearchResult start = await service.FindCurrentStartAsync("F123", true, 42);
        JourneyServiceResult result = await service.BeginAsync(new("F123", "Commander", true, "New", "", start.Entry!));
        Assert.Equal("New", result.Journey!.Name);
        Assert.Equal(result.Journey.FileName, (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
    }

    [Theory]
    [InlineData("C:\\Users\\Commander\\Journals\\Journal.2026-07-01T000000.01.log")]
    [InlineData("/home/commander/journals/Journal.2026-07-01T000000.01.log")]
    [InlineData(StartingJournal)]
    public async Task LibraryUpgradesLegacyMetadataOnceAndKeepsAnExactBackup(string journalPath)
    {
        string path = await WriteLegacyJourneyAsync(journalPath);
        byte[] original = await File.ReadAllBytesAsync(path);
        JourneyService service = CreateService();
        JourneyDocument journey = Assert.Single((await service.LoadAllAsync("F123")).Journeys);
        Assert.Equal(StartingJournal, journey.StartingJournal);
        Assert.Equal(LegacyName, journey.FileName);
        Assert.Equal(3, Assert.Single(journey.VisitedSystems).Counts.Screenshots);
        Assert.Equal(2, Assert.Single(journey.VisitedSystems).Counts.Notes);
        JsonObject converted = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(1, converted["schemaVersion"]!.GetValue<int>());
        Assert.Equal(StartingJournal, converted["startingJournal"]!.GetValue<string>());
        Assert.Equal("keep-root", converted["futureField"]!.GetValue<string>());
        Assert.Equal("keep-visit", converted["visitedSystems"]![0]!["futureVisit"]!.GetValue<string>());
        Assert.Equal("keep-count", converted["visitedSystems"]![0]!["count"]!["futureCount"]!.GetValue<string>());
        Assert.Equal("keep-star", converted["visitedSystems"]![0]!["starRef"]!["futureStar"]!.GetValue<string>());
        Assert.Equal(original, await File.ReadAllBytesAsync(path + ".pre-upgrade.bak"));
        byte[] firstUpgrade = await File.ReadAllBytesAsync(path);
        await service.LoadAllAsync("F123");
        Assert.Equal(firstUpgrade, await File.ReadAllBytesAsync(path));
        Assert.Equal(original, await File.ReadAllBytesAsync(path + ".pre-upgrade.bak"));
    }

    [Theory]
    [InlineData("missing-folder")]
    [InlineData("missing-file")]
    [InlineData("missing-arrival")]
    public async Task OlderJourneyRemainsActiveAndCanBeConcludedWhenItsJournalHistoryIsUnavailable(string reason)
    {
        string path = await WriteLegacyJourneyAsync(StartingJournal);
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, LegacyName);
        if (reason != "missing-folder")
        {
            Directory.CreateDirectory(JournalDirectory);
        }
        if (reason == "missing-arrival")
        {
            await WriteJournalAsync("F123", "2026-07-01T00:07:00Z");
        }
        JourneyService service = CreateService();
        JourneyServiceResult result = await service.InitializeActiveAsync("F123", true);
        Assert.NotNull(result.Journey);
        Assert.NotEmpty(result.Errors);
        Assert.Equal("Original expedition", result.Journey.Name);
        Assert.Equal(result.Journey, service.ActiveJourney);
        Assert.Equal(3, Assert.Single(result.Journey.VisitedSystems).Counts.Screenshots);
        Assert.Equal(LegacyName, (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        JourneyDocument concluded = Assert.IsType<JourneyDocument>(
            await service.ConcludeActiveAsync(
                "Commander",
                DateTimeOffset.Parse("2026-07-02T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture)
            )
        );
        Assert.False(concluded.IsActive);
        Assert.Null((await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        Assert.True(File.Exists(path));
        Assert.Equal(2, Assert.Single(concluded.VisitedSystems).Counts.Notes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullProfileReferenceWinsOverAServiceSnapshotFromAnEarlierJourney(bool anotherCommander)
    {
        await WriteJournalAsync("F123");
        JourneyService service = CreateService();
        JourneyJournalSystemSearchResult first = await service.FindCurrentStartAsync("F123", true, 42);
        JourneyDocument original = (
            await service.BeginAsync(new("F123", "Commander", true, "First", "", first.Entry!))
        ).Journey!;
        string nextFid = anotherCommander ? "F456" : "F123";
        await Profiles.SaveActiveJourneyAsync(nextFid, "Commander", true, null);
        await WriteJournalAsync(nextFid, "2026-07-02T00:05:00Z");
        JourneyJournalSystemSearchResult next = await service.FindCurrentStartAsync(nextFid, true, 42);
        JourneyServiceResult begun = await service.BeginAsync(new(nextFid, "Commander", true, "Next", "", next.Entry!));
        Assert.Equal(nextFid, begun.Journey!.FrontierId);
        Assert.Equal("Next", begun.Journey.Name);
        Assert.True(File.Exists(original.FilePath));
        if (anotherCommander)
        {
            Assert.Equal(original.FileName, (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        }
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("future")]
    [InlineData("foreign")]
    [InlineData("invalid-reference")]
    public async Task UnavailableFilesArePreservedUntilTheUserClearsTheirActiveSelection(string kind)
    {
        string path = await WriteLegacyJourneyAsync(StartingJournal);
        string original = await File.ReadAllTextAsync(path);
        JsonObject root = JsonNode.Parse(original)!.AsObject();
        if (kind == "future")
        {
            root["schemaVersion"] = 999;
        }
        if (kind == "foreign")
        {
            root["fid"] = "F456";
        }
        original = kind == "malformed" ? "{broken" : root.ToJsonString();
        await File.WriteAllTextAsync(path, original);
        string reference = kind == "invalid-reference" ? "." : LegacyName;
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, reference);
        JourneyService service = CreateService();
        JourneyServiceResult initialized = await service.InitializeActiveAsync("F123", true);
        JourneyRecoveryReference recovery = Assert.IsType<JourneyRecoveryReference>(initialized.Recovery);
        Assert.Equal(reference, recovery.FileName);
        Assert.Null(initialized.Journey);
        Assert.Equal(reference, (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".pre-upgrade.bak"));
        Assert.True(await service.ClearUnavailableJourneyAsync(recovery));
        Assert.Null((await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryDoesNotDetachARepairedFileOrANewerActiveSelection(bool newerSelection)
    {
        string path = await WriteLegacyJourneyAsync(StartingJournal);
        string original = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, "{broken");
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, LegacyName);
        JourneyService service = CreateService();
        JourneyRecoveryReference recovery = (await service.InitializeActiveAsync("F123", true)).Recovery!;
        if (newerSelection)
        {
            await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, "newer-selection");
        }
        else
        {
            await File.WriteAllTextAsync(path, original);
        }
        Assert.False(await service.ClearUnavailableJourneyAsync(recovery));
        Assert.Equal(
            newerSelection ? "newer-selection" : LegacyName,
            (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyReferencesNormalizeWithinTheSelectedGalaxyProfile(bool odyssey)
    {
        await WriteLegacyJourneyAsync(StartingJournal);
        await Profiles.SaveActiveJourneyAsync(
            "F123",
            "Commander",
            odyssey,
            $"C:\\old\\journey\\F123\\{LegacyName}.json"
        );
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", !odyssey, "keep-other-galaxy");
        JourneyServiceResult initialized = await CreateService().InitializeActiveAsync("F123", odyssey);
        Assert.Equal(LegacyName, initialized.Journey!.FileName);
        Assert.Equal(LegacyName, (await Profiles.LoadAsync("F123", odyssey)).Data!.ActiveJourneyFileName);
        Assert.Equal("keep-other-galaxy", (await Profiles.LoadAsync("F123", !odyssey)).Data!.ActiveJourneyFileName);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("concluded")]
    [InlineData("normalized")]
    public async Task RecoveryCannotOverwriteASelectionChangedAfterTheProfileWasRead(string kind)
    {
        string path = await WriteLegacyJourneyAsync(StartingJournal);
        if (kind == "concluded")
        {
            JsonObject root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            root["endTime"] = "2026-07-01T01:00:00Z";
            await File.WriteAllTextAsync(path, root.ToJsonString());
        }
        string expected = kind switch
        {
            "missing" => "missing",
            "normalized" => LegacyName + ".json",
            _ => LegacyName,
        };
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, expected);
        string profilePath = Profiles.GetProfilePath("F123", true);
        var service = new JourneyService(
            new JourneyStore(DataDirectory),
            new JourneyJournalHistoryReader(JournalDirectory),
            Profiles,
            new ExobiologyReferenceCatalog([]),
            message =>
            {
                if (!message.StartsWith("Journeys: resolve", StringComparison.Ordinal))
                {
                    return;
                }
                JsonObject current = JsonNode.Parse(File.ReadAllText(profilePath))!.AsObject();
                current["activeJourney"] = "newer-selection";
                File.WriteAllText(profilePath, current.ToJsonString());
            }
        );
        JourneyServiceResult result = await service.InitializeActiveAsync("F123", true);
        Assert.Null(result.Journey);
        Assert.Contains(result.Errors, error => error.Contains("selection changed", StringComparison.Ordinal));
        Assert.Equal("newer-selection", (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
    }

    [Fact]
    public async Task FailedMetadataUpgradeKeepsSavedHistoryAvailableAndLeavesTheOriginalUntouched()
    {
        string path = await WriteLegacyJourneyAsync(StartingJournal);
        string original = await File.ReadAllTextAsync(path);
        Directory.CreateDirectory(path + ".pre-upgrade.bak");
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, LegacyName);
        JourneyService service = CreateService();
        JourneyCatalogResult catalog = await service.LoadAllAsync("F123");
        Assert.Equal("Original expedition", Assert.Single(catalog.Journeys).Name);
        Assert.Contains(
            catalog.Errors,
            error => error.Contains("upgrade could not be saved", StringComparison.Ordinal)
        );
        JourneyServiceResult active = await service.InitializeActiveAsync("F123", true);
        Assert.NotNull(active.Journey);
        Assert.Contains(active.Errors, error => error.Contains("upgrade could not be saved", StringComparison.Ordinal));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [JourneyJournalFolderTests.UnixPermissionsTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAccessFailureDoesNotMasqueradeAsADeletedJourney(bool blockDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string path = await WriteLegacyJourneyAsync(StartingJournal);
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, LegacyName);
        string blocked = blockDirectory ? Path.GetDirectoryName(path)! : path;
        UnixFileMode original = File.GetUnixFileMode(blocked);
        try
        {
            File.SetUnixFileMode(blocked, blockDirectory ? UnixFileMode.None : UnixFileMode.UserWrite);
            JourneyLoadResult load = await new JourneyStore(DataDirectory).LoadAsync("F123", LegacyName);
            Assert.True(load.Exists);
            Assert.Null(load.Journey);
            Assert.NotNull(load.Error);
            JourneyServiceResult initialized = await CreateService().InitializeActiveAsync("F123", true);
            Assert.NotNull(initialized.Recovery);
            Assert.Equal(LegacyName, (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        }
        finally
        {
            File.SetUnixFileMode(blocked, original);
        }
    }

    [Fact]
    public async Task ClearingAnUnavailableLiveJourneyKeepsTheCurrentLegacyJourneyActive()
    {
        string path = await WriteLegacyJourneyAsync(StartingJournal);
        await File.WriteAllTextAsync(path, "{broken");
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, LegacyName);
        JourneyService service = CreateService();
        JourneyRecoveryReference recovery = Assert.IsType<JourneyRecoveryReference>(
            (await service.InitializeActiveAsync("F123", true)).Recovery
        );
        await WriteJournalAsync("F123", odyssey: false);
        JourneyJournalSystemSearchResult start = await service.FindCurrentStartAsync("F123", false, 42);
        JourneyDocument legacy = (
            await service.BeginAsync(new("F123", "Commander", false, "Legacy", "", start.Entry!))
        ).Journey!;
        Assert.True(await service.ClearUnavailableJourneyAsync(recovery));
        Assert.Equal(legacy, service.ActiveJourney);
        Assert.Equal(legacy.FileName, (await Profiles.LoadAsync("F123", false)).Data!.ActiveJourneyFileName);
        Assert.Null((await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        Assert.False(await service.ClearUnavailableJourneyAsync(recovery));
    }

    [Fact]
    public async Task BeginDiagnosticsIdentifyTheProfileAndMemoryStateWithoutDumpingServiceKeys()
    {
        await WriteJournalAsync("F123");
        var messages = new List<string>();
        JourneyService service = CreateService(messages.Add);
        JourneyJournalSystemSearchResult start = await service.FindCurrentStartAsync("F123", true, 42);
        await service.BeginAsync(new("F123", "Commander", true, "First", "", start.Entry!));
        string profilePath = Profiles.GetProfilePath("F123", true);
        JsonObject profile = JsonNode.Parse(await File.ReadAllTextAsync(profilePath))!.AsObject();
        profile["activeJourney"] = null;
        profile["inaraApiKey"] = "synthetic-private-key";
        await File.WriteAllTextAsync(profilePath, profile.ToJsonString());
        await WriteJournalAsync("F123", "2026-07-02T00:05:00Z");
        start = await service.FindCurrentStartAsync("F123", true, 42);
        await service.BeginAsync(new("F123", "Commander", true, "Second", "", start.Entry!));
        Assert.Contains(
            messages,
            message =>
                message.Contains("savedReferencePresent=False", StringComparison.Ordinal)
                && message.Contains("memoryCommander=F123", StringComparison.Ordinal)
        );
        Assert.Contains(
            messages,
            message =>
                message.Contains(profilePath, StringComparison.Ordinal)
                && message.Contains("mode=live", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(messages, message => message.Contains("synthetic-private-key", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AKeptUnavailableRecordDoesNotOccupyTheFilenameForANewJourney(bool futureVersion)
    {
        await WriteJournalAsync("F123");
        string folder = Path.Combine(DataDirectory, "journey", "F123");
        Directory.CreateDirectory(folder);
        string original = futureVersion
            ? """{"schemaVersion":999,"startTime":"2026-07-01T00:04:59.990Z"}"""
            : "{broken";
        string path = Path.Combine(folder, "20260701_000500.json");
        await File.WriteAllTextAsync(path, original);
        await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, "20260701_000500");
        JourneyService service = CreateService();
        JourneyRecoveryReference recovery = Assert.IsType<JourneyRecoveryReference>(
            (await service.InitializeActiveAsync("F123", true)).Recovery
        );
        Assert.True(await service.ClearUnavailableJourneyAsync(recovery));
        JourneyJournalSystemSearchResult start = await service.FindCurrentStartAsync("F123", true, 42);
        JourneyDocument created = (
            await service.BeginAsync(new("F123", "Commander", true, "Recovered", "", start.Entry!))
        ).Journey!;
        Assert.NotEqual(path, created.FilePath);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        Assert.Equal(created.FileName, (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnreferencedOlderJourneyCanBeResumedAndCompleted(bool concludedReference)
    {
        await WriteLegacyJourneyAsync(StartingJournal);
        if (concludedReference)
        {
            string oldPath = Path.Combine(DataDirectory, "journey", "F123", "completed.json");
            await File.WriteAllTextAsync(
                oldPath,
                """{"fid":"F123","startTime":"2026-06-01T00:00:00Z","endTime":"2026-06-02T00:00:00Z"}"""
            );
            await Profiles.SaveActiveJourneyAsync("F123", "Commander", true, "completed");
        }
        JourneyService service = CreateService();
        JourneyServiceResult result = await service.ResumeAsync("F123", true, LegacyName);
        Assert.Equal("Original expedition", result.Journey!.Name);
        Assert.True(service.IsActiveFor("F123", true));
        Assert.False(service.IsActiveFor("F123", false));
        Assert.False(service.IsActiveFor("F456", true));
        Assert.Equal(LegacyName, (await Profiles.LoadAsync("F123", true)).Data!.ActiveJourneyFileName);
        Assert.Equal(3, Assert.Single(result.Journey.VisitedSystems).Counts.Screenshots);
        Assert.NotNull(await service.ConcludeActiveAsync("Commander", DateTimeOffset.UtcNow));
    }

    private async Task<string> WriteLegacyJourneyAsync(string journalPath)
    {
        string folder = Path.Combine(DataDirectory, "journey", "F123");
        Directory.CreateDirectory(folder);
        JsonObject root = JsonNode
            .Parse(
                """
                {
                  "fid":"F123", "commander":"Commander", "name":"Original expedition", "description":"Keep my history",
                  "startingJournal":"placeholder", "startTime":"2026-07-01T00:04:59.990Z", "watermark":"2026-07-01T00:10:00Z",
                  "futureField":"keep-root", "visitedSystems":[{
                    "starRef":{"name":"Sol","id64":42,"x":0,"y":0,"z":0,"futureStar":"keep-star"},
                    "arrived":"2026-07-01T00:05:00Z", "futureVisit":"keep-visit",
                    "count":{"screenshots":3,"notes":2,"futureCount":"keep-count"}
                  }]
                }
                """
            )!
            .AsObject();
        root["startingJournal"] = journalPath;
        string path = Path.Combine(folder, LegacyName + ".json");
        await File.WriteAllTextAsync(path, root.ToJsonString());
        return path;
    }

    private async Task WriteJournalAsync(string fid, string timestamp = "2026-07-01T00:05:00Z", bool odyssey = true)
    {
        Directory.CreateDirectory(JournalDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(JournalDirectory, StartingJournal),
            $$"""
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":{{odyssey.ToString().ToLowerInvariant()}}}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Commander","FID":"{{fid}}"}
            {"timestamp":"{{timestamp}}","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """ + "\n"
        );
    }

    private JourneyService CreateService(Action<string>? log = null) =>
        new(
            new JourneyStore(DataDirectory),
            new JourneyJournalHistoryReader(JournalDirectory),
            Profiles,
            new ExobiologyReferenceCatalog([]),
            log
        );

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
