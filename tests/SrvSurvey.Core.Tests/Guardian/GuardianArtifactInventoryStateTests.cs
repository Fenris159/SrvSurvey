using SrvSurvey.Core.Guardian;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianArtifactInventoryStateTests
{
    [Fact]
    public void ResetMapsCargoNamesAndIgnoresUnrelatedCommodities()
    {
        var state = new GuardianArtifactInventoryState();
        var cargo = new CargoSnapshot(
            DateTimeOffset.UtcNow,
            "Cargo",
            "SRV",
            8,
            [
                new CargoItem("ancientcasket", "Guardian Casket", 2, 0),
                new CargoItem("ANCIENTORB", "Guardian Orb", 1, 0),
                new CargoItem("gold", "Gold", 5, 0),
            ]);

        Assert.True(state.Reset(cargo));

        Assert.Equal(2, state.GetCount("ca"));
        Assert.Equal(2, state.GetCount("casket"));
        Assert.Equal(1, state.GetCount("Guardian Orb"));
        Assert.Equal(0, state.GetCount("gold"));
        Assert.False(state.Reset(cargo));
    }

    [Fact]
    public void RequirementsPreserveDuplicateArtifactQuantities()
    {
        var state = new GuardianArtifactInventoryState();
        state.Reset(new CargoSnapshot(
            DateTimeOffset.UtcNow,
            "Cargo",
            "SRV",
            2,
            [new CargoItem("ancientcasket", null, 1, 0)]));

        var requirements = state.GetRequirements(["ca", "casket", "or"]);

        var casket = Assert.Single(
            requirements,
            requirement => requirement.ShortCode == "ca");
        Assert.Equal(2, casket.Required);
        Assert.Equal(1, casket.Available);
        Assert.False(casket.IsMet);
        Assert.False(state.HasItems(["ca", "ca"]));
        Assert.True(state.HasItems(["casket"]));
    }

    [Fact]
    public void ApplyTracksCollectAndEjectWithoutNegativeCounts()
    {
        var state = new GuardianArtifactInventoryState();

        Assert.True(state.Apply(Event(
            "CollectCargo",
            "\"Type\":\"ancienttablet\"")));
        Assert.True(state.Apply(Event(
            "CollectCargo",
            "\"Type\":\"ancienttablet\"")));
        Assert.True(state.Apply(Event(
            "EjectCargo",
            "\"Type\":\"ancienttablet\",\"Count\":5")));

        Assert.Equal(0, state.GetCount("ta"));
        Assert.False(state.Apply(Event(
            "EjectCargo",
            "\"Type\":\"ancienttablet\",\"Count\":1")));
        Assert.False(state.Apply(Event(
            "CollectCargo",
            "\"Type\":\"gold\"")));
    }

    [Fact]
    public void CargoJournalInventoryReplacesStaleCounts()
    {
        var state = new GuardianArtifactInventoryState();
        state.Reset(new CargoSnapshot(
            DateTimeOffset.UtcNow,
            "Cargo",
            "Ship",
            3,
            [new CargoItem("ancienturn", null, 3, 0)]));

        Assert.True(state.Apply(Event(
            "Cargo",
            "\"Vessel\":\"SRV\",\"Inventory\":["
                + "{\"Name\":\"ancientorb\",\"Count\":1},"
                + "{\"Name\":\"ANCIENTORB\",\"Count\":1}]")));

        Assert.Equal(0, state.GetCount("ur"));
        Assert.Equal(2, state.GetCount("or"));
    }

    [Fact]
    public void CargoJournalWithoutInventoryDoesNotClearState()
    {
        var state = new GuardianArtifactInventoryState();
        state.Reset(new CargoSnapshot(
            DateTimeOffset.UtcNow,
            "Cargo",
            "Ship",
            1,
            [new CargoItem("ancientrelic", null, 1, 0)]));

        Assert.False(state.Apply(Event("Cargo", "\"Vessel\":\"Ship\",\"Count\":1")));

        Assert.Equal(1, state.GetCount("re"));
    }

    private static JournalEventEnvelope Event(string name, string properties)
    {
        var json = $"{{\"timestamp\":\"2026-07-24T12:00:00Z\","
            + $"\"event\":\"{name}\",{properties}}}";
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var journalEvent, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }
}
