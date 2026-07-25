using System.Globalization;

namespace SrvSurvey.Core.Exobiology;

public sealed class BiologyPredictionEvaluator
{
    public const double MaterialPresenceThreshold = 0.25;

    private readonly BiologyCriteriaCatalog catalog;

    public BiologyPredictionEvaluator(BiologyCriteriaCatalog catalog)
    {
        this.catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
    }

    public BiologyPredictionResult Evaluate(
        BiologyPredictionContext context,
        BiologyPredictionKnowledge? knowledge = null,
        string? targetVariant = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        knowledge ??= BiologyPredictionKnowledge.Empty;

        var state = new EvaluationState(context, knowledge, targetVariant);
        foreach (var criteria in catalog.Roots)
        {
            state.Evaluate(
                criteria,
                genus: null,
                species: null,
                variant: null,
                commonChildren: null,
                inheritedClauses: []);
        }

        return state.CreateResult();
    }

    private sealed class EvaluationState
    {
        private readonly BiologyPredictionContext context;
        private readonly BiologyPredictionKnowledge knowledge;
        private readonly string? targetVariant;
        private readonly HashSet<string> predictions = new(
            StringComparer.Ordinal);
        private readonly HashSet<string> missingProperties = new(
            StringComparer.Ordinal);
        private readonly List<BiologyCriteriaClause> targetClauses = [];

        public EvaluationState(
            BiologyPredictionContext context,
            BiologyPredictionKnowledge knowledge,
            string? targetVariant)
        {
            this.context = context;
            this.knowledge = knowledge;
            this.targetVariant = targetVariant;
        }

        public bool Evaluate(
            BiologyCriteriaNode criteria,
            string? genus,
            string? species,
            string? variant,
            IReadOnlyList<BiologyCriteriaNode>? commonChildren,
            IReadOnlyList<BiologyCriteriaClause> inheritedClauses)
        {
            commonChildren = criteria.CommonChildren ?? commonChildren;
            genus = criteria.Genus ?? genus;
            species = criteria.Species ?? species;
            variant = criteria.Variant ?? variant;

            if (targetVariant is null && ShouldSkipKnown(genus, species))
            {
                return false;
            }

            if (!criteria.Query.All(Matches))
            {
                return false;
            }

            var currentClauses = inheritedClauses
                .Concat(criteria.Query.Where(
                    clause => clause.Operator != BiologyCriteriaOperator.Comment))
                .ToArray();
            var currentName = FormatPredictionName(genus, species, variant);
            var targetMatch = false;
            if (currentName is not null)
            {
                targetMatch = string.Equals(
                    targetVariant,
                    currentName,
                    StringComparison.Ordinal);
                if (targetVariant is null || targetMatch)
                {
                    predictions.Add(currentName);
                }

                if (targetMatch)
                {
                    targetClauses.AddRange(currentClauses);
                }
            }

            var children = criteria.UseCommonChildren
                ? commonChildren
                : criteria.Children;
            if (children is null)
            {
                return targetMatch;
            }

            foreach (var child in children)
            {
                targetMatch |= Evaluate(
                    child,
                    genus,
                    species,
                    variant,
                    commonChildren,
                    currentClauses);
            }

            return targetMatch;
        }

        public BiologyPredictionResult CreateResult()
        {
            return new BiologyPredictionResult(
                predictions.Order(StringComparer.Ordinal).ToArray(),
                missingProperties.Order(StringComparer.Ordinal).ToArray(),
                targetClauses
                    .DistinctBy(clause => clause.RawText, StringComparer.Ordinal)
                    .ToArray());
        }

