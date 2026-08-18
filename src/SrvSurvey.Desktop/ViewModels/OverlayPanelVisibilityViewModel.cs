using System.ComponentModel;
using System.Runtime.CompilerServices;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayPanelVisibilityViewModel : INotifyPropertyChanged
{
    private readonly OverlayPanelVisibilitySettingsStore store;
    private readonly OverlayWindowRegistry registry;
    private string persistenceStatus = string.Empty;

    public OverlayPanelVisibilityViewModel(
        OverlayPanelVisibilitySettingsStore store,
        GlobalInputSettingsViewModel inputSettings,
        OverlayWindowRegistry? registry = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentNullException.ThrowIfNull(inputSettings);
        this.registry = registry ?? OverlayWindowRegistry.Shared;

        var stored = store.Load();
        Panels = OverlayLayoutCatalog.Supported
            .Select(definition =>
            {
                var shortcut = inputSettings.Bindings.Single(binding =>
                    string.Equals(
                        binding.Definition.OverlayPlotterName,
                        definition.Name,
                        StringComparison.Ordinal));
                var panel = new OverlayPanelVisibilityEntryViewModel(
                    definition,
                    ResolveCategory(definition.Name),
                    stored.GetValueOrDefault(definition.Name, true),
                    shortcut,
                    Save);
                this.registry.SetUserVisibility(
                    definition.Name,
                    panel.IsEnabled);
                return panel;
            })
            .ToArray();
    }

    public IReadOnlyList<OverlayPanelVisibilityEntryViewModel> Panels { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PersistenceStatus
    {
        get => persistenceStatus;
        private set
        {
            if (string.Equals(persistenceStatus, value, StringComparison.Ordinal))
            {
                return;
            }

            persistenceStatus = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(PersistenceStatus)));
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(HasPersistenceStatus)));
        }
    }

    public bool HasPersistenceStatus => PersistenceStatus.Length > 0;

    public IReadOnlyList<OverlayPanelVisibilityEntryViewModel> ForCategory(
        OverlaySettingsCategory category)
    {
        return Panels.Where(panel => panel.Category == category
            || (panel.PlotterName == "PlotSphericalSearch"
                && category is OverlaySettingsCategory.Exploration
                    or OverlaySettingsCategory.Travel)).ToArray();
    }

    public bool Toggle(string plotterName)
    {
        var panel = Panels.FirstOrDefault(candidate => string.Equals(
            candidate.PlotterName,
            plotterName,
            StringComparison.Ordinal));
        if (panel is null)
        {
            return false;
        }

        panel.IsEnabled = !panel.IsEnabled;
        return true;
    }

    private void Save(OverlayPanelVisibilityEntryViewModel changed)
    {
        registry.SetUserVisibility(changed.PlotterName, changed.IsEnabled);
        try
        {
            store.Save(Panels.ToDictionary(
                panel => panel.PlotterName,
                panel => panel.IsEnabled,
                StringComparer.Ordinal));
            PersistenceStatus = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            PersistenceStatus =
                "Panel availability changed for this session but could not be saved: "
                + exception.Message;
        }
    }

    private static OverlaySettingsCategory ResolveCategory(string plotterName)
    {
        return plotterName switch
        {
            "PlotBioStatus" or "PlotBioSystem" or "PlotGrounded"
                or "PlotMiniTrack" or "PlotPriorScans" or "PlotTrackTarget" =>
                OverlaySettingsCategory.Exobiology,
            "PlotBodyInfo" or "PlotFlightWarning" or "PlotFSS"
                or "PlotFSSInfo" or "PlotGalMap" or "PlotSysStatus" =>
                OverlaySettingsCategory.Exploration,
            "PlotFleetCarrierRoute" or "PlotJumpInfo" or "PlotRouteBio"
                or "PlotStationInfo" => OverlaySettingsCategory.Travel,
            "PlotSphericalSearch" => OverlaySettingsCategory.Boxel,
            "PlotGuardians" or "PlotGuardianStatus" or "PlotGuardianSystem"
                or "PlotRamTah" => OverlaySettingsCategory.Guardian,
            "PlotFootCombat" or "PlotHumanSite" or "PlotMassacre"
                or "PlotQuestMini" => OverlaySettingsCategory.Quests,
            "PlotBuildCommodities" => OverlaySettingsCategory.Colonization,
            _ => OverlaySettingsCategory.Global,
        };
    }
}

public sealed class OverlayPanelVisibilityEntryViewModel
    : INotifyPropertyChanged
{
    private readonly Action<OverlayPanelVisibilityEntryViewModel> save;
    private bool isEnabled;

    public OverlayPanelVisibilityEntryViewModel(
        OverlayLayoutDefinition definition,
        OverlaySettingsCategory category,
        bool isEnabled,
        InputBindingViewModel shortcut,
        Action<OverlayPanelVisibilityEntryViewModel> save)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
        Category = category;
        this.isEnabled = isEnabled;
        Shortcut = shortcut ?? throw new ArgumentNullException(nameof(shortcut));
        this.save = save ?? throw new ArgumentNullException(nameof(save));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayLayoutDefinition Definition { get; }

    public string PlotterName => Definition.Name;

    public string DisplayName => Definition.DisplayName;

    public string Description =>
        $"When off, the {DisplayName} panel is rendered inactive and is not visible until toggled on.";

    public OverlaySettingsCategory Category { get; }

    public InputBindingViewModel Shortcut { get; }

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (isEnabled == value)
            {
                return;
            }

            isEnabled = value;
            OnPropertyChanged();
            save(this);
        }
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
