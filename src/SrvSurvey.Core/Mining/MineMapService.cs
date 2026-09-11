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
    High,
}

public sealed record MineMapMarker
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Material { get; init; } = string.Empty;

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

    public MineMapRating MineralAmount { get; init; }

    public MineMapRating Density { get; init; }

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
    SurfaceCoordinate? PlayerLocation);

public sealed record MineMapCommandResult(
    bool Succeeded,
    string Message,
    MineMapSurvey? Survey = null);

/// <summary>
/// Owns surface-mine survey command handling, spherical placement, and shared
/// bookmark persistence. Callers only provide live journal context and present
/// the returned feedback.
/// </summary>
public sealed class MineMapService : IDisposable
{
    public const double LocationRadiusMeters = 2_470;
    public const double MarkerDeleteRadiusMeters = 500;

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

    public MineMapService(
        string dataDirectory,
        BookmarkCatalog? bookmarkCatalog = null)
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

    public void Dispose()
    {
        bookmarks.Changed -= OnBookmarksChanged;
        gate.Dispose();
    }

    public async Task<IReadOnlyList<MineMapCommandResult>> ApplyJournalEventsAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        MineMapCommandContext? context,
        bool allowMutations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        if (!allowMutations)
        {
            return [];
        }

        var results = new List<MineMapCommandResult>();
        foreach (var journalEvent in journalEvents)
        {
            if (journalEvent.EventName != "SendText"
                || !journalEvent.Payload.TryGetProperty("Message", out var value)
                || value.ValueKind != JsonValueKind.String
                || value.GetString() is not { } message
                || !IsMineMapCommand(message))
            {
                continue;
            }

            results.Add(context is null
                ? Failure("A current Commander, body, and surface position are required for Surface Mining map commands.")
                : await ExecuteAsync(message, context, cancellationToken)
                    .ConfigureAwait(false));
        }

