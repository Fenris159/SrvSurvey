using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningMissionTrackerTests
{
    [Fact]
    public void DeliveryIsCumulativeAndCargoCannotBeAllocatedTwice()
    {
        var tracker = new MiningMissionTracker();
        Apply(tracker, """{"event":"MissionAccepted","Name":"Mission_Mining","MissionID":1,"Commodity":"$platinum_name;","Count":10}""");
        Apply(tracker, """{"event":"MissionAccepted","Name":"Mission_Mining","MissionID":2,"Commodity":"platinum","Count":10}""");
        Apply(tracker, """{"event":"CargoDepot","MissionID":1,"UpdateType":"Deliver","ItemsDelivered":4}""");
        Apply(tracker, """{"event":"CargoDepot","MissionID":1,"UpdateType":"Deliver","ItemsDelivered":4}""");
        tracker.UpdateCargo([new CargoItem("platinum", null, 8, 0)]);
        Assert.Equal(4, tracker.Missions[0].Delivered);
        Assert.Equal(6, tracker.Missions[0].OnBoard);
        Assert.Equal(2, tracker.Missions[1].OnBoard);
        Apply(tracker, """{"event":"MissionCompleted","MissionID":1}""");
        Assert.Equal("Completed", tracker.Missions[0].Status);
    }

    private static void Apply(MiningMissionTracker tracker, string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out var entry, out _));
        tracker.Apply(entry!);
    }
}
