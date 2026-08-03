using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSiteVisitCatalog
{
    private readonly GuardianSiteVisit[] visits;

    private GuardianSiteVisitCatalog(IEnumerable<GuardianSiteVisit> visits)
    {
        this.visits = visits.ToArray();
    }

    public IReadOnlyList<GuardianSiteVisit> Visits => visits;

    public int VisitedCount => visits.Count(visit => visit.IsVisited);

    public int SurveyCompleteCount => visits.Count(visit => visit.IsSurveyComplete);

    public static GuardianSiteVisitCatalog Merge(
        GuardianSiteCatalog references,
        GuardianCommanderDataReadResult commanderData,
        GuardianPublishedSiteCatalog publishedSites,
        GuardianSurveyCompletionCalculator completionCalculator)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(commanderData);
        ArgumentNullException.ThrowIfNull(publishedSites);
        ArgumentNullException.ThrowIfNull(completionCalculator);

        var mergedReferences = references.Sites.ToList();
        foreach (var survey in commanderData.Surveys.Where(survey =>
                     !mergedReferences.Any(reference =>
                         reference.Kind != GuardianSiteKind.Beacon
                         && IsSameSurvey(reference, survey))))
        {
            mergedReferences.Add(CreateCommanderReference(survey, references));
        }

        foreach (var beacon in commanderData.Beacons.Where(beacon =>
                     !mergedReferences.Any(reference =>
                         reference.Kind == GuardianSiteKind.Beacon
                         && IsSameBody(
                             reference,
                             beacon.SystemAddress,
                             beacon.BodyId,
                             beacon.BodyName))))
        {
            mergedReferences.Add(CreateCommanderReference(beacon, references));
        }

        return new GuardianSiteVisitCatalog(
            mergedReferences.Select(reference => Merge(
                reference,
                commanderData,
                publishedSites,
                completionCalculator)));
    }

    private static GuardianSiteVisit Merge(
        GuardianSiteReference reference,
        GuardianCommanderDataReadResult commanderData,
        GuardianPublishedSiteCatalog publishedSites,
        GuardianSurveyCompletionCalculator completionCalculator)
    {
        if (reference.Kind == GuardianSiteKind.Beacon)
        {
            var beacon = commanderData.Beacons.FirstOrDefault(
                visit => IsSameBody(
                    reference,
                    visit.SystemAddress,
                    visit.BodyId,
                    visit.BodyName));
            return new GuardianSiteVisit(
                reference,
                beacon?.FirstVisited ?? DateTimeOffset.MinValue,
                beacon?.LastVisited ?? DateTimeOffset.MinValue,
                beacon?.Notes ?? string.Empty,
                reference.SurveyProgress,
                reference.IsSurveyComplete,
                beacon?.Path,
                beacon is not null,
                null,
                beacon?.ScannedLocations.Count ?? 0);
        }

        var survey = commanderData.Surveys.FirstOrDefault(
            candidate => IsSameSurvey(reference, candidate));
        if (survey is null)
        {
            return new GuardianSiteVisit(
                reference,
                DateTimeOffset.MinValue,
                DateTimeOffset.MinValue,
                string.Empty,
                reference.SurveyProgress,
                reference.IsSurveyComplete,
                null,
                false,
                null,
                0);
        }

        var published = publishedSites.Find(reference);
        var surveyData = new GuardianSurveyData
        {
            SiteType = string.Equals(
                survey.SiteType,
                "Unknown",
                StringComparison.OrdinalIgnoreCase)
                    ? reference.SiteType
                    : survey.SiteType,
            SiteHeading = survey.Survey.SiteHeading,
            RelicTowerHeading = survey.Survey.RelicTowerHeading,
            Location = survey.Survey.Location,
            PoiStatuses = survey.Survey.PoiStatuses,
            RelicHeadings = survey.Survey.RelicHeadings,
            ComponentMaterials = survey.Survey.ComponentMaterials,
            RawPointsOfInterest = survey.Survey.RawPointsOfInterest,
        };
        var completion = completionCalculator.Calculate(surveyData, published);
        var progress = reference.IsSurveyComplete
            ? reference.SurveyProgress
            : completion.Progress;
        return new GuardianSiteVisit(
            reference,
            survey.FirstVisited,
            survey.LastVisited,
            survey.Notes,
            progress,
            reference.IsSurveyComplete || completion.IsComplete,
            survey.Path,
            true,
            completion,
            survey.ActiveObelisks.Count);
    }

    private static bool IsSameSurvey(
        GuardianSiteReference reference,
        GuardianCommanderSiteSurvey survey)
    {
        if (!IsSameBody(
                reference,
                survey.SystemAddress,
                survey.BodyId,
                survey.BodyName))
        {
            return false;
        }

        if (reference.Kind == GuardianSiteKind.Ruins)
        {
            return survey.Index == reference.Index;
        }

        return string.Equals(
                survey.SiteType,
                "Unknown",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                survey.SiteType,
                reference.SiteType,
                StringComparison.OrdinalIgnoreCase);
    }

    private static GuardianSiteReference CreateCommanderReference(
        GuardianCommanderSiteSurvey survey,
        GuardianSiteCatalog references)
    {
        var kind = survey.Name.StartsWith(
            "$Ancient:#index=",
            StringComparison.Ordinal)
                ? GuardianSiteKind.Ruins
                : GuardianSiteKind.Structure;
        var position = GetKnownSystemPosition(references, survey.SystemAddress);
        return new GuardianSiteReference(
            0,
            kind,
            survey.SystemName,
            survey.SystemAddress,
            RemoveSystemPrefix(survey.BodyName, survey.SystemName),
            survey.BodyId,
            survey.SiteType,
            survey.Index,
            0,
            position,
            survey.Survey.Location?.Latitude,
            survey.Survey.Location?.Longitude,
            survey.Survey.SiteHeading,
            survey.Survey.RelicTowerHeading,
            0,
            survey.LastVisited,
            null,
            null,
            true);
    }

    private static GuardianSiteReference CreateCommanderReference(
        GuardianCommanderBeaconVisit beacon,
        GuardianSiteCatalog references)
    {
        var location = beacon.ScannedLocations
            .OrderByDescending(pair => pair.Key)
            .Select(pair => (GuardianSurfaceLocation?)pair.Value)
            .FirstOrDefault();
        return new GuardianSiteReference(
            0,
            GuardianSiteKind.Beacon,
            beacon.SystemName,
            beacon.SystemAddress,
            RemoveSystemPrefix(beacon.BodyName, beacon.SystemName),
            beacon.BodyId,
            "Beacon",
            0,
            0,
            GetKnownSystemPosition(references, beacon.SystemAddress),
            location?.Latitude,
            location?.Longitude,
            -1,
            -1,
            0,
            beacon.LastVisited,
            null,
            null,
            true);
    }

    private static GalacticCoordinate GetKnownSystemPosition(
        GuardianSiteCatalog references,
        long systemAddress)
    {
        return references.FindBySystemAddress(systemAddress)
            .Select(reference => (GalacticCoordinate?)reference.Position)
            .FirstOrDefault()
            ?? new GalacticCoordinate(0, 0, 0);
    }

    private static bool IsSameBody(
        GuardianSiteReference reference,
        long systemAddress,
        int bodyId,
        string bodyName)
    {
        if (reference.SystemAddress != systemAddress)
        {
            return false;
        }

        return reference.BodyId >= 0 && bodyId >= 0
            ? reference.BodyId == bodyId
            : string.Equals(
                reference.BodyName,
                RemoveSystemPrefix(bodyName, reference.SystemName),
                StringComparison.OrdinalIgnoreCase);
    }

    private static string RemoveSystemPrefix(string bodyName, string systemName)
    {
        return bodyName.StartsWith(
            systemName,
            StringComparison.OrdinalIgnoreCase)
                ? bodyName[systemName.Length..].Trim()
                : bodyName;
    }
}

public sealed record GuardianSiteVisit(
    GuardianSiteReference Reference,
    DateTimeOffset FirstVisited,
    DateTimeOffset LastVisited,
    string Notes,
    int SurveyProgress,
    bool IsSurveyComplete,
    string? CommanderFilePath,
    bool HasCommanderData,
    GuardianSurveyCompletion? Completion,
    int RecordedObeliskOrLocationCount)
{
    public bool IsVisited => LastVisited != DateTimeOffset.MinValue;
}
