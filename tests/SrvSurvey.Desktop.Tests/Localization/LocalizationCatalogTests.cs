using SrvSurvey.Desktop.Localization;

namespace SrvSurvey.Desktop.Tests.Localization;

[Collection(LocalizationTestCollection.Name)]
public sealed class LocalizationCatalogTests : IDisposable
{
    [Fact]
    public void LegacyGermanCatalogIsEmbeddedWithoutEncodingLoss()
    {
        LocalizationCatalog.Initialize("de");

        Assert.Equal("de", LocalizationCatalog.CurrentLanguage);
        Assert.Equal(1_090, LocalizationCatalog.TranslationCount);
        Assert.Equal("Himmelskörper", LocalizationCatalog.Translate("Bodies"));
        Assert.Equal(
            "Avalonia-only text",
            LocalizationCatalog.Translate("Avalonia-only text"));
    }

    [Fact]
    public void EveryShippedLegacyLanguageCatalogRetainsAllSourceStrings()
    {
        foreach (var language in LocalizationCatalog.Languages.Where(
                     language => language.Code != "en"))
        {
            LocalizationCatalog.Initialize(language.Code);

            Assert.Equal(1_090, LocalizationCatalog.TranslationCount);
        }
    }

    [Theory]
    [InlineData("DE", "de")]
    [InlineData("pt-br", "pt-BR")]
    [InlineData("zh-hans", "zh-Hans")]
    [InlineData("not-a-language", "en")]
    [InlineData(null, "en")]
    public void LanguageCodesAreNormalizedToSupportedValues(
        string? value,
        string expected)
    {
        Assert.Equal(expected, LocalizationCatalog.NormalizeLanguage(value));
    }

    public void Dispose()
    {
        LocalizationCatalog.Initialize("en");
    }
}
