using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Exobiology;

public static class BiologyPredictionContextBuilder
{
    private static readonly Lazy<NebulaCatalog> DefaultNebulaCatalog = new(NebulaCatalog.LoadEmbedded);
    private static readonly Lazy<ExobiologyReferenceCatalog> DefaultReferenceCatalog = new(
        ExobiologyReferenceCatalog.LoadEmbedded
    );

    public static BiologyPredictionInputs? Build(
        SystemScanSnapshot system,
        int bodyId,
        NebulaCatalog? nebulaCatalog = null,
        ExobiologyReferenceCatalog? referenceCatalog = null
    )
    {
        ArgumentNullException.ThrowIfNull(system);
        SystemScanBodySnapshot? body = system.Bodies.FirstOrDefault(candidate => candidate.BodyId == bodyId);
        if (body is null || body.Kind != SystemBodyKind.LandablePlanet || body.Parents.Count == 0)
        {
            return null;
        }

        SystemScanBodySnapshot[] parentStars = GetParentStars(system.Bodies, body);
        var brightestStar = parentStars
            .Select(star => new { Star = star, Brightness = GetRelativeBrightness(system.Bodies, body, star) })
            .Where(candidate => candidate.Brightness > 0)
            .OrderByDescending(candidate => candidate.Brightness)
            .ThenBy(candidate => candidate.Star.BodyId)
            .FirstOrDefault();
        if (brightestStar is null || FlattenStarType(brightestStar.Star.StarClass) is not { } starType)
        {
            return null;
        }

        SystemScanBodySnapshot? primaryStar = system.Bodies.FirstOrDefault(IsMainStar);
        GalacticCoordinate? position = system.StarPosition;
        nebulaCatalog ??= DefaultNebulaCatalog.Value;
        var context = new BiologyPredictionContext
        {
            PlanetClass = body.PlanetClass,
            SurfaceGravity = body.SurfaceGravity / 10,
            SurfaceTemperature = body.SurfaceTemperature,
            SurfacePressure = body.SurfacePressure / 100_000,
            Atmosphere = body.Atmosphere?.Replace(" atmosphere", string.Empty, StringComparison.Ordinal),
            AtmosphereType = body.AtmosphereType,
            AtmosphereComposition = body.AtmosphereComposition,
            DistanceFromArrivalLs = body.DistanceFromArrivalLs,
            Volcanism = string.IsNullOrEmpty(body.Volcanism) ? "None" : body.Volcanism,
            Materials = body.Materials,
            RegionId = position is null ? null : GalacticRegionMap.Find(position.Value)?.Id,
            StarTypes = [starType],
            ParentStarTypes = parentStars
                .Select(star => FlattenStarType(star.StarClass))
                .Where(type => type is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            PrimaryStarType = FlattenStarType(primaryStar?.StarClass),
            NebulaDistanceLy = position is null ? null : nebulaCatalog.FindDistanceToClosest(position.Value),
            IsWithinGuardianBubble = position is null
                ? null
                : GuardianBubbleLocator.IsWithinKnownBubble(position.Value),
        };

        return new BiologyPredictionInputs(
            context,
            CreateKnowledge(body, referenceCatalog ?? DefaultReferenceCatalog.Value)
        );
    }

    public static string? FlattenStarType(string? starType)
    {
        if (string.IsNullOrEmpty(starType))
        {
            return null;
        }

        if (starType[0] is 'D' or 'W' or 'C')
        {
            return starType[0].ToString();
        }

        return starType.Length > 1 && starType[1] == '_' ? starType[0].ToString() : starType;
    }

    private static BiologyPredictionKnowledge CreateKnowledge(
        SystemScanBodySnapshot body,
        ExobiologyReferenceCatalog referenceCatalog
    )
    {
        var knownOrganisms = body
            .Organisms.Select(organism => new
            {
                Genus = ResolveGenusDisplayName(organism, referenceCatalog),
                Species = organism.SpeciesLocalized ?? organism.Species,
            })
            .Where(organism => !string.IsNullOrWhiteSpace(organism.Genus))
            .ToArray();
        string[] knownGenera = knownOrganisms
            .Select(organism => organism.Genus!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var knownSpecies = knownOrganisms
            .Where(organism => !string.IsNullOrWhiteSpace(organism.Species))
            .GroupBy(organism => organism.Genus!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Species!, StringComparer.OrdinalIgnoreCase);

        return new BiologyPredictionKnowledge
        {
            AllGeneraKnown = body.Organisms.Count > 0 && body.Organisms.Count == body.BiologicalSignalCount,
            KnownGenera = knownGenera,
            KnownSpeciesByGenus = knownSpecies,
        };
    }

    private static string? ResolveGenusDisplayName(
        SystemOrganismSnapshot organism,
        ExobiologyReferenceCatalog referenceCatalog
    )
    {
        ExobiologyReference? reference = organism.EntryId is > 0
            ? referenceCatalog.FindByEntryId(organism.EntryId.Value)
            : null;
        reference ??=
            referenceCatalog.FindByVariant(organism.Variant) ?? referenceCatalog.FindBySpecies(organism.Species);
        if (reference is not null)
        {
            return ExobiologyReferenceCatalog.GetGenusDisplayName(reference);
        }

        if (!string.IsNullOrWhiteSpace(organism.Genus))
        {
            return ExobiologyReferenceCatalog.GetGenusDisplayName(organism.Genus);
        }

        return organism.GenusLocalized;
    }

    private static SystemScanBodySnapshot[] GetParentStars(
        IReadOnlyList<SystemScanBodySnapshot> bodies,
        SystemScanBodySnapshot body
    )
    {
        var result = new Dictionary<int, SystemScanBodySnapshot>();
        foreach (SystemBodyParentSnapshot parent in body.Parents)
        {
            if (parent.Kind == SystemBodyParentKind.Star)
            {
                SystemScanBodySnapshot? star = bodies.FirstOrDefault(candidate =>
                    candidate.BodyId == parent.BodyId && candidate.Kind == SystemBodyKind.Star
                );
                if (star is not null)
                {
                    result.TryAdd(star.BodyId, star);
                }
            }
            else if (parent.Kind == SystemBodyParentKind.Null)
            {
                foreach (
                    SystemScanBodySnapshot? star in bodies.Where(candidate =>
                        candidate.Kind == SystemBodyKind.Star
                        && (candidate.BodyId == parent.BodyId || HasBarycentreParent(candidate, parent.BodyId))
                    )
                )
                {
                    result.TryAdd(star.BodyId, star);
                }
            }
        }

        return result.Values.ToArray();
    }

    private static bool HasBarycentreParent(SystemScanBodySnapshot body, int targetBodyId)
    {
        foreach (SystemBodyParentSnapshot parent in body.Parents)
        {
            if (parent.BodyId == targetBodyId)
            {
                return true;
            }

            if (parent.Kind != SystemBodyParentKind.Null)
            {
                return false;
            }
        }

        return false;
    }

    private static double GetRelativeBrightness(
        IReadOnlyList<SystemScanBodySnapshot> bodies,
        SystemScanBodySnapshot body,
        SystemScanBodySnapshot star
    )
    {
        SystemScanBodySnapshot? commonParent = GetParentBodies(bodies, body)
            .FirstOrDefault(parent =>
                parent.BodyId == star.BodyId
                || GetParentBodies(bodies, star).Any(starParent => starParent.BodyId == parent.BodyId)
            );
        double bodyDistance = GetSquaredPathDistance(bodies, body, commonParent);
        double starDistance = GetSquaredPathDistance(bodies, star, commonParent);
        double distance = Math.Sqrt(bodyDistance + starDistance);
        if (distance <= 0 || star.RadiusMeters <= 0 || star.SurfaceTemperature <= 0)
        {
            return 0;
        }

        double temperatureSquared = Math.Pow(star.SurfaceTemperature, 2);
        double relativeRadiance = star.RadiusMeters * temperatureSquared / distance;
        return Math.Pow(relativeRadiance, 2);
    }

    private static double GetSquaredPathDistance(
        IReadOnlyList<SystemScanBodySnapshot> bodies,
        SystemScanBodySnapshot body,
        SystemScanBodySnapshot? target
    )
    {
        if (target?.BodyId == body.BodyId)
        {
            return 0;
        }

        double distance = Math.Pow(body.SemiMajorAxis, 2);
        foreach (SystemScanBodySnapshot parent in GetParentBodies(bodies, body))
        {
            if (parent.BodyId == target?.BodyId)
            {
                return distance;
            }

            distance += Math.Pow(parent.SemiMajorAxis, 2);
        }

        return distance;
    }

    private static SystemScanBodySnapshot[] GetParentBodies(
        IReadOnlyList<SystemScanBodySnapshot> bodies,
        SystemScanBodySnapshot body
    )
    {
        return body
            .Parents.Select(parent => bodies.FirstOrDefault(candidate => candidate.BodyId == parent.BodyId))
            .Where(parent => parent is not null)
            .Cast<SystemScanBodySnapshot>()
            .ToArray();
    }

    private static bool IsMainStar(SystemScanBodySnapshot body)
    {
        return body.Kind == SystemBodyKind.Star && (body.BodyId == 0 || body.Name.EndsWith('A'));
    }
}

public sealed record BiologyPredictionInputs(BiologyPredictionContext Context, BiologyPredictionKnowledge Knowledge);
