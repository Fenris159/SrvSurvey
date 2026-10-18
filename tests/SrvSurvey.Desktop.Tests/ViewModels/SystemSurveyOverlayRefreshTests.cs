using System.Collections;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class SystemSurveyViewModelTests
{
    /// <summary>Flight status must not replace unchanged lists bound by the survey overlay panels.</summary>
    [Theory]
    [InlineData("Fss")]
    [InlineData("LastFssRewards")]
    [InlineData("Dss")]
    [InlineData("BiologicalBodies")]
    [InlineData("BiologyBodies")]
    [InlineData("OrganismGroups")]
    [InlineData("Signals")]
    [InlineData("Materials")]
    [InlineData("Atmosphere")]
    [InlineData("Rings")]
    public void OverlayRefreshRetainsUnchangedSurveyRows(string list)
    {
        SystemSurveyViewModel model = CreateViewModel();
        var status = new EliteStatus
        {
            BodyName = "Test 1",
            Flags = StatusFlags.HasLatLong,
            PlanetRadius = 6000000,
            Heading = 90,
            GuiFocus = list == "BiologyBodies" ? GuiFocus.SystemMap : GuiFocus.NoFocus,
        };
        model.ApplyUpdate(
            [
                Parse("""{"event":"Location","StarSystem":"Test","SystemAddress":42}"""),
                Parse(BodyInformationScan),
                Parse(
                    """{"event":"FSSBodySignals","SystemAddress":42,"BodyName":"Test 1","BodyID":1,"Signals":[{"Type":"$SAA_SignalType_Biological;","Count":1}],"Genuses":[{"Genus":"$Codex_Ent_Aleoids_Genus_Name;","Genus_Localised":"Aleoida"}]}"""
                ),
            ],
            status
        );
        if (list != "Dss")
        {
            model.ApplyUpdate(
                [Parse("""{"event":"SAAScanComplete","SystemAddress":42,"BodyName":"Test 1","BodyID":1}""")],
                null
            );
        }

        object before = ReadOverlayRows(model, list);
        Assert.NotEmpty(Assert.IsType<IEnumerable>(before, exactMatch: false));
        model.ApplyUpdate([], status with { Heading = 180 });
        Assert.Same(before, ReadOverlayRows(model, list));
        Assert.Equal(180, model.CurrentStatus!.Heading);
    }

    private static object ReadOverlayRows(SystemSurveyViewModel model, string list) =>
        list switch
        {
            "Fss" => model.FssBodies,
            "LastFssRewards" => model.LastFssBiologyRewardBands,
            "Dss" => model.DssBodies,
            "BiologicalBodies" => model.BiologicalBodies,
            "BiologyBodies" => model.BiologySurveyDisplay.Bodies,
            "OrganismGroups" => model.BiologySurveyDisplay.OrganismGroups,
            "Signals" => model.BiologyStatus!.Signals,
            "Materials" => model.BodyInformationDisplay.Materials,
            "Atmosphere" => model.BodyInformationDisplay.AtmosphereComposition,
            "Rings" => model.BodyInformationDisplay.Rings,
            _ => throw new ArgumentOutOfRangeException(nameof(list)),
        };
}
