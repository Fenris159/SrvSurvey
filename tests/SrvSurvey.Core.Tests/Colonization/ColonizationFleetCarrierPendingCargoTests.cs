using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationFleetCarrierPendingCargoTests
{
    [Fact]
    public void NeedsServerBaselineWhenCargoMissingOrEmpty()
    {
        Assert.True(ColonizationFleetCarrierPendingCargo.NeedsServerBaseline(null));
        Assert.True(ColonizationFleetCarrierPendingCargo.NeedsServerBaseline(new Dictionary<string, int>()));
        Assert.True(
            ColonizationFleetCarrierPendingCargo.NeedsServerBaseline(new Dictionary<string, int> { ["steel"] = 0 })
        );
        Assert.False(
            ColonizationFleetCarrierPendingCargo.NeedsServerBaseline(new Dictionary<string, int> { ["steel"] = 1 })
        );
    }

    [Fact]
    public void MergesQueuedDeltasAndDropsCancelledCommodities()
    {
        var pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        ColonizationFleetCarrierPendingCargo.MergeDelta(pending, new Dictionary<string, int> { ["Steel"] = 10 });
        ColonizationFleetCarrierPendingCargo.MergeDelta(
            pending,
            new Dictionary<string, int> { ["$Steel_Name;"] = -4, ["Water"] = 3 }
        );
        ColonizationFleetCarrierPendingCargo.MergeDelta(pending, new Dictionary<string, int> { ["water"] = -3 });

        Assert.Equal(6, pending["steel"]);
        Assert.False(pending.ContainsKey("water"));
    }
}
