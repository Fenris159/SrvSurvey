using System.Globalization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningCargoReconciliationTests
{
    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    public void CargoConfirmsBothSameSecondOutputsAndRepeatedNotifications(int notifications, int actual)
    {
        var run = new Replay();
        run.Start();
        JournalEventEnvelope last = null!;
        for (int i = 0; i < notifications; i++)
        {
            last = run.Apply("MiningRefined", "\"Type\":\"$platinum_name;\"", second: 3);
        }
        Assert.True(run.Current.HasUnconfirmedYield);
        run.Apply("Cargo", $"\"Count\":{actual}");
        Assert.Equal(actual, run.Current.RefinedTons);
        Assert.False(run.Current.HasUnconfirmedYield);
        Assert.Equal(actual, run.Current.Collections.Sum(c => c.Count));
        run.Apply(last);
        Assert.Equal(actual, run.Current.RefinedTons);
    }

    [Fact]
    public void ExistingCargoPurchasesTransfersCollectionsAndLimpetsAreNotMiningYield()
    {
        var run = new Replay();
        run.Start(11); // Ten existing tons and the first prospector limpet.
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":11");
        foreach (
            (string name, string fields, int total) in new (string, string, int)[]
            {
                ("MarketBuy", "\"Type\":\"gold\",\"Count\":5", 16),
                ("CollectCargo", "\"Type\":\"gold\"", 17),
                ("CargoTransfer", "\"Transfers\":[{\"Direction\":\"toship\",\"Count\":3}]", 20),
                ("EjectCargo", "\"Type\":\"gold\",\"Count\":4", 16),
                ("MarketSell", "\"Type\":\"gold\",\"Count\":2", 14),
                ("BuyDrones", "\"Count\":10", 24),
                ("SellDrones", "\"Count\":2", 22),
                ("LaunchDrone", "\"Type\":\"Repair\"", 21),
                ("ColonisationContribution", "\"Contributions\":[{\"Amount\":2}]", 19),
                ("MissionCompleted", "\"CommodityReward\":[{\"Count\":3}]", 22),
            }
        )
        {
            run.Apply(name, fields);
            run.Apply("Cargo", $"\"Count\":{total}");
            Assert.Equal(1, run.Current.RefinedTons);
        }
    }

    [Fact]
    public void CargoConfirmationDuringTravelDoesNotResumeTheClockAndCarryoverIsSeparate()
    {
        var run = new Replay();
        run.Start();
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("SupercruiseEntry");
        DateTimeOffset? paused = run.Current.PausedAt;
        run.Apply("Cargo", "\"Count\":1");
        Assert.Equal(paused, run.Current.PausedAt);
        Assert.False(run.Current.HasUnconfirmedYield);
        run.Apply("Docked");
        MiningSession completed = Assert.Single(run.State.Data.History);
        double rate = completed.TonsPerHour;
        run.Apply("CargoTransfer", "\"Transfers\":[{\"Direction\":\"tocarrier\",\"Count\":1}]");
        run.Apply("Cargo", "\"Count\":0");
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":2");
        Assert.Null(run.State.Session.Current);
        Assert.Equal(1, completed.RefinedTons);
        Assert.Equal(2, completed.CarryoverTons);
        Assert.Equal(rate, completed.TonsPerHour, 6);
    }

    [Fact]
    public void ManualPauseAndNonShipOrMalformedSnapshotsCannotAddYield()
    {
        var run = new Replay();
        run.Start();
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":1");
        run.Current.PausedAt = DateTimeOffset.Parse("2026-10-28T12:00:10Z", CultureInfo.InvariantCulture);
        run.Apply("MiningRefined", "\"Type\":\"gold\"");
        run.Apply("Cargo", "\"Count\":2");
        Assert.Equal(1, run.Current.RefinedTons);
        run.Apply("Cargo", "\"Count\":99,\"Vessel\":\"SRV\"");
        run.Apply("Cargo", "\"Count\":-1");
        run.Apply("Cargo", "\"Count\":\"invalid\"");
        Assert.Equal(1, run.Current.RefinedTons);
    }

    [Fact]
    public void DelayedLimpetSnapshotsBeforeRefiningDoNotLoseTheFirstTon()
    {
        var run = new Replay();
        run.Start(3);
        run.Apply("Cargo", "\"Count\":3"); // First launch is not reflected yet.
        run.Apply("LaunchDrone", "\"Type\":\"Collection\"");
        run.Apply("Cargo", "\"Count\":1");
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":2");
        Assert.Equal(1, run.Current.RefinedTons);
        Assert.False(run.Current.HasUnconfirmedYield);
    }

    [Fact]
    public void MixedMineralSplitIsEstimatedButTotalAndEngineeringMaterialsArePreserved()
    {
        var run = new Replay();
        run.Start();
        run.Apply("MaterialCollected", "\"Category\":\"Raw\",\"Name\":\"iron\",\"Count\":3");
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("MiningRefined", "\"Type\":\"gold\"");
        run.Apply("Cargo", "\"Count\":5"); // Cargo total can exceed the notification count.
        Assert.Equal(5, run.Current.RefinedTons);
        Assert.Equal(3, run.Current.EngineeringCount);
        Assert.True(run.Current.EstimatedMineralBreakdown);
        Assert.False(run.Current.HasUnconfirmedYield);
        Assert.Contains("mineral split is estimated", MiningReport.Text([run.Current]));
        Assert.Contains("mineral split is estimated", MiningReport.Html([run.Current]));
        run.Apply("MiningRefined", "\"Type\":\"gold\"");
        run.Apply("Cargo", "\"Count\":4");
        Assert.Equal(4, run.Current.RefinedTons);
        Assert.Equal(3, run.Current.EngineeringCount);
    }

    [Fact]
    public void MissingInitialCargoAndImportedSummariesRemainEstimates()
    {
        var run = new Replay();
        run.Apply("LaunchDrone", "\"Type\":\"Prospector\"");
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":20");
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":21");
        Assert.True(run.Current.HasUnconfirmedYield);
        Assert.Equal(2, run.Current.RefinedTons);
        Assert.Contains("cargo confirmation is incomplete", MiningReport.Text([run.Current]));
        Assert.Contains("cargo confirmation is incomplete", MiningReport.Html([run.Current]));
        MiningSession imported = Assert.Single(MiningReportImport.ReadCsv(MiningReport.Csv([run.Current])));
        Assert.True(imported.HasUnconfirmedYield);
    }

    [Fact]
    public void OldCargoSnapshotsAndMalformedExternalQuantitiesCannotRewriteConfirmedYield()
    {
        var run = new Replay();
        run.Start();
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":1");
        run.Apply("Cargo", "\"Count\":99", second: 1);
        run.Apply("MarketBuy", "\"Count\":\"invalid\"");
        run.Apply("CargoTransfer", "\"Transfers\":[{\"Direction\":\"unknown\",\"Count\":10}]");
        run.Apply("Cargo", "\"Count\":1");
        Assert.Equal(1, run.Current.RefinedTons);
        Assert.False(run.Current.HasUnconfirmedYield);
    }

    [Theory]
    [InlineData("Synthesis", "\"Name\":\"Limpet Basic\"", 5)]
    [InlineData("Died", "", 0)]
    [InlineData("ShipyardBuy", "", 0)]
    [InlineData("ShipyardSwap", "", 10)]
    [InlineData("CargoDepot", "\"UpdateType\":\"Deliver\"", 0)]
    [InlineData("MissionAccepted", "", 10)]
    [InlineData("MissionCompleted", "", 0)]
    [InlineData("MissionAbandoned", "", 0)]
    [InlineData("MissionFailed", "", 0)]
    public void CargoOperationsWithoutExactQuantitiesPreserveConfirmedGrossYield(string name, string fields, int cargo)
    {
        var run = new Replay();
        run.Start();
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":1");
        run.Apply(name, fields);
        run.Apply("Cargo", $"\"Count\":{cargo}");
        Assert.Equal(1, run.Current.RefinedTons);
        Assert.False(run.Current.HasUnconfirmedYield);
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", $"\"Count\":{cargo + 1}");
        Assert.Equal(2, run.Current.RefinedTons);
        Assert.False(run.Current.HasUnconfirmedYield);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefineryOutputOverlappingAnUnquantifiedCargoChangeRemainsAnEstimate(bool pendingBefore)
    {
        var run = new Replay();
        run.Start();
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":1");
        if (pendingBefore)
        {
            run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        }
        run.Apply("Synthesis", "\"Name\":\"Limpet Basic\"");
        if (!pendingBefore)
        {
            run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        }
        run.Apply("Cargo", "\"Count\":6");
        Assert.Equal(2, run.Current.RefinedTons);
        Assert.True(run.Current.HasUnconfirmedYield);
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":7");
        Assert.Equal(3, run.Current.RefinedTons);
        Assert.True(run.Current.HasUnconfirmedYield);
    }

    [Fact]
    public void SynthesisThatDoesNotChangeCargoDoesNotInterruptConfirmation()
    {
        var run = new Replay();
        run.Start();
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":1");
        run.Apply("Synthesis", "\"Name\":\"FSD Basic\"");
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":2");
        Assert.Equal(2, run.Current.RefinedTons);
        Assert.False(run.Current.HasUnconfirmedYield);
    }

    /// <summary>Late snapshots cannot turn production observed during a manual pause into active yield.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PausedProductionStaysExcludedWhenCargoArrivesAfterResume(bool snapshotWhilePaused, bool reopen)
    {
        var run = new Replay();
        run.Start();
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":1");
        run.State.Session.Pause(DateTimeOffset.Parse("2026-10-28T12:00:05Z", CultureInfo.InvariantCulture));
        run.Apply("MiningRefined", "\"Type\":\"platinum\"", second: 6);
        if (snapshotWhilePaused)
        {
            run.Apply("Cargo", "\"Count\":2", second: 7);
        }
        if (reopen)
        {
            run.Reopen();
        }
        run.State.Session.Resume(DateTimeOffset.Parse("2026-10-28T12:00:08Z", CultureInfo.InvariantCulture));
        if (!snapshotWhilePaused)
        {
            run.Apply("Cargo", "\"Count\":2", second: 9);
        }
        Assert.Equal(1, run.Current.RefinedTons);
        run.Apply("MiningRefined", "\"Type\":\"platinum\"", second: 10);
        run.Apply("Cargo", "\"Count\":3", second: 11);
        Assert.Equal(2, run.Current.RefinedTons);
        Assert.False(run.Current.HasUnconfirmedYield);
    }

    /// <summary>Unconfirmed pre-pause production is retained as an estimate when a paused reading cannot attribute it.</summary>
    [Fact]
    public void ConfirmationDuringManualPauseDoesNotEraseEarlierProvisionalYield()
    {
        var run = new Replay();
        run.Start();
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.Apply("Cargo", "\"Count\":1");
        run.Apply("MiningRefined", "\"Type\":\"platinum\"");
        run.State.Session.Pause(DateTimeOffset.Parse("2026-10-28T12:00:06Z", CultureInfo.InvariantCulture));
        run.Apply("Cargo", "\"Count\":2", second: 7);
        run.State.Session.Resume(DateTimeOffset.Parse("2026-10-28T12:00:08Z", CultureInfo.InvariantCulture));
        run.Apply("MiningRefined", "\"Type\":\"platinum\"", second: 9);
        run.Apply("Cargo", "\"Count\":3", second: 10);
        Assert.Equal(3, run.Current.RefinedTons);
        Assert.True(run.Current.HasUnconfirmedYield);
    }

    /// <summary>Pausing a session before any production does not create a permanent uncertainty flag.</summary>
    [Fact]
    public void PausingAnEmptySessionKeepsSubsequentCargoConfirmationUsable()
    {
        var run = new Replay();
        run.Start();
        run.State.Session.Pause(DateTimeOffset.Parse("2026-10-28T12:00:03Z", CultureInfo.InvariantCulture));
        run.Apply("Cargo", "\"Count\":0", second: 4);
        run.State.Session.Resume(DateTimeOffset.Parse("2026-10-28T12:00:05Z", CultureInfo.InvariantCulture));
        run.Apply("MiningRefined", "\"Type\":\"platinum\"", second: 6);
        run.Apply("Cargo", "\"Count\":1", second: 7);
        Assert.Equal(1, run.Current.RefinedTons);
        Assert.False(run.Current.HasUnconfirmedYield);
    }

    private sealed class Replay
    {
        private int offset;
        public MiningWorkspaceState State { get; private set; } = new(new MiningCommanderData());
        public MiningSession Current => State.Session.Current!;

        public void Start(int count = 1)
        {
            Apply("Cargo", $"\"Count\":{count}");
            Apply("LaunchDrone", "\"Type\":\"Prospector\"");
        }

        public void Reopen() => State = new MiningWorkspaceState(MiningStore.Parse(MiningStore.Export(State.Data)));

        public JournalEventEnvelope Apply(string name, string fields = "", int? second = null)
        {
            int record = ++offset;
            string extra = fields.Length > 0 ? "," + fields : "";
            Assert.True(
                JournalEventEnvelope.TryParse(
                    $$"""{"event":"{{name}}","timestamp":"2026-10-28T12:00:{{(second ?? record).ToString("00", CultureInfo.InvariantCulture)}}Z"{{extra}}} """,
                    out JournalEventEnvelope? entry,
                    out _
                )
            );
            entry = entry! with
            {
                SourceRecordIdentity = "physical-record:" + record.ToString(CultureInfo.InvariantCulture),
            };
            Apply(entry);
            return entry;
        }

        public void Apply(JournalEventEnvelope entry) => State.Apply(entry, false, "Sol", "Ring", "Python");
    }
}
