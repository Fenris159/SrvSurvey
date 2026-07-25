using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Core.Tests.Guardian;

public sealed class GuardianSurveyReferenceTests
{
    [Fact]
    public void EmbeddedTemplatesPreserveEveryLegacyMapPoint()
    {
        var catalog = GuardianSiteTemplateCatalog.LoadEmbedded();

        Assert.Equal(13, catalog.Templates.Count);
        var beta = Assert.IsType<GuardianSiteTemplate>(catalog.Find("beta"));
        Assert.Equal("Beta", beta.Name);
        Assert.Equal(new GuardianMapPoint(487, 556), beta.ImageOffset);
        Assert.Equal(390, beta.PointsOfInterest.Count);
        Assert.Equal(50, beta.SurveyPoints.Count);
        Assert.Equal(8, beta.RelicTowers.Count);
        Assert.Contains(
            beta.PointsOfInterest,
            point => point.Type == GuardianPoiType.BrokenObelisk);
    }

    [Fact]
    public void EmbeddedPublishedCatalogMatchesEveryKnownSurveySite()
    {
        var sites = GuardianSiteCatalog.LoadEmbedded();
        var published = GuardianPublishedSiteCatalog.LoadEmbedded();

        Assert.Equal(729, published.Count);
        Assert.All(
            sites.Sites.Where(site => site.Kind != GuardianSiteKind.Beacon),
            site => Assert.NotNull(published.Find(site)));

        var gr1 = published.Find(GuardianSiteKind.Ruins, 1);
        Assert.NotNull(gr1);
        Assert.Equal("Beta", gr1.SiteType);
        Assert.Equal(332, gr1.SiteHeading);
        Assert.Equal(93, gr1.RelicTowerHeading);
        Assert.Equal(
            new GuardianSurfaceLocation(-46.576923, 133.985107),
            gr1.Location);
        Assert.Equal(10, gr1.ActiveObelisks.Count);
        Assert.Contains(
            gr1.ActiveObelisks,
            obelisk => obelisk.Name == "A08"
                && obelisk.LogCode == "H9"
                && obelisk.ItemCodes.SequenceEqual(["ca", "ca"]));
    }

    [Fact]
    public void CompletionMatchesEveryPublishedLegacySummary()
    {
        var sites = GuardianSiteCatalog.LoadEmbedded();
        var published = GuardianPublishedSiteCatalog.LoadEmbedded();
        var calculator = new GuardianSurveyCompletionCalculator(
            GuardianSiteTemplateCatalog.LoadEmbedded());

        var differences = new List<string>();
        foreach (var reference in sites.Sites.Where(
            site => site.Kind != GuardianSiteKind.Beacon))
        {
            var publicSurvey = Assert.IsType<GuardianPublishedSite>(
                published.Find(reference));
            var completion = calculator.Calculate(
                new GuardianSurveyData
                {
                    SiteType = reference.SiteType,
                    Location = publicSurvey.Location,
                },
                publicSurvey);
            if (completion.Progress != reference.SurveyProgress)
            {
                differences.Add(
                    $"{reference.DisplayId}: {completion.Progress} != "
                        + reference.SurveyProgress);
            }
        }

        Assert.Empty(differences);
    }

    [Fact]
    public void LocalSurveyValuesOverridePublishedFallbacks()
    {
        var templates = GuardianSiteTemplateCatalog.LoadEmbedded();
        var published = GuardianPublishedSiteCatalog.LoadEmbedded()
            .Find(GuardianSiteKind.Ruins, 1);
        var calculator = new GuardianSurveyCompletionCalculator(templates);
        var firstKnownPoint = published!.PoiStatuses.Keys.First();
        var survey = new GuardianSurveyData
        {
            SiteType = "Beta",
            SiteHeading = 332,
            RelicTowerHeading = 93,
            Location = published.Location,
            PoiStatuses = new Dictionary<string, GuardianPoiStatus>
            {
                [firstKnownPoint] = GuardianPoiStatus.Unknown,
            },
        };

        var completion = calculator.Calculate(survey, published);

        Assert.Equal(98, completion.Progress);
        Assert.False(completion.IsComplete);
        Assert.False(calculator.IsSurveyComplete(survey, published));
    }

    [Fact]
    public void LegacyRawPointsAreImplicitlyPresentForCompletion()
    {
        var template = new GuardianSiteTemplate(
            "Test",
            "Test",
            string.Empty,
            new GuardianMapPoint(0, 0),
            1,
            [],
            [],
            new Dictionary<string, GuardianMapPoint>());
        var survey = new GuardianSurveyData
        {
            SiteType = "Test",
            SiteHeading = 0,
            Location = new GuardianSurfaceLocation(0, 0),
            RawPointsOfInterest =
            [
                new GuardianPointOfInterest(
                    "x1",
                    GuardianPoiType.Orb,
                    10,
                    20,
                    0),
            ],
        };
        var calculator = new GuardianSurveyCompletionCalculator(
            new GuardianSiteTemplateCatalog([template]));

        var completion = calculator.Calculate(survey);

        Assert.Equal(1, completion.ConfirmedPointCount);
        Assert.Equal(1, completion.PresentPuddleCount);
        Assert.Equal(100, completion.Progress);
        Assert.True(completion.IsComplete);
    }

    [Fact]
    public void TemplateLoadRejectsUnknownPointType()
    {
        using var stream = new MemoryStream(
            "{\"Alpha\":{\"poi\":[{\"name\":\"x\",\"type\":\"mystery\"}]}}"u8
                .ToArray());

        Assert.Throws<InvalidDataException>(
            () => GuardianSiteTemplateCatalog.Load(stream));
    }
}
