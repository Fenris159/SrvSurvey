using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Localization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Localization;

[Collection(LocalizationTestCollection.Name)]
public sealed class PaidReceiptLocalizationTests : IDisposable
{
    [Theory]
    [InlineData(0, "1 gescannt, 0 DSS, 0 gelandet")]
    [InlineData(null, "1 gescannt, 0 DSS; Landungen nicht verfügbar")]
    public async Task PaidBodySummariesTranslateTheCompleteLandingPhrase(int? landed, string expected)
    {
        string data = Path.Combine(Path.GetTempPath(), "SrvSurvey-paid-translation-" + Guid.NewGuid());
        await using var model = new ExplorationHistoryViewModel(data, new CommanderProfileStore(data), () => []);
        var row = new ExplorationHistoryRowViewModel(
            new()
            {
                Status = ExplorationDataStatus.Paid,
                ScannedBodies = 1,
                Payment = new(100, 1, 1, landed, ["Alpha"], true),
            },
            model.CloseDetailsCommand
        );
        LocalizationCatalog.Initialize("de");
        Assert.Equal(expected, LocalizationCatalog.Translate(row.Contents));
    }

    public void Dispose() => LocalizationCatalog.Initialize("en");
}
