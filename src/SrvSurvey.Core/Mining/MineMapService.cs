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

    public int? RigCount { get; init; }

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

public enum MineMapSurveyGuidePhase
{
    Border,
    Center,
    ConfirmCenter,
    Waypoint,
    Complete,
}

public sealed record MineMapSurveyGuideState(
    MineMapSurveyGuidePhase Phase,
    string FrontierId,
    long SystemAddress,
    int BodyId,
    Guid? SurveyId = null,
    IReadOnlyList<SurfaceCoordinate>? Waypoints = null,
    int WaypointIndex = 0
)
{
    public SurfaceCoordinate? CurrentWaypoint =>
        Waypoints is { } points && WaypointIndex >= 0 && WaypointIndex < points.Count ? points[WaypointIndex] : null;
}

/// <summary>
/// Owns surface-mine survey command handling, spherical placement, and shared
/// bookmark persistence. Callers only provide live journal context and present
/// the returned feedback.
/// </summary>
public sealed class MineMapService : IDisposable
{
    private sealed record MarkerPlacement(
        SurfaceCoordinate Location,
        string Material,
        string Description,
        MineMapRating MineralAmount,
        MineMapRating Density,
        bool IsHere
    );

    public const double MarkerDeleteRadiusMeters = 500;
    public const double DuplicateMarkerRadiusMeters = 100;
    public const double MarkerMoveRadiusMeters = 200;
    public const double SurveyScannerRadiusMeters = 2_000;
    public const double SurveyWaypointArrivalRadiusMeters = 200;
    private const double LocationBoundaryToleranceMeters = 1;
    private const double SurveyWaypointSpacingMeters = 1_800;

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

    public MineMapSurveyGuideState? SurveyGuide { get; private set; }

    public bool IsFavorite(Guid surveyId) =>
        bookmarks.Items.FirstOrDefault(bookmark => bookmark.Id == surveyId)?.IsFavorite == true;

