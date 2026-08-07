using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class CommanderProfileViewModelTests
{
    [Fact]
    public async Task UnlinkedStateShowsConnectionExperienceWithoutFetching()
    {
        var account = new StubAccountService(
            new FrontierAccountState(false, null, null));
        using var viewModel = new CommanderProfileViewModel(account);

        await viewModel.OpenAsync();

        Assert.True(viewModel.IsUnlinked);
        Assert.False(viewModel.IsLinked);
        Assert.False(viewModel.HasSnapshot);
        Assert.Equal(0, account.RefreshCount);
    }

    [Fact]
    public void AuthorizationCallbackIsForwardedForWindowActivation()
    {
        var account = new StubAccountService(
            new FrontierAccountState(false, null, null));
        using var viewModel = new CommanderProfileViewModel(account);
        var received = 0;
        viewModel.AuthorizationCallbackReceived += (_, _) => received++;

        account.RaiseAuthorizationCallbackReceived();

        Assert.Equal(1, received);
    }

    [Fact]
    public async Task CachedSnapshotProjectsCompactCommanderAndCarrierRows()
    {
        var snapshot = CreateSnapshot(DateTimeOffset.UtcNow);
        var account = new StubAccountService(
            new FrontierAccountState(true, snapshot, snapshot.FetchedAt));
        using var viewModel = new CommanderProfileViewModel(account);

        await viewModel.OpenAsync();

        Assert.True(viewModel.IsLinked);
        Assert.Equal("Fenris", viewModel.CommanderName);
        Assert.Contains("1,000", viewModel.Balance);
        Assert.Equal("Surveyor · Cobra Mk III", viewModel.CurrentShipDescription);
        Assert.Equal("Sol · Galileo", viewModel.CurrentLocation);
        Assert.Equal("Raven's Rest · RAV-001", viewModel.CarrierTitle);
        Assert.Single(viewModel.Ships);
        Assert.Single(viewModel.CarrierCargo);
        Assert.Single(viewModel.CarrierBuyOrders);
        Assert.Equal("Not For Sale", Assert.Single(viewModel.CarrierCapacityRows).Category);
        Assert.Contains("24,000", viewModel.CarrierCapacityHeader);
        Assert.Single(viewModel.CommanderReputation);
        Assert.EndsWith(
            "/Ranks/exploration/rank-9.png",
            Assert.Single(viewModel.Ranks).IconPath);
        Assert.EndsWith("/Factions/federation.png", viewModel.FactionIconPath);
        Assert.Single(viewModel.MarketCommodities);
        Assert.Single(viewModel.ShipyardShips);
        Assert.Single(viewModel.ShipyardModules);
        var goal = Assert.Single(viewModel.CommunityGoals);
        Assert.Equal(50, goal.Progress);
        Assert.Contains("250", goal.PlayerContribution);
        Assert.Equal("Trade delivery", goal.Activity);
        Assert.Equal("Sol · Galileo", goal.Location);
        Assert.Equal("5,000 / 10,000", goal.ProgressText);
        Assert.True(goal.HasBriefing);
        Assert.Equal(0, account.RefreshCount);
    }

    [Fact]
    public async Task SnapshotProjectionCollectionsKeepStableIdentityBetweenReads()
    {
        var snapshot = CreateSnapshot(DateTimeOffset.UtcNow);
        var account = new StubAccountService(new FrontierAccountState(
            true,
            snapshot,
            snapshot.FetchedAt));
        using var viewModel = new CommanderProfileViewModel(account);
        await viewModel.OpenAsync();

        Assert.Same(viewModel.Ranks, viewModel.Ranks);
        Assert.Same(viewModel.CurrentShipValueRows, viewModel.CurrentShipValueRows);
        Assert.Same(viewModel.CurrentShipConditionRows, viewModel.CurrentShipConditionRows);
        Assert.Same(viewModel.CurrentShipModules, viewModel.CurrentShipModules);
        Assert.Same(viewModel.CurrentShipLivery, viewModel.CurrentShipLivery);
        Assert.Same(viewModel.CurrentShipLaunchBays, viewModel.CurrentShipLaunchBays);
        Assert.Same(viewModel.Ships, viewModel.Ships);
        Assert.Same(viewModel.CarrierCapacityRows, viewModel.CarrierCapacityRows);
        Assert.Same(viewModel.CarrierCargo, viewModel.CarrierCargo);
        Assert.Same(viewModel.CarrierLocker, viewModel.CarrierLocker);
        Assert.Same(viewModel.CarrierSellOrders, viewModel.CarrierSellOrders);
        Assert.Same(viewModel.CarrierBuyOrders, viewModel.CarrierBuyOrders);
        Assert.Same(viewModel.CarrierOperations, viewModel.CarrierOperations);
        Assert.Same(viewModel.CarrierFinances, viewModel.CarrierFinances);
        Assert.Same(viewModel.CarrierServiceTaxation, viewModel.CarrierServiceTaxation);
        Assert.Same(viewModel.CarrierCrew, viewModel.CarrierCrew);
        Assert.Same(viewModel.CarrierItinerary, viewModel.CarrierItinerary);
        Assert.Same(viewModel.CarrierReputation, viewModel.CarrierReputation);
        Assert.Same(viewModel.CommanderReputation, viewModel.CommanderReputation);
        Assert.Same(viewModel.MarketCommodities, viewModel.MarketCommodities);
        Assert.Same(viewModel.MarketEconomies, viewModel.MarketEconomies);
        Assert.Same(viewModel.ShipyardShips, viewModel.ShipyardShips);
        Assert.Same(viewModel.ShipyardModules, viewModel.ShipyardModules);
        Assert.Same(viewModel.CommunityGoals, viewModel.CommunityGoals);

        var priorRanks = viewModel.Ranks;
        var refreshed = CreateSnapshot(snapshot.FetchedAt.AddMinutes(1));
        account.SetState(new FrontierAccountState(
            true,
            refreshed,
            refreshed.FetchedAt));
        await viewModel.OpenAsync();

        Assert.NotSame(priorRanks, viewModel.Ranks);
    }

    [Fact]
    public async Task CachedRawGoalFieldsUpgradeWithoutNetworkRefresh()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-07-30T12:00:00Z");
        var snapshot = CreateSnapshot(fetchedAt);
        var cachedGoal = Assert.Single(snapshot.CommunityGoals!) with
        {
            Description = string.Empty,
            System = string.Empty,
            Market = string.Empty,
            CurrentTotal = 0,
            TargetTotal = null,
            ActivityType = string.Empty,
            HasPlayerContributionData = false,
            HasContributorData = false,
            DataPoints =
            [
                new("goal.starsystem_name", "Carcosa"),
                new("goal.market_name", "Robardin Rock"),
                new("goal.activityType", "tradelist"),
                new("goal.qty", "1971753"),
                new("goal.target_qty", "34500000"),
                new("goal.bulletin", "Colonia Council calls on pilots.\n\n- Cargo rack reward"),
            ],
        };
        using var viewModel = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(
                true,
                snapshot with { CommunityGoals = [cachedGoal] },
                fetchedAt)),
            () => fetchedAt);

        await viewModel.OpenAsync();

        var goal = Assert.Single(viewModel.CommunityGoals);
        Assert.Equal("Robardin Rock", goal.Market);
        Assert.Equal("Carcosa", goal.System);
        Assert.Equal("Trade delivery", goal.Activity);
        Assert.Equal("1,971,753 / 34,500,000", goal.ProgressText);
        Assert.Equal("5.72% complete", goal.ProgressPercent);
        Assert.Contains("Cargo rack reward", goal.Briefing);
        Assert.Equal(
            "Personal progress not supplied by Frontier or local journals",
            goal.PlayerContribution);
    }

    [Fact]
    public async Task JournalReputationPopulatesCommanderWithoutFleetCarrier()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var snapshot = CreateSnapshot(fetchedAt) with
        {
            Carrier = null,
            CommanderReputation = [],
            CommanderReputationFetchedAt = null,
        };
        using var viewModel = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(
                true,
                snapshot,
                snapshot.FetchedAt)));
        await viewModel.OpenAsync();

        viewModel.UpdateJournalReputation(
            "Fenris",
            [ParseJournalEvent(
                """
                {"timestamp":"2026-07-29T12:05:00Z","event":"Reputation","Empire":25.5,"Federation":91,"Independent":100,"Alliance":-12}
                """)]);

        Assert.Null(viewModel.Carrier);
        Assert.Equal(4, viewModel.CommanderReputation.Count);
        Assert.Equal(
            "91%",
            viewModel.CommanderReputation.Single(item =>
                item.Faction == "Federation").Score);

        viewModel.UpdateJournalReputation("Another Commander", []);

        Assert.Empty(viewModel.CommanderReputation);
    }

    [Fact]
    public async Task OlderJournalReputationDoesNotOverrideNewerCapiData()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var snapshot = CreateSnapshot(fetchedAt) with
        {
            Carrier = null,
        };
        using var viewModel = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(
                true,
                snapshot,
                snapshot.FetchedAt)));
        await viewModel.OpenAsync();

        viewModel.UpdateJournalReputation(
            "Fenris",
            [ParseJournalEvent(
                """
                {"timestamp":"2026-07-29T11:00:00Z","event":"Reputation","Federation":10}
                """)]);

        Assert.Equal(
            "100%",
            Assert.Single(viewModel.CommanderReputation).Score);
    }

    [Fact]
    public async Task JournalAddsPersonalGoalProgressWithoutLosingInaraDetails()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
        var accountGoal = Assert.Single(CreateSnapshot(fetchedAt).CommunityGoals!) with
        {
            Description = "Expanded global briefing",
            PlayerContribution = 0,
            PlayerPercentile = null,
            Bonus = 0,
            HasPlayerContributionData = false,
            Contributors = 2_913,
            TierReached = "Tier 0 / 1",
            HasContributorData = true,
            DataPoints =
            [
                new("inara.fetchedAt", fetchedAt.ToString("O")),
            ],
        };
        var snapshot = CreateSnapshot(fetchedAt) with
        {
            CommunityGoals = [accountGoal],
            CommunityGoalsFetchedAt = fetchedAt,
            InaraCommunityGoalsFetchedAt = fetchedAt,
        };
        using var viewModel = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(
                true,
                snapshot,
                snapshot.FetchedAt)));
        await viewModel.OpenAsync();

        viewModel.UpdateJournalCommunityGoals(
            "Fenris",
            [ParseJournalEvent(
                """
                {
                  "timestamp":"2026-07-31T12:05:00Z",
                  "event":"CommunityGoal",
                  "CurrentGoals":[
                    {
                      "CGID":6,
                      "Title":"Deliver medicines",
                      "SystemName":"Sol",
                      "MarketName":"Galileo",
                      "Expiry":"2026-08-02T12:00:00Z",
                      "IsComplete":false,
                      "CurrentTotal":6000,
                      "PlayerContribution":325,
                      "NumContributors":3000,
                      "TierReached":"Tier 1",
                      "PlayerPercentileBand":25,
                      "Bonus":2000000
                    }
                  ]
                }
                """)]);

        var goal = Assert.Single(viewModel.CommunityGoals);
        Assert.Equal("Expanded global briefing", goal.Briefing);
        Assert.Equal("6,000 / 10,000", goal.ProgressText);
        Assert.Equal("325 contributed", goal.PlayerContribution);
        Assert.Contains("Top 25%", goal.PlayerStanding);
        Assert.Equal("3,000 commanders", goal.Contributors);
        Assert.Equal("Tier 1", goal.Tier);
        Assert.True(goal.HasSourceStatus);
        Assert.Contains("Inara", goal.SourceStatus);

        viewModel.UpdateJournalCommunityGoals("Another Commander", []);

        goal = Assert.Single(viewModel.CommunityGoals);
        Assert.Equal(
            "Personal progress not supplied by Frontier or local journals",
            goal.PlayerContribution);
        Assert.Equal("2,913 commanders", goal.Contributors);
        Assert.Equal("Tier 0 / 1", goal.Tier);
    }

    [Fact]
    public async Task HistoricalJournalProgressSupplementsCompletedInaraGoal()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
        var accountGoal = Assert.Single(CreateSnapshot(fetchedAt).CommunityGoals!) with
        {
            Id = null,
            Title = "Vista Genomics Exobiology Initiative",
            ExpiresAt = DateTimeOffset.Parse("2026-07-09T10:00:00Z"),
            IsComplete = true,
            PlayerContribution = 0,
            PlayerPercentile = null,
            Bonus = 0,
            HasPlayerContributionData = false,
            DataPoints =
            [
                new("inara.sourceOnly", "true"),
                new("inara.fetchedAt", fetchedAt.ToString("O")),
                new("inara.lastUpdate", "2026-07-09T10:10:00Z"),
            ],
        };
        var snapshot = CreateSnapshot(fetchedAt) with
        {
            CommunityGoals = [accountGoal],
            CommunityGoalsFetchedAt = fetchedAt,
        };
        var historyGoal = accountGoal with
        {
            Id = 850,
            PlayerContribution = 2,
            PlayerPercentile = 100,
            Bonus = 45_000_000,
            HasPlayerContributionData = true,
            DataPoints =
            [
                new(
                    "journal.communityGoalTimestamp",
                    "2026-07-23T06:54:24Z"),
            ],
        };
        var history = new StubCommunityGoalHistoryReader(
            new CommunityGoalJournalHistoryReadResult([historyGoal], string.Empty));
        using var viewModel = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(
                true,
                snapshot,
                snapshot.FetchedAt)),
            communityGoalHistoryReader: history);

        await viewModel.SetCommanderContextAsync(
            "F472567",
            "Fenris",
            refreshIfOpen: false);
        await viewModel.OpenAsync();

        var goal = Assert.Single(viewModel.CommunityGoals);
        Assert.Equal("2 contributed", goal.PlayerContribution);
        Assert.Contains("Top 100%", goal.PlayerStanding);
        Assert.Contains("45,000,000 CR", goal.PlayerStanding);
        Assert.Contains("Global goal supplied by Inara", goal.SourceStatus);
        Assert.Contains("Personal progress restored from local journals", goal.SourceStatus);
        Assert.Equal("F472567", history.RequestedFrontierId);
    }

    [Fact]
    public async Task CompletedGoalsArePresentedNewestFirst()
    {
        var fetchedAt = DateTimeOffset.Parse("2026-07-31T12:00:00Z");
        var template = Assert.Single(CreateSnapshot(fetchedAt).CommunityGoals!);
        FrontierCommunityGoalSnapshot Completed(
            string title,
            string lastUpdate) => template with
            {
                Id = null,
                Title = title,
                IsComplete = true,
                ExpiresAt = DateTimeOffset.Parse(lastUpdate).AddHours(1),
                DataPoints =
            [
                new("inara.lastUpdate", lastUpdate),
            ],
            };
        var snapshot = CreateSnapshot(fetchedAt) with
        {
            CommunityGoals =
            [
                Completed("Distant Worlds III", "2026-03-27T14:00:00Z"),
                Completed("Newest completion", "2026-07-23T10:00:00Z"),
                Completed("Middle completion", "2026-06-18T09:00:00Z"),
            ],
        };
        using var viewModel = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(
                true,
                snapshot,
                snapshot.FetchedAt)));

        await viewModel.OpenAsync();

        Assert.Equal(
            ["Newest completion", "Middle completion", "Distant Worlds III"],
            viewModel.CommunityGoals.Select(goal => goal.Title));
    }

    [Fact]
    public async Task PaneExpansionStateIsCachedAndIsolatedAcrossTabs()
    {
        var snapshot = CreateSnapshot(DateTimeOffset.UtcNow);
        var account = new StubAccountService(new FrontierAccountState(
            true,
            snapshot,
            snapshot.FetchedAt));
        using var viewModel = new CommanderProfileViewModel(account);
        await viewModel.OpenAsync();
        var profileNotifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => profileNotifications.Add(args.PropertyName);

        var ownedFleet = viewModel.PaneStates.CommanderOwnedFleet;
        var currentShipCargo = viewModel.PaneStates.CurrentShipCargo;
        var carrierCargo = viewModel.PaneStates.CarrierStoredCargo;
        Assert.NotSame(ownedFleet, currentShipCargo);
        Assert.NotSame(currentShipCargo, carrierCargo);

        ownedFleet.IsExpanded = false;

        Assert.False(ownedFleet.IsExpanded);
        Assert.True(currentShipCargo.IsExpanded);
        Assert.True(carrierCargo.IsExpanded);
        Assert.Empty(profileNotifications);

        currentShipCargo.IsExpanded = false;

        Assert.False(currentShipCargo.IsExpanded);
        Assert.True(carrierCargo.IsExpanded);
        Assert.Empty(profileNotifications);

        var goalPane = Assert.Single(viewModel.CommunityGoals).PaneState;
        goalPane.IsExpanded = true;
        Assert.Same(goalPane, Assert.Single(viewModel.CommunityGoals).PaneState);

        var refreshed = CreateSnapshot(snapshot.FetchedAt.AddMinutes(1));
        account.SetState(new FrontierAccountState(
            true,
            refreshed,
            refreshed.FetchedAt));
        await viewModel.OpenAsync();

        Assert.False(ownedFleet.IsExpanded);
        Assert.False(currentShipCargo.IsExpanded);
        Assert.True(carrierCargo.IsExpanded);
        Assert.Same(goalPane, Assert.Single(viewModel.CommunityGoals).PaneState);
        Assert.True(goalPane.IsExpanded);
    }

    [Fact]
    public void LocalCompanionInventoryIsProjectedAndSuppressedWhenAmbiguous()
    {
        using var viewModel = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(false, null, null)));
        var timestamp = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var cargo = new CargoSnapshot(
            timestamp,
            "Cargo",
            "Ship",
            3,
            [new CargoItem("gold", "Gold", 3, 1)]);
        var locker = new ShipLockerSnapshot(
            timestamp,
            "ShipLocker",
            [
                new ShipLockerItem("Components", "microelectrode", "Microelectrode", 4),
                new ShipLockerItem("Items", "healthmonitor", "Health Monitor", 2),
                new ShipLockerItem("Data", "opinionpolls", "Opinion Polls", 1),
            ]);

        viewModel.UpdateLocalInventory(cargo, locker, isSuppressed: false);

        Assert.Equal("Gold", Assert.Single(viewModel.CurrentShipCargo).Name);
        Assert.Contains("stolen", Assert.Single(viewModel.CurrentShipCargo).Detail);
        Assert.Equal(3, viewModel.CurrentShipLocker.Count);
        Assert.Equal(["Items", "Components", "Data"],
            viewModel.CurrentShipLockerGroups.Select(group => group.Category));
        var cargoRows = viewModel.CurrentShipCargo;
        var lockerRows = viewModel.CurrentShipLocker;
        var inventoryNotifications = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) =>
            inventoryNotifications.Add(eventArgs.PropertyName);
        viewModel.UpdateLocalInventory(cargo, locker, isSuppressed: false);
        Assert.Same(cargoRows, viewModel.CurrentShipCargo);
        Assert.Same(lockerRows, viewModel.CurrentShipLocker);
        Assert.Empty(inventoryNotifications);

        var lockerGroup = viewModel.CurrentShipLockerGroups[0];
        Assert.False(lockerGroup.IsExpanded);
        lockerGroup.ToggleCommand.Execute(null);
        Assert.True(lockerGroup.IsExpanded);
        viewModel.UpdateLocalInventory(cargo, locker, isSuppressed: false);
        Assert.Same(lockerGroup, viewModel.CurrentShipLockerGroups
            .Single(group => group.Category == lockerGroup.Category));
        var refreshedLocker = locker with
        {
            Timestamp = locker.Timestamp.AddSeconds(1),
        };
        viewModel.UpdateLocalInventory(cargo, refreshedLocker, isSuppressed: false);
        var rebuiltLockerGroup = viewModel.CurrentShipLockerGroups
            .Single(group => group.Category == lockerGroup.Category);
        Assert.NotSame(lockerGroup, rebuiltLockerGroup);
        Assert.True(rebuiltLockerGroup.IsExpanded);
        Assert.Equal("Microelectrode",
            viewModel.CurrentShipLocker.Single(item => item.Category == "Components").Name);
        Assert.Contains("updated", viewModel.LocalInventoryStatus);

        viewModel.UpdateLocalInventory(cargo, locker, isSuppressed: true);

        Assert.Empty(viewModel.CurrentShipCargo);
        Assert.Empty(viewModel.CurrentShipLocker);
        Assert.Contains("multiple Elite windows", viewModel.LocalInventoryStatus);
    }

    [Fact]
    public async Task CommanderSwitchSelectsSavedProfileAndClearsLocalInventory()
    {
        var first = CreateSnapshot(DateTimeOffset.UtcNow) with
        {
            CommanderId = 123,
        };
        var second = CreateSnapshot(DateTimeOffset.UtcNow.AddMinutes(1)) with
        {
            CommanderName = "Second",
            CommanderId = 456,
            Credits = 2_000,
        };
        var account = new StubAccountService(
            new FrontierAccountState(false, null, null));
        account.SetStateForCommander(
            "F123",
            new FrontierAccountState(true, first, first.FetchedAt));
        account.SetStateForCommander(
            "F456",
            new FrontierAccountState(true, second, second.FetchedAt));
        using var viewModel = new CommanderProfileViewModel(account);

        await viewModel.SetCommanderContextAsync(
            "F123",
            "Fenris",
            refreshIfOpen: true);
        viewModel.UpdateLocalInventory(
            new CargoSnapshot(
                DateTimeOffset.UtcNow,
                "Cargo",
                "Ship",
                1,
                [new CargoItem("gold", "Gold", 1, 0)]),
            null,
            isSuppressed: false);
        Assert.Equal("Fenris", viewModel.CommanderName);
        Assert.True(viewModel.HasCurrentShipCargo);

        await viewModel.SetCommanderContextAsync(
            "F456",
            "Second",
            refreshIfOpen: true);

        Assert.Equal("Second", viewModel.CommanderName);
        Assert.Contains("2,000", viewModel.Balance);
        Assert.False(viewModel.HasCurrentShipCargo);
        Assert.Equal("F456", account.ActiveFrontierId);

        await viewModel.SetCommanderContextAsync(
            "F123",
            "Fenris",
            refreshIfOpen: true);

        Assert.Equal("Fenris", viewModel.CommanderName);
        Assert.Equal("F123", account.ActiveFrontierId);
    }

    [Fact]
    public async Task ManualCommanderSelectionOverridesConsoleWithoutMixingJournalInventory()
    {
        var fetchedAt = DateTimeOffset.UtcNow;
        var first = CreateSnapshot(fetchedAt) with
        {
            CommanderId = 123,
        };
        var second = CreateSnapshot(fetchedAt) with
        {
            CommanderName = "Second",
            CommanderId = 456,
            Credits = 2_000,
        };
        var account = new StubAccountService(
            new FrontierAccountState(false, null, null));
        account.SetStateForCommander(
            "F123",
            new FrontierAccountState(true, first, first.FetchedAt));
        account.SetStateForCommander(
            "F456",
            new FrontierAccountState(true, second, second.FetchedAt));
        using var viewModel = new CommanderProfileViewModel(account);
        await viewModel.SetCommanderContextAsync(
            "F123",
            "Fenris",
            refreshIfOpen: true);
        viewModel.UpdateLocalInventory(
            new CargoSnapshot(
                fetchedAt,
                "Cargo",
                "Ship",
                1,
                [new CargoItem("gold", "Gold", 1, 0)]),
            null,
            isSuppressed: false);

        Assert.Equal(2, viewModel.CommanderSelectionOptions.Count);
        Assert.True(viewModel.SelectedCommanderOption!.IsAutomatic);
        Assert.True(viewModel.HasCurrentShipCargo);
        Assert.DoesNotContain(
            viewModel.CommanderSelectionOptions,
            option => option.FrontierId == "F123" && !option.IsAutomatic);

        var secondOption = Assert.Single(
            viewModel.CommanderSelectionOptions,
            option => option.FrontierId == "F456" && !option.IsAutomatic);
        await viewModel.SelectCommanderAsync(secondOption);

        Assert.False(viewModel.IsAutomaticCommanderSelection);
        Assert.Equal("F456", account.ActiveFrontierId);
        Assert.Equal("Second", viewModel.CommanderName);
        Assert.False(viewModel.HasCurrentShipCargo);
        Assert.Contains("different Frontier account", viewModel.LocalInventoryStatus);

        var automatic = Assert.Single(
            viewModel.CommanderSelectionOptions,
            option => option.IsAutomatic);
        await viewModel.SelectCommanderAsync(automatic);

        Assert.True(viewModel.IsAutomaticCommanderSelection);
        Assert.Equal("F123", account.ActiveFrontierId);
        Assert.Equal("Fenris", viewModel.CommanderName);
        Assert.True(viewModel.HasCurrentShipCargo);
    }

    [Fact]
    public async Task ManualCommanderSelectionSurvivesJournalCommanderDetection()
    {
        var fetchedAt = DateTimeOffset.UtcNow;
        var first = CreateSnapshot(fetchedAt) with
        {
            CommanderId = 123,
        };
        var second = CreateSnapshot(fetchedAt) with
        {
            CommanderName = "Second",
            CommanderId = 456,
        };
        var account = new StubAccountService(
            new FrontierAccountState(false, null, null));
        account.SetStateForCommander(
            "F123",
            new FrontierAccountState(true, first, first.FetchedAt));
        account.SetStateForCommander(
            "F456",
            new FrontierAccountState(true, second, second.FetchedAt));
        using var viewModel = new CommanderProfileViewModel(account);
        await viewModel.SetCommanderContextAsync(
            "F123",
            "Fenris",
            refreshIfOpen: true);
        var secondOption = Assert.Single(
            viewModel.CommanderSelectionOptions,
            option => option.FrontierId == "F456" && !option.IsAutomatic);
        await viewModel.SelectCommanderAsync(secondOption);

        await viewModel.SetCommanderContextAsync(
            "F789",
            "Third",
            refreshIfOpen: true);

        Assert.False(viewModel.IsAutomaticCommanderSelection);
        Assert.Equal("F456", account.ActiveFrontierId);
        Assert.Equal("Second", viewModel.CommanderName);
        Assert.Contains("Third (F789)", viewModel.CommanderSelectionDescription);
    }

    [Fact]
    public async Task CurrentShipSeparatesLiveryAndGroupsLocalizedModuleLoadout()
    {
        var fetchedAt = DateTimeOffset.UtcNow;
        var original = CreateSnapshot(fetchedAt);
        var ship = original.CurrentShip! with
        {
            Paintwork = 62_060,
            Modules =
            [
                new FrontierShipModuleSnapshot(
                    "MainEngines",
                    101,
                    "Int_Engine_Size2_Class1_Name",
                    "Standard propulsion system for ships.",
                    9_000,
                    false,
                    99,
                    true,
                    1,
                    "Felicity Farseer",
                    "engine_tuned",
                    3,
                    ["drag_drives"],
                    string.Empty),
                new FrontierShipModuleSnapshot(
                    "PaintJob",
                    102,
                    "PaintJob_PantherMkII_03_02_Name",
                    "PaintJob_PantherMkII_03_02_Info",
                    0,
                    false,
                    100,
                    true,
                    1,
                    string.Empty,
                    string.Empty,
                    null,
                    [],
                    string.Empty),
                new FrontierShipModuleSnapshot(
                    "Decal1",
                    103,
                    "Decal_SquadronLogo_Dynamic_Name",
                    "Decal_SquadronLogo_Dynamic_Info",
                    0,
                    false,
                    100,
                    true,
                    1,
                    string.Empty,
                    string.Empty,
                    null,
                    [],
                    string.Empty),
            ],
            DataPoints =
            [
                new FrontierDataPointSnapshot(
                    "ship.modules.MainEngines.module.name",
                    "int_engine_size2_class1"),
                new FrontierDataPointSnapshot(
                    "ship.modules.PaintJob.module.name",
                    "PaintJob_PantherMkII_03_02"),
                new FrontierDataPointSnapshot(
                    "ship.modules.Decal1.module.name",
                    "Decal_SquadronLogo_Dynamic"),
            ],
        };
        var snapshot = original with
        {
            CurrentShip = ship,
            Ships = [ship],
        };
        using var viewModel = new CommanderProfileViewModel(
            new StubAccountService(new FrontierAccountState(
                true,
                snapshot,
                snapshot.FetchedAt)));

        await viewModel.OpenAsync();

        var module = Assert.Single(viewModel.CurrentShipModules);
        Assert.Equal("Thrusters", module.Name);
        Assert.Equal("2E", module.ClassRating);
        Assert.Equal("Core Internal", module.Group);
        Assert.True(module.HasEngineering);
        var moduleGroup = Assert.Single(viewModel.CurrentShipModuleGroups);
        Assert.True(moduleGroup.IsExpanded);
        moduleGroup.ToggleCommand.Execute(null);
        Assert.False(moduleGroup.IsExpanded);
        viewModel.UpdateLocalInventory(null, null, isSuppressed: false);
        Assert.Same(moduleGroup, Assert.Single(viewModel.CurrentShipModuleGroups));
        Assert.False(moduleGroup.IsExpanded);
        Assert.Equal(2, viewModel.CurrentShipLivery.Count);
        Assert.All(viewModel.CurrentShipLivery,
            item => Assert.DoesNotContain("_", item.Name));
        Assert.Equal("Squadron Logo",
            viewModel.CurrentShipLivery.Single(item => item.Category == "Decal").Name);
        Assert.Equal("6%", viewModel.CurrentShipPaintwork);
        Assert.DoesNotContain(viewModel.CurrentShipConditionRows,
            item => item.Label == "Paintwork");
    }

    [Fact]
    public async Task OpeningStaleLinkedProfileRefreshesOnlyOncePerViewSession()
    {
        var stale = CreateSnapshot(DateTimeOffset.UtcNow.AddHours(-1));
        var refreshed = CreateSnapshot(DateTimeOffset.UtcNow);
        var account = new StubAccountService(
            new FrontierAccountState(true, stale, stale.FetchedAt),
            refreshed);
        using var viewModel = new CommanderProfileViewModel(account);

        await viewModel.OpenAsync();
        await viewModel.OpenAsync();

        Assert.Equal(1, account.RefreshCount);
        Assert.Equal(refreshed.FetchedAt, viewModel.Snapshot!.FetchedAt);
    }

    [Fact]
    public async Task CommanderCardNavigationSelectsProfileOutsideCategoryList()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-profile-navigation-{Guid.NewGuid():N}");
        try
        {
            var profile = new CommanderProfileViewModel(
                new StubAccountService(new FrontierAccountState(false, null, null)));
            using var main = new MainWindowViewModel(Path.Combine(root, "journals"),
                new MainWindowViewModelOptions
                {
                    FrontierProfile = profile,
                });

            await main.ShowProfileAsync();

            Assert.True(main.IsProfileSelected);
            Assert.Null(main.SelectedNavigation);
            Assert.False(main.IsOverviewSelected);

            main.SelectedNavigation = main.NavigationItems.Single(
                item => item.Key == "exploration");

            Assert.False(main.IsProfileSelected);
            Assert.True(main.IsExplorationSelected);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task MainJournalRefreshSuppliesReputationWhenCarrierIsAbsent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-profile-reputation-{Guid.NewGuid():N}");
        try
        {
            var journals = Path.Combine(root, "journals");
            Directory.CreateDirectory(journals);
            await File.WriteAllTextAsync(
                Path.Combine(journals, "Journal.2026-07-29T120000.01.log"),
                """
                {"timestamp":"2026-07-29T12:00:00Z","event":"Commander","Name":"Fenris","FID":"F123"}
                {"timestamp":"2026-07-29T12:00:01Z","event":"LoadGame","Commander":"Fenris","FID":"F123","Odyssey":true}
                {"timestamp":"2026-07-29T12:05:00Z","event":"Reputation","Empire":25.5,"Federation":91,"Independent":100,"Alliance":-12}

                """);
            var fetchedAt = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
            var snapshot = CreateSnapshot(fetchedAt) with
            {
                Carrier = null,
                CommanderReputation = [],
                CommanderReputationFetchedAt = null,
            };
            var account = new StubAccountService(new FrontierAccountState(
                true,
                snapshot,
                snapshot.FetchedAt));
            var profile = new CommanderProfileViewModel(account);
            await profile.OpenAsync();
            using var main = new MainWindowViewModel(journals,
                new MainWindowViewModelOptions
                {
                    AppDataPaths = new AppDataPaths(
                    Path.Combine(root, "config"),
                    Path.Combine(root, "profile"),
                    Path.Combine(root, "cache"),
                    []),
                    FrontierProfile = profile,
                });

            await main.ShowProfileAsync();
            await main.RefreshAsync();

            Assert.Equal("F123", account.ActiveFrontierId);
            Assert.Null(profile.Carrier);
            Assert.Equal(4, profile.CommanderReputation.Count);
            Assert.Equal(
                "91%",
                profile.CommanderReputation.Single(item =>
                    item.Faction == "Federation").Score);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static FrontierAccountSnapshot CreateSnapshot(DateTimeOffset fetchedAt)
    {
        var ship = new FrontierShipSnapshot(
            7,
            "Cobra Mk III",
            "Surveyor",
            "SRV-07",
            "Sol",
            "Galileo",
            250_000,
            true,
            100,
            100);
        var carrier = new FrontierCarrierSnapshot(
            "RAV-001",
            "Raven's Rest",
            "Colonia",
            "Normal Operation",
            "All",
            5_000_000,
            100_000,
            200_000,
            300_000,
            400_000,
            500_000,
            900,
            1000,
            24000,
            [new FrontierCapacitySnapshot("Cargo Not For Sale", 1000)],
            [new FrontierInventorySnapshot("Cargo", "Tritium", 10, 500_000)],
            [],
            [],
            [new FrontierMarketOrderSnapshot("Commodity", "Gold", 10, 5, 1000, false)],
            ["Refuel"]);
        return new FrontierAccountSnapshot(
            "Fenris",
            1000,
            0,
            true,
            true,
            "Sol",
            "Galileo",
            ship,
            [new FrontierRankSnapshot("explore", "Exploration", 8, "Elite")],
            [ship],
            ["Horizons"],
            carrier,
            fetchedAt,
            LastSystemDetails: new FrontierLocationSnapshot(
                1,
                2,
                "Sol",
                "Federation",
                "Pilots Federation",
                []),
            LastStationDetails: new FrontierLocationSnapshot(
                3,
                2,
                "Galileo",
                "Federation",
                "Pilots Federation",
                ["Shipyard", "Market"]),
            Market: new FrontierMarketSnapshot(
                3,
                "Galileo",
                "Starport",
                [],
                [],
                [],
                [],
                [],
                [new FrontierCommoditySnapshot(
                    4,
                    "Metals",
                    "Gold",
                    "Legal",
                    100,
                    90,
                    95,
                    2,
                    3,
                    50,
                    200,
                    [])],
                fetchedAt),
            Shipyard: new FrontierShipyardSnapshot(
                3,
                "Galileo",
                "Starport",
                [],
                [],
                [],
                [],
                [],
                [new FrontierOutfittingModuleSnapshot(
                    4,
                    "Utility",
                    "Heat Sink Launcher",
                    3_500,
                    string.Empty,
                    7)],
                [new FrontierShipForSaleSnapshot(
                    5,
                    "Sidewinder",
                    32_000,
                    string.Empty,
                    -1)],
                fetchedAt),
            CommunityGoals:
            [
                new FrontierCommunityGoalSnapshot(
                    6,
                    "Deliver medicines",
                    "Support the relief effort.",
                    "Deliver Basic Medicines",
                    "Global reward",
                    "Sol",
                    "Galileo",
                    fetchedAt.AddDays(2),
                    false,
                    5_000,
                    10_000,
                    250,
                    40,
                    "Tier 2",
                    25,
                    1_500_000,
                    10,
                    false,
                    DataPoints: [],
                    ActivityType: "tradelist",
                    HasPlayerContributionData: true,
                    HasContributorData: true),
            ],
            CommanderReputation:
            [
                new FrontierReputationSnapshot("Federation", 100),
            ],
            CommanderReputationFetchedAt: fetchedAt);
    }

    private static JournalEventEnvelope ParseJournalEvent(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error), error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }

    private sealed class StubAccountService : IFrontierAccountService
    {
        private FrontierAccountState state;
        private readonly FrontierAccountSnapshot? refreshed;
        private readonly Dictionary<string, FrontierAccountState> commanderStates =
            new(StringComparer.OrdinalIgnoreCase);

        public StubAccountService(
            FrontierAccountState state,
            FrontierAccountSnapshot? refreshed = null)
        {
            this.state = state;
            this.refreshed = refreshed;
        }

        public int RefreshCount { get; private set; }

        public event EventHandler? AuthorizationCallbackReceived;

        public string? ActiveFrontierId { get; private set; }

        public string? ActiveCommanderName { get; private set; }

        public void SetActiveCommander(string? frontierId, string? commanderName)
        {
            ActiveFrontierId = frontierId;
            ActiveCommanderName = commanderName;
        }

        public void SetState(FrontierAccountState value)
        {
            state = value;
        }

        public void SetStateForCommander(
            string frontierId,
            FrontierAccountState value)
        {
            commanderStates[frontierId] = value;
        }

        public Task<IReadOnlyList<FrontierLinkedCommander>> GetLinkedCommandersAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<FrontierLinkedCommander> linked = commanderStates
                .Where(pair => pair.Value.IsLinked)
                .Select(pair => new FrontierLinkedCommander(
                    pair.Key,
                    pair.Value.Snapshot?.CommanderName ?? pair.Key))
                .ToArray();
            return Task.FromResult(linked);
        }

        public Task<FrontierAccountState> GetStateAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                ActiveFrontierId is not null
                && commanderStates.TryGetValue(ActiveFrontierId, out var scoped)
                    ? scoped
                    : state);
        }

        public Task<FrontierAccountSnapshot> ConnectAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task CancelConnectionAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<FrontierAccountSnapshot> RefreshAsync(
            CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.FromResult(refreshed ?? state.Snapshot
                ?? throw new InvalidOperationException("No snapshot configured."));
        }

        public Task UnlinkAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        public void RaiseAuthorizationCallbackReceived()
        {
            AuthorizationCallbackReceived?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class StubCommunityGoalHistoryReader(
        CommunityGoalJournalHistoryReadResult result)
        : ICommunityGoalJournalHistoryReader
    {
        public string? RequestedFrontierId { get; private set; }

        public Task<CommunityGoalJournalHistoryReadResult> ReadAsync(
            string frontierId,
            CancellationToken cancellationToken = default)
        {
            RequestedFrontierId = frontierId;
            return Task.FromResult(result);
        }
    }
}
