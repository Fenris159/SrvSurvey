using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class CommanderProfileViewModelTests
{
    /// <summary>Concurrent personal and squadron requests keep independent destinations and cancellations.</summary>
    [Fact]
    public async Task PersonalAndSquadronJournalJumpsRemainIndependent()
    {
        var at = DateTimeOffset.Parse(
            "2026-10-29T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        FrontierAccountSnapshot baseline = CreateSnapshot(at);
        FrontierAccountSnapshot cached = baseline with
        {
            Carrier = baseline.Carrier! with { CurrentJump = "", DataPoints = [new("fleetcarrier.id", "111")] },
            SquadronCarrier = baseline.Carrier with
            {
                Callsign = "SQD-001",
                CurrentJump = "",
                DataPoints = [new("squadron.squadronCarrier.market.id", "222")],
            },
            CarrierFetchedAt = at,
            SquadronCarrierFetchedAt = at,
        };
        using var model = new CommanderProfileViewModel(new StubAccountService(new(true, cached, at)), () => at);
        await model.OpenAsync();
        model.UpdateJournalCarrierJump(
            "Fenris",
            [
                ParseJournalEvent(
                    """{"timestamp":"2026-10-29T12:01:00Z","event":"CarrierJumpRequest","CarrierID":111,"SystemName":"Personal","DepartureTime":"2026-10-29T12:20:00Z"}"""
                ),
            ]
        );
        model.UpdateJournalCarrierJump(
            "Fenris",
            [
                ParseJournalEvent(
                    """{"timestamp":"2026-10-29T12:02:00Z","event":"CarrierJumpRequest","CarrierID":222,"SystemName":"Squadron","DepartureTime":"2026-10-29T12:20:00Z"}"""
                ),
            ]
        );
        Assert.Equal("Personal", model.CarrierOperations.Single(row => row.Label == "Current jump").Value);
        model.SetCarrierWorkspaceKind(CarrierWorkspaceKind.Squadron);
        Assert.Equal("Squadron", model.CarrierOperations.Single(row => row.Label == "Current jump").Value);
        model.UpdateJournalCarrierJump(
            "Fenris",
            [
                ParseJournalEvent(
                    """{"timestamp":"2026-10-29T12:03:00Z","event":"CarrierJumpCancelled","CarrierID":111}"""
                ),
            ]
        );
        Assert.Equal("Squadron", model.CarrierOperations.Single(row => row.Label == "Current jump").Value);
        model.SetCarrierWorkspaceKind(CarrierWorkspaceKind.Personal);
        Assert.Equal("None plotted", model.CarrierOperations.Single(row => row.Label == "Current jump").Value);
        model.UpdateJournalCarrierJump(
            "Fenris",
            [
                ParseJournalEvent(
                    """{"timestamp":"2026-10-29T12:02:30Z","event":"CarrierJumpRequest","CarrierID":111,"SystemName":"Stale"}"""
                ),
            ]
        );
        Assert.Equal("None plotted", model.CarrierOperations.Single(row => row.Label == "Current jump").Value);
        model.UpdateJournalCarrierJump(
            "Fenris",
            [ParseJournalEvent("""{"timestamp":"2026-10-29T12:04:00Z","event":"CarrierJumpCancelled"}""")]
        );
        model.SetCarrierWorkspaceKind(CarrierWorkspaceKind.Squadron);
        Assert.Equal("Squadron", model.CarrierOperations.Single(row => row.Label == "Current jump").Value);
    }

    /// <summary>The selected carrier uses its own endpoint timestamp, independent of the profile and the other carrier.</summary>
    [Theory]
    [InlineData(false, 1, "Local")]
    [InlineData(false, 3, "Remote")]
    [InlineData(true, 1, "Local")]
    [InlineData(true, 3, "Remote")]
    public async Task CarrierFreshnessUsesOnlyItsOwnEndpoint(bool squadron, int endpointSecond, string expected)
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        FrontierAccountSnapshot original = CreateSnapshot(start.AddSeconds(10));
        FrontierAccountSnapshot cached = original with
        {
            Carrier = original.Carrier! with { CurrentJump = "Remote", DataPoints = [new("fleetcarrier.id", "111")] },
            SquadronCarrier = original.Carrier with
            {
                CurrentJump = "Remote",
                DataPoints = [new("squadron.squadronCarrier.market.id", "222")],
            },
            CarrierFetchedAt = start.AddSeconds(squadron ? 20 : endpointSecond),
            SquadronCarrierFetchedAt = start.AddSeconds(squadron ? endpointSecond : 20),
        };
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new(true, cached, cached.FetchedAt)),
            () => start
        );
        await model.OpenAsync();
        model.SetCarrierWorkspaceKind(squadron ? CarrierWorkspaceKind.Squadron : CarrierWorkspaceKind.Personal);
        long id = squadron ? 222 : 111;
        model.UpdateJournalCarrierJump(
            "Fenris",
            [
                ParseJournalEvent(
                    $$"""{"timestamp":"1970-01-01T00:00:02Z","event":"CarrierJumpRequest","CarrierID":{{id}},"SystemName":"Local"}"""
                ),
            ]
        );
        Assert.Equal(expected, model.CarrierOperations.Single(row => row.Label == "Current jump").Value);
    }

    /// <summary>Late historical records cannot rewind newer local reputation.</summary>
    [Fact]
    public async Task OlderJournalReputationCannotRewindNewerObservation()
    {
        FrontierAccountSnapshot cached = CreateSnapshot(DateTimeOffset.UnixEpoch);
        using var model = new CommanderProfileViewModel(
            new StubAccountService(new(true, cached, cached.FetchedAt)),
            () => cached.FetchedAt
        );
        await model.OpenAsync();
        model.UpdateJournalReputation(
            "Fenris",
            [ParseJournalEvent("""{"timestamp":"2026-10-29T12:02:00Z","event":"Reputation","Federation":85}""")]
        );
        model.UpdateJournalReputation(
            "Fenris",
            [ParseJournalEvent("""{"timestamp":"2026-10-29T12:01:00Z","event":"Reputation","Federation":50}""")]
        );
        Assert.Equal("85%", Assert.Single(model.CommanderReputation).Score);
    }

    /// <summary>A response completed after a journal record is fresh only when its endpoint request also began later.</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public async Task ProfileSectionsUseRequestTimeInsteadOfResponseArrival(int requestedSecond, bool journalWins)
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        FrontierAccountSnapshot cached = CreateSnapshot(start) with
        {
            CarrierFetchedAt = start,
            CommunityGoalsFetchedAt = start,
        };
        var account = new StubAccountService(new(true, cached, start));
        using var model = new CommanderProfileViewModel(account, () => start);
        await model.SetCommanderContextAsync("F123", "Fenris", true);
        await model.OpenAsync();
        var pending = new TaskCompletionSource<FrontierAccountSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        account.PendingRefresh = pending.Task;
        if (requestedSecond > 2)
        {
            model.UpdateJournalState("F123", ReconciliationEvents(2));
        }
        model.RefreshCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => account.RefreshCount == 1, TimeSpan.FromSeconds(3)));
        if (requestedSecond <= 2)
        {
            model.UpdateJournalState("F123", ReconciliationEvents(2));
        }
        DateTimeOffset fetched = start.AddSeconds(requestedSecond);
        pending.SetResult(
            cached with
            {
                FetchedAt = fetched,
                CommanderReputationFetchedAt = fetched,
                CarrierFetchedAt = fetched,
                CommunityGoalsFetchedAt = fetched,
            }
        );
        Assert.True(SpinWait.SpinUntil(() => !model.IsBusy, TimeSpan.FromSeconds(3)));

        Assert.Equal(journalWins ? "85%" : "100%", Assert.Single(model.CommanderReputation).Score);
        Assert.Equal(
            journalWins ? "900 contributed" : "250 contributed",
            Assert.Single(model.CommunityGoals).PlayerContribution
        );
        Assert.Equal(
            journalWins ? "Achenar" : "None plotted",
            model.CarrierOperations.Single(row => row.Label == "Current jump").Value
        );
        Assert.Equal(journalWins ? 8 : 7, model.CurrentShip!.Id);
        Assert.Equal(journalWins ? "Python" : "Surveyor · Cobra Mk III", model.CurrentShipDescription);

        model.UpdateJournalState("F123", ReconciliationEvents(4));
        Assert.Equal("85%", Assert.Single(model.CommanderReputation).Score);
        Assert.Equal("900 contributed", Assert.Single(model.CommunityGoals).PlayerContribution);
        Assert.Equal("Achenar", model.CarrierOperations.Single(row => row.Label == "Current jump").Value);
        Assert.Equal(8, model.CurrentShip.Id);
        Assert.Single(model.Ships, row => row.Status == "CURRENT");
        Assert.Equal("2 ships", model.FleetCount);
    }

    /// <summary>All profile reducers share per-record commander attribution, including a batch spanning a pilot change.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedCommanderBatchCannotApplyThePreviousPilotsProfileEvents(bool includeNewPilotEvents)
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        FrontierAccountSnapshot cached = CreateSnapshot(start) with
        {
            CommanderName = "Second",
            CommanderReputation = [new("Federation", 50)],
            CarrierFetchedAt = start,
            CommunityGoalsFetchedAt = start,
        };
        using var model = new CommanderProfileViewModel(new StubAccountService(new(true, cached, start)), () => start);
        await model.SetCommanderContextAsync("F456", "Second", true);
        await model.OpenAsync();
        var entries = new List<JournalEventEnvelope>(ReconciliationEvents(1))
        {
            ParseJournalEvent("""{"timestamp":"1970-01-01T00:00:02Z","event":"Fileheader"}"""),
            ParseJournalEvent(
                """{"timestamp":"1970-01-01T00:00:03Z","event":"Commander","Name":"Second","FID":"F456"}"""
            ),
            ParseJournalEvent(
                """{"timestamp":"1970-01-01T00:00:04Z","event":"LoadGame","Commander":"Second","FID":"F456"}"""
            ),
        };
        if (includeNewPilotEvents)
        {
            entries.AddRange(ReconciliationEvents(5));
        }
        model.UpdateJournalState("F123", entries, "Fenris");
        Assert.Equal(includeNewPilotEvents ? "85%" : "50%", Assert.Single(model.CommanderReputation).Score);
        Assert.Equal(
            includeNewPilotEvents ? "900 contributed" : "250 contributed",
            Assert.Single(model.CommunityGoals).PlayerContribution
        );
        Assert.Equal(
            includeNewPilotEvents ? "Achenar" : "None plotted",
            model.CarrierOperations.Single(row => row.Label == "Current jump").Value
        );
        Assert.Equal(includeNewPilotEvents ? 8 : 7, model.CurrentShip!.Id);
    }

    /// <summary>A ship swap invalidates the old ship's modules immediately and a complete journal loadout supplies its replacement.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JournalShipSwapAndLoadoutRefreshAllCurrentShipProjections(bool storedInCache)
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        FrontierAccountSnapshot original = CreateSnapshot(start);
        FrontierAccountSnapshot cached = original with
        {
            CurrentShip = original.CurrentShip! with
            {
                Modules =
                [
                    new("MainEngines", 1, "Old engine", "", 1, false, 100, true, 1, "", "", null, [], "old_engine"),
                ],
            },
            Ships = storedInCache
                ?
                [
                    original.CurrentShip,
                    original.CurrentShip with
                    {
                        Id = 8,
                        Type = "Python",
                        Name = "Stored Python",
                        HullHealth = null,
                        ShieldHealth = null,
                        Modules = [],
                    },
                ]
                : original.Ships,
        };
        using var model = new CommanderProfileViewModel(new StubAccountService(new(true, cached, start)), () => start);
        model.UpdateJournalState(null, []);
        await model.SetCommanderContextAsync("F123", "Fenris", true);
        await model.OpenAsync();
        Assert.Single(model.CurrentShipModules);
        model.UpdateJournalState("F123", [ReconciliationEvents(1)[3]]);
        Assert.Empty(model.CurrentShipModules);
        Assert.Equal("Health unavailable", model.CurrentShipHealth);
        model.UpdateJournalState(
            "F123",
            [
                ParseJournalEvent(
                    """
                    {"timestamp":"1970-01-01T00:00:02Z","event":"Loadout","Ship":"python","ShipID":8,"ShipName":"New ship","ShipIdent":"NEW","HullHealth":0.9,"Modules":[{"Slot":"MainEngines","Item":"int_engine_size5_class5"}]}
                    """
                ),
            ]
        );
        Assert.Single(model.CurrentShipModules);
        Assert.Contains("New ship", model.CurrentShipDescription);
        Assert.Equal("Hull 90%", model.CurrentShipHealth);
        Assert.Equal("New ship", Assert.Single(model.Ships, row => row.Status == "CURRENT").Name);
    }

    private static JournalEventEnvelope[] ReconciliationEvents(int seconds)
    {
        string at = DateTimeOffset.UnixEpoch.AddSeconds(seconds).ToString("O");
        return
        [
            ParseJournalEvent($$"""{"timestamp":"{{at}}","event":"Reputation","Federation":85}"""),
            ParseJournalEvent(
                $$"""{"timestamp":"{{at}}","event":"CommunityGoal","CurrentGoals":[{"CGID":6,"Title":"Deliver medicines","SystemName":"Sol","MarketName":"Galileo","PlayerContribution":900}]}"""
            ),
            ParseJournalEvent(
                $$"""{"timestamp":"{{at}}","event":"CarrierJumpRequest","CarrierID":111,"SystemName":"Achenar"}"""
            ),
            ParseJournalEvent($$"""{"timestamp":"{{at}}","event":"ShipyardSwap","ShipID":8,"ShipType":"python"}"""),
        ];
    }
}
