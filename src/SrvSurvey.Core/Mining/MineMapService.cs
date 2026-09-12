using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Mining;

public enum MineMapRating
{
    Low,
    Medium,
    High,
}

public sealed record MineMapMarker
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Material { get; init; } = string.Empty;

    public MineMapRating MineralAmount { get; init; }

    public MineMapRating Density { get; init; }

    public SurfaceCoordinate Location { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record MineMapSurvey
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string FrontierId { get; init; } = string.Empty;

    public string CommanderName { get; init; } = string.Empty;

    public string SystemName { get; init; } = string.Empty;

    public long SystemAddress { get; init; }

    public GalacticCoordinate SystemPosition { get; init; }

    public int BodyId { get; init; }

    public string BodyName { get; init; } = string.Empty;

    public string BodyType { get; init; } = string.Empty;

    public double ArrivalDistanceLs { get; init; }

    public int LocationSignal { get; init; }

    public string Name => $"Mining Location Signal {LocationSignal}";

    public double LocationRadiusMeters { get; init; }

    public double PlanetRadiusMeters { get; init; }

    public SurfaceCoordinate Center { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public IReadOnlyList<MineMapMarker> Markers { get; init; } = [];

    public string Notes { get; init; } = string.Empty;
}

public sealed record MineMapCommandContext(
    string FrontierId,
    string CommanderName,
    string SystemName,
    long SystemAddress,
    GalacticCoordinate SystemPosition,
    int BodyId,
    string BodyName,
    string BodyType,
    double ArrivalDistanceLs,
    double PlanetRadiusMeters,
    SurfaceCoordinate? PlayerLocation
);

public sealed record MineMapCommandResult(bool Succeeded, string Message, MineMapSurvey? Survey = null);

/// <summary>
/// Owns surface-mine survey command handling, spherical placement, and shared
/// bookmark persistence. Callers only provide live journal context and present
/// the returned feedback.
/// </summary>
public sealed class MineMapService : IDisposable
{
    public const double MarkerDeleteRadiusMeters = 500;
    public const double DuplicateMarkerRadiusMeters = 100;
    public const double MarkerMoveRadiusMeters = 200;
    private const double LocationBoundaryToleranceMeters = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly BookmarkCatalog bookmarks;
    private readonly string legacyDirectory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<MineMapSurvey> surveys;

    public MineMapService(string dataDirectory, BookmarkCatalog? bookmarkCatalog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        bookmarks = bookmarkCatalog ?? new BookmarkCatalog(dataDirectory);
        legacyDirectory = Path.Combine(dataDirectory, "mine-maps");
        MigrateLegacySurveys();
        surveys = ReadSurveys();
        bookmarks.Changed += OnBookmarksChanged;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<MineMapSurvey> Surveys => surveys;

    public MineMapSurvey? ActiveSurvey { get; private set; }

    public bool IsFavorite(Guid surveyId) =>
        bookmarks.Items.FirstOrDefault(bookmark => bookmark.Id == surveyId)?.IsFavorite == true;

    public bool SetFavorite(Guid surveyId, bool isFavorite)
    {
        var bookmark = bookmarks.Items.FirstOrDefault(candidate => candidate.Id == surveyId);
        if (bookmark is null)
        {
            return false;
        }

        if (bookmark.IsFavorite != isFavorite)
        {
            bookmarks.Save(bookmark with { IsFavorite = isFavorite });
        }

        return true;
    }

    public void Dispose()
    {
        bookmarks.Changed -= OnBookmarksChanged;
        gate.Dispose();
    }

    public async Task<IReadOnlyList<MineMapCommandResult>> ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        MineMapCommandContext? context,
        bool allowMutations,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (!allowMutations)
        {
            return [];
        }

        var results = new List<MineMapCommandResult>();
        foreach (var journalEvent in journalEvents)
        {
            if (
                journalEvent.EventName != "SendText"
                || !journalEvent.Payload.TryGetProperty("Message", out var value)
                || value.ValueKind != JsonValueKind.String
                || value.GetString() is not { } message
                || !IsMineMapCommand(message)
            )
            {
                continue;
            }

            results.Add(
                context is null
                    ? Failure(
                        "A current Commander, body, and surface position are required for Surface Mining map commands."
                    )
                    : await ExecuteAsync(message, context, cancellationToken).ConfigureAwait(false)
            );
        }

        return results;
    }

    public async Task<MineMapCommandResult> ExecuteAsync(
        string command,
        MineMapCommandContext context,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(context);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return command.Trim().StartsWith(".mining", StringComparison.OrdinalIgnoreCase)
                ? CreateSurvey(command, context, cancellationToken)
                : ApplyMarker(command, context, cancellationToken);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return Failure("Surface Mining map data could not be saved: " + exception.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    public void UpdateContext(MineMapCommandContext? context)
    {
        var next = ResolveSurveyAtLocation(context);
        if (ReferenceEquals(ActiveSurvey, next))
        {
            return;
        }

        ActiveSurvey = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool SelectSurvey(Guid surveyId)
    {
        var selected = surveys.FirstOrDefault(survey => survey.Id == surveyId);
        if (selected is null)
        {
            return false;
        }

        ActiveSurvey = selected;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid surveyId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var survey = surveys.FirstOrDefault(candidate => candidate.Id == surveyId);
            if (survey is null)
            {
                return false;
            }

            bookmarks.Delete(surveyId);
            if (ActiveSurvey?.Id == surveyId)
            {
                ActiveSurvey = null;
            }
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private MineMapCommandResult CreateSurvey(
        string command,
        MineMapCommandContext context,
        CancellationToken cancellationToken
    )
    {
        var parts = Split(command);
        if (
            parts.Length == 3
            && parts[0].Equals(".mining", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("center", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("here", StringComparison.OrdinalIgnoreCase)
        )
        {
            return RecenterSurvey(context, cancellationToken);
        }

        if (
            parts.Length != 4
            || !TryHeading(parts[1], out var heading)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var radiusKm)
            || !double.IsFinite(radiusKm)
            || radiusKm <= 0
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var signal)
            || signal <= 0
        )
        {
            return Failure("Use .mining <heading 0-359> <border radius km> <location number> or .mining center here.");
        }

        if (!HasSurfaceContext(context))
        {
            return Failure(
                "A current body, planet radius, and surface position are required to save a mining location."
            );
        }

        var now = DateTimeOffset.UtcNow;
        var existing = surveys.FirstOrDefault(survey =>
            MatchesContext(survey, context) && survey.LocationSignal == signal
        );
        var center = GetDestination(
            context.PlayerLocation!.Value,
            heading,
            radiusKm * 1000,
            context.PlanetRadiusMeters
        );
        var survey = (existing ?? new MineMapSurvey { Id = Guid.NewGuid(), CreatedAt = now }) with
        {
            FrontierId = context.FrontierId,
            CommanderName = context.CommanderName,
            SystemName = context.SystemName,
            SystemAddress = context.SystemAddress,
            SystemPosition = context.SystemPosition,
            BodyId = context.BodyId,
            BodyName = context.BodyName,
            BodyType = context.BodyType,
            ArrivalDistanceLs = context.ArrivalDistanceLs,
            LocationSignal = signal,
            LocationRadiusMeters = radiusKm * 1000,
            PlanetRadiusMeters = context.PlanetRadiusMeters,
            Center = center,
            UpdatedAt = now,
        };
        SaveAndReplace(survey, cancellationToken);
        ActiveSurvey = survey;
        Changed?.Invoke(this, EventArgs.Empty);
        return Success($"{survey.Name} center saved at heading {heading:0}° with a {radiusKm:0.##} km border.", survey);
    }

    private MineMapCommandResult RecenterSurvey(MineMapCommandContext context, CancellationToken cancellationToken)
    {
        if (!HasSurfaceContext(context))
        {
            return Failure(
                "A current body, planet radius, and surface position are required to recenter a mining map."
            );
        }

        var active = ResolveSurveyForRecenter(context);
        if (active is null)
        {
            return Failure("Enter or select a saved Surface Mining map before using .mining center here.");
        }

        var updated = active with { Center = context.PlayerLocation!.Value, UpdatedAt = DateTimeOffset.UtcNow };
        SaveAndReplace(updated, cancellationToken);
        ActiveSurvey = updated;
        Changed?.Invoke(this, EventArgs.Empty);
        return Success(
            $"{updated.Name} center moved to your current position. Existing markers were preserved.",
            updated
        );
    }

    private MineMapCommandResult ApplyMarker(
        string command,
        MineMapCommandContext context,
        CancellationToken cancellationToken
    )
    {
        var parts = Split(command);
        if (parts.Length < 3 || !parts[0].Equals(".mine", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                "Use .mine <heading> <material> <distance km> <low|medium|high>/<low|medium|high>, .mine <material> <low|medium|high>/<low|medium|high> here, .mine move <commodity> here, or .mine delete here."
            );
        }

        var active = ResolveActiveSurvey(context);
        if (active is null)
        {
            return Failure("Move inside a saved Surface Mining map or create one with .mining before adding markers.");
        }

        if (
            parts.Length == 3
            && parts[1].Equals("delete", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("here", StringComparison.OrdinalIgnoreCase)
        )
        {
            return DeleteMarkerHere(active, context, cancellationToken);
        }

        if (parts[1].Equals("move", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length < 4 || !parts[^1].Equals("here", StringComparison.OrdinalIgnoreCase))
            {
                return Failure("Use .mine move <commodity> here.");
            }

            return MoveMarkerHere(active, string.Join(' ', parts[2..^1]), context, cancellationToken);
        }

        SurfaceCoordinate location;
        string material;
        string placementDescription;
        MineMapRating mineralAmount;
        MineMapRating density;
        var isHerePlacement = false;
        if (
            parts.Length >= 4
            && parts[^1].Equals("here", StringComparison.OrdinalIgnoreCase)
            && TryRatings(parts[^2], out mineralAmount, out density)
        )
        {
            if (context.PlayerLocation is not { } current)
            {
                return Failure(
                    "A live surface position is required for .mine <material> <low|medium|high>/<low|medium|high> here."
                );
            }

            material = string.Join(' ', parts[1..^2]).Trim();
            location = current;
            placementDescription = "at your current position";
            isHerePlacement = true;
        }
        else if (
            parts.Length >= 5
            && TryHeading(parts[1], out var heading)
            && double.TryParse(parts[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var distanceKm)
            && double.IsFinite(distanceKm)
            && distanceKm >= 0
            && TryRatings(parts[^1], out mineralAmount, out density)
        )
        {
            if (context.PlayerLocation is not { } current)
            {
                return Failure("A live surface position is required for a bearing-and-distance marker.");
            }

            material = string.Join(' ', parts[2..^2]).Trim();
            location = GetDestination(current, heading, distanceKm * 1000, active.PlanetRadiusMeters);
            placementDescription = $"at {heading:0}°, {distanceKm:0.00} km from your position";
        }
        else
        {
            return Failure(
                "Use .mine <heading 0-359> <material> <distance km> <low|medium|high>/<low|medium|high> or .mine <material> <low|medium|high>/<low|medium|high> here."
            );
        }

        if (material.Length == 0)
        {
            return Failure("Enter a mineral or metal name for the map marker.");
        }
        if (!SurfaceMiningCommodityCatalog.TryResolve(material, out var commodity))
        {
            return Failure(
                $"'{material}' is not a supported surface-mining commodity. See Surface Mining > Hotspot List for accepted names."
            );
        }

        material = commodity.Name;

        if (!isHerePlacement)
        {
            var duplicate = active
                .Markers.Where(marker => string.Equals(marker.Material, material, StringComparison.OrdinalIgnoreCase))
                .Select(marker => new
                {
                    Marker = marker,
                    Distance = SurfaceNavigation.GetDistance(location, marker.Location, active.PlanetRadiusMeters),
                })
                .Where(candidate => candidate.Distance <= DuplicateMarkerRadiusMeters)
                .OrderBy(candidate => candidate.Distance)
                .FirstOrDefault();
            if (duplicate is not null)
            {
                return Failure(
                    $"A {duplicate.Marker.Material} marker is already {duplicate.Distance:0} m from that position. Bearing-and-distance placements must be more than {DuplicateMarkerRadiusMeters:0} m apart; use the 'here' command to deliberately record an overlapping deposit."
                );
            }
        }

        var marker = new MineMapMarker
        {
            Material = material,
            MineralAmount = mineralAmount,
            Density = density,
            Location = location,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var updated = active with { Markers = [.. active.Markers, marker], UpdatedAt = DateTimeOffset.UtcNow };
        SaveAndReplace(updated, cancellationToken);
        ActiveSurvey = updated;
        Changed?.Invoke(this, EventArgs.Empty);
        return Success(
            $"Added {material} ({mineralAmount} amount, {density} density) to {updated.Name} {placementDescription}.",
            updated
        );
    }

    private MineMapCommandResult DeleteMarkerHere(
        MineMapSurvey active,
        MineMapCommandContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.PlayerLocation is not { } current)
        {
            return Failure("A live surface position is required for .mine delete here.");
        }

        var nearest = active
            .Markers.Select(marker => new
            {
                Marker = marker,
                Distance = SurfaceNavigation.GetDistance(current, marker.Location, active.PlanetRadiusMeters),
            })
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        if (nearest is null || nearest.Distance > MarkerDeleteRadiusMeters)
        {
            return Failure("No mine marker is within 0.5 km of your current position.");
        }

        var updated = active with
        {
            Markers = active.Markers.Where(marker => marker.Id != nearest.Marker.Id).ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        SaveAndReplace(updated, cancellationToken);
        ActiveSurvey = updated;
        Changed?.Invoke(this, EventArgs.Empty);
        return Success($"Removed the nearest {nearest.Marker.Material} marker ({nearest.Distance:0} m away).", updated);
    }

    private MineMapCommandResult MoveMarkerHere(
        MineMapSurvey active,
        string material,
        MineMapCommandContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.PlayerLocation is not { } current)
        {
            return Failure("A live surface position is required for .mine move <commodity> here.");
        }

        material = material.Trim();
        if (material.Length == 0)
        {
            return Failure("Use .mine move <commodity> here.");
        }

        if (!SurfaceMiningCommodityCatalog.TryResolve(material, out var commodity))
        {
            return Failure(
                $"'{material}' is not a supported surface-mining commodity. See Surface Mining > Hotspot List for accepted names."
            );
        }

        var nearest = active
            .Markers.Where(marker => string.Equals(marker.Material, commodity.Name, StringComparison.OrdinalIgnoreCase))
            .Select(marker => new
            {
                Marker = marker,
                Distance = SurfaceNavigation.GetDistance(current, marker.Location, active.PlanetRadiusMeters),
            })
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        if (nearest is null || nearest.Distance > MarkerMoveRadiusMeters)
        {
            return Failure(
                $"No {commodity.Name} marker is within {MarkerMoveRadiusMeters:0} m of your current position."
            );
        }

        var movedMarker = nearest.Marker with { Location = current };
        var updated = active with
        {
            Markers = active.Markers.Select(marker => marker.Id == movedMarker.Id ? movedMarker : marker).ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        SaveAndReplace(updated, cancellationToken);
        ActiveSurvey = updated;
        Changed?.Invoke(this, EventArgs.Empty);
        return Success(
            $"Moved the nearest {movedMarker.Material} marker {nearest.Distance:0} m to your current position.",
            updated
        );
    }

    private MineMapSurvey? ResolveActiveSurvey(MineMapCommandContext context)
    {
        ActiveSurvey = ResolveSurveyAtLocation(context);
        return ActiveSurvey;
    }

    private MineMapSurvey? ResolveSurveyForRecenter(MineMapCommandContext context)
    {
        if (ActiveSurvey is { } current && MatchesContext(current, context))
        {
            return surveys.FirstOrDefault(survey => survey.Id == current.Id) ?? current;
        }

        var atLocation = ResolveSurveyAtLocation(context);
        if (atLocation is not null)
        {
            return atLocation;
        }

        var matching = surveys.Where(survey => MatchesContext(survey, context)).Take(2).ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    private MineMapSurvey? ResolveSurveyAtLocation(MineMapCommandContext? context)
    {
        if (context?.PlayerLocation is not { } playerLocation)
        {
            return null;
        }

        var candidates = surveys
            .Where(survey =>
                MatchesContext(survey, context) && survey.PlanetRadiusMeters > 0 && survey.LocationRadiusMeters > 0
            )
            .Select(survey => new
            {
                Survey = survey,
                Distance = SurfaceNavigation.GetDistance(survey.Center, playerLocation, survey.PlanetRadiusMeters),
            })
            .Where(candidate =>
                candidate.Distance <= candidate.Survey.LocationRadiusMeters + LocationBoundaryToleranceMeters
            )
            .ToArray();
        if (ActiveSurvey is { } active)
        {
            var retained = candidates.FirstOrDefault(candidate => candidate.Survey.Id == active.Id);
            if (retained is not null)
            {
                return retained.Survey;
            }
        }

        return candidates
            .OrderBy(candidate => candidate.Distance / candidate.Survey.LocationRadiusMeters)
            .ThenByDescending(candidate => candidate.Survey.UpdatedAt)
            .Select(candidate => candidate.Survey)
            .FirstOrDefault();
    }

    private void SaveAndReplace(MineMapSurvey survey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = bookmarks.Items.FirstOrDefault(item => item.Id == survey.Id);
        SaveBookmark(survey, existing);
    }

    private void SaveBookmark(MineMapSurvey survey, GalacticBookmark? existing)
    {
        bookmarks.Save(
            new GalacticBookmark
            {
                Id = survey.Id,
                System = survey.SystemName,
                Body = survey.BodyName,
                Ring = existing?.Ring ?? string.Empty,
                Position = survey.SystemPosition,
                Category = existing?.Category ?? "Surface Mining",
                CategoryAssignments = existing?.EffectiveCategoryAssignments ?? [BookmarkCategoryCatalog.SurfaceMining],
                Notes = existing?.Notes ?? survey.Notes,
                Minerals = string.Join(
                    ", ",
                    survey
                        .Markers.Select(marker => marker.Material)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                ),
                LastMined = existing?.LastMined ?? string.Empty,
                Hotspot = survey.Name,
                AverageYield = existing?.AverageYield ?? string.Empty,
                Screenshots = existing?.Screenshots ?? [],
                Rating = existing?.Rating ?? 0,
                RingType = existing?.RingType ?? string.Empty,
                Reserve = existing?.Reserve ?? string.Empty,
                Overlaps = existing?.Overlaps ?? string.Empty,
                ResourceExtractionSites = existing?.ResourceExtractionSites ?? string.Empty,
                IsFavorite = existing?.IsFavorite ?? false,
                SurfaceMiningMap = survey with { Notes = existing?.Notes ?? survey.Notes },
                Updated = survey.UpdatedAt,
            }
        );
    }

    public static SurfaceCoordinate GetDestination(
        SurfaceCoordinate origin,
        double bearingDegrees,
        double distanceMeters,
        double planetRadiusMeters
    )
    {
        if (!double.IsFinite(bearingDegrees))
        {
            throw new ArgumentOutOfRangeException(nameof(bearingDegrees));
        }
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceMeters));
        }
        if (!double.IsFinite(planetRadiusMeters) || planetRadiusMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMeters));
        }

        var latitude = DegreesToRadians(origin.Latitude);
        var longitude = DegreesToRadians(origin.Longitude);
        var bearing = DegreesToRadians(SurfaceNavigation.NormalizeDegrees(bearingDegrees));
        var angularDistance = distanceMeters / planetRadiusMeters;
        var targetLatitude = Math.Asin(
            Math.Sin(latitude) * Math.Cos(angularDistance)
                + Math.Cos(latitude) * Math.Sin(angularDistance) * Math.Cos(bearing)
        );
        var targetLongitude =
            longitude
            + Math.Atan2(
                Math.Sin(bearing) * Math.Sin(angularDistance) * Math.Cos(latitude),
                Math.Cos(angularDistance) - Math.Sin(latitude) * Math.Sin(targetLatitude)
            );
        var longitudeDegrees = RadiansToDegrees(targetLongitude);
        longitudeDegrees = ((longitudeDegrees + 540) % 360) - 180;
        return new SurfaceCoordinate(RadiansToDegrees(targetLatitude), longitudeDegrees);
    }

    private void OnBookmarksChanged(object? sender, EventArgs eventArgs)
    {
        var activeId = ActiveSurvey?.Id;
        surveys = ReadSurveys();
        ActiveSurvey = activeId is { } id ? surveys.FirstOrDefault(survey => survey.Id == id) : ActiveSurvey;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private MineMapSurvey[] ReadSurveys() =>
        bookmarks
            .Items.Where(bookmark =>
                bookmark.SurfaceMiningMap is not null && bookmark.HasCategory(BookmarkCategoryCatalog.SurfaceMining)
            )
            .Select(bookmark =>
                bookmark.SurfaceMiningMap! with
                {
                    SystemName = bookmark.System,
                    BodyName = bookmark.Body,
                    SystemPosition = bookmark.Position ?? bookmark.SurfaceMiningMap.SystemPosition,
                    Notes = bookmark.Notes,
                }
            )
            .OrderByDescending(survey => survey.UpdatedAt)
            .ToArray();

    private void MigrateLegacySurveys()
    {
        var markerPath = Path.Combine(legacyDirectory, ".bookmarks-migrated");
        if (File.Exists(markerPath) || !Directory.Exists(legacyDirectory))
        {
            return;
        }

        var importedIds = bookmarks.Items.Select(bookmark => bookmark.Id).ToHashSet();
        foreach (var survey in LoadLegacySurveys(legacyDirectory).Where(survey => !importedIds.Contains(survey.Id)))
        {
            try
            {
                bookmarks.Save(
                    new GalacticBookmark
                    {
                        Id = survey.Id,
                        System = survey.SystemName,
                        Body = survey.BodyName,
                        Position = survey.SystemPosition,
                        Category = "Surface Mining",
                        CategoryAssignments = [BookmarkCategoryCatalog.SurfaceMining],
                        Minerals = string.Join(
                            ", ",
                            (survey.Markers ?? [])
                                .Where(marker => marker is not null)
                                .Select(marker => marker.Material)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                        ),
                        Hotspot = survey.Name,
                        SurfaceMiningMap = survey,
                        Updated = survey.UpdatedAt,
                    }
                );
                importedIds.Add(survey.Id);
            }
            catch (JsonException)
            {
                // Skip invalid legacy records without preventing later imports.
            }
        }

        File.WriteAllText(markerPath, "Surface mining maps now use bookmarks.json.");
    }

    private static MineMapSurvey[] LoadLegacySurveys(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var loaded = new List<MineMapSurvey>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var survey = JsonSerializer.Deserialize<MineMapSurvey>(File.ReadAllText(path), JsonOptions);
                if (survey is not null && survey.Id != Guid.Empty)
                {
                    loaded.Add(survey);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // One corrupt bookmark must not hide the remaining catalog.
            }
        }

        return loaded.OrderByDescending(survey => survey.UpdatedAt).ToArray();
    }

    private static bool IsMineMapCommand(string command)
    {
        var trimmed = command.Trim();
        return trimmed.Equals(".mining", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(".mining ", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(".mine", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(".mine ", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] Split(string command) =>
        command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryHeading(string value, out double heading) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out heading)
        && double.IsFinite(heading)
        && heading is >= 0 and < 360;

    private static bool TryRatings(string value, out MineMapRating mineralAmount, out MineMapRating density)
    {
        mineralAmount = default;
        density = default;
        var values = value.Split('/', StringSplitOptions.TrimEntries);
        return values.Length == 2 && TryRating(values[0], out mineralAmount) && TryRating(values[1], out density);
    }

    private static bool TryRating(string value, out MineMapRating rating)
    {
        if (value.Equals("low", StringComparison.OrdinalIgnoreCase))
        {
            rating = MineMapRating.Low;
            return true;
        }
        if (value.Equals("medium", StringComparison.OrdinalIgnoreCase))
        {
            rating = MineMapRating.Medium;
            return true;
        }
        if (value.Equals("high", StringComparison.OrdinalIgnoreCase))
        {
            rating = MineMapRating.High;
            return true;
        }

        rating = default;
        return false;
    }

    private static bool HasSurfaceContext(MineMapCommandContext context) =>
        !string.IsNullOrWhiteSpace(context.FrontierId)
        && !string.IsNullOrWhiteSpace(context.SystemName)
        && context.SystemAddress > 0
        && context.BodyId >= 0
        && !string.IsNullOrWhiteSpace(context.BodyName)
        && context.PlanetRadiusMeters > 0
        && context.PlayerLocation is not null;

    private static bool MatchesContext(MineMapSurvey survey, MineMapCommandContext context) =>
        string.Equals(survey.FrontierId, context.FrontierId, StringComparison.OrdinalIgnoreCase)
        && survey.SystemAddress == context.SystemAddress
        && survey.BodyId == context.BodyId;

    private static MineMapCommandResult Success(string message, MineMapSurvey survey) => new(true, message, survey);

    private static MineMapCommandResult Failure(string message) => new(false, message);

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private static double RadiansToDegrees(double radians) => radians * 180d / Math.PI;
}
