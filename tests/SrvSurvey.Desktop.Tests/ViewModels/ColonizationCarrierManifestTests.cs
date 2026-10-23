using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ColonizationCarrierManifestTests
{
    [Theory]
    [InlineData("initialize")]
    [InlineData("load")]
    [InlineData("refresh")]
    [InlineData("location")]
    [InlineData("cargo")]
    public async Task InvalidCarrierResponsesRemainRecoverableInTheEditor(string operation)
    {
        var client = new ManifestClient();
        bool saved = false;
        bool closed = false;
        using ColonizationCarrierManifestViewModel model = Create(client, _ => saved = true);
        model.CloseRequested += () => closed = true;
        var failure = new InvalidDataException("Malformed carrier response");
        if (operation == "initialize")
        {
            client.Failure = failure;
            await model.InitializeAsync();
        }
        else
        {
            await model.InitializeAsync();
            switch (operation)
            {
                case "load":
                    client.Read = () => Task.FromException<ColonizationFleetCarrier?>(failure);
                    await model.LoadSelectedAsync();
                    break;
                case "refresh":
                    client.RefreshFailure = failure;
                    await model.RefreshAsync();
                    break;
                case "location":
                    model.SystemName = "Achenar";
                    client.LocationWrite = () => throw failure;
                    await model.SaveAsync();
                    break;
                default:
                    client.CargoFailure = failure;
                    await model.SaveAsync();
                    break;
            }
        }
        Assert.Contains("Malformed carrier response", model.Status);
        Assert.False(model.IsBusy);
        Assert.False(saved);
        Assert.False(closed);
    }

    [Theory]
    [InlineData(43, 43)]
    [InlineData(0, 42)]
    [InlineData(999, 42)]
    public async Task PreferredCarrierSelectsLinkedTeammatesAndFallsBackWhenUnavailable(long preferred, long expected)
    {
        using var model = new ColonizationCarrierManifestViewModel(
            new ManifestClient(),
            "Cmdr",
            "key",
            () => true,
            _ => { }
        )
        {
            PreferredCarrierMarketId = preferred,
        };
        await model.InitializeAsync();
        Assert.Equal(expected, model.SelectedCarrier!.MarketId);
    }

    [Fact]
    public void RefreshAttemptStorePreservesOtherSettingsAndHandlesMalformedData()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "srvsurvey-carrier-cooldown-" + Guid.NewGuid().ToString("N"),
            "ui.json"
        );
        try
        {
            var store = new ColonizationSettingsStore(path);
            Assert.Null(store.LoadCarrierRefreshAttempt(42));
            store.SaveEnabled(true);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            store.SaveCarrierRefreshAttempt(42, now);
            store.SaveCarrierRefreshAttempt(42, now.AddSeconds(1));
            var reopened = new ColonizationSettingsStore(path);
            Assert.Equal(now.AddSeconds(1), reopened.LoadCarrierRefreshAttempt(42));
            Assert.Null(reopened.LoadCarrierRefreshAttempt(43));
            Assert.True(reopened.LoadEnabled());
            File.WriteAllText(path, """{"Colonization":{"CarrierRefreshAttempts":0}}""");
            Assert.Null(reopened.LoadCarrierRefreshAttempt(42));
            File.WriteAllText(path, """{"Colonization":{"CarrierRefreshAttempts":{"42":"invalid"}}}""");
            Assert.Null(reopened.LoadCarrierRefreshAttempt(42));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public async Task RefreshCooldownIncludesFailuresAndPersistsWhenTheEditorIsReopened()
    {
        var clock = new ManifestClock();
        var attempts = new Dictionary<long, DateTimeOffset>();
        var policy = new ColonizationCarrierRefreshCooldown(
            id => attempts.GetValueOrDefault(id),
            (id, time) => attempts[id] = time,
            clock
        );
        var client = new ManifestClient { RefreshFailure = new HttpRequestException("Try later") };
        using (
            var model = new ColonizationCarrierManifestViewModel(
                client,
                "Cmdr",
                "key",
                () => true,
                _ => { },
                refreshCooldown: policy
            )
        )
        {
            await model.InitializeAsync();
            await model.RefreshAsync();
            Assert.Contains("Try later", model.Status);
            Assert.False(model.CanRefresh);
            await model.RefreshAsync();
            Assert.Equal(1, client.RefreshCalls);
        }
        client.RefreshFailure = null;
        using var reopened = new ColonizationCarrierManifestViewModel(
            client,
            "Cmdr",
            "key",
            () => true,
            _ => { },
            refreshCooldown: new ColonizationCarrierRefreshCooldown(
                id => attempts.GetValueOrDefault(id),
                timeProvider: clock
            )
        );
        await reopened.InitializeAsync();
        Assert.False(reopened.CanRefresh);
        reopened.SelectedCarrier = reopened.Carriers[1];
        await reopened.LoadSelectedAsync();
        Assert.True(reopened.CanRefresh);
        reopened.SelectedCarrier = reopened.Carriers[0];
        await reopened.LoadSelectedAsync();
        clock.Now += TimeSpan.FromMinutes(5);
        Assert.True(reopened.CanRefresh);
        await reopened.RefreshAsync();
        Assert.Equal(2, client.RefreshCalls);
        Assert.False(reopened.CanRefresh);
    }

    [Fact]
    public async Task HiddenCargoKeepsEditsAcrossSortingTextModeAndSaving()
    {
        var client = new ManifestClient
        {
            Linked =
            [
                ManifestClient.First with
                {
                    Cargo = new()
                    {
                        ["steel"] = 100,
                        ["platinum"] = 7,
                        ["metalloys"] = 3,
                        ["tritium"] = 25,
                    },
                },
            ],
        };
        using ColonizationCarrierManifestViewModel model = Create(client);
        await model.InitializeAsync();
        Assert.True(model.HideNonColonyCommodities);
        Assert.Single(Assert.Single(model.Groups).Rows);
        model.HideNonColonyCommodities = false;
        Assert.Equal("Metals", Row(model, "platinum").Category);
        Assert.Equal("Industrial Materials", Row(model, "metalloys").Category);
        Row(model, "platinum").AmountText = "9";
        model.HideNonColonyCommodities = true;
        model.SortCommand.Execute(null);
        Assert.Single(Assert.Single(model.Groups).Rows);
        model.ToggleTextCommand.Execute(null);
        Assert.Contains("Platinum 9", model.BulkText);
        model.ToggleTextCommand.Execute(null);
        Assert.Single(Assert.Single(model.Groups).Rows);
        await model.SaveAsync();
        Assert.Equal(9, client.SentCargo!["platinum"]);
        Assert.Equal(25, client.SentCargo["tritium"]);
        Assert.Equal(3, client.SentCargo["metalloys"]);
    }

    private sealed class ManifestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task RefreshCannotBeRepeatedImmediately()
    {
        using ColonizationCarrierManifestViewModel model = Create(new());
        await model.InitializeAsync();
        Assert.True(model.CanRefresh);
        await model.RefreshAsync();
        Assert.False(model.CanRefresh);
    }

    [Fact]
    public async Task DefaultTableHidesNonColonyCargoButSavingPreservesIt()
    {
        var client = new ManifestClient
        {
            Linked =
            [
                ManifestClient.First with
                {
                    Cargo = new() { ["steel"] = 100, ["platinum"] = 7 },
                },
            ],
        };
        using ColonizationCarrierManifestViewModel model = Create(client);
        await model.InitializeAsync();
        Assert.DoesNotContain(model.Groups.SelectMany(group => group.Rows), row => row.Key == "platinum");
        await model.SaveAsync();
        Assert.Equal(7, client.SentCargo!["platinum"]);
    }

    [Fact]
    public async Task TextModeRoundTripPreservesLegacyKeysWithTheSameDisplayName()
    {
        var client = new ManifestClient
        {
            Linked =
            [
                ManifestClient.First with
                {
                    Cargo = new() { ["micro controllers"] = 55, ["microcontrollers"] = 60 },
                },
            ],
        };
        using ColonizationCarrierManifestViewModel model = Create(client);
        await model.InitializeAsync();
        model.ToggleTextCommand.Execute(null);
        Assert.True(model.TextMode);
        model.ToggleTextCommand.Execute(null);
        Assert.False(model.TextMode);
        Assert.Equal(55, Row(model, "micro controllers").Amount);
        Assert.Equal(60, Row(model, "microcontrollers").Amount);
    }

    [Fact]
    public async Task ReturningToTheTableKeepsThePreviousAmountsWhenTheTextIsInvalid()
    {
        using ColonizationCarrierManifestViewModel model = Create(new());
        await model.InitializeAsync();
        model.ToggleTextCommand.Execute(null);
        model.BulkText = "Steel nope";
        model.ToggleTextCommand.Execute(null);
        Assert.False(model.TextMode);
        Assert.Equal(100, Row(model, "steel").Amount);
        Assert.NotEmpty(model.Status);
    }

    [Fact]
    public async Task CommodityPickerSortsByCategoryThenName()
    {
        using ColonizationCarrierManifestViewModel model = Create(new());
        await model.InitializeAsync();
        Assert.Equal(
            model
                .AvailableCommodities.OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            model.AvailableCommodities
        );
    }

    [Fact]
    public async Task JournalComparisonUsesLocalTotalsStaysLiveAndNeverCopiesRavenEdits()
    {
        var local = new ColonizationLocalCarrierCargo();
        var client = new ManifestClient
        {
            Linked = [ManifestClient.First, ManifestClient.Second with { Cargo = new() { ["steel"] = 1 } }],
        };
        using ColonizationCarrierManifestViewModel model = Create(client, localCargo: local);
        await model.InitializeAsync();
        Assert.Equal("N/A", Row(model, "steel").JournalAmount);
        Assert.Equal("N/A", model.JournalTotal);
        local.Observe(LocalCarrier(240), DateTimeOffset.UnixEpoch);
        Assert.Equal("240", Row(model, "steel").JournalAmount);
        Assert.Equal("0", Row(model, "water").JournalAmount);
        Row(model, "steel").AmountText = "999";
        Assert.Equal("240", model.JournalTotal);
        await model.RefreshAsync();
        Assert.Equal("200", Row(model, "steel").AmountText);
        Assert.Equal("240", Row(model, "steel").JournalAmount);
        local.Observe(LocalCarrier(180), DateTimeOffset.UnixEpoch.AddMinutes(1));
        Assert.Equal("180", Row(model, "steel").JournalAmount);
        Assert.Equal("200", Row(model, "steel").AmountText);
        model.SelectedCarrier = model.Carriers[1];
        await model.LoadSelectedAsync();
        Assert.Equal("N/A", Row(model, "steel").JournalAmount);
        model.SelectedCarrier = model.Carriers[0];
        await model.LoadSelectedAsync();
        CarrierManifestRowViewModel row = Row(model, "steel");
        model.Dispose();
        local.Observe(LocalCarrier(50), DateTimeOffset.UnixEpoch.AddMinutes(2));
        Assert.Equal("180", row.JournalAmount);
    }

    internal static FrontierCarrierSnapshot LocalCarrier(int steel) =>
        new(
            "ABC-123",
            "Supply",
            "Sol",
            "NormalOperation",
            "All",
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            [],
            [new("Commodity", "steel", steel, 0)],
            [],
            [],
            [],
            [],
            Market: new(42, "ABC-123", "FleetCarrier", [], [], [], [], [], [], DateTimeOffset.UnixEpoch)
        );

    [Fact]
    public async Task SavesCargoWithoutInventingALocationForCarriersWhoseSystemIsUnknown()
    {
        var client = new ManifestClient { Linked = [ManifestClient.First with { SystemName = null }] };
        using ColonizationCarrierManifestViewModel model = Create(client);
        await model.InitializeAsync();
        Assert.Equal(string.Empty, model.SystemName);
        Assert.True(model.CanSave);
        Row(model, "steel").AmountText = "200";
        await model.SaveAsync();
        Assert.Equal(["cargo"], client.Writes);
        Assert.Equal(200, client.SentCargo!["steel"]);
    }

    [Fact]
    public async Task IncludesTeammateCarriersAndLoadsTheChosenManifestWithoutRenamingIt()
    {
        var client = new ManifestClient();
        using ColonizationCarrierManifestViewModel model = Create(client);
        await model.InitializeAsync();
        Assert.Equal(2, model.Carriers.Count);
        Assert.Equal("Other Cmdr", model.Owner);
        Assert.Equal("ABC-123", model.RawName);
        Assert.Equal("42", model.MarketId);
        Assert.Equal("Squadron and Friends", model.Access);
        Assert.Equal("Allowed", model.Notorious);
        Assert.Equal("Sol", model.SystemName);
        Assert.Equal("175", model.Total);
        Assert.True(model.CanSave);
        Assert.DoesNotContain(model.Groups, group => group.Name == "Other");
        model.SelectedCarrier = model.Carriers[1];
        Assert.False(model.HasCarrier);
        await model.LoadSelectedAsync();
        Assert.Equal("XYZ-987", model.RawName);
        Assert.Equal("Denied", model.Notorious);
        Assert.Equal("Unknown", model.Access);
        Assert.Equal("", model.Owner);
        Assert.Equal("43", model.MarketId);
    }

    [Fact]
    public async Task SaveSendsAbsoluteCountsAndZeroesForDeletedKeysAndUpdatesLocationBeforeClosing()
    {
        var client = new ManifestClient();
        ColonizationFleetCarrier? saved = null;
        using ColonizationCarrierManifestViewModel model = Create(client, carrier => saved = carrier);
        bool closed = false;
        model.CloseRequested += () => closed = true;
        await model.InitializeAsync();
        Row(model, "steel").AmountText = "300";
        Row(model, "water").RemoveCommand.Execute(null);
        model.SystemName = " Alpha Centauri ";
        await model.SaveAsync();
        Assert.True(closed);
        Assert.Equal(["location:Alpha Centauri", "cargo"], client.Writes);
        Assert.Equal(300, client.SentCargo!["steel"]);
        Assert.Equal(0, client.SentCargo["water"]);
        Assert.Equal(25, client.SentCargo["legacy spaced good"]);
        Assert.Equal("Team supply", saved!.DisplayName);
        Assert.Equal("Alpha Centauri", saved.SystemName);
        Assert.Equal(300, saved.Cargo["steel"]);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("overflow")]
    public async Task RejectsInvalidAmountsAndAllowsAddingRemovingAndSorting(string invalid)
    {
        using ColonizationCarrierManifestViewModel model = Create(new ManifestClient());
        model.AddCommodity();
        await model.InitializeAsync();
        Row(model, "steel").AmountText = invalid;
        Assert.False(model.CanSave);
        await model.SaveAsync();
        Row(model, "steel").AmountText = "50";
        Assert.True(model.CanSave);
        model.NewCommodity = model.AvailableCommodities.Single(item => item.Key == "basicmedicines");
        model.AddCommodity();
        Assert.Equal(0, Row(model, "basicmedicines").Amount);
        Row(model, "basicmedicines").AmountText = "11";
        Assert.Equal("136", model.Total);
        model.SortCommand.Execute(null);
        Assert.Equal("A–Z", model.SortLabel);
        Assert.False(Assert.Single(model.Groups).HasName);
        model.SortCommand.Execute(null);
        Assert.True(model.Groups.Count > 1);
    }

    [Fact]
    public async Task BulkEditingPreservesUnknownKeysValidatesDuplicatesAndSupportsClearingAllCargo()
    {
        var client = new ManifestClient();
        using ColonizationCarrierManifestViewModel model = Create(client);
        await model.InitializeAsync();
        model.ToggleTextCommand.Execute(null);
        Assert.Contains("Steel 100", model.BulkText);
        Assert.False(model.CanSave);
        model.BulkText = "Steel -1";
        model.ToggleTextCommand.Execute(null);
        Assert.False(model.TextMode);
        model.ToggleTextCommand.Execute(null);
        model.BulkText = "Steeel 10";
        model.ToggleTextCommand.Execute(null);
        Assert.Contains("Unknown commodity", model.Status);
        model.ToggleTextCommand.Execute(null);
        model.BulkText = "Steel 1\nsteel 2";
        model.ToggleTextCommand.Execute(null);
        Assert.Contains("only once", model.Status);
        model.ToggleTextCommand.Execute(null);
        model.BulkText = "Steel 10\nlegacy spaced good 15\nBasic Medicines 20";
        model.ToggleTextCommand.Execute(null);
        Assert.False(model.TextMode);
        model.HideNonColonyCommodities = false;
        Assert.Equal(15, Row(model, "legacy spaced good").Amount);
        Assert.Equal(20, Row(model, "basicmedicines").Amount);
        foreach (CarrierManifestRowViewModel row in model.Groups.SelectMany(group => group.Rows).ToArray())
        {
            row.RemoveCommand.Execute(null);
        }
        Assert.Equal("0", model.Total);
        await model.SaveAsync();
        Assert.All(client.SentCargo!, item => Assert.Equal(0, item.Value));
    }

    [Fact]
    public async Task RefreshReplacesUnsavedEditsAndExistingLocationCannotBeCleared()
    {
        var client = new ManifestClient();
        ColonizationFleetCarrier? saved = null;
        using ColonizationCarrierManifestViewModel model = Create(client, carrier => saved = carrier);
        await model.InitializeAsync();
        Row(model, "steel").AmountText = "999";
        await model.RefreshAsync();
        Assert.Equal(200, Row(model, "steel").Amount);
        Assert.NotNull(saved);
        model.SystemName = " ";
        Assert.False(model.CanSave);
    }

    [Fact]
    public async Task ErrorsRemainInThePopupAndEmptyOrMissingCarriersDoNotEnableSave()
    {
        var client = new ManifestClient { Failure = new HttpRequestException("Wait five minutes") };
        using ColonizationCarrierManifestViewModel model = Create(client);
        await model.InitializeAsync();
        Assert.Contains("Wait five minutes", model.Status);
        Assert.False(model.IsBusy);
        client.Failure = null;
        client.Linked = [];
        await model.InitializeAsync();
        Assert.Contains("No Fleet Carriers", model.Status);
        Assert.False(model.CanSave);
        client.Linked = [ManifestClient.First];
        client.Missing = true;
        await model.InitializeAsync();
        Assert.Contains("no longer", model.Status);
        Assert.False(model.HasCarrier);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingOrChangingContextDuringLocationWritePreventsCargoWrite(bool dispose)
    {
        var client = new ManifestClient();
        bool current = true;
        using var model = new ColonizationCarrierManifestViewModel(
            client,
            "Cmdr",
            "key",
            () => current,
            _ => Assert.Fail("Stale save")
        );
        await model.InitializeAsync();
        model.SystemName = "Achenar";
        client.LocationWrite = () =>
        {
            if (dispose)
            {
                model.Dispose();
            }
            else
            {
                current = false;
            }
        };
        await model.SaveAsync();
        Assert.Null(client.SentCargo);
        model.Dispose();
        model.Dispose();
        await model.RefreshAsync();
        await model.InitializeAsync();
        Assert.False(model.CanSave);
    }

    [Fact]
    public async Task BusyOperationsCannotSwitchCarriersOrOverlapAndLateReadsAreIgnored()
    {
        var client = new ManifestClient();
        using ColonizationCarrierManifestViewModel model = Create(client);
        await model.InitializeAsync();
        var response = new TaskCompletionSource<ColonizationFleetCarrier?>();
        client.Read = () => response.Task;
        model.SelectedCarrier = model.Carriers[1];
        Task read = model.LoadSelectedAsync();
        Assert.True(model.IsBusy);
        model.SelectedCarrier = model.Carriers[0];
        Assert.Equal(43, model.SelectedCarrier.MarketId);
        await model.LoadSelectedAsync();
        model.Dispose();
        response.SetResult(ManifestClient.Second);
        await read;
        Assert.False(model.HasCarrier);
    }

    internal static ColonizationCarrierManifestViewModel Create(
        ManifestClient client,
        Action<ColonizationFleetCarrier>? saved = null,
        ColonizationLocalCarrierCargo? localCargo = null
    ) => new(client, "Cmdr", "key", () => true, saved ?? (_ => { }), localCargo);

    internal static CarrierManifestRowViewModel Row(ColonizationCarrierManifestViewModel model, string key) =>
        model.Groups.SelectMany(group => group.Rows).Single(row => row.Key == key);

    internal sealed class ManifestClient : IRavenColonialCarrierManifestClient
    {
        public static ColonizationFleetCarrier First { get; } =
            new()
            {
                MarketId = 42,
                Name = "ABC-123",
                DisplayName = "Team supply",
                Owner = "Other Cmdr",
                SystemName = "Sol",
                Access = "SquadronAndFriends",
                Notorious = true,
                Cargo = new()
                {
                    ["steel"] = 100,
                    ["water"] = 50,
                    ["legacy spaced good"] = 25,
                },
            };
        public static ColonizationFleetCarrier Second { get; } =
            new()
            {
                MarketId = 43,
                Name = "XYZ-987",
                DisplayName = "Your supply",
                SystemName = "Sol",
            };
        public IReadOnlyList<ColonizationFleetCarrier> Linked { get; set; } = [First, Second];
        public Exception? Failure { get; set; }
        public bool Missing { get; set; }
        public Exception? RefreshFailure { get; set; }
        public Exception? CargoFailure { get; set; }
        public int RefreshCalls { get; private set; }
        public Func<Task<ColonizationFleetCarrier?>>? Read { get; set; }
        public Action? LocationWrite { get; set; }
        public List<string> Writes { get; } = [];
        public IReadOnlyDictionary<string, int>? SentCargo { get; private set; }

        public Task<IReadOnlyList<ColonizationFleetCarrier>> GetLinkedFleetCarriersAsync(
            string commander,
            CancellationToken cancellationToken
        ) =>
            Failure is null
                ? Task.FromResult(Linked)
                : Task.FromException<IReadOnlyList<ColonizationFleetCarrier>>(Failure);

        public Task<ColonizationFleetCarrier?> GetFleetCarrierAsync(
            long marketId,
            CancellationToken cancellationToken = default
        ) => Read?.Invoke() ?? Task.FromResult(Missing ? null : Linked.Single(carrier => carrier.MarketId == marketId));

        public Task<ColonizationFleetCarrier> RefreshFleetCarrierAsync(
            long marketId,
            string apiKey,
            CancellationToken cancellationToken
        )
        {
            RefreshCalls++;
            return RefreshFailure is not null
                ? Task.FromException<ColonizationFleetCarrier>(RefreshFailure)
                : Task.FromResult(
                    Linked.Single(item => item.MarketId == marketId) with
                    {
                        Cargo = new() { ["steel"] = 200 },
                    }
                );
        }

        public Task<ColonizationFleetCarrier> SetFleetCarrierLocationAsync(
            long marketId,
            string systemName,
            string apiKey,
            CancellationToken cancellationToken
        )
        {
            Writes.Add("location:" + systemName);
            LocationWrite?.Invoke();
            return Task.FromResult(First with { SystemName = systemName });
        }

        public Task<IReadOnlyDictionary<string, int>> ReplaceFleetCarrierCargoAsync(
            long marketId,
            IReadOnlyDictionary<string, int> cargo,
            string apiKey,
            CancellationToken cancellationToken = default
        )
        {
            Writes.Add("cargo");
            SentCargo = cargo;
            return CargoFailure is null
                ? Task.FromResult(cargo)
                : Task.FromException<IReadOnlyDictionary<string, int>>(CargoFailure);
        }
    }
}
