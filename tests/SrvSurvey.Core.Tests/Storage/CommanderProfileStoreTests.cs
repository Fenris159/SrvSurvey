using System.Text.Json.Nodes;
using SrvSurvey.Core.Combat;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Storage;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class CommanderProfileStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-commander-profile-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadReadsLegacyExplorationFields()
    {
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "F123-live.json"),
            """
            {
              "fid": "F123",
              "commander": "Drew",
              "isOdyssey": true,
              "explRewards": 123456,
              "distanceTravelled": 42.5,
              "countJumps": 3,
              "countScans": 4,
              "countDSS": 5,
              "countLanded": 6,
              "rccApiKey": "legacy-secret",
              "inaraApiKey": "inara-secret",
              "edsmCommanderName": "EDSM Drew",
              "edsmApiKey": "edsm-secret",
              "futureSetting": { "enabled": true }
            }
            """);
        var store = new CommanderProfileStore(temporaryDirectory);

        var result = await store.LoadAsync("F123", isOdyssey: true);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Exists);
        Assert.NotNull(result.Data);
        Assert.Equal("Drew", result.Data.CommanderName);
        Assert.Equal("legacy-secret", result.Data.RavenColonialApiKey);
        Assert.Equal("inara-secret", result.Data.InaraApiKey);
        Assert.Equal("EDSM Drew", result.Data.EdsmCommanderName);
        Assert.Equal("edsm-secret", result.Data.EdsmApiKey);
        Assert.Equal(
            new ExplorationSnapshot(123456, 42.5, 3, 4, 5, 6),
            result.Data.Exploration);
    }

    [Fact]
    public async Task SavePreservesUnknownAndConcurrentFields()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            "{\"fid\":\"F123\",\"commander\":\"Drew\",\"futureSetting\":{\"enabled\":true},\"activeJourney\":\"Before\"}");
        var store = new CommanderProfileStore(temporaryDirectory);

        await File.WriteAllTextAsync(
            path,
            "{\"fid\":\"F123\",\"commander\":\"Drew\",\"futureSetting\":{\"enabled\":true},\"activeJourney\":\"Changed elsewhere\"}");
        await store.SaveExplorationAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            new ExplorationSnapshot(9000, 12.25, 1, 2, 3, 4));

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["futureSetting"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("Changed elsewhere", root["activeJourney"]!.GetValue<string>());
        Assert.Equal(9000, root["explRewards"]!.GetValue<long>());
        Assert.Equal(12.25, root["distanceTravelled"]!.GetValue<double>());
        Assert.Equal(4, root["countLanded"]!.GetValue<int>());
    }

    [Fact]
    public async Task SaveRefusesToOverwriteMalformedProfile()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        const string malformed = "{\"fid\":\"F123\",";
        await File.WriteAllTextAsync(path, malformed);
        var store = new CommanderProfileStore(temporaryDirectory);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveExplorationAsync(
                "F123",
                "Drew",
                isOdyssey: true,
                ExplorationSnapshot.Empty));

        Assert.Contains("was not overwritten", exception.Message);
        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SaveCreatesCorrectLiveAndLegacyNames()
    {
        var store = new CommanderProfileStore(temporaryDirectory);

        await store.SaveExplorationAsync(
            "F123",
            "Drew",
            isOdyssey: false,
            ExplorationSnapshot.Empty);

        Assert.True(File.Exists(Path.Combine(temporaryDirectory, "F123-legacy.json")));
        Assert.False(File.Exists(Path.Combine(temporaryDirectory, "F123-live.json")));
    }

    [Fact]
    public async Task LoadsAndSavesLegacyMassacreMissionState()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "fid": "F123",
              "trackMassacres": [
                {
                  "missionId": 879230525,
                  "missionGiver": "Raven Colonial Corporation",
                  "targetFaction": "Grabru Crimson Family",
                  "expires": "2026-07-26T08:06:52+00:00",
                  "killCount": 7,
                  "remaining": 4
                }
              ],
              "futureSetting": 42
            }
            """);
        var store = new CommanderProfileStore(temporaryDirectory);

        var loaded = await store.LoadAsync("F123", isOdyssey: true);

        var mission = Assert.Single(loaded.Data!.Combat.MassacreMissions);
        Assert.Equal(879230525, mission.MissionId);
        Assert.Equal("Raven Colonial Corporation", mission.MissionGiver);
        Assert.Equal("Grabru Crimson Family", mission.TargetFaction);
        Assert.Equal(7, mission.KillCount);
        Assert.Equal(4, mission.Remaining);

        await store.SaveCombatAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            new CombatSnapshot(
            [
                mission with { Remaining = 3 },
            ]));

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(
            3,
            root["trackMassacres"]![0]!["remaining"]!.GetValue<int>());
        Assert.Equal(42, root["futureSetting"]!.GetValue<int>());
    }

    [Fact]
    public async Task SavingEmptyCombatStateClearsLegacyMissionList()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            """{"fid":"F123","trackMassacres":[{"missionId":1}]}""");
        var store = new CommanderProfileStore(temporaryDirectory);

        await store.SaveCombatAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            CombatSnapshot.Empty);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Null(root["trackMassacres"]);
    }

    [Fact]
    public async Task SavesAndClearsCommanderScopedRavenApiKeyLosslessly()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            "{\"fid\":\"F123\",\"futureSetting\":42}");
        var store = new CommanderProfileStore(temporaryDirectory);

        await store.SaveRavenColonialApiKeyAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            "  secret-key  ");

        var saved = await store.LoadAsync("F123", isOdyssey: true);
        Assert.Equal("secret-key", saved.Data?.RavenColonialApiKey);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(42, root["futureSetting"]!.GetValue<int>());

        await store.SaveRavenColonialApiKeyAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            "  ");

        var cleared = await store.LoadAsync("F123", isOdyssey: true);
        Assert.Null(cleared.Data?.RavenColonialApiKey);
        root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.False(root.ContainsKey("rccApiKey"));
        Assert.Equal(42, root["futureSetting"]!.GetValue<int>());
    }

    [Fact]
    public async Task SavesAndClearsCommanderScopedInaraApiKeyLosslessly()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            "{\"fid\":\"F123\",\"futureSetting\":42}");
        var store = new CommanderProfileStore(temporaryDirectory);

        await store.SaveInaraApiKeyAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            "  personal-key  ");

        var saved = await store.LoadAsync("F123", isOdyssey: true);
        Assert.Equal("personal-key", saved.Data?.InaraApiKey);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(42, root["futureSetting"]!.GetValue<int>());

        await store.SaveInaraApiKeyAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            "  ");

        var cleared = await store.LoadAsync("F123", isOdyssey: true);
        Assert.Null(cleared.Data?.InaraApiKey);
        root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.False(root.ContainsKey("inaraApiKey"));
        Assert.Equal(42, root["futureSetting"]!.GetValue<int>());
    }

    [Fact]
    public async Task SavesAndClearsCommanderScopedEdsmCredentialsLosslessly()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            "{\"fid\":\"F123\",\"futureSetting\":42}");
        var store = new CommanderProfileStore(temporaryDirectory);

        await store.SaveEdsmCredentialsAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            "  EDSM Drew  ",
            "  personal-key  ");

        var saved = await store.LoadAsync("F123", isOdyssey: true);
        Assert.Equal("EDSM Drew", saved.Data?.EdsmCommanderName);
        Assert.Equal("personal-key", saved.Data?.EdsmApiKey);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(42, root["futureSetting"]!.GetValue<int>());

        await store.SaveEdsmCredentialsAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            null,
            "  ");

        var cleared = await store.LoadAsync("F123", isOdyssey: true);
        Assert.Null(cleared.Data?.EdsmCommanderName);
        Assert.Null(cleared.Data?.EdsmApiKey);
        root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.False(root.ContainsKey("edsmCommanderName"));
        Assert.False(root.ContainsKey("edsmApiKey"));
        Assert.Equal(42, root["futureSetting"]!.GetValue<int>());
    }

    [Fact]
    public async Task LoadsSavesAndClearsActiveJourneyLosslessly()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            "{\"fid\":\"F123\",\"activeJourney\":\"20260701_120000\",\"futureSetting\":42}");
        var store = new CommanderProfileStore(temporaryDirectory);

        var loaded = await store.LoadAsync("F123", isOdyssey: true);

        Assert.Equal("20260701_120000", loaded.Data?.ActiveJourneyFileName);

        await store.SaveActiveJourneyAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            "20260724_123456");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(
            "20260724_123456",
            root["activeJourney"]!.GetValue<string>());
        Assert.Equal(42, root["futureSetting"]!.GetValue<int>());

        await store.SaveActiveJourneyAsync(
            "F123",
            "Drew",
            isOdyssey: true,
            null);
        root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.False(root.ContainsKey("activeJourney"));
        Assert.Equal(42, root["futureSetting"]!.GetValue<int>());
    }

    [Fact]
    public async Task LoadAndSavePreserveLegacyExobiologyFields()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "fid": "F123",
              "lastOrganicScan": "123|7|species",
              "scanOne": {
                "location": { "lat": 12.5, "long": -45.25, "futureCoordinate": 8 },
                "radius": 150,
                "genus": "$Codex_Ent_Aleoids_Genus_Name;",
                "species": "$Codex_Ent_Aleoids_01_Name;",
                "status": "Active",
                "entryId": 2310101,
                "body": "Test A 1",
                "futureSample": true
              },
              "scanTwo": null,
              "organicRewards": 7252500,
              "scannedBioEntryIds": ["123_7_2310101_7252500_False"],
              "countRadicoidaUnica": 2
            }
            """);
        var store = new CommanderProfileStore(temporaryDirectory);

        var loaded = await store.LoadAsync("F123", isOdyssey: true);

        Assert.NotNull(loaded.Data);
        var bio = loaded.Data.Exobiology;
        Assert.Equal("123|7|species", bio.LastOrganicScan);
        Assert.Equal(new SurfaceLocation(12.5, -45.25), bio.ScanOne?.Location);
        Assert.Equal(7_252_500, bio.OrganicRewards);
        Assert.Equal(2, bio.CountRadicoidaUnica);
        Assert.Single(bio.ScannedBioEntryIds);

        await store.SaveExobiologyAsync("F123", "Drew", true, bio);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["scanOne"]!["futureSample"]!.GetValue<bool>());
        Assert.Equal(
            8,
            root["scanOne"]!["location"]!["futureCoordinate"]!.GetValue<int>());
        Assert.Equal(
            "123_7_2310101_7252500_False",
            root["scannedBioEntryIds"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task ExobiologySaveRefusesMalformedProfileOverwrite()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        const string malformed = "{\"fid\":\"F123\",";
        await File.WriteAllTextAsync(path, malformed);
        var store = new CommanderProfileStore(temporaryDirectory);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveExobiologyAsync(
                "F123",
                "Drew",
                true,
                ExobiologySnapshot.Empty));

        Assert.Equal(malformed, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ConcurrentFeatureSavesDoNotLoseEitherUpdate()
    {
        var store = new CommanderProfileStore(temporaryDirectory);
        var bio = new ExobiologySnapshot(
            null,
            null,
            null,
            42,
            ["123_7_2310101_42_False"],
            0);

        await Task.WhenAll(
            store.SaveExplorationAsync(
                "F123",
                "Drew",
                true,
                new ExplorationSnapshot(99, 10, 1, 2, 3, 4)),
            store.SaveExobiologyAsync("F123", "Drew", true, bio));

        var result = await store.LoadAsync("F123", true);
        Assert.NotNull(result.Data);
        Assert.Equal(99, result.Data.Exploration.EstimatedRewards);
        Assert.Equal(42, result.Data.Exobiology.OrganicRewards);
        Assert.Single(result.Data.Exobiology.ScannedBioEntryIds);
    }

    [Fact]
    public async Task LoadAndSavePreserveLegacySphereLimitFields()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "fid": "F123",
              "sphereLimit": {
                "active": true,
                "centerSystemName": "Sol",
                "centerStarPos": [0, 0, 0],
                "radius": 250,
                "futureSphereOption": "preserve"
              }
            }
            """);
        var store = new CommanderProfileStore(temporaryDirectory);

        var loaded = await store.LoadAsync("F123", true);

        Assert.NotNull(loaded.Data);
        Assert.Equal(
            new SphereLimitSnapshot(
                true,
                "Sol",
                new GalacticCoordinate(0, 0, 0),
                250),
            loaded.Data.SphereLimit);

        await store.SaveSphereLimitAsync(
            "F123",
            "Drew",
            true,
            new SphereLimitSnapshot(
                false,
                "Colonia",
                new GalacticCoordinate(-9530.5, -910.28125, 19808.125),
                100));

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var sphere = root["sphereLimit"]!.AsObject();
        Assert.False(sphere["active"]!.GetValue<bool>());
        Assert.Equal("Colonia", sphere["centerSystemName"]!.GetValue<string>());
        Assert.Equal(-9530.5, sphere["centerStarPos"]![0]!.GetValue<double>());
        Assert.Equal("preserve", sphere["futureSphereOption"]!.GetValue<string>());
    }

    [Fact]
    public async Task ConcurrentSphereAndFeatureSavesDoNotLoseUpdates()
    {
        var store = new CommanderProfileStore(temporaryDirectory);

        await Task.WhenAll(
            store.SaveExplorationAsync(
                "F123",
                "Drew",
                true,
                new ExplorationSnapshot(99, 10, 1, 2, 3, 4)),
            store.SaveSphereLimitAsync(
                "F123",
                "Drew",
                true,
                new SphereLimitSnapshot(
                    true,
                    "Sol",
                    new GalacticCoordinate(0, 0, 0),
                    100)));

        var result = await store.LoadAsync("F123", true);
        Assert.NotNull(result.Data);
        Assert.Equal(99, result.Data.Exploration.EstimatedRewards);
        Assert.True(result.Data.SphereLimit.Active);
        Assert.Equal("Sol", result.Data.SphereLimit.CenterSystemName);
    }

    [Fact]
    public async Task LoadAndSavePreserveLegacyBoxelSearchFields()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "fid": "F123",
              "boxelSearch": {
                "active": true,
                "startedOn": "2026-07-01T00:00:00-05:00",
                "boxel": "Praea Euq IL-P c5-19|84456510258",
                "current": "Praea Euq IL-P c5-0",
                "currentCount": 45,
                "lowMassCode": "b",
                "completed": ["Praea Euq IL-P c5-"],
                "autoCopy": true,
                "collapsed": false,
                "skipAlreadyVisited": true,
                "skipKnownToSpansh": true,
                "completeOnFssAllBodies": false,
                "futureBoxelOption": 42
              }
            }
            """);
        var store = new CommanderProfileStore(temporaryDirectory);

        var loaded = await store.LoadAsync("F123", true);

        Assert.NotNull(loaded.Data);
        var boxelSearch = loaded.Data.BoxelSearch;
        Assert.True(boxelSearch.Active);
        Assert.Equal("Praea Euq IL-P c5-19", boxelSearch.TopBoxel?.Name);
        Assert.Equal(84456510258, boxelSearch.TopBoxel?.SystemAddress);
        Assert.Equal(45, boxelSearch.CurrentCount);
        Assert.Equal('b', boxelSearch.LowMassCode);
        Assert.Equal(BoxelCompletionMode.EnterSystem, boxelSearch.CompletionMode);

        await store.SaveBoxelSearchAsync(
            "F123",
            "Drew",
            true,
            new BoxelSearchSnapshot
            {
                Active = false,
                TopBoxel = boxelSearch.TopBoxel,
                StartedOn = boxelSearch.StartedOn,
                Current = boxelSearch.Current,
                CurrentCount = boxelSearch.CurrentCount,
                LowMassCode = boxelSearch.LowMassCode,
                CompletedPrefixes = boxelSearch.CompletedPrefixes,
                AutoCopy = boxelSearch.AutoCopy,
                Collapsed = boxelSearch.Collapsed,
                SkipAlreadyVisited = boxelSearch.SkipAlreadyVisited,
                SkipKnownToSpansh = boxelSearch.SkipKnownToSpansh,
                CompletionMode = BoxelCompletionMode.FssAllBodies,
            });

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var saved = root["boxelSearch"]!.AsObject();
        Assert.False(saved["active"]!.GetValue<bool>());
        Assert.True(saved["completeOnFssAllBodies"]!.GetValue<bool>());
        Assert.Equal(42, saved["futureBoxelOption"]!.GetValue<int>());
        Assert.Equal(
            "Praea Euq IL-P c5-19|84456510258",
            saved["boxel"]!.GetValue<string>());
    }

    [Fact]
    public async Task BoxelSearchRoundTripPreservesSystemAndAreaProgress()
    {
        var store = new CommanderProfileStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        await store.SaveBoxelSearchAsync(
            "F123",
            "Drew",
            true,
            new BoxelSearchSnapshot
            {
                Active = true,
                TopBoxel = top,
                Current = top,
                CurrentCount = 4,
                CompletedSystems =
                [
                    "Praea Euq IL-P c5-0",
                    "Praea Euq IL-P c5-2",
                ],
                EmptySystems = ["Praea Euq IL-P c5-1"],
                DeferredSystems = ["Praea Euq IL-P c5-3"],
                DeferredRanges =
                [
                    new BoxelDeferredRangeSnapshot
                    {
                        Prefix = top.Prefix,
                        StartSystemNumber = 2,
                        SortDescending = true,
                        Exceptions = [3],
                    },
                ],
                ProgressByPrefix = new Dictionary<string, int>
                {
                    [top.Prefix] = 4,
                },
                SortDescending = true,
                SavedSearchFileName = "saved-search.json",
            });

        var loaded = await store.LoadAsync("F123", true);

        Assert.NotNull(loaded.Data);
        Assert.Equal(2, loaded.Data.BoxelSearch.CompletedSystems.Count);
        Assert.Equal(
            ["Praea Euq IL-P c5-1"],
            loaded.Data.BoxelSearch.EmptySystems);
        Assert.Equal(
            ["Praea Euq IL-P c5-3"],
            loaded.Data.BoxelSearch.DeferredSystems);
        var deferredRange = Assert.Single(loaded.Data.BoxelSearch.DeferredRanges);
        Assert.Equal(top.Prefix, deferredRange.Prefix);
        Assert.Equal(2, deferredRange.StartSystemNumber);
        Assert.True(deferredRange.SortDescending);
        Assert.Equal([3], deferredRange.Exceptions);
        Assert.Equal(4, loaded.Data.BoxelSearch.ProgressByPrefix[top.Prefix]);
        Assert.True(loaded.Data.BoxelSearch.SortDescending);
        Assert.Equal(
            "saved-search.json",
            loaded.Data.BoxelSearch.SavedSearchFileName);
    }

    [Fact]
    public async Task LoadAndSavePreserveLegacyRamTahFields()
    {
        Directory.CreateDirectory(temporaryDirectory);
        var path = Path.Combine(temporaryDirectory, "F123-live.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "fid": "F123",
              "commander": "Drew",
              "decodeTheRuinsMissionActive": "Active",
              "decodeTheLogsMissionActive": 2,
              "decodeTheRuins": ["B2", "B1", "B2"],
              "decodeTheLogs": ["#28", "#1"],
              "futureRamTahOption": { "enabled": true }
            }
            """);
        var store = new CommanderProfileStore(temporaryDirectory);

        var loaded = await store.LoadAsync("F123", true);

        Assert.NotNull(loaded.Data);
        Assert.Equal(RamTahMissionStatus.Active, loaded.Data.RamTah.AncientRuinsMissionStatus);
        Assert.Equal(RamTahMissionStatus.Complete, loaded.Data.RamTah.GuardianLogsMissionStatus);
        Assert.Equal(["B1", "B2"], loaded.Data.RamTah.AncientRuinsLogs);
        Assert.Equal(["#1", "#28"], loaded.Data.RamTah.GuardianLogs);

        var updated = new RamTahSnapshot(
            RamTahMissionStatus.Complete,
            RamTahMissionStatus.Active,
            ["T20", "B1"],
            ["#2", "#1"]);
        await store.SaveRamTahAsync("F123", "Drew", true, updated);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.True(root["futureRamTahOption"]!["enabled"]!.GetValue<bool>());
        Assert.Equal("Complete", root["decodeTheRuinsMissionActive"]!.GetValue<string>());
        Assert.Equal("Active", root["decodeTheLogsMissionActive"]!.GetValue<string>());
        Assert.Equal(["B1", "T20"], root["decodeTheRuins"]!.AsArray()
            .Select(value => value!.GetValue<string>()));
        Assert.Equal(["#1", "#2"], root["decodeTheLogs"]!.AsArray()
            .Select(value => value!.GetValue<string>()));

        loaded = await store.LoadAsync("F123", true);
        Assert.NotNull(loaded.Data);
        Assert.Equal(updated.AncientRuinsMissionStatus, loaded.Data.RamTah.AncientRuinsMissionStatus);
        Assert.Equal(updated.GuardianLogsMissionStatus, loaded.Data.RamTah.GuardianLogsMissionStatus);
        Assert.Equal(["B1", "T20"], loaded.Data.RamTah.AncientRuinsLogs);
        Assert.Equal(["#1", "#2"], loaded.Data.RamTah.GuardianLogs);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