        private bool ShouldSkipKnown(string? genus, string? species)
        {
            if (!string.IsNullOrEmpty(genus)
                && knowledge.AllGeneraKnown
                && knowledge.KnownGenera.Count > 0
                && !knowledge.KnownGenera.Contains(
                    genus,
                    StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            return genus is not null
                && species is not null
                && knowledge.KnownSpeciesByGenus.Keys.Contains(
                    genus,
                    StringComparer.OrdinalIgnoreCase);
        }

        private bool Matches(BiologyCriteriaClause clause)
        {
            if (clause.Operator == BiologyCriteriaOperator.Comment)
            {
                return true;
            }

            if (!TryGetValue(clause.Property, out var bodyValue))
            {
                missingProperties.Add(clause.Property);
                return false;
            }

            return clause.Operator switch
            {
                BiologyCriteriaOperator.Is => MatchesAny(clause, bodyValue),
                BiologyCriteriaOperator.All => MatchesAll(clause, bodyValue),
                BiologyCriteriaOperator.Not => MatchesNone(clause, bodyValue),
                BiologyCriteriaOperator.Range => MatchesRange(clause, bodyValue),
                BiologyCriteriaOperator.Composition =>
                    MatchesComposition(clause, bodyValue),
                _ => throw new InvalidOperationException(
                    $"Unsupported biology criteria operator: {clause.Operator}"),
            };
        }

        private bool MatchesAny(
            BiologyCriteriaClause clause,
            object bodyValue)
        {
            if (clause.Property == "mats"
                && bodyValue is IReadOnlyDictionary<string, double> materials)
            {
                return clause.Values.Any(value => materials.Any(
                    material => string.Equals(
                            material.Key,
                            value,
                            StringComparison.OrdinalIgnoreCase)
                        && material.Value > MaterialPresenceThreshold));
            }

            var bodyValues = ToStrings(bodyValue);
            if (clause.Property == "body")
            {
                return clause.Values.Any(value => bodyValues.Any(
                    body => body.StartsWith(
                        value,
                        StringComparison.OrdinalIgnoreCase)));
            }

            if (clause.Property == "volcanism")
            {
                if (clause.Values[0] == "Any")
                {
                    return bodyValues.Any(
                        value => !value.Equals(
                            "None",
                            StringComparison.OrdinalIgnoreCase));
                }

                return clause.Values.Any(value => bodyValues.Any(
                    body => body.Contains(
                        value,
                        StringComparison.OrdinalIgnoreCase)));
            }

            return clause.Values.Any(value => bodyValues.Any(
                body => body.Equals(
                    value,
                    StringComparison.OrdinalIgnoreCase)));
        }

        private static bool MatchesAll(
            BiologyCriteriaClause clause,
            object bodyValue)
        {
            var bodyValues = ToStrings(bodyValue);
            return clause.Values.All(value => bodyValues.Any(
                body => body.Equals(
                    value,
                    StringComparison.OrdinalIgnoreCase)));
        }

        private static bool MatchesNone(
            BiologyCriteriaClause clause,
            object bodyValue)
        {
            var bodyValues = ToStrings(bodyValue);
            return !clause.Values.Any(value => bodyValues.Any(
                body => body.Equals(
                    value,
                    StringComparison.OrdinalIgnoreCase)));
        }

        private static bool MatchesRange(
            BiologyCriteriaClause clause,
            object bodyValue)
        {
            if (bodyValue is not double value)
            {
                throw new InvalidOperationException(
                    $"Biology criteria '{clause}' requires a numeric value.");
            }

            return (clause.Minimum is null || value >= clause.Minimum)
                && (clause.Maximum is null || value <= clause.Maximum);
        }

        private static bool MatchesComposition(
            BiologyCriteriaClause clause,
            object bodyValue)
        {
            if (bodyValue is not IReadOnlyDictionary<string, double> composition)
            {
                throw new InvalidOperationException(
                    $"Biology criteria '{clause}' requires a composition.");
            }

            return clause.Compositions.Any(requirement => composition.Any(
                item => item.Key.Equals(
                        requirement.Key,
                        StringComparison.OrdinalIgnoreCase)
                    && item.Value >= requirement.Value));
        }

        private bool TryGetValue(string property, out object value)
        {
            object? candidate = property switch
            {
                "body" => context.PlanetClass,
                "gravity" => context.SurfaceGravity,
                "temp" => context.SurfaceTemperature,
                "pressure" => context.SurfacePressure,
                "atmosphere" => context.Atmosphere,
                "atmosType" => context.AtmosphereType,
                "atmosComp" => NormalizeAtmosphereComposition(
                    context.AtmosphereComposition),
                "matsComp" or "mats" => context.Materials,
                "dist" => context.DistanceFromArrivalLs,
                "volcanism" => context.Volcanism,
                "regions" => context.RegionId?.ToString(
                    CultureInfo.InvariantCulture),
                "star" => context.StarTypes,
                "parentStar" => context.ParentStarTypes,
                "primaryStar" => context.PrimaryStarType,
                "nebulae" => context.NebulaDistanceLy,
                "guardian" => context.IsWithinGuardianBubble?.ToString(),
                _ => throw new InvalidOperationException(
                    $"Unsupported biology criteria property: {property}"),
            };

            if (candidate is null)
            {
                value = null!;
                return false;
            }

            value = candidate;
            return true;
        }

        private static IReadOnlyDictionary<string, double>?
            NormalizeAtmosphereComposition(
                IReadOnlyDictionary<string, double>? composition)
        {
            if (composition?.Count != 1)
            {
                return composition;
            }

            var item = composition.First();
            return new Dictionary<string, double>(
                StringComparer.OrdinalIgnoreCase)
            {
                [item.Key] = 100,
            };
        }

        private static IReadOnlyList<string> ToStrings(object value)
        {
            return value switch
            {
                string single => [single],
                IReadOnlyList<string> list => list,
                IReadOnlyDictionary<string, double> dictionary =>
                    dictionary.Keys.ToArray(),
                _ => throw new InvalidOperationException(
                    $"Biology criteria expected text values, not {value.GetType().Name}."),
            };
        }

        private static string? FormatPredictionName(
            string? genus,
            string? species,
            string? variant)
        {
            if (genus is null || species is null || variant is null)
            {
                return null;
            }

            return variant.Length == 0
                ? species
                : $"{genus} {species} - {variant}".Trim();
        }
    }
}

public sealed record BiologyPredictionContext
{
    public string? PlanetClass { get; init; }

    public double? SurfaceGravity { get; init; }

    public double? SurfaceTemperature { get; init; }

    public double? SurfacePressure { get; init; }

    public string? Atmosphere { get; init; }

    public string? AtmosphereType { get; init; }

    public IReadOnlyDictionary<string, double>? AtmosphereComposition { get; init; }

    public double? DistanceFromArrivalLs { get; init; }

    public string? Volcanism { get; init; }

    public IReadOnlyDictionary<string, double>? Materials { get; init; }

    public int? RegionId { get; init; }

    public IReadOnlyList<string>? StarTypes { get; init; }

    public IReadOnlyList<string>? ParentStarTypes { get; init; }

    public string? PrimaryStarType { get; init; }

    public double? NebulaDistanceLy { get; init; }

    public bool? IsWithinGuardianBubble { get; init; }
}

public sealed record BiologyPredictionKnowledge
{
    public static BiologyPredictionKnowledge Empty { get; } = new();

    public bool AllGeneraKnown { get; init; }

    public IReadOnlyCollection<string> KnownGenera { get; init; } = [];

    public IReadOnlyDictionary<string, string> KnownSpeciesByGenus { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record BiologyPredictionResult(
    IReadOnlyList<string> Predictions,
    IReadOnlyList<string> MissingProperties,
    IReadOnlyList<BiologyCriteriaClause> TargetClauses)
{
    public bool HasCompleteContext => MissingProperties.Count == 0;
}