        return results;
    }

    public async Task<MineMapCommandResult> ExecuteAsync(
        string command,
        MineMapCommandContext context,
        CancellationToken cancellationToken = default)
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
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException)
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
        MineMapSurvey? next = null;
        if (context is not null)
        {
            next = ActiveSurvey is { } active && MatchesContext(active, context)
                ? surveys.FirstOrDefault(survey => survey.Id == active.Id)
                    ?? active
                : surveys.Where(survey => MatchesContext(survey, context))
                .OrderByDescending(survey => survey.UpdatedAt)
                .FirstOrDefault();
        }
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

    public async Task<bool> DeleteAsync(
        Guid surveyId,
        CancellationToken cancellationToken = default)
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
        CancellationToken cancellationToken)
    {
        var parts = Split(command);
        if (parts.Length != 4
            || !TryHeading(parts[1], out var heading)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var signal)
            || signal <= 0
            || !TryRatings(parts[3], out var mineralAmount, out var density))
        {
            return Failure("Use .mining <heading 0-359> <location number> <high|low>/<high|low>.");
        }

        if (!HasSurfaceContext(context))
        {
            return Failure("A current body, planet radius, and surface position are required to save a mining location.");
        }

        var now = DateTimeOffset.UtcNow;
        var existing = surveys.FirstOrDefault(survey => MatchesContext(survey, context)
            && survey.LocationSignal == signal);
        var center = GetDestination(
            context.PlayerLocation!.Value,
            heading,
            LocationRadiusMeters,
            context.PlanetRadiusMeters);
        var survey = (existing ?? new MineMapSurvey
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
        }) with
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
            MineralAmount = mineralAmount,
            Density = density,
            PlanetRadiusMeters = context.PlanetRadiusMeters,
            Center = center,
            UpdatedAt = now,
        };
        SaveAndReplace(survey, cancellationToken);
        ActiveSurvey = survey;
        Changed?.Invoke(this, EventArgs.Empty);
        return Success($"{survey.Name} center saved at heading {heading:0}°. "
            + $"Mineral amount {mineralAmount}; density {density}.", survey);
    }

    private MineMapCommandResult ApplyMarker(
        string command,
        MineMapCommandContext context,
        CancellationToken cancellationToken)
    {
        var parts = Split(command);
        if (parts.Length < 3
            || !parts[0].Equals(".mine", StringComparison.OrdinalIgnoreCase))
        {
            return Failure("Use .mine <heading> <material> <distance km>, .mine <material> here, or .mine delete here.");
        }

        var active = ResolveActiveSurvey(context);
        if (active is null)
        {
            return Failure("Create or select a mining location with .mining before adding map markers.");
        }

        if (parts.Length == 3
            && parts[1].Equals("delete", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("here", StringComparison.OrdinalIgnoreCase))
        {
            return DeleteMarkerHere(active, context, cancellationToken);
        }

        SurfaceCoordinate location;
        string material;
        if (parts.Length >= 3
            && parts[^1].Equals("here", StringComparison.OrdinalIgnoreCase))
        {
            if (context.PlayerLocation is not { } current)
            {
                return Failure("A live surface position is required for .mine <material> here.");
            }

            material = string.Join(' ', parts[1..^1]).Trim();
            location = current;
        }
        else if (parts.Length >= 4
            && TryHeading(parts[1], out var heading)
            && double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var distanceKm)
            && double.IsFinite(distanceKm)
            && distanceKm >= 0)
        {
            material = string.Join(' ', parts[2..^1]).Trim();
            location = GetDestination(
                active.Center,
                heading,
                distanceKm * 1000,
                active.PlanetRadiusMeters);
        }
        else
        {
            return Failure("Use .mine <heading 0-359> <material> <distance km> or .mine <material> here.");
        }

        if (material.Length == 0)
        {
            return Failure("Enter a mineral or metal name for the map marker.");
        }
        if (!SurfaceMiningCommodityCatalog.TryResolve(material, out var commodity))
        {
            return Failure($"'{material}' is not a supported surface-mining commodity. See Surface Mining > Hotspot List for accepted names.");
        }

        material = commodity.Name;

        var marker = new MineMapMarker
        {
            Material = material,
            Location = location,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var updated = active with
        {
            Markers = [.. active.Markers, marker],
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        SaveAndReplace(updated, cancellationToken);
        ActiveSurvey = updated;
        Changed?.Invoke(this, EventArgs.Empty);
        var distance = SurfaceNavigation.GetDistance(
            updated.Center,
            location,
            updated.PlanetRadiusMeters);
        var bearing = SurfaceNavigation.GetBearing(updated.Center, location);
        return Success($"Added {material} to {updated.Name} at {bearing:0}°, {distance / 1000:0.00} km.", updated);
    }

    private MineMapCommandResult DeleteMarkerHere(
        MineMapSurvey active,
        MineMapCommandContext context,
        CancellationToken cancellationToken)
    {
        if (context.PlayerLocation is not { } current)
        {
            return Failure("A live surface position is required for .mine delete here.");
        }

        var nearest = active.Markers
            .Select(marker => new
            {
                Marker = marker,
                Distance = SurfaceNavigation.GetDistance(
                    current,
                    marker.Location,
                    active.PlanetRadiusMeters),
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

    private MineMapSurvey? ResolveActiveSurvey(MineMapCommandContext context)
    {
        if (ActiveSurvey is { } current && MatchesContext(current, context))
        {
            return current;
        }

        ActiveSurvey = surveys.Where(survey => MatchesContext(survey, context))
            .OrderByDescending(survey => survey.UpdatedAt)
            .FirstOrDefault();
        return ActiveSurvey;
    }

    private void SaveAndReplace(
        MineMapSurvey survey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = bookmarks.Items.FirstOrDefault(item => item.Id == survey.Id);
        SaveBookmark(survey, existing);
    }

    private void SaveBookmark(MineMapSurvey survey, GalacticBookmark? existing)
    {
        bookmarks.Save(new GalacticBookmark
        {
            Id = survey.Id,
            System = survey.SystemName,
            Body = survey.BodyName,
            Ring = existing?.Ring ?? string.Empty,
            Position = survey.SystemPosition,
            Category = existing?.Category ?? "Surface Mining",
            CategoryAssignments = existing?.EffectiveCategoryAssignments
                ?? [BookmarkCategoryCatalog.SurfaceMining],
            Notes = existing?.Notes ?? survey.Notes,
            Minerals = string.Join(", ", survey.Markers
                .Select(marker => marker.Material)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
            LastMined = existing?.LastMined ?? string.Empty,
            Hotspot = survey.Name,
            AverageYield = existing?.AverageYield ?? string.Empty,
            Screenshots = existing?.Screenshots ?? [],
            Rating = existing?.Rating ?? 0,
            RingType = existing?.RingType ?? string.Empty,
            Reserve = existing?.Reserve ?? string.Empty,
            Overlaps = existing?.Overlaps ?? string.Empty,
            ResourceExtractionSites = existing?.ResourceExtractionSites ?? string.Empty,
            SurfaceMiningMap = survey with { Notes = existing?.Notes ?? survey.Notes },
            Updated = survey.UpdatedAt,
        });
    }

    public static SurfaceCoordinate GetDestination(
        SurfaceCoordinate origin,
        double bearingDegrees,
        double distanceMeters,
        double planetRadiusMeters)
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
            + Math.Cos(latitude) * Math.Sin(angularDistance) * Math.Cos(bearing));
        var targetLongitude = longitude + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angularDistance) * Math.Cos(latitude),
            Math.Cos(angularDistance) - Math.Sin(latitude) * Math.Sin(targetLatitude));
        var longitudeDegrees = RadiansToDegrees(targetLongitude);
        longitudeDegrees = ((longitudeDegrees + 540) % 360) - 180;
        return new SurfaceCoordinate(
            RadiansToDegrees(targetLatitude),
            longitudeDegrees);
    }

    private void OnBookmarksChanged(object? sender, EventArgs eventArgs)
    {
        var activeId = ActiveSurvey?.Id;
        surveys = ReadSurveys();
        ActiveSurvey = activeId is { } id
            ? surveys.FirstOrDefault(survey => survey.Id == id)
            : ActiveSurvey;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private MineMapSurvey[] ReadSurveys() => bookmarks.Items
        .Where(bookmark => bookmark.SurfaceMiningMap is not null
            && bookmark.HasCategory(BookmarkCategoryCatalog.SurfaceMining))
        .Select(bookmark => bookmark.SurfaceMiningMap! with
        {
            SystemName = bookmark.System,
            BodyName = bookmark.Body,
            SystemPosition = bookmark.Position ?? bookmark.SurfaceMiningMap.SystemPosition,
            Notes = bookmark.Notes,
        })
        .OrderByDescending(survey => survey.UpdatedAt)
        .ToArray();

    private void MigrateLegacySurveys()
    {
        var markerPath = Path.Combine(legacyDirectory, ".bookmarks-migrated");
        if (File.Exists(markerPath)
            || !Directory.Exists(legacyDirectory))
        {
            return;
        }

        var importedIds = bookmarks.Items.Select(bookmark => bookmark.Id).ToHashSet();
        foreach (var survey in LoadLegacySurveys(legacyDirectory)
            .Where(survey => !importedIds.Contains(survey.Id)))
        {
            try
            {
                bookmarks.Save(new GalacticBookmark
                {
                    Id = survey.Id,
                    System = survey.SystemName,
                    Body = survey.BodyName,
                    Position = survey.SystemPosition,
                    Category = "Surface Mining",
                    CategoryAssignments = [BookmarkCategoryCatalog.SurfaceMining],
                    Minerals = string.Join(", ", (survey.Markers ?? [])
                        .Where(marker => marker is not null)
                        .Select(marker => marker.Material)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
                    Hotspot = survey.Name,
                    SurfaceMiningMap = survey,
                    Updated = survey.UpdatedAt,
                });
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
                var survey = JsonSerializer.Deserialize<MineMapSurvey>(
                    File.ReadAllText(path),
                    JsonOptions);
                if (survey is not null && survey.Id != Guid.Empty)
                {
                    loaded.Add(survey);
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or JsonException)
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

    private static string[] Split(string command) => command.Trim().Split(
        ' ',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryHeading(string value, out double heading) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out heading)
        && double.IsFinite(heading)
        && heading is >= 0 and < 360;

    private static bool TryRatings(
        string value,
        out MineMapRating mineralAmount,
        out MineMapRating density)
    {
        mineralAmount = default;
        density = default;
        var values = value.Split('/', StringSplitOptions.TrimEntries);
        return values.Length == 2
            && TryRating(values[0], out mineralAmount)
            && TryRating(values[1], out density);
    }

    private static bool TryRating(string value, out MineMapRating rating)
    {
        if (value.Equals("low", StringComparison.OrdinalIgnoreCase))
        {
            rating = MineMapRating.Low;
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

    private static MineMapCommandResult Success(string message, MineMapSurvey survey) =>
        new(true, message, survey);

    private static MineMapCommandResult Failure(string message) => new(false, message);

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private static double RadiansToDegrees(double radians) => radians * 180d / Math.PI;
}
