using SrvSurvey.Desktop.Localization;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Localization;

[Collection(LocalizationTestCollection.Name)]
public sealed class GuidesLocalizedSearchTests
{
    /// <summary>Search accepts translated headings while retaining original chat commands.</summary>
    [Theory]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("zh-Hans")]
    public void TranslatedSubjectsAndEnglishCommandsAreSearchable(string language)
    {
        string previous = LocalizationCatalog.CurrentLanguage;
        try
        {
            LocalizationCatalog.Initialize(language);
            const string title = "Calibrate the six rig circles";
            string translated = LocalizationCatalog.Translate(title);
            Assert.NotEqual(title, translated);
            var guides = new GuidesViewModel(GuideCatalog.Create()) { SearchText = translated };
            Assert.Contains(guides.SearchResults, result => result.Title == title);
            guides.SearchText = ".mine splat";
            Assert.Contains(guides.SearchResults, result => result.Title == "Surface Mining maps");
        }
        finally
        {
            LocalizationCatalog.Initialize(previous);
        }
    }
}
