namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSurveyCompletionCalculator(
    GuardianSiteTemplateCatalog templates)
{
    private readonly GuardianSiteTemplateCatalog templates = templates
        ?? throw new ArgumentNullException(nameof(templates));

    public GuardianSurveyCompletion Calculate(
        GuardianSurveyData survey,
        GuardianPublishedSite? published = null)
    {
        ArgumentNullException.ThrowIfNull(survey);
        var siteType = string.Equals(
            survey.SiteType,
            "Unknown",
            StringComparison.OrdinalIgnoreCase)
                ? published?.SiteType
                : survey.SiteType;
        var template = templates.Find(siteType);
        if (template is null)
        {
            return GuardianSurveyCompletion.Empty;
        }

        var isRuins = IsRuins(siteType);
        var points = survey.RawPointsOfInterest is null
            ? template.SurveyPoints
            : template.SurveyPoints
                .Concat(survey.RawPointsOfInterest)
                .Where(point => point.Type is not GuardianPoiType.Obelisk
                    and not GuardianPoiType.BrokenObelisk)
                .ToArray();
        var siteHeading = survey.SiteHeading != -1
            ? survey.SiteHeading
            : published?.SiteHeading ?? -1;
        var relicTowerHeading = survey.RelicTowerHeading != -1
            ? survey.RelicTowerHeading
            : published?.RelicTowerHeading ?? -1;

        var tallies = TallyPoints(survey, published, points, isRuins);
        var score = (siteHeading != -1 ? 1 : 0) + tallies.Confirmed;
        var maxScore = points.Count + 1;
        if (isRuins)
        {
            maxScore++;
            if (relicTowerHeading != -1)
            {
                score++;
            }
        }
        else
        {
            maxScore += tallies.PresentRelics;
            score += tallies.RelicHeadingScore;
        }

        var progress = maxScore == 0
            ? 0
            : (int)(100d / maxScore * score);
        return new GuardianSurveyCompletion(
            score,
            maxScore,
            tallies.Confirmed,
            points.Count,
            tallies.PresentRelics,
            tallies.PresentPuddles,
            template.PointsOfInterest.Count(
                point => IsBasicPoi(point.Type)),
            tallies.RelicsNeedingHeading,
            progress,
            progress == 100);
    }

    private static PointTallies TallyPoints(
        GuardianSurveyData survey,
        GuardianPublishedSite? published,
        IReadOnlyList<GuardianPointOfInterest> points,
        bool isRuins)
    {
        var confirmed = 0;
        var presentRelics = 0;
        var presentPuddles = 0;
        var relicsNeedingHeading = 0;
        var relicHeadingScore = 0;

        foreach (var point in points)
        {
            var status = GetStatus(survey, published, point.Name);
            if (status != GuardianPoiStatus.Unknown)
            {
                confirmed++;
            }

            if (point.Type == GuardianPoiType.Relic
                && status == GuardianPoiStatus.Present)
            {
                presentRelics++;
                if (GetRelicHeading(survey, published, point.Name) is null)
                {
                    relicsNeedingHeading++;
                }
                else if (!isRuins)
                {
                    relicHeadingScore++;
                }
            }
            else if (status == GuardianPoiStatus.Present
                && IsBasicPoi(point.Type))
            {
                presentPuddles++;
            }
        }

        return new PointTallies(
            confirmed,
            presentRelics,
            presentPuddles,
            relicsNeedingHeading,
            relicHeadingScore);
    }

    private readonly record struct PointTallies(
        int Confirmed,
        int PresentRelics,
        int PresentPuddles,
        int RelicsNeedingHeading,
        int RelicHeadingScore);

    public bool IsSurveyComplete(
        GuardianSurveyData survey,
        GuardianPublishedSite? published = null)
    {
        ArgumentNullException.ThrowIfNull(survey);
        var siteType = string.Equals(
            survey.SiteType,
            "Unknown",
            StringComparison.OrdinalIgnoreCase)
                ? published?.SiteType
                : survey.SiteType;
        if (templates.Find(siteType) is null || survey.Location is null)
        {
            return false;
        }

        var siteHeading = survey.SiteHeading != -1
            ? survey.SiteHeading
            : published?.SiteHeading ?? -1;
        if (siteHeading == -1)
        {
            return false;
        }

        var relicTowerHeading = survey.RelicTowerHeading != -1
            ? survey.RelicTowerHeading
            : published?.RelicTowerHeading ?? -1;
        if (IsRuins(siteType) && relicTowerHeading == -1)
        {
            return false;
        }

        return Calculate(survey, published).IsComplete;
    }

    private static GuardianPoiStatus GetStatus(
        GuardianSurveyData survey,
        GuardianPublishedSite? published,
        string name)
    {
        if (survey.PoiStatuses.TryGetValue(name, out var local))
        {
            return local;
        }

        if (survey.RawPointsOfInterest?.Any(point => string.Equals(
                point.Name,
                name,
                StringComparison.Ordinal)) == true)
        {
            return GuardianPoiStatus.Present;
        }

        return published?.PoiStatuses.GetValueOrDefault(name)
            ?? GuardianPoiStatus.Unknown;
    }

    private static int? GetRelicHeading(
        GuardianSurveyData survey,
        GuardianPublishedSite? published,
        string name)
    {
        if (survey.RelicHeadings.TryGetValue(name, out var local))
        {
            return local;
        }

        var raw = survey.RawPointsOfInterest?.FirstOrDefault(
            point => string.Equals(point.Name, name, StringComparison.Ordinal));
        if (raw is not null)
        {
            return (int)raw.Rotation;
        }

        return published?.RelicHeadings.TryGetValue(name, out var publishedHeading)
            == true
                ? publishedHeading
                : null;
    }

    private static bool IsRuins(string? siteType)
    {
        return siteType is not null
            && (siteType.Equals("Alpha", StringComparison.OrdinalIgnoreCase)
                || siteType.Equals("Beta", StringComparison.OrdinalIgnoreCase)
                || siteType.Equals("Gamma", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBasicPoi(GuardianPoiType type)
    {
        return type is GuardianPoiType.Casket
            or GuardianPoiType.Orb
            or GuardianPoiType.Tablet
            or GuardianPoiType.Totem
            or GuardianPoiType.Urn
            or GuardianPoiType.Unknown;
    }
}

public sealed class GuardianSurveyData
{
    public string SiteType { get; init; } = "Unknown";

    public int SiteHeading { get; init; } = -1;

    public int RelicTowerHeading { get; init; } = -1;

    public GuardianSurfaceLocation? Location { get; init; }

    public IReadOnlyDictionary<string, GuardianPoiStatus> PoiStatuses { get; init; }
        = new Dictionary<string, GuardianPoiStatus>(
            StringComparer.Ordinal);

    public IReadOnlyDictionary<string, int> RelicHeadings { get; init; }
        = new Dictionary<string, int>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, GuardianComponentLoadout>
        ComponentMaterials
    { get; init; }
        = new Dictionary<string, GuardianComponentLoadout>(
            StringComparer.Ordinal);

    public IReadOnlyList<GuardianPointOfInterest>? RawPointsOfInterest { get; init; }
}

public sealed record GuardianSurveyCompletion(
    int Score,
    int MaxScore,
    int ConfirmedPointCount,
    int TotalPointCount,
    int PresentRelicCount,
    int PresentPuddleCount,
    int MaximumPuddleCount,
    int RelicsNeedingHeading,
    int Progress,
    bool IsComplete)
{
    public static GuardianSurveyCompletion Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        false);
}
