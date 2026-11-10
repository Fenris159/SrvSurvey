using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Edsm;

namespace SrvSurvey.Core.Tests.Edsm;

[Trait("Category", "JournalUploadScenario")]
public sealed class EdsmContextQualityTests
{
    [Fact]
    public void MismatchedJournalSystemsNeverReceiveThePreviousSystemOrStation()
    {
        var context = new EdsmJournalContext();
        context.Apply(FirstLocation());
        var raw = JObject.Parse("""{"event":"Scan","SystemAddress":456,"BodyName":"Test B 1","MassEM":3560.053223}""");
        JObject output = context.AddTransientFields(raw);
        Assert.Equal(456, output.Value<long>("SystemAddress"));
        Assert.Equal(3560.053223, output.Value<double>("MassEM"));
        Assert.Null(output["_systemName"]);
        Assert.Null(output["_systemAddress"]);
        Assert.Null(output["_systemCoordinates"]);
        Assert.Null(output["_stationName"]);
        Assert.Null(output["_marketId"]);
    }

    [Fact]
    public void ANewSystemWithoutCoordinatesDoesNotInheritOldCoordinates()
    {
        var context = new EdsmJournalContext();
        context.Apply(FirstLocation());
        var docked = JObject.Parse(
            """{"event":"Docked","StarSystem":"Test B","SystemAddress":456,"StationName":"Cold Probabilities (ii-42)","MarketID":128000222}"""
        );
        context.Apply(docked);
        JObject output = context.AddTransientFields(docked);
        Assert.Equal("Test B", output.Value<string>("_systemName"));
        Assert.Equal(456, output.Value<long>("_systemAddress"));
        Assert.Equal("Cold Probabilities (ii-42)", output.Value<string>("_stationName"));
        Assert.Equal(128000222, output.Value<long>("_marketId"));
        Assert.Null(output["_systemCoordinates"]);
    }

    private static JObject FirstLocation() =>
        JObject.Parse(
            """{"event":"Location","StarSystem":"Test A","SystemAddress":123,"StarPos":[1,2,3],"Docked":true,"StationName":"First Port","MarketID":128000111}"""
        );
}
