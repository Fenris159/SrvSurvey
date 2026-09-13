using SrvSurvey.Desktop.Localization;

namespace SrvSurvey.Desktop.Tests.Localization;

[Collection(LocalizationTestCollection.Name)]
public sealed class LocalizationCatalogTests : IDisposable
{
    [Fact]
    public void LanguageSelectorContainsEverySupportedLanguage()
    {
        Assert.Equal(
            ["en", "de", "es", "fr", "pt-BR", "ru", "zh-Hans", "ps"],
            LocalizationCatalog.Languages.Select(language => language.Code)
        );
    }

    [Fact]
    public void LegacyGermanCatalogIsEmbeddedWithoutEncodingLoss()
    {
        LocalizationCatalog.Initialize("de");

        Assert.Equal("de", LocalizationCatalog.CurrentLanguage);
        Assert.Equal(1_090, LocalizationCatalog.LegacyTranslationCount);
        Assert.Equal(7_839, LocalizationCatalog.ApplicationTranslationCount);
        Assert.Equal(7_839, LocalizationCatalog.SourceCount);
        Assert.Equal("Himmelskörper", LocalizationCatalog.Translate("Bodies"));
        Assert.Equal("Neues Lesezeichen", LocalizationCatalog.Translate("New bookmark"));
    }

    [Fact]
    public void EveryShippedLegacyLanguageCatalogRetainsAllSourceStrings()
    {
        foreach (var language in LocalizationCatalog.Languages.Where(language => language.Code != "en"))
        {
            LocalizationCatalog.Initialize(language.Code);

            Assert.Equal(1_090, LocalizationCatalog.LegacyTranslationCount);
            Assert.Equal(LocalizationCatalog.SourceCount, LocalizationCatalog.ApplicationTranslationCount);
        }
    }

    [Theory]
    [InlineData("Close", "Schließen")]
    [InlineData("CURRENT SYSTEM", "AKTUELLES SYSTEM")]
    [InlineData("ATMOSPHERE", "ATMOSPHÄRE")]
    [InlineData("SEARCH GUIDANCE", "SUCHANLEITUNG")]
    [InlineData("Next jump", "Nächster Sprung")]
    public void SafeLegacyLabelVariantsReuseUniqueTranslations(string source, string expected)
    {
        LocalizationCatalog.Initialize("de");

        Assert.Equal(expected, LocalizationCatalog.Translate(source));
    }

    [Fact]
    public void DynamicAvaloniaFormatRetainsRuntimeValues()
    {
        LocalizationCatalog.Initialize("de");
        var template = LocalizationCatalog.Translate("Loaded {0} active Raven Colonial projects.");

        Assert.Equal(
            template.Replace("{0}", "3", StringComparison.Ordinal),
            LocalizationCatalog.Translate("Loaded 3 active Raven Colonial projects.")
        );
        Assert.NotEqual("Loaded {0} active Raven Colonial projects.", template);
    }

    [Fact]
    public void SurfaceMiningCommandExamplesRemainExecutableInEveryTranslation()
    {
        KeyValuePair<string, string[]>[] examples =
        [
            new(
                "Adds a listed Hotspot List commodity at a bearing and distance from your live position anywhere inside the saved map border, followed by that deposit's mineral amount and density. Use l, m, or h for Low, Medium, or High; full words also work. A same-commodity marker within 100 m is declined as a likely duplicate. Example: .mine 342 jadeite 1.87 h/m",
                [".mine 342 jadeite 1.87 h/m"]
            ),
            new(
                "Adds a listed commodity and its mineral amount and density at your current surface coordinates. Use l, m, or h for Low, Medium, or High; full words also work. This precise placement may overlap an existing marker. Example: .mine monazite m/h here",
                [".mine monazite m/h here"]
            ),
            new(
                "Drive to the orange location border, face the center marker, and send .mining <bearing> <border radius km> <location number>. Example: .mining 120 6.44 4.",
                [".mining <bearing> <border radius km> <location number>", ".mining 120 6.44 4"]
            ),
            new(
                "To correct a marker, stand at its true position and send .mine move <commodity> here. The nearest marker matching that commodity must be within 200 m. Example: .mine move haematite here.",
                [".mine move <commodity> here", ".mine move haematite here"]
            ),
            new(
                "While standing near a mapped deposit, send .mine rigs <number> to record its positive rig capacity on the nearest marker. The count remains visible in brackets when marker names are hidden. Example: .mine rigs 4.",
                [".mine rigs <number>", ".mine rigs 4"]
            ),
            new(".mining survey", [".mining survey"]),
            new(".mining waypoint <next|prev>", [".mining waypoint <next|prev>"]),
            new(".mining waypoint next", [".mining waypoint next"]),
            new(".mining waypoint prev", [".mining waypoint prev"]),
            new(".mining survey complete", [".mining survey complete"]),
            new(".mine splat", [".mine splat"]),
            new(".mine splat cancel", [".mine splat cancel"]),
        ];

        foreach (
            LocalizationLanguage language in LocalizationCatalog.Languages.Where(language =>
                language.Code is not "en" and not "ps"
            )
        )
        {
            LocalizationCatalog.Initialize(language.Code);
            foreach (KeyValuePair<string, string[]> example in examples)
            {
                string translation = LocalizationCatalog.Translate(example.Key);
                foreach (string command in example.Value)
                {
                    Assert.Contains(command, translation, StringComparison.Ordinal);
                }
            }
        }
    }

    [Theory]
    [InlineData("Codex details available · type .show", "Codex-Details verfügbar · Typ .show")]
    [InlineData("Reference image available · type .show", "Referenzbild verfügbar · Typ .show")]
    [InlineData("· FF bonus", "· FF-Bonus")]
    public void BiologyStatusTextUsesTheCorrectUnicodeCatalogKeys(string source, string expected)
    {
        LocalizationCatalog.Initialize("de");

        string translated = LocalizationCatalog.Translate(source);
        Assert.Equal(expected, translated);
        Assert.NotEqual(source, translated);
    }

    [Fact]
    public void UnknownTextStillFallsBackWithoutGuessing()
    {
        LocalizationCatalog.Initialize("de");

        Assert.Equal(
            "Text that is absent from every catalog",
            LocalizationCatalog.Translate("Text that is absent from every catalog")
        );
    }

    [Theory]
    [InlineData("DE", "de")]
    [InlineData("pt-br", "pt-BR")]
    [InlineData("zh-hans", "zh-Hans")]
    [InlineData("not-a-language", "en")]
    [InlineData(null, "en")]
    public void LanguageCodesAreNormalizedToSupportedValues(string? value, string expected)
    {
        Assert.Equal(expected, LocalizationCatalog.NormalizeLanguage(value));
    }

    public void Dispose()
    {
        LocalizationCatalog.Initialize("en");
    }
}
