using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Journeys;

public sealed class JourneyJournalFolderTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-journey-commander-folders-{Guid.NewGuid():N}"
    );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SearchesAllFoldersByCommanderAndGameModeInJournalOrder(bool reverseFolders)
    {
        string steam = CreateFolder("steam");
        string epic = CreateFolder("epic");
        await WriteJournalAsync(
            steam,
            "Journal.2026-07-01T000000.01.log",
            "F123",
            """{"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}"""
        );
        const string latest = "Journal.2026-07-03T000000.01.log";
        await WriteJournalAsync(
            epic,
            latest,
            "F123",
            """
            {"timestamp":"2026-07-03T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-03T00:06:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """
        );
        await WriteJournalAsync(
            steam,
            latest,
            "F999",
            """{"timestamp":"2026-07-03T00:10:00Z","event":"FSDJump","StarSystem":"Foreign","SystemAddress":42,"StarPos":[0,0,0]}"""
        );
        await WriteJournalAsync(
            steam,
            "Journal.2026-07-04T000000.01.log",
            "F123",
            """{"timestamp":"2026-07-04T00:05:00Z","event":"FSDJump","StarSystem":"Legacy","SystemAddress":42,"StarPos":[0,0,0]}""",
            odyssey: false
        );
        string[] folders = reverseFolders ? [epic, steam] : [steam, epic];
        var reader = new JourneyJournalHistoryReader([
            .. folders,
            Path.Combine(steam, "."),
            Path.Combine(directory, "missing"),
        ]);
        JourneyJournalSystemSearchResult current = await reader.FindCurrentLocationAsync("F123", true, 42);
        Assert.Equal(latest, current.Entry!.JournalFileName);
        Assert.Equal("Location", current.Entry.Event.EventName);
        Assert.Equal("Sol", current.Entry.System.Name);
        Assert.Equal("FSDJump", (await reader.FindLatestFsdJumpAsync("F123", true, 42)).Entry!.Event.EventName);
        Assert.Equal("Legacy", (await reader.FindCurrentLocationAsync("F123", false, 42)).Entry!.System.Name);
        JourneyJournalSystemVisitsResult visits = await reader.FindArrivalsAsync("F123", true, 42);
        Assert.Equal([3, 1], visits.Entries.Select(entry => entry.Event.Timestamp!.Value.Day));
        Assert.Equal(2, (await reader.FindArrivalsAsync("F123", true, "Sol")).Entries.Count);
        Assert.Single((await reader.FindArrivalsAsync("F123", true, "Sol", new DateOnly(2026, 7, 3))).Entries);
        Assert.Null((await reader.FindCurrentLocationAsync("F123", true, 99)).Entry);
    }

    [Fact]
    public async Task SameNamedCommanderJournalsUseTheLatestMatchingEventAcrossFolders()
    {
        string first = CreateFolder("a");
        string second = CreateFolder("b");
        const string name = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(
            first,
            name,
            "F123",
            """{"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}"""
        );
        await WriteJournalAsync(
            second,
            name,
            "F123",
            """{"timestamp":"2026-07-01T00:10:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}"""
        );
        var reader = new JourneyJournalHistoryReader([first, second]);
        Assert.Equal(
            10,
            (await reader.FindCurrentLocationAsync("F123", true, 42)).Entry!.Event.Timestamp!.Value.Minute
        );
    }

    [Fact]
    public async Task BeginResumeAndReprocessFollowCommanderAcrossFoldersAndCountCopiedJournalsOnce()
    {
        string steam = CreateFolder("steam");
        string epic = CreateFolder("epic");
        string backup = CreateFolder("backup");
        const string start = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(
            steam,
            start,
            "F999",
            """
            {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Foreign","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}
            """
        );
        await WriteJournalAsync(
            epic,
            start,
            "F123",
            """
            {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}
            {"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}
            """
        );
        File.Copy(Path.Combine(epic, start), Path.Combine(backup, start));
        const string next = "Journal.2026-07-02T000000.01.log";
        await WriteJournalAsync(
            steam,
            next,
            "F123",
            """
            {"timestamp":"2026-07-02T00:05:00Z","event":"FSDJump","StarSystem":"Achenar","SystemAddress":43,"StarPos":[1,0,0]}
            {"timestamp":"2026-07-02T00:06:00Z","event":"Screenshot"}
            """
        );
        File.Copy(Path.Combine(steam, next), Path.Combine(epic, next));
        JourneyService service = CreateService([steam, epic, backup]);
        JourneyJournalSystemSearchResult location = await service.FindCurrentStartAsync("F123", true, 42);
        JourneyServiceResult begun = await service.BeginAsync(
            new("F123", "Drew", true, "Across launchers", "", location.Entry!)
        );
        Assert.Equal(["Sol", "Achenar"], begun.Journey!.VisitedSystems.Select(visit => visit.StarSystem.Name));
        Assert.Equal([2, 1], begun.Journey.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        Assert.Equal(start, begun.Journey.StartingJournal);
        await WriteJournalAsync(
            backup,
            "Journal.2026-07-03T000000.01.log",
            "F123",
            """
            {"timestamp":"2026-07-03T00:05:00Z","event":"FSDJump","StarSystem":"Sirius","SystemAddress":44,"StarPos":[2,0,0]}
            {"timestamp":"2026-07-03T00:06:00Z","event":"Screenshot"}
            """
        );
        JourneyService restarted = CreateService([backup, epic, steam]);
        JourneyServiceResult resumed = await restarted.InitializeActiveAsync("F123", true);
        Assert.Equal(
            ["Sol", "Achenar", "Sirius"],
            resumed.Journey!.VisitedSystems.Select(visit => visit.StarSystem.Name)
        );
        Assert.Equal([2, 1, 1], resumed.Journey.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        JourneyServiceResult reprocessed = await restarted.ReprocessAsync(resumed.Journey, true);
        Assert.Equal(
            ["Sol", "Achenar", "Sirius"],
            reprocessed.Journey!.VisitedSystems.Select(visit => visit.StarSystem.Name)
        );
        Assert.Equal([2, 1, 1], reprocessed.Journey.VisitedSystems.Select(visit => visit.Counts.Screenshots));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplayMergesOverlappingJournalsChronologically(bool startInLaterJournal)
    {
        string first = CreateFolder("first");
        string second = CreateFolder("second");
        const string start = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(
            first,
            start,
            "F123",
            """
            {"timestamp":"2026-07-01T01:00:00Z","event":"Screenshot"}
            {"timestamp":"2026-07-03T01:00:00Z","event":"Screenshot"}
            """
        );
        await WriteJournalAsync(
            second,
            "Journal.2026-07-02T000000.01.log",
            "F123",
            """{"timestamp":"2026-07-02T01:00:00Z","event":"Screenshot"}"""
        );
        var reader = new JourneyJournalHistoryReader([first, second]);
        JourneyJournalReadResult replay = await reader.ReadFromAsync(
            startInLaterJournal ? "Journal.2026-07-02T000000.01.log" : start,
            "F123",
            true
        );
        int[] expected = startInLaterJournal ? [2, 3] : [1, 2, 3];
        Assert.Equal(
            expected,
            replay.Events.Where(entry => entry.EventName == "Screenshot").Select(entry => entry.Timestamp!.Value.Day)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatestLocationUsesEventTimeAcrossEarlierOpenedJournals(bool reverseFolders)
    {
        string steam = CreateFolder("steam");
        string epic = CreateFolder("epic");
        await WriteJournalAsync(
            steam,
            "Journal.2026-07-01T000000.01.log",
            "F123",
            """
            {"timestamp":"2026-07-03T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-03T00:06:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """
        );
        await WriteJournalAsync(
            epic,
            "Journal.2026-07-02T000000.01.log",
            "F123",
            """
            {"timestamp":"2026-07-02T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-02T00:06:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """
        );
        string[] folders = reverseFolders ? [epic, steam] : [steam, epic];
        var reader = new JourneyJournalHistoryReader(folders);
        Assert.Equal(3, (await reader.FindCurrentLocationAsync("F123", true, 42)).Entry!.Event.Timestamp!.Value.Day);
        Assert.Equal(3, (await reader.FindLatestFsdJumpAsync("F123", true, 42)).Entry!.Event.Timestamp!.Value.Day);
    }

    [Fact]
    public async Task JourneyReplayIncludesOverlappingActivityAfterItsStartAndIgnoresEarlierActivity()
    {
        string steam = CreateFolder("steam");
        string epic = CreateFolder("epic");
        const string older = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(
            steam,
            older,
            "F123",
            """
            {"timestamp":"2026-07-01T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-02T00:01:00Z","event":"Screenshot"}
            {"timestamp":"2026-07-02T00:06:00Z","event":"Screenshot"}
            {"timestamp":"2026-07-03T00:05:00Z","event":"FSDJump","StarSystem":"Sirius","SystemAddress":44,"StarPos":[2,0,0]}
            {"timestamp":"2026-07-03T00:06:00Z","event":"Screenshot"}
            """
        );
        await WriteJournalAsync(
            epic,
            "Journal.2026-07-02T000000.01.log",
            "F123",
            """
            {"timestamp":"2026-07-02T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-02T00:07:00Z","event":"Screenshot"}
            """
        );
        JourneyService service = CreateService([steam, epic]);
        JourneyJournalSystemSearchResult location = await service.FindCurrentStartAsync("F123", true, 42);
        JourneyServiceResult begun = await service.BeginAsync(
            new("F123", "Drew", true, "Overlapping journals", "", location.Entry!)
        );
        Assert.Equal(["Sol", "Sirius"], begun.Journey!.VisitedSystems.Select(visit => visit.StarSystem.Name));
        Assert.Equal([2, 1], begun.Journey.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        await File.AppendAllTextAsync(
            Path.Combine(steam, older),
            """{"timestamp":"2026-07-04T00:05:00Z","event":"Screenshot"}""" + "\n"
        );
        JourneyService restarted = CreateService([epic, steam]);
        JourneyServiceResult resumed = await restarted.InitializeActiveAsync("F123", true);
        Assert.Equal([2, 2], resumed.Journey!.VisitedSystems.Select(visit => visit.Counts.Screenshots));
        JourneyServiceResult reprocessed = await restarted.ReprocessAsync(resumed.Journey, true);
        Assert.Equal(["Sol", "Sirius"], reprocessed.Journey!.VisitedSystems.Select(visit => visit.StarSystem.Name));
        Assert.Equal([2, 2], reprocessed.Journey.VisitedSystems.Select(visit => visit.Counts.Screenshots));
    }

    [UnixPermissionsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InaccessibleSourcesDoNotBlockReadableCommanderHistory(bool blockFile)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string active = CreateFolder("active");
        string unrelated = CreateFolder("unrelated");
        const string start = "Journal.2026-07-02T000000.01.log";
        await WriteJournalAsync(
            active,
            start,
            "F123",
            """
            {"timestamp":"2026-07-02T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-02T00:06:00Z","event":"Screenshot"}
            """
        );
        const string foreign = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(
            unrelated,
            foreign,
            "F999",
            """{"timestamp":"2026-07-01T00:05:00Z","event":"Screenshot"}"""
        );
        string blocked = blockFile ? Path.Combine(unrelated, foreign) : unrelated;
        UnixFileMode original = File.GetUnixFileMode(blocked);
        try
        {
            File.SetUnixFileMode(blocked, blockFile ? UnixFileMode.UserWrite : UnixFileMode.UserExecute);
            bool denied = false;
            try
            {
                if (blockFile)
                {
                    using FileStream probe = File.OpenRead(blocked);
                }
                else
                {
                    Directory.GetFiles(blocked);
                }
            }
            catch (UnauthorizedAccessException)
            {
                denied = true;
            }
            Assert.True(denied, "The current account can bypass Unix filesystem permissions.");
            var reader = new JourneyJournalHistoryReader([unrelated, active]);
            JourneyJournalSystemSearchResult current = await reader.FindCurrentLocationAsync("F123", true, 42);
            Assert.Equal("Sol", current.Entry!.System.Name);
            Assert.Contains(current.Errors, error => error.Contains(blocked, StringComparison.Ordinal));
            JourneyJournalSystemVisitsResult visits = await reader.FindArrivalsAsync("F123", true, 42);
            Assert.Single(visits.Entries);
            Assert.Contains(visits.Errors, error => error.Contains(blocked, StringComparison.Ordinal));
            JourneyJournalReadResult replay = await reader.ReadFromAsync(start, "F123", true);
            Assert.Single(replay.Events, entry => entry.EventName == "Screenshot");
            Assert.Contains(replay.Errors, error => error.Contains(blocked, StringComparison.Ordinal));
        }
        finally
        {
            File.SetUnixFileMode(blocked, original);
        }
    }

    [Fact]
    public async Task AnotherCommandersSameNamedFileCannotReplaceAMissingStartingJournal()
    {
        string steam = CreateFolder("steam");
        string epic = CreateFolder("epic");
        const string start = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(steam, start, "F999", """{"timestamp":"2026-07-01T00:05:00Z","event":"Screenshot"}""");
        await WriteJournalAsync(
            epic,
            "Journal.2026-07-02T000000.01.log",
            "F123",
            """{"timestamp":"2026-07-02T00:05:00Z","event":"Screenshot"}"""
        );
        var reader = new JourneyJournalHistoryReader([steam, epic]);
        await Assert.ThrowsAsync<FileNotFoundException>(() => reader.ReadFromAsync(start, "F123", true));
    }

    [Fact]
    public async Task MissingFoldersAndStartingJournalsReportErrorsAndInvalidFolderListsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new JourneyJournalHistoryReader((IReadOnlyList<string>)null!));
        Assert.Throws<ArgumentException>(() => new JourneyJournalHistoryReader(Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => new JourneyJournalHistoryReader([" "]));
        var missing = new JourneyJournalHistoryReader([Path.Combine(directory, "missing")]);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => missing.FindCurrentLocationAsync("F123", true, 42));
        var empty = new JourneyJournalHistoryReader([CreateFolder("empty")]);
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            empty.ReadFromAsync("Journal.2026-07-01T000000.01.log", "F123", true)
        );
    }

    [Fact]
    public async Task NonMonotonicJournalRetainsNewerArrivalsBeforeAnOlderFinalRecord()
    {
        string first = CreateFolder("first");
        string second = CreateFolder("second");
        const string older = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(
            first,
            older,
            "F123",
            """
            {"timestamp":"2026-07-04T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}
            """
        );
        await WriteJournalAsync(
            second,
            "Journal.2026-07-03T000000.01.log",
            "F123",
            """
            {"timestamp":"2026-07-03T00:05:00Z","event":"FSDJump","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """
        );
        var reader = new JourneyJournalHistoryReader([first, second]);
        Assert.Equal(older, (await reader.FindCurrentLocationAsync("F123", true, 42)).Entry!.JournalFileName);
        Assert.Equal(older, (await reader.FindLatestFsdJumpAsync("F123", true, 42)).Entry!.JournalFileName);
        Assert.Single((await reader.FindArrivalsAsync("F123", true, 42, new DateOnly(2026, 7, 4))).Entries);
        JourneyJournalReadResult read = await reader.ReadFromAsync("Journal.2026-07-03T000000.01.log", "F123", true);
        Assert.Equal(2, read.Events.Count(entry => entry.EventName == "FSDJump"));
    }

    [Fact]
    public async Task HeaderOnlyStartingCopyCannotOverwriteAnExistingJourney()
    {
        string folder = CreateFolder("journals");
        const string start = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(
            folder,
            start,
            "F123",
            """
            {"timestamp":"2026-07-01T00:05:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}
            """
        );
        JourneyService service = CreateService([folder]);
        JourneyJournalSystemSearchResult location = await service.FindCurrentStartAsync("F123", true, 42);
        JourneyDocument journey = (
            await service.BeginAsync(new("F123", "Drew", true, "Original", "", location.Entry!))
        ).Journey!;
        string saved = await File.ReadAllTextAsync(journey.FilePath);
        await WriteJournalAsync(folder, start, "F123", "");
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ReprocessAsync(journey, true));
        Assert.Equal(saved, await File.ReadAllTextAsync(journey.FilePath));
        Assert.Equal(journey, service.ActiveJourney);
    }

    [Fact]
    public async Task ResumeBackfillsOlderActivityWithoutDuplicatingItOrLosingManualNotes()
    {
        string original = CreateFolder("original");
        string added = CreateFolder("added");
        const string start = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(
            original,
            start,
            "F123",
            """
            {"timestamp":"2026-07-01T00:05:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            {"timestamp":"2026-07-01T00:10:00Z","event":"Screenshot"}
            """
        );
        JourneyService service = CreateService([original]);
        JourneyJournalSystemSearchResult location = await service.FindCurrentStartAsync("F123", true, 42);
        await service.BeginAsync(new("F123", "Drew", true, "Backfilled", "", location.Entry!));
        Assert.True(await service.IncrementNoteCountAsync(42));
        await WriteJournalAsync(
            added,
            "Journal.2026-07-01T000100.01.log",
            "F123",
            """
            {"timestamp":"2026-07-01T00:08:00Z","event":"Screenshot"}
            """
        );
        for (int attempt = 0; attempt < 2; attempt++)
        {
            JourneyServiceResult resumed = await CreateService([added, original]).InitializeActiveAsync("F123", true);
            JourneySystemVisit visit = Assert.Single(resumed.Journey!.VisitedSystems);
            Assert.Equal(2, visit.Counts.Screenshots);
            Assert.Equal(1, visit.Counts.Notes);
            Assert.Equal(0, resumed.ProcessedEventCount);
        }
        // A removed journal must not undo activity which has already been saved.
        File.Delete(Path.Combine(added, "Journal.2026-07-01T000100.01.log"));
        JourneyServiceResult retained = await CreateService([original, added]).InitializeActiveAsync("F123", true);
        Assert.Equal(2, Assert.Single(retained.Journey!.VisitedSystems).Counts.Screenshots);
    }

    [UnixPermissionsTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReprocessPreservesSavedHistoryUntilAllSourcesCanBeRead(bool blockFolder)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string first = CreateFolder("first");
        string second = CreateFolder("second");
        const string start = "Journal.2026-07-01T000000.01.log";
        await WriteJournalAsync(
            first,
            start,
            "F123",
            """
            {"timestamp":"2026-07-01T00:05:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """
        );
        const string later = "Journal.2026-07-02T000000.01.log";
        await WriteJournalAsync(
            second,
            later,
            "F123",
            """
            {"timestamp":"2026-07-02T00:05:00Z","event":"FSDJump","StarSystem":"Sirius","SystemAddress":44,"StarPos":[2,0,0]}
            {"timestamp":"2026-07-02T00:06:00Z","event":"Screenshot"}
            """
        );
        JourneyService service = CreateService([first, second]);
        JourneyJournalSystemSearchResult location = await service.FindCurrentStartAsync("F123", true, 42);
        JourneyDocument journey = (
            await service.BeginAsync(new("F123", "Drew", true, "Saved", "", location.Entry!))
        ).Journey!;
        string saved = await File.ReadAllTextAsync(journey.FilePath);
        string blocked = blockFolder ? second : Path.Combine(second, later);
        UnixFileMode permissions = File.GetUnixFileMode(blocked);
        try
        {
            File.SetUnixFileMode(blocked, blockFolder ? UnixFileMode.UserExecute : UnixFileMode.UserWrite);
            JourneyServiceResult result = await service.ReprocessAsync(journey, true);
            Assert.NotEmpty(result.Errors);
            Assert.Equal(journey, result.Journey);
            Assert.Equal(journey, service.ActiveJourney);
            Assert.Equal(0, result.ProcessedEventCount);
            Assert.Equal(saved, await File.ReadAllTextAsync(journey.FilePath));
            JourneyServiceResult resumed = await CreateService([first, second]).InitializeActiveAsync("F123", true);
            Assert.Equal(2, resumed.Journey!.VisitedSystems.Count);
        }
        finally
        {
            File.SetUnixFileMode(blocked, permissions);
        }
        await File.AppendAllTextAsync(
            Path.Combine(second, later),
            "malformed\n" + """{"timestamp":"2026-07-02T00:07:00Z","event":"Screenshot"}""" + "\n"
        );
        JourneyServiceResult repaired = await service.ReprocessAsync(journey, true);
        Assert.Single(repaired.Errors);
        Assert.Equal(2, repaired.Journey!.VisitedSystems[1].Counts.Screenshots);
    }

    [Fact]
    public async Task ShiftedCopiesCountIdenticalRepeatedEventsOncePerOccurrence()
    {
        string first = CreateFolder("first");
        string second = CreateFolder("second");
        const string journal = "Journal.2026-07-01T000000.01.log";
        const string screenshots = """
            {"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}
            {"timestamp":"2026-07-01T00:06:00Z","event":"Screenshot"}
            """;
        await WriteJournalAsync(first, journal, "F123", screenshots);
        await WriteJournalAsync(
            second,
            journal,
            "F123",
            """{"timestamp":"2026-07-01T00:05:00Z","event":"Music"}""" + "\n" + screenshots
        );
        var reader = new JourneyJournalHistoryReader([first, second]);
        JourneyJournalReadResult read = await reader.ReadFromAsync(journal, "F123", true);
        Assert.Equal(2, read.Events.Count(entry => entry.EventName == "Screenshot"));
    }

    [Fact]
    public async Task ResumeDoesNotReducePreviouslySavedActivityWhenJournalsAreIncomplete()
    {
        string folder = CreateFolder("journals");
        await WriteJournalAsync(
            folder,
            "Journal.2026-07-01T000000.01.log",
            "F123",
            """
            {"timestamp":"2026-07-01T00:05:00Z","event":"Location","StarSystem":"Sol","SystemAddress":42,"StarPos":[0,0,0]}
            """
        );
        JourneyService service = CreateService([folder]);
        JourneyJournalSystemSearchResult location = await service.FindCurrentStartAsync("F123", true, 42);
        JourneyDocument journey = (
            await service.BeginAsync(new("F123", "Drew", true, "Retained", "", location.Entry!))
        ).Journey!;
        JourneySystemVisit original = Assert.Single(journey.VisitedSystems);
        JourneyCounts[] counts =
        [
            JourneyCounts.Empty with
            {
                BodyScans = 1,
            },
            JourneyCounts.Empty with
            {
                DetailedSurfaceScans = 1,
            },
            JourneyCounts.Empty with
            {
                NewCodexEntries = 1,
            },
            JourneyCounts.Empty with
            {
                Organisms = 1,
            },
            JourneyCounts.Empty with
            {
                Touchdowns = 1,
            },
            JourneyCounts.Empty with
            {
                BodyCount = 1,
            },
            JourneyCounts.Empty with
            {
                Screenshots = 1,
            },
            JourneyCounts.Empty with
            {
                ExobiologyRewards = 1,
            },
            JourneyCounts.Empty with
            {
                ExplorationRewards = 1,
            },
            JourneyCounts.Empty with
            {
                Stars = 1,
            },
        ];
        foreach (JourneyCounts savedCounts in counts)
        {
            await service.SaveAsync(journey with { VisitedSystems = [original with { Counts = savedCounts }] });
            JourneyServiceResult resumed = await CreateService([folder]).InitializeActiveAsync("F123", true);
            Assert.Equal(savedCounts, Assert.Single(resumed.Journey!.VisitedSystems).Counts);
        }
        JourneySystemVisit[] detailedVisits =
        [
            original with
            {
                LandedOn = new Dictionary<string, int> { ["Sol A 1"] = 1 },
            },
            original with
            {
                SubCategories = new Dictionary<string, int> { ["Organic"] = 1 },
            },
            original with
            {
                SurfaceSignals = new Dictionary<string, int> { ["Biological"] = 1 },
            },
            original with
            {
                FssSignals = new Dictionary<string, int> { ["Signal"] = 1 },
            },
            original with
            {
                CodexScanned = new HashSet<long> { 1 },
            },
            original with
            {
                BodiesScanned = new HashSet<int> { 1 },
            },
            original with
            {
                CodexNew = new HashSet<string> { "Organic" },
            },
        ];
        foreach (JourneySystemVisit visit in detailedVisits)
        {
            JourneyDocument saved = await service.SaveAsync(journey with { VisitedSystems = [visit] });
            string serialized = await File.ReadAllTextAsync(saved.FilePath);
            await CreateService([folder]).InitializeActiveAsync("F123", true);
            Assert.Equal(serialized, await File.ReadAllTextAsync(saved.FilePath));
        }
        await service.SaveAsync(
            journey with
            {
                VisitedSystems = [original, original with { Arrived = original.Arrived.AddMinutes(1) }],
            }
        );
        Assert.Equal(
            2,
            (await CreateService([folder]).InitializeActiveAsync("F123", true)).Journey!.VisitedSystems.Count
        );
        await service.SaveAsync(journey with { Watermark = journey.Watermark.AddMinutes(1) });
        Assert.Equal(
            journey.Watermark.AddMinutes(1),
            (await CreateService([folder]).InitializeActiveAsync("F123", true)).Journey!.Watermark
        );
    }

    private string CreateFolder(string name)
    {
        string path = Path.Combine(directory, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class UnixPermissionsTheoryAttribute : TheoryAttribute
    {
        public UnixPermissionsTheoryAttribute()
        {
            if (OperatingSystem.IsWindows() || string.Equals(Environment.UserName, "root", StringComparison.Ordinal))
            {
                Skip = "This test requires an unprivileged Unix account.";
            }
        }
    }

    private JourneyService CreateService(IReadOnlyList<string> folders) =>
        new(
            new JourneyStore(Path.Combine(directory, "data")),
            new JourneyJournalHistoryReader(folders),
            new CommanderProfileStore(Path.Combine(directory, "data")),
            new ExobiologyReferenceCatalog([])
        );

    private static Task WriteJournalAsync(
        string folder,
        string name,
        string frontierId,
        string events,
        bool odyssey = true
    ) =>
        File.WriteAllTextAsync(
            Path.Combine(folder, name),
            $$"""
            {"timestamp":"2026-07-01T00:00:00Z","event":"Fileheader","Odyssey":{{odyssey.ToString().ToLowerInvariant()}}}
            {"timestamp":"2026-07-01T00:00:01Z","event":"Commander","Name":"Drew","FID":"{{frontierId}}"}
            """
                + "\n"
                + events
                + "\n"
        );

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
