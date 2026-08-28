using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class GuardianUncataloguedSelectionTests
{
    [Fact]
    public async Task InitialSiteTypeRevealsAndOpensActiveUncataloguedSurvey()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-guardian-selection-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var viewModel = new GuardianViewModel(root);
            await viewModel.LoadProfileAsync("F123", isOdyssey: true);
            var previous = Assert.IsType<GuardianSiteRowViewModel>(viewModel.SelectedSite);
            viewModel.FilterText = previous.Reference.SystemName;

            await viewModel.ApplyJournalEventsAsync(
            [
                Parse("""{"event":"Location","StarSystem":"Diagnostic Uncatalogued","SystemAddress":9000000000001}"""),
                Parse("""{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":9000000000001,"BodyID":7,"BodyName":"Diagnostic Uncatalogued A 1","Latitude":10,"Longitude":20}"""),
            ],
            "Drew");

            Assert.Equal(previous.Reference, viewModel.SelectedSite?.Reference);
            await viewModel.ApplyJournalEventsAsync(
                [Parse("""{"event":"SendText","Message":"b"}""")],
                "Drew");

            Assert.Equal(string.Empty, viewModel.FilterText);
            Assert.Equal(
                9000000000001,
                viewModel.SelectedSite?.Reference.SystemAddress);
            Assert.Equal(1, viewModel.SelectedWorkspaceTabIndex);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(json, out var parsed, out var error),
            error);
        return Assert.IsType<JournalEventEnvelope>(parsed);
    }
}
