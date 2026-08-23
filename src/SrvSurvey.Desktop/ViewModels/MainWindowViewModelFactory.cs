using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Inara;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>
/// Owns the production construction seam for the main-window feature graph.
/// Startup resources transfer exactly once: first to construction rollback,
/// then to the completed <see cref="MainWindowViewModel"/>.
/// </summary>
internal static class MainWindowViewModelFactory
{
    public static MainWindowViewModel Create(
        MainWindowViewModelStartup startup)
    {
        ArgumentNullException.ThrowIfNull(startup);

        var construction = startup.CreateConstructionContext();
        startup.TransferOwnershipToConstruction();
        return new MainWindowViewModel(
            startup.ConfiguredJournalDirectory,
            construction);
    }

    internal static MainWindowViewModel CreateForTesting(
        string? configuredJournalDirectory,
        MainWindowViewModelConstructionContext construction)
    {
        ArgumentNullException.ThrowIfNull(construction);
        return new MainWindowViewModel(
            configuredJournalDirectory,
            construction);
    }
}

internal sealed class MainWindowViewModelStartup : IDisposable
{
    private OverlayInteractionViewModel? ownedOverlayInteraction;
    private IFirstFootfallInferenceService? ownedFirstFootfallInferenceService;

    public MainWindowViewModelStartup(
        string? configuredJournalDirectory,
        MainWindowFoundationInputs foundation,
        MainWindowOverlayInputs overlay,
        MainWindowExplorationInputs exploration,
        MainWindowTravelInputs travel,
        MainWindowOnlineInputs online)
    {
        ConfiguredJournalDirectory = configuredJournalDirectory;
        Foundation = foundation
            ?? throw new ArgumentNullException(nameof(foundation));
        Overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        Exploration = exploration
            ?? throw new ArgumentNullException(nameof(exploration));
        Travel = travel ?? throw new ArgumentNullException(nameof(travel));
        Online = online ?? throw new ArgumentNullException(nameof(online));
        ownedOverlayInteraction = overlay.OverlayInteraction;
        ownedFirstFootfallInferenceService =
            exploration.FirstFootfallInferenceService;
    }

    public string? ConfiguredJournalDirectory { get; }

    public MainWindowFoundationInputs Foundation { get; }

    public MainWindowOverlayInputs Overlay { get; }

    public MainWindowExplorationInputs Exploration { get; }

    public MainWindowTravelInputs Travel { get; }

    public MainWindowOnlineInputs Online { get; }

    public void Dispose()
    {
        TryDispose(ownedFirstFootfallInferenceService);
        ownedFirstFootfallInferenceService = null;
        TryDispose(ownedOverlayInteraction);
        ownedOverlayInteraction = null;
    }

    private void TryDispose(IDisposable? resource)
    {
        try
        {
            resource?.Dispose();
        }
        catch (Exception exception)
        {
            try
            {
                Foundation.ApplicationLogService?.Append(
                    "Main window startup cleanup failed: "
                    + exception.Message);
            }
            catch
            {
                // Startup cleanup must preserve the original failure.
            }
        }
    }

    internal MainWindowViewModelConstructionContext CreateConstructionContext()
    {
        return new MainWindowViewModelConstructionContext
        {
            Foundation = Foundation,
            Overlay = Overlay,
            Exploration = Exploration,
            Travel = Travel,
            Online = Online,
        };
    }

    internal void TransferOwnershipToConstruction()
    {
        ownedFirstFootfallInferenceService = null;
        ownedOverlayInteraction = null;
    }
}

internal sealed class MainWindowViewModelStartupResource<T>(
    T resource,
    Action<Exception>? reportFailure = null)
    : IDisposable
    where T : class, IDisposable
{
    private T? ownedResource = resource
        ?? throw new ArgumentNullException(nameof(resource));

    public T Transfer()
    {
        var result = ownedResource
            ?? throw new InvalidOperationException(
                "The startup resource was already transferred.");
        ownedResource = null;
        return result;
    }

    public void Dispose()
    {
        try
        {
            ownedResource?.Dispose();
        }
        catch (Exception exception)
        {
            try
            {
                reportFailure?.Invoke(exception);
            }
            catch
            {
                // Startup cleanup must preserve the original failure.
            }
        }

        ownedResource = null;
    }
}

internal sealed class MainWindowViewModelConstructionContext
{
    public MainWindowFoundationInputs Foundation { get; init; } = new();

    public MainWindowOverlayInputs Overlay { get; init; } = new();

    public MainWindowExplorationInputs Exploration { get; init; } = new();

    public MainWindowTravelInputs Travel { get; init; } = new();

    public MainWindowOnlineInputs Online { get; init; } = new();

    public Action<MainWindowViewModelConstructionCheckpoint>? Checkpoint
    {
        get;
        init;
    }
}

internal sealed class MainWindowFoundationInputs
{
    public RavenThemeService? ThemeService { get; init; }

    public AppDataPaths? AppDataPaths { get; init; }

    public GlobalInputSettingsViewModel? InputSettings { get; init; }