    public bool SetFavorite(Guid surveyId, bool isFavorite)
    {
        GalacticBookmark? bookmark = bookmarks.Items.FirstOrDefault(candidate => candidate.Id == surveyId);
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
        MineMapSurvey? next = ResolveSurveyAtLocation(context);
        bool guideChanged = UpdateSurveyGuide(context);
        if (ReferenceEquals(ActiveSurvey, next) && !guideChanged)
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
            parts.Length == 2
            && parts[0].Equals(".mining", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("survey", StringComparison.OrdinalIgnoreCase)
        )
        {
            return StartSurveyGuide(context);
        }

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
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double radiusKm)
            || !double.IsFinite(radiusKm)
            || radiusKm <= 0
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int signal)
            || signal <= 0
        )
        {
            return Failure(
                "Use .mining survey, .mining <heading 0-359> <border radius km> <location number>, or .mining center here."
            );
        }

        double radiusMeters = radiusKm * 1000;
        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0)
        {
            return Failure(
                "Use .mining survey, .mining <heading 0-359> <border radius km> <location number>, or .mining center here."
            );
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
        SurfaceCoordinate center = GetDestination(
            context.PlayerLocation!.Value,
            heading,
            radiusMeters,
            context.PlanetRadiusMeters
        );
        MineMapSurvey survey = (existing ?? new MineMapSurvey { Id = Guid.NewGuid(), CreatedAt = now }) with
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
            LocationRadiusMeters = radiusMeters,
            PlanetRadiusMeters = context.PlanetRadiusMeters,
            Center = center,
            UpdatedAt = now,
        };
        SaveAndReplace(survey, cancellationToken);
        ActiveSurvey = survey;
        if (SurveyGuide is { Phase: MineMapSurveyGuidePhase.Border } guide && MatchesContext(guide, context))
        {
            SurveyGuide = guide with
            {
                Phase = MineMapSurveyGuidePhase.Center,
                SurveyId = survey.Id,
                Waypoints = null,
                WaypointIndex = 0,
            };
        }
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

        MineMapSurvey? active = ResolveSurveyForRecenter(context);
        if (active is null)
        {
            return Failure("Enter or select a saved Surface Mining map before using .mining center here.");
        }

        MineMapSurvey updated = active with
        {
            Center = context.PlayerLocation!.Value,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        SaveAndReplace(updated, cancellationToken);
        ActiveSurvey = updated;
        if (
            SurveyGuide is { } guide
            && guide.Phase is MineMapSurveyGuidePhase.Center or MineMapSurveyGuidePhase.ConfirmCenter
            && MatchesContext(guide, context)
            && (guide.SurveyId is null || guide.SurveyId == updated.Id)
        )
        {
            IReadOnlyList<SurfaceCoordinate> waypoints = CreateSurveyWaypoints(updated);
            SurveyGuide = guide with
            {
                Phase = waypoints.Count == 0 ? MineMapSurveyGuidePhase.Complete : MineMapSurveyGuidePhase.Waypoint,
                SurveyId = updated.Id,
                Waypoints = waypoints,
                WaypointIndex = 0,
            };
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return Success(
            $"{updated.Name} center moved to your current position. Existing markers were preserved.",
            updated
        );
    }

    public void DismissSurveyGuide()
    {
        if (SurveyGuide is null)
        {
            return;
        }

        SurveyGuide = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static IReadOnlyList<SurfaceCoordinate> CreateSurveyWaypoints(MineMapSurvey survey)
    {
        ArgumentNullException.ThrowIfNull(survey);
        if (
            !double.IsFinite(survey.LocationRadiusMeters)
            || survey.LocationRadiusMeters <= SurveyScannerRadiusMeters
            || !double.IsFinite(survey.PlanetRadiusMeters)
            || survey.PlanetRadiusMeters <= 0
        )
        {
            return [];
        }

        var result = new List<SurfaceCoordinate>();
        double spiralGrowth = SurveyScannerRadiusMeters / (2 * Math.PI);
        double angle = 2 * Math.PI;
        double radius = SurveyScannerRadiusMeters;
        while (radius < survey.LocationRadiusMeters)
        {
            result.Add(
                GetDestination(
                    survey.Center,
                    SurfaceNavigation.NormalizeDegrees(angle * 180 / Math.PI),
                    radius,
                    survey.PlanetRadiusMeters
                )
            );
            double tangentLength = Math.Sqrt((radius * radius) + (spiralGrowth * spiralGrowth));
            angle += SurveyWaypointSpacingMeters / tangentLength;
            radius = Math.Min(survey.LocationRadiusMeters, spiralGrowth * angle);
        }

        double finalBearing = SurfaceNavigation.NormalizeDegrees(angle * 180 / Math.PI);
        AddWaypointIfSeparated(result, survey, finalBearing, survey.LocationRadiusMeters);
        int perimeterCount = Math.Max(
            1,
            (int)Math.Ceiling(2 * Math.PI * survey.LocationRadiusMeters / SurveyWaypointSpacingMeters)
        );
        for (int index = 1; index < perimeterCount; index++)
        {
            double bearing = finalBearing + (360d * index / perimeterCount);
            AddWaypointIfSeparated(result, survey, bearing, survey.LocationRadiusMeters);
        }

        return result;
    }

    private MineMapCommandResult StartSurveyGuide(MineMapCommandContext context)
    {
        if (!HasSurfaceContext(context))
        {
            return Failure(
                "A current body, planet radius, and surface position are required to start guided survey mode."
            );
        }

        MineMapSurvey? active = ResolveSurveyAtLocation(context);
        SurveyGuide = new MineMapSurveyGuideState(
            active is null ? MineMapSurveyGuidePhase.Border : MineMapSurveyGuidePhase.Center,
            context.FrontierId,
            context.SystemAddress,
            context.BodyId,
            active?.Id
        );
        if (active is not null)
        {
            ActiveSurvey = active;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return active is null
            ? new MineMapCommandResult(
                true,
                "Guided survey started. Drive to the orange border, face the center, then record the heading, radius, and signal number."
            )
            : Success($"Guided survey started for {active.Name}. Drive to its saved center.", active);
    }

    private bool UpdateSurveyGuide(MineMapCommandContext? context)
    {
        if (SurveyGuide is not { } guide || context is null)
        {
            return false;
        }

        if (!MatchesContext(guide, context))
        {
            SurveyGuide = null;
            return true;
        }

        if (context.PlayerLocation is not { } player)
        {
            return false;
        }

        MineMapSurvey? survey = guide.SurveyId is { } surveyId
            ? surveys.FirstOrDefault(candidate => candidate.Id == surveyId)
            : null;
        if (survey is null)
        {
            return false;
        }

        if (guide.Phase == MineMapSurveyGuidePhase.Center)
        {
            double distance = SurfaceNavigation.GetDistance(player, survey.Center, survey.PlanetRadiusMeters);
            if (distance <= SurveyWaypointArrivalRadiusMeters)
            {
                SurveyGuide = guide with { Phase = MineMapSurveyGuidePhase.ConfirmCenter };
                return true;
            }
        }
        else if (
            guide.Phase == MineMapSurveyGuidePhase.Waypoint
            && guide.CurrentWaypoint is { } waypoint
            && SurfaceNavigation.GetDistance(player, waypoint, survey.PlanetRadiusMeters)
                <= SurveyWaypointArrivalRadiusMeters
        )
        {
            int nextIndex = guide.WaypointIndex + 1;
            SurveyGuide = guide with
            {
                Phase =
                    nextIndex >= (guide.Waypoints?.Count ?? 0)
                        ? MineMapSurveyGuidePhase.Complete
                        : MineMapSurveyGuidePhase.Waypoint,
                WaypointIndex = nextIndex,
            };
            return true;
        }

        return false;
    }

    private static void AddWaypointIfSeparated(
        List<SurfaceCoordinate> result,
        MineMapSurvey survey,
        double bearing,
        double distance
    )
    {
        SurfaceCoordinate waypoint = GetDestination(survey.Center, bearing, distance, survey.PlanetRadiusMeters);
        if (result.Count == 0 || SurfaceNavigation.GetDistance(result[^1], waypoint, survey.PlanetRadiusMeters) >= 1)
        {
            result.Add(waypoint);
        }
    }

    private static bool MatchesContext(MineMapSurveyGuideState guide, MineMapCommandContext context) =>
        guide.FrontierId == context.FrontierId
        && guide.SystemAddress == context.SystemAddress
        && guide.BodyId == context.BodyId;

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
                "Use .mine <heading> <material> <distance km> <low|medium|high>/<low|medium|high>, .mine <material> <low|medium|high>/<low|medium|high> here, .mine rigs <number>, .mine move <commodity> here, or .mine delete here."
            );
        }

        var active = ResolveActiveSurvey(context);
        if (active is null)
        {
            return Failure("Move inside a saved Surface Mining map or create one with .mining before adding markers.");
        }

        MineMapCommandResult? managementResult = ApplyMarkerManagementCommand(
            parts,
            active,
            context,
            cancellationToken
        );
        if (managementResult is not null)
        {
            return managementResult;
        }

        (MarkerPlacement? placement, MineMapCommandResult? placementFailure) = ParseMarkerPlacement(
            parts,
            active,
            context
        );
        if (placementFailure is not null)
        {
            return placementFailure;
        }

        MarkerPlacement markerPlacement = placement!;
        string material = markerPlacement.Material;
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

        if (!markerPlacement.IsHere)
        {
            var duplicate = active
                .Markers.Where(marker => string.Equals(marker.Material, material, StringComparison.OrdinalIgnoreCase))
                .Select(marker => new
                {
                    Marker = marker,
                    Distance = SurfaceNavigation.GetDistance(
                        markerPlacement.Location,
                        marker.Location,
                        active.PlanetRadiusMeters
                    ),
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
            MineralAmount = markerPlacement.MineralAmount,
            Density = markerPlacement.Density,
            Location = markerPlacement.Location,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var updated = active with { Markers = [.. active.Markers, marker], UpdatedAt = DateTimeOffset.UtcNow };
        SaveAndReplace(updated, cancellationToken);
        ActiveSurvey = updated;
        Changed?.Invoke(this, EventArgs.Empty);
        return Success(
            $"Added {material} ({markerPlacement.MineralAmount} amount, {markerPlacement.Density} density) to {updated.Name} {markerPlacement.Description}.",
            updated
        );
    }

    private MineMapCommandResult? ApplyMarkerManagementCommand(
        string[] parts,
        MineMapSurvey active,
        MineMapCommandContext context,
        CancellationToken cancellationToken
    )
    {
        if (
            parts.Length == 3
            && parts[1].Equals("delete", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("here", StringComparison.OrdinalIgnoreCase)
        )
        {
            return DeleteMarkerHere(active, context, cancellationToken);
        }

        if (parts[1].Equals("rigs", StringComparison.OrdinalIgnoreCase))
        {
            return
                parts.Length != 3
                || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int rigCount)
                || rigCount <= 0
                ? Failure("Use .mine rigs <positive number>.")
                : SetNearestMarkerRigCount(active, rigCount, context, cancellationToken);
        }

        if (!parts[1].Equals("move", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parts.Length < 4 || !parts[^1].Equals("here", StringComparison.OrdinalIgnoreCase)
            ? Failure("Use .mine move <commodity> here.")
            : MoveMarkerHere(active, string.Join(' ', parts[2..^1]), context, cancellationToken);
    }

    private MineMapCommandResult SetNearestMarkerRigCount(
        MineMapSurvey active,
        int rigCount,
        MineMapCommandContext context,
        CancellationToken cancellationToken
    )
    {
        if (context.PlayerLocation is not { } current)
        {
            return Failure("A live surface position is required for .mine rigs <number>.");
        }

        var nearest = active
            .Markers.Select(marker => new
            {
                Marker = marker,
                Distance = SurfaceNavigation.GetDistance(current, marker.Location, active.PlanetRadiusMeters),
            })
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();
        if (nearest is null)
        {
            return Failure("Add a mine marker before setting its rig count.");
        }

        MineMapMarker updatedMarker = nearest.Marker with { RigCount = rigCount };
        MineMapSurvey updated = active with
        {
            Markers = active.Markers.Select(marker => marker.Id == updatedMarker.Id ? updatedMarker : marker).ToArray(),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        SaveAndReplace(updated, cancellationToken);
        ActiveSurvey = updated;
        Changed?.Invoke(this, EventArgs.Empty);
        return Success(
            $"Set the nearest {updatedMarker.Material} marker to {rigCount:N0} rig{(rigCount == 1 ? string.Empty : "s")} ({nearest.Distance:0} m away).",
            updated
        );
    }

    private static (MarkerPlacement? Placement, MineMapCommandResult? Failure) ParseMarkerPlacement(
        string[] parts,
        MineMapSurvey active,
        MineMapCommandContext context
    )
    {
        if (
            parts.Length >= 4
            && parts[^1].Equals("here", StringComparison.OrdinalIgnoreCase)
            && TryRatings(parts[^2], out MineMapRating mineralAmount, out MineMapRating density)
        )
        {
            return context.PlayerLocation is not { } current
                ? (
                    null,
                    Failure(
                        "A live surface position is required for .mine <material> <low|medium|high>/<low|medium|high> here."
                    )
                )
                : (
                    new MarkerPlacement(
                        current,
                        string.Join(' ', parts[1..^2]).Trim(),
                        "at your current position",
                        mineralAmount,
                        density,
                        true
                    ),
                    null
                );
        }

        if (
            parts.Length < 5
            || !TryHeading(parts[1], out double heading)
            || !double.TryParse(parts[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double distanceKm)
            || !double.IsFinite(distanceKm)
            || distanceKm < 0
            || !TryRatings(parts[^1], out mineralAmount, out density)
        )
        {
            return (null, InvalidMarkerPlacement());
        }

        if (context.PlayerLocation is not { } origin)
        {
            return (null, Failure("A live surface position is required for a bearing-and-distance marker."));
        }

        double distanceMeters = distanceKm * 1000;
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0)
        {
            return (null, InvalidMarkerPlacement());
        }

        return (
            new MarkerPlacement(
                GetDestination(origin, heading, distanceMeters, active.PlanetRadiusMeters),
                string.Join(' ', parts[2..^2]).Trim(),
                $"at {heading:0}°, {distanceKm:0.00} km from your position",
                mineralAmount,
                density,
                false
            ),
            null
        );
    }

    private static MineMapCommandResult InvalidMarkerPlacement() =>
        Failure(
            "Use .mine <heading 0-359> <material> <distance km> <low|medium|high>/<low|medium|high> or .mine <material> <low|medium|high>/<low|medium|high> here."
        );

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

        if (!SurfaceMiningCommodityCatalog.TryResolve(material, out SurfaceMiningCommodity? commodity))
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

        MineMapMarker movedMarker = nearest.Marker with { Location = current };
        MineMapSurvey updated = active with
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

        MineMapSurvey? atLocation = ResolveSurveyAtLocation(context);
        if (atLocation is not null)
        {
            return atLocation;
        }

        MineMapSurvey[] matching = surveys.Where(survey => MatchesContext(survey, context)).Take(2).ToArray();
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
