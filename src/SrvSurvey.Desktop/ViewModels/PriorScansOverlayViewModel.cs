using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class PriorScansOverlayViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SystemSurveyViewModel survey;
    private readonly ICanonnSystemPoiClient client;
    private readonly PriorScanPlanner planner;
    private readonly Func<string?> commanderNameProvider;
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA2213:Disposable fields should be disposed",
        Justification = "An in-flight refresh may release this gate after disposal cancellation.")]
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private readonly CancellationTokenSource disposalCancellation = new();
    private CanonnSystemPoiResult? cachedResult;
    private string? cachedKey;
    private string? failedKey;
    private DateTimeOffset retryAfter;
    private IReadOnlyList<PriorScanSpeciesViewModel> species = [];
    private IReadOnlyList<PriorScanRadarTargetViewModel> radarTargets = [];
    private IReadOnlyList<PriorScanSurfaceMarkerViewModel> surfaceMarkers = [];
    private string statusText = "Waiting for surface navigation context.";
    private string inputMode;
    private bool isLoading;
    private bool disposed;

    public PriorScansOverlayViewModel(
        SystemSurveyViewModel survey,
        ICanonnSystemPoiClient client,
        ExobiologyReferenceCatalog catalog,
        Func<string?> commanderNameProvider,
        OverlayPlatformCapabilities capabilities)
    {
        this.survey = survey ?? throw new ArgumentNullException(nameof(survey));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        planner = new PriorScanPlanner(
            catalog ?? throw new ArgumentNullException(nameof(catalog)));
        this.commanderNameProvider = commanderNameProvider
            ?? throw new ArgumentNullException(nameof(commanderNameProvider));
        ArgumentNullException.ThrowIfNull(capabilities);
        inputMode = capabilities.SupportsClickThrough
            ? "PASSIVE"
            : "UNAVAILABLE";
        survey.PropertyChanged += OnSurveyPropertyChanged;
        Recalculate();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<PriorScanSpeciesViewModel> Species
    {
        get => species;
        private set
        {
            if (SetField(ref species, value))
            {
                OnPropertyChanged(nameof(HasSpecies));
                OnPropertyChanged(nameof(SpeciesCountText));
                OnPropertyChanged(nameof(ShouldShow));
            }
        }
    }

    public IReadOnlyList<PriorScanRadarTargetViewModel> RadarTargets
    {
        get => radarTargets;
        private set
        {
            if (SetField(ref radarTargets, value))
            {
                OnPropertyChanged(nameof(HasRadarTargets));
            }
        }
    }

    /// <summary>
    /// Absolute-coordinate markers for PlotGrounded surface radar
    /// (legacy <c>drawPriorScans</c>), separate from the Prior Scans overlay radar.
    /// </summary>
    public IReadOnlyList<PriorScanSurfaceMarkerViewModel> SurfaceMarkers
    {
        get => surfaceMarkers;
        private set => SetField(ref surfaceMarkers, value);
    }

    public bool HasSpecies => Species.Count > 0;

    public bool HasRadarTargets => RadarTargets.Count > 0;

    public bool ShowRadar => survey.ShowCanonnSignalsOnRadar;

    public bool UseSmallRadarCircles => survey.UseSmallCanonnRadarCircles;

    public string SpeciesCountText => Species.Count == 1
        ? "1 known species"
        : $"{Species.Count:N0} known species";

    public string BodyName => survey.CurrentStatus?.BodyName
        ?? "Current body";

    public string HeadingText => survey.CurrentStatus is { } status
        ? $"HEADING {status.NormalizedHeading:000}°"
        : "HEADING —";

    public string FilterText => survey.SkipPriorScansLowValue
        ? $"Signals below {FormatCredits(survey.PriorScanMinimumValue)} hidden"
        : "All known signal values";

    public string StatusText
    {
        get => statusText;
        private set => SetField(ref statusText, value);
    }

    public string InputMode
    {
        get => inputMode;
        private set => SetField(ref inputMode, value);
    }

    public bool IsLoading
    {
        get => isLoading;
        private set
        {
            if (SetField(ref isLoading, value))
            {
                OnPropertyChanged(nameof(ShouldShow));
            }
        }
    }

    public bool ShouldShow => survey.ShouldLoadPriorScans && HasSpecies;

    public async Task RefreshAsync()
    {
        if (disposed || !TryCreateContext(out var context))
        {
            Recalculate();
            return;
        }

        if (cachedResult is not null
            && string.Equals(cachedKey, context.CacheKey, StringComparison.Ordinal))
        {
            Recalculate(context);
            return;
        }

        if (string.Equals(failedKey, context.CacheKey, StringComparison.Ordinal)
            && DateTimeOffset.UtcNow < retryAfter)
        {
            return;
        }

        if (!await refreshLock.WaitAsync(
            0,
            CancellationToken.None).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            if (disposed || !TryCreateContext(out context))
            {
                Recalculate();
                return;
            }

            if (cachedResult is not null
                && string.Equals(
                    cachedKey,
                    context.CacheKey,
                    StringComparison.Ordinal))
            {
                Recalculate(context);
                return;
            }

            IsLoading = true;
            StatusText = $"Loading Canonn signals for {context.SystemName}…";
            var result = await client.GetAsync(
                context.SystemName,
                context.CommanderName,
                disposalCancellation.Token).ConfigureAwait(true);
            if (disposed)
            {
                return;
            }

            cachedResult = result;
            cachedKey = context.CacheKey;
            failedKey = null;
            retryAfter = default;
            Recalculate(context);
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or JsonException
                or TaskCanceledException
                or IOException
                or InvalidOperationException)
        {
            if (!disposed)
            {
                cachedResult = null;
                cachedKey = null;
                failedKey = context.CacheKey;
                retryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
                Species = [];
                RadarTargets = [];
                SurfaceMarkers = [];
                StatusText = "Canonn prior scans are unavailable: "
                    + exception.Message;
            }
        }
        finally
        {
            if (!disposed)
            {
                IsLoading = false;
            }

            refreshLock.Release();
        }
    }

    public void ApplyPreparation(OverlayPreparationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InputMode = result.IsClickThrough ? "PASSIVE" : "BLOCKED";
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        survey.PropertyChanged -= OnSurveyPropertyChanged;
        disposalCancellation.Cancel();
        disposalCancellation.Dispose();
    }

    private void OnSurveyPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SystemSurveyViewModel.Snapshot)
            or nameof(SystemSurveyViewModel.CurrentStatus)
            or nameof(SystemSurveyViewModel.CurrentExobiology)
            or nameof(SystemSurveyViewModel.ShouldLoadPriorScans)
            or nameof(SystemSurveyViewModel.SkipPriorScansLowValue)
            or nameof(SystemSurveyViewModel.PriorScanMinimumValue)
            or nameof(SystemSurveyViewModel.HideOwnCanonnSignals)
            or nameof(SystemSurveyViewModel.ShowCanonnSignalsOnRadar)
            or nameof(SystemSurveyViewModel.UseSmallCanonnRadarCircles)
            or nameof(SystemSurveyViewModel.ShouldSuppressForActiveBuildProjects)
            or nameof(SystemSurveyViewModel.AreBiologyOverlaysSuppressedForRepeatVisit))
        {
            Recalculate();
        }
    }

    private void Recalculate()
    {
        if (!TryCreateContext(out var context))
        {
            Species = [];
            RadarTargets = [];
            SurfaceMarkers = [];
            StatusText = "Waiting for surface navigation context.";
            RaiseContextProperties();
            return;
        }

        if (cachedResult is null
            || !string.Equals(cachedKey, context.CacheKey, StringComparison.Ordinal))
        {
            Species = [];
            RadarTargets = [];
            SurfaceMarkers = [];
            RaiseContextProperties();
            return;
        }

        Recalculate(context);
    }

    private void Recalculate(PriorScanContext context)
    {
        if (cachedResult is null)
        {
            return;
        }

        var plan = planner.CreatePlan(new PriorScanPlanRequest(
            context.BodyShortName,
            context.BodyRadiusMeters,
            context.CurrentLocation,
            context.HeadingDegrees,
            cachedResult.Signals,
            context.AnalyzedEntryIds,
            context.PersonalSamples,
            context.ActiveSpeciesName,
            survey.SkipPriorScansLowValue,
            survey.PriorScanMinimumValue,
            survey.HideOwnCanonnSignals));
        Species = plan.Species
            .Select(item => PriorScanSpeciesViewModel.Create(
                item,
                survey.CurrentStatus?.Altitude ?? 0))
            .ToArray();
        RadarTargets = Species
            .Where(item => !item.IsAnalyzed)
            .SelectMany(item => item.Targets.Select(target =>
                new PriorScanRadarTargetViewModel(
                    target.DistanceMeters,
                    target.RelativeBearingDegrees,
                    item.SampleRadiusMeters,
                    item.IsActive,
                    target.IsClose)))
            .ToArray();
        // Absolute lat/long for PlotGrounded-style surface radar rings.
        SurfaceMarkers = plan.Species
            .Where(item => !item.IsAnalyzed)
            .SelectMany(item =>
            {
                var genus = ExobiologyReferenceCatalog.GetGenusName(
                    item.SpeciesName);
                var radius = ExobiologyReferenceCatalog.GetSampleDistanceMeters(
                    genus);
                return item.Targets.Select(target =>
                    new PriorScanSurfaceMarkerViewModel(
                        item.DisplayName,
                        target.Location,
                        survey.UseSmallCanonnRadarCircles
                            ? Math.Min(radius, 40)
                            : radius,
                        item.IsActive,
                        target.State == PriorScanTargetState.Close));
            })
            .ToArray();
        StatusText = Species.Count == 0
            ? "No unfiltered Canonn biology coordinates remain for this body."
            : "Surface bearings update from the live journal status.";
        RaiseContextProperties();
    }

    private bool TryCreateContext(out PriorScanContext context)
    {
        context = null!;
        var snapshot = survey.Snapshot;
        var status = survey.CurrentStatus;
        if (!survey.ShouldLoadPriorScans
            || status is null
            || string.IsNullOrWhiteSpace(snapshot.SystemName)
            || string.IsNullOrWhiteSpace(status.BodyName))
        {
            return false;
        }

        var body = ResolveCurrentBody(snapshot, status.BodyName);
        var radius = (double)status.PlanetRadius;
        if (radius <= 0)
        {
            radius = body?.RadiusMeters ?? 0;
        }

        if (radius <= 0)
        {
            return false;
        }

        SurfaceCoordinate location;
        try
        {
            location = new SurfaceCoordinate(status.Latitude, status.Longitude);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var bodyShortName = body?.ShortName
            ?? GetBodyShortName(status.BodyName, snapshot.SystemName);
        var analyzed = body?.Organisms
            .Where(organism => organism.IsAnalyzed && organism.EntryId is > 0)
            .Select(organism => organism.EntryId!.Value)
            .ToArray()
            ?? [];
        var samples = new[]
            {
                survey.CurrentExobiology.ScanOne,
                survey.CurrentExobiology.ScanTwo,
            }
            .Where(sample => sample is not null
                && (string.IsNullOrWhiteSpace(sample.Body)
                    || string.Equals(
                        sample.Body,
                        status.BodyName,
                        StringComparison.OrdinalIgnoreCase)))
            .Cast<BioSampleSnapshot>()
            .Select(TryCreatePersonalSample)
            .Where(sample => sample is not null)
            .Cast<PriorScanPersonalSample>()
            .ToArray();
        var commander = commanderNameProvider()?.Trim() ?? string.Empty;
        context = new PriorScanContext(
            snapshot.SystemName,
            bodyShortName,
            radius,
            location,
            status.NormalizedHeading,
            analyzed,
            samples,
            (survey.CurrentExobiology.ScanTwo
                ?? survey.CurrentExobiology.ScanOne)?.Species,
            commander,
            snapshot.SystemName + "|" + commander);
        return true;
    }

    private void RaiseContextProperties()
    {
        OnPropertyChanged(nameof(BodyName));
        OnPropertyChanged(nameof(HeadingText));
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(ShowRadar));
        OnPropertyChanged(nameof(UseSmallRadarCircles));
        OnPropertyChanged(nameof(ShouldShow));
    }

    private static SystemScanBodySnapshot? ResolveCurrentBody(
        SystemScanSnapshot snapshot,
        string bodyName)
    {
        return snapshot.Bodies.FirstOrDefault(body => string.Equals(
                body.Name,
                bodyName,
                StringComparison.OrdinalIgnoreCase))
            ?? (snapshot.CurrentBodyId is { } bodyId
                ? snapshot.Bodies.FirstOrDefault(body => body.BodyId == bodyId)
                : null);
    }

    private static string GetBodyShortName(
        string bodyName,
        string systemName)
    {
        return bodyName.StartsWith(systemName, StringComparison.OrdinalIgnoreCase)
            ? bodyName[systemName.Length..].Trim()
            : bodyName;
    }

    private static PriorScanPersonalSample? TryCreatePersonalSample(
        BioSampleSnapshot sample)
    {
        try
        {
            return new PriorScanPersonalSample(
                sample.Species,
                new SurfaceCoordinate(
                    sample.Location.Latitude,
                    sample.Location.Longitude));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string FormatCredits(long value)
    {
        return value >= 1_000_000
            ? $"{value / 1_000_000d:N1} M CR"
            : value >= 1_000
                ? $"{value / 1_000d:N0} K CR"
                : $"{value:N0} CR";
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record PriorScanContext(
        string SystemName,
        string BodyShortName,
        double BodyRadiusMeters,
        SurfaceCoordinate CurrentLocation,
        double HeadingDegrees,
        IReadOnlyCollection<long> AnalyzedEntryIds,
        IReadOnlyList<PriorScanPersonalSample> PersonalSamples,
        string? ActiveSpeciesName,
        string CommanderName,
        string CacheKey);
}

public sealed record PriorScanSpeciesViewModel(
    string DisplayName,
    string RewardText,
    bool IsAnalyzed,
    bool IsActive,
    double RowOpacity,
    int SampleRadiusMeters,
    string ApproachText,
    bool HasShallowApproach,
    bool HasIdealApproach,
    bool HasSteepApproach,
    bool HasTooSteepApproach,
    IReadOnlyList<PriorScanTargetViewModel> Targets)
{
    public static PriorScanSpeciesViewModel Create(
        PriorScanSpecies species,
        double altitudeMeters)
    {
        var genus = ExobiologyReferenceCatalog.GetGenusName(
            species.SpeciesName);
        var approachAngle = altitudeMeters > 500
            && species.Targets.Count > 0
            && species.Targets[0] is { DistanceMeters: > 0 } target
                ? Math.Atan(altitudeMeters / target.DistanceMeters)
                    * 180d / Math.PI
                : 0;
        var showApproach = approachAngle > 5;
        return new PriorScanSpeciesViewModel(
            species.DisplayName,
            FormatCredits(species.Reward),
            species.IsAnalyzed,
            species.IsActive,
            species.IsAnalyzed ? 0.5 : 1,
            ExobiologyReferenceCatalog.GetSampleDistanceMeters(genus),
            showApproach ? $"-{approachAngle:N0}°" : string.Empty,
            showApproach && approachAngle <= 30,
            approachAngle is > 30 and <= 50,
            approachAngle is > 50 and <= 60,
            approachAngle > 60,
            species.Targets.Select(PriorScanTargetViewModel.Create).ToArray());
    }

    private static string FormatCredits(long value)
    {
        return value >= 1_000_000
            ? $"{value / 1_000_000d:N2} M CR"
            : value >= 1_000
                ? $"{value / 1_000d:N1} K CR"
                : $"{value:N0} CR";
    }
}

public sealed record PriorScanTargetViewModel(
    double DistanceMeters,
    double RelativeBearingDegrees,
    string DistanceText,
    string BearingText,
    bool IsClose,
    bool IsFar,
    bool IsAnalyzed)
{
    public static PriorScanTargetViewModel Create(PriorScanTarget target)
    {
        return new PriorScanTargetViewModel(
            target.DistanceMeters,
            target.RelativeBearingDegrees,
            FormatDistance(target.DistanceMeters),
            $"{target.BearingDegrees:000}°",
            target.State == PriorScanTargetState.Close,
            target.State == PriorScanTargetState.Far,
            target.State == PriorScanTargetState.Analyzed);
    }

    private static string FormatDistance(double meters)
    {
        return meters >= 1_000_000
            ? $"{meters / 1_000_000d:N1} Mm"
            : (meters >= 1_000) switch
            {
                true => $"{meters / 1_000d:N1} km",
                false => $"{meters:N0} m"
            };
    }
}

public sealed record PriorScanRadarTargetViewModel(
    double DistanceMeters,
    double RelativeBearingDegrees,
    int SampleRadiusMeters,
    bool IsActive,
    bool IsClose);

/// <summary>
/// Absolute surface position for a Canonn prior-scan ring on PlotGrounded.
/// </summary>
public sealed record PriorScanSurfaceMarkerViewModel(
    string DisplayName,
    SurfaceCoordinate Location,
    int SampleRadiusMeters,
    bool IsActive,
    bool IsClose);
