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
        Assert.Equal(1_090, LocalizationCatalog.LegacyTranslationCount);
        Assert.Equal(5_528, LocalizationCatalog.ApplicationTranslationCount);
        Assert.Equal(5_528, LocalizationCatalog.SourceCount);
        Assert.Equal("Himmelskörper", LocalizationCatalog.Translate("Bodies"));
        Assert.Equal(
            "Plattformübergreifender Erkundungsbegleiter",
            LocalizationCatalog.Translate(
                "Cross-platform exploration companion"));
    }

    [Fact]
    public void EveryShippedLegacyLanguageCatalogRetainsAllSourceStrings()
    {
        foreach (var language in LocalizationCatalog.Languages.Where(
                     language => language.Code != "en"))
        {
            LocalizationCatalog.Initialize(language.Code);

            Assert.Equal(1_090, LocalizationCatalog.LegacyTranslationCount);
            Assert.Equal(
                LocalizationCatalog.SourceCount,
                LocalizationCatalog.ApplicationTranslationCount);
        }
    }

    [Theory]
    [InlineData("Close", "Schließen")]
    [InlineData("CURRENT SYSTEM", "AKTUELLES SYSTEM")]
    [InlineData("ATMOSPHERE", "ATMOSPHÄRE")]
    [InlineData("Next jump", "Nächster Sprung")]
    public void SafeLegacyLabelVariantsReuseUniqueTranslations(
        string source,
        string expected)
    {
        LocalizationCatalog.Initialize("de");

        Assert.Equal(expected, LocalizationCatalog.Translate(source));
    }

    [Fact]
    public void DynamicAvaloniaFormatRetainsRuntimeValues()
    {
        LocalizationCatalog.Initialize("de");
        var template = LocalizationCatalog.Translate(
            "Loaded {0} active Raven Colonial projects.");

        Assert.Equal(
            template.Replace("{0}", "3", StringComparison.Ordinal),
            LocalizationCatalog.Translate(
                "Loaded 3 active Raven Colonial projects."));
        Assert.NotEqual(
            "Loaded {0} active Raven Colonial projects.",
            template);
    }

    [Theory]
    [InlineData(
        "Codex details available · type .show",
        "Codex-Details verfügbar · Typ .show")]
    [InlineData(
        "Reference image available · type .show",
        "Referenzbild verfügbar · Typ .show")]
    [InlineData("· FF bonus", "· FF-Bonus")]
    public void BiologyStatusTextUsesTheCorrectUnicodeCatalogKeys(
        string source,
        string expected)
    {
        LocalizationCatalog.Initialize("de");

        Assert.Equal(expected, LocalizationCatalog.Translate(source));
    }

    [Fact]
    public void UnknownTextStillFallsBackWithoutGuessing()
    {
        LocalizationCatalog.Initialize("de");

        Assert.Equal(
            "Text that is absent from every catalog",
            LocalizationCatalog.Translate(
                "Text that is absent from every catalog"));
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