    public ApplicationLogService? ApplicationLogService { get; init; }

    public string? TargetFrontierId { get; init; }

    public CommanderPreferenceSettingsStore? CommanderPreferenceSettingsStore
    {
        get;
        init;
    }

    public bool CommanderPreferenceCommandLineOverride { get; init; }

    public string? CommanderPreferenceInitialStatus { get; init; }

    public CommanderProfileViewModel? FrontierProfile { get; init; }

    public bool IsDiagnosticReplay { get; init; }

    public string? DiagnosticReplayStatus { get; init; }

    public HttpClient? ExternalNetworkClient { get; init; }

    public Func<Avalonia.PixelRect?>? ReplayViewportProvider { get; init; }
}

internal sealed class MainWindowOverlayInputs
{
    public LegacyOverlayLayoutStore? OverlayLayoutStore { get; init; }

    public LegacyOverlayLayout? OverlayLayout { get; init; }

    public OverlayInteractionViewModel? OverlayInteraction { get; init; }

    public OverlayThemeSettingsViewModel? OverlayThemeSettings { get; init; }

    public IScreenshotProcessingService? ScreenshotProcessingService
    {
        get;
        init;
    }

    public GuardianOverlaySettingsStore? GuardianOverlaySettingsStore
    {
        get;
        init;
    }

    public DesktopBehaviorSettingsStore? DesktopBehaviorSettingsStore
    {
        get;
        init;
    }
}

internal sealed class MainWindowExplorationInputs
{
    public IBoxelSystemResolver? BoxelSystemResolver { get; init; }

    public IFirstFootfallInferenceService? FirstFootfallInferenceService
    {
        get;
        init;
    }

    public ISystemBodyDataClient? SystemBodyDataClient { get; init; }

    public IEliteGameProcessDetector? EliteGameProcessDetector { get; init; }

    public TimeSpan? SystemBodyDataRetryDelay { get; init; }

    public HumanSiteSettingsStore? HumanSiteSettingsStore { get; init; }
}

internal sealed class MainWindowTravelInputs
{
    public IGameWindowSwitcher? GameWindowSwitcher { get; init; }

    public StationInfoSettingsStore? StationInfoSettingsStore { get; init; }
}

internal sealed class MainWindowOnlineInputs
{
    public ICanonnHumanSiteClient? CanonnHumanSiteClient { get; init; }

    public ICanonnHumanSitePublisher? CanonnHumanSitePublisher { get; init; }

    public IEddnPublisher? EddnPublisher { get; init; }

    public IVoxStellarPublisher? VoxStellarPublisher { get; init; }

    public IInaraPublisher? InaraPublisher { get; init; }

    public GreenGasGiantPublicationCoordinator?
        GreenGasGiantPublicationCoordinator
    {
        get;
        init;
    }
}

internal enum MainWindowViewModelConstructionCheckpoint
{
    FoundationReady,
    OverlayReady,
    ExplorationReady,
    TravelReady,
    OnlineAndShellReady,
}

internal sealed class MainWindowViewModelConstructionRollback(
    ApplicationLogService? applicationLogService)
{
    // Registration follows construction order so rollback is deterministic and
    // releases later feature families before their dependencies.
    private readonly Stack<Func<ValueTask>> cleanup = new();

    public void Add(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        cleanup.Push(() =>
        {
            action();
            return ValueTask.CompletedTask;
        });
    }

    public void Add(Func<ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        cleanup.Push(action);
    }

    public void Add(IDisposable? resource)
    {
        if (resource is not null)
        {
            Add(resource.Dispose);
        }
    }

    public void AddIfCreated(object? provided, IDisposable? created)
    {
        if (provided is null)
        {
            Add(created);
        }
    }

    public void Commit()
    {
        cleanup.Clear();
    }

    public void Rollback()
    {
        var failures = Task.Run(RollbackCoreAsync, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        foreach (var failure in failures)
        {
            try
            {
                applicationLogService?.Append(
                    "Main window construction cleanup failed: "
                    + failure.Message);
            }
            catch
            {
                // Construction must preserve the original failure.
            }
        }
    }

    private async Task<IReadOnlyList<Exception>> RollbackCoreAsync()
    {
        List<Exception> failures = [];
        while (cleanup.TryPop(out var action))
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        return failures;
    }
}

internal sealed class MainWindowViewModelConstructionOwnership<T>(
    T? initialResource)
    : IDisposable
    where T : class, IDisposable
{
    private T? ownedResource = initialResource;

    public void Own(T candidateResource)
    {
        ArgumentNullException.ThrowIfNull(candidateResource);
        if (ownedResource is not null
            && !ReferenceEquals(ownedResource, candidateResource))
        {
            throw new InvalidOperationException(
                "Construction ownership cannot replace an owned resource.");
        }

        ownedResource = candidateResource;
    }

    public void Transfer()
    {
        ownedResource = null;
    }

    public void Dispose()
    {
        var disposingResource = ownedResource;
        ownedResource = null;
        disposingResource?.Dispose();
    }
}
