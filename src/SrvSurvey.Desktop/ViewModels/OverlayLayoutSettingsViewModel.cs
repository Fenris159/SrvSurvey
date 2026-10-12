using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayLayoutSettingsViewModel : INotifyPropertyChanged
{
    private readonly LegacyOverlayLayoutStore store;
    private readonly LegacyOverlayLayout activeLayout;
    private readonly DelegateCommand saveCommand;
    private readonly DelegateCommand resetSelectedCommand;
    private readonly DelegateCommand recoverCommand;
    private OverlayPlacementEditorViewModel? selectedOverlay;
    private string statusMessage = string.Empty;
    private bool hasLoadError;

    public OverlayLayoutSettingsViewModel(LegacyOverlayLayoutStore store, LegacyOverlayLayout activeLayout)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.activeLayout = activeLayout ?? throw new ArgumentNullException(nameof(activeLayout));
        recoverCommand = new DelegateCommand(RecoverLayout, () => hasLoadError);
        RecoverCommand = recoverCommand;
        saveCommand = new DelegateCommand(Save, () => IsDirty && !hasLoadError);
        resetSelectedCommand = new DelegateCommand(ResetSelected, () => SelectedOverlay is not null);
        ReloadCommand = new DelegateCommand(Reload);
        SaveCommand = saveCommand;
        ResetSelectedCommand = resetSelectedCommand;
        Reload();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<OverlayPlacementEditorViewModel> Overlays { get; private set; } = [];

    public OverlayPlacementEditorViewModel? SelectedOverlay
    {
        get => selectedOverlay;
        set
        {
            if (SetField(ref selectedOverlay, value))
            {
                resetSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsDirty => Overlays.Any(overlay => overlay.IsDirty);

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (SetField(ref statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public ICommand SaveCommand { get; }

    public ICommand ResetSelectedCommand { get; }

    public ICommand ReloadCommand { get; }

    /// <summary>Offers an explicit backed-up repair when a damaged file prevents editing.</summary>
    public ICommand RecoverCommand { get; }

    /// <summary>Shows recovery only while the layout reader reports a failure.</summary>
    public bool CanRecoverLayout => hasLoadError;

    /// <summary>Identifies the current profile's save location for remote troubleshooting.</summary>
    public string SaveDirectory => store.DirectoryPath;

    /// <summary>Runs explicit recovery without discarding malformed originals.</summary>
    private void RecoverLayout()
    {
        try
        {
            string preserved = store.RecoverInvalidLayout();
            activeLayout.ReplaceWith(store.Load());
            Reload();
            StatusMessage = "Overlay layout recovered. Previous files were preserved at " + preserved;
        }
        catch (Exception exception) when (LegacyOverlayLayoutStore.IsPersistenceFailure(exception))
        {
            StatusMessage = "Overlay layout recovery could not finish: " + exception.Message;
            store.ReportFailure("Layout recovery failed", exception);
        }
    }

    public LegacyOverlayScaleMigrationResult MigrateLegacyScaleOverrides(double renderScaling)
    {
        LegacyOverlayScaleMigrationResult result = store.MigrateLegacyScaleOverrides(renderScaling);
        if (result.MigratedCount == 0)
        {
            return result;
        }

        LegacyOverlayLayout migrated = store.Load();
        if (migrated.Error is not null)
        {
            throw new InvalidDataException(migrated.Error);
        }

        activeLayout.ReplaceWith(migrated);
        Reload();
        return result;
    }

    /// <summary>Loads opacity rows with their complete placement metadata and exposes recoverable file errors.</summary>
    private void Reload()
    {
        LegacyOverlayLayout layout = store.Load();
        hasLoadError = layout.Error is not null;
        OnPropertyChanged(nameof(CanRecoverLayout));
        recoverCommand.RaiseCanExecuteChanged();
        double inheritedOpacity = (layout.DefaultOpacity ?? 1d) * 100d;
        Overlays = OverlayLayoutCatalog
            .Supported.Select(definition => new OverlayPlacementEditorViewModel(
                definition,
                layout.Placements.GetValueOrDefault(definition.Name, definition.DefaultPlacement),
                inheritedOpacity,
                OnEditorChanged
            ))
            .ToArray();
        SelectedOverlay = Overlays.Count > 0 ? Overlays[0] : null;
        StatusMessage = layout.Error ?? "Opacity overrides are ready. Changes apply to visible overlays after Save.";
        OnPropertyChanged(nameof(Overlays));
        OnEditorChanged();
    }

    /// <summary>Merges only edited opacity or position fields into the latest persisted layout.</summary>
    private void Save()
    {
        OverlayPlacementEditorViewModel[] dirty = Overlays.Where(overlay => overlay.IsDirty).ToArray();
        if (dirty.Length == 0 || hasLoadError)
        {
            return;
        }

        try
        {
            var session = new OverlayPositionEditSession(
                new LegacyOverlayLayout(
                    dirty.ToDictionary(
                        overlay => overlay.Name,
                        overlay => overlay.OriginalPlacement,
                        StringComparer.Ordinal
                    ),
                    null,
                    null
                )
            );
            foreach (OverlayPlacementEditorViewModel overlay in dirty)
            {
                session.SetPlacement(overlay.Name, overlay.Placement);
            }
            LegacyOverlayLayoutSaveResult result = store.SaveChanges(session);
            LegacyOverlayLayout updated = result.CommittedLayout ?? store.Load();
            if (updated.Error is not null)
            {
                throw new InvalidDataException(updated.Error);
            }

            activeLayout.ReplaceWith(updated);
            foreach (OverlayPlacementEditorViewModel overlay in Overlays)
            {
                overlay.AcceptChanges();
            }

            StatusMessage =
                $"Saved {result.UpdatedPlacementCount:N0} overlay setting(s). "
                + "Visible overlays update immediately."
                + (result.BackupPath is null ? string.Empty : $" Previous layout backup: {result.BackupPath}");
        }
        catch (Exception exception) when (LegacyOverlayLayoutStore.IsPersistenceFailure(exception))
        {
            StatusMessage = "The overlay layout was not changed: " + exception.Message;
            store.ReportFailure("Opacity save failed; draft retained", exception);
        }

        OnEditorChanged();
    }

    private void ResetSelected()
    {
        SelectedOverlay?.ResetToDefault();
    }

    private void OnEditorChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        saveCommand.RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
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

    private sealed class DelegateCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => execute();

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class OverlayPlacementEditorViewModel : INotifyPropertyChanged
{
    private readonly OverlayLayoutDefinition definition;
    private readonly Action changed;
    private LegacyOverlayPlacement acceptedPlacement;
    private LegacyHorizontalAnchor horizontalAnchor;
    private int horizontalOffset;
    private LegacyVerticalAnchor verticalAnchor;
    private int verticalOffset;
    private bool useCustomOpacity;
    private double customOpacityPercent;

    public OverlayPlacementEditorViewModel(
        OverlayLayoutDefinition definition,
        LegacyOverlayPlacement placement,
        double inheritedOpacityPercent,
        Action changed
    )
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
        acceptedPlacement = placement ?? throw new ArgumentNullException(nameof(placement));
        horizontalAnchor = placement.Horizontal;
        horizontalOffset = placement.HorizontalOffset;
        verticalAnchor = placement.Vertical;
        verticalOffset = placement.VerticalOffset;
        useCustomOpacity = placement.Opacity is not null;
        customOpacityPercent = (placement.Opacity * 100d) ?? Math.Clamp(inheritedOpacityPercent, 0, 100);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static IReadOnlyList<LegacyHorizontalAnchor> HorizontalAnchors { get; } =
        Enum.GetValues<LegacyHorizontalAnchor>();

    public static IReadOnlyList<LegacyVerticalAnchor> VerticalAnchors { get; } = Enum.GetValues<LegacyVerticalAnchor>();

    public IReadOnlyList<LegacyHorizontalAnchor> HorizontalAnchorOptions { get; } = HorizontalAnchors;

    public IReadOnlyList<LegacyVerticalAnchor> VerticalAnchorOptions { get; } = VerticalAnchors;

    public string Name => definition.Name;

    public string DisplayName => definition.DisplayName;

    public string Description => definition.ConfigurationBinding;

    public LegacyHorizontalAnchor HorizontalAnchor
    {
        get => horizontalAnchor;
        set => SetField(ref horizontalAnchor, value);
    }

    public int HorizontalOffset
    {
        get => horizontalOffset;
        set => SetField(ref horizontalOffset, value);
    }

    public LegacyVerticalAnchor VerticalAnchor
    {
        get => verticalAnchor;
        set => SetField(ref verticalAnchor, value);
    }

    public int VerticalOffset
    {
        get => verticalOffset;
        set => SetField(ref verticalOffset, value);
    }

    public bool UseCustomOpacity
    {
        get => useCustomOpacity;
        set
        {
            if (SetField(ref useCustomOpacity, value))
            {
                OnPropertyChanged(nameof(IsCustomOpacityEnabled));
            }
        }
    }

    public bool IsCustomOpacityEnabled => UseCustomOpacity;

    public double CustomOpacityPercent
    {
        get => customOpacityPercent;
        set => SetField(ref customOpacityPercent, Math.Clamp(value, 0, 100));
    }

    /// <summary>Provides the opening record for merging only this editor's changed fields.</summary>
    internal LegacyOverlayPlacement OriginalPlacement => acceptedPlacement;

    /// <summary>Retains metadata for opacity-only edits and drops a stale coordinate frame after a direct position edit.</summary>
    public LegacyOverlayPlacement Placement =>
        acceptedPlacement with
        {
            Horizontal = HorizontalAnchor,
            HorizontalOffset = HorizontalOffset,
            Vertical = VerticalAnchor,
            VerticalOffset = VerticalOffset,
            Opacity = UseCustomOpacity ? CustomOpacityPercent / 100d : null,
            PositionReference = HasPositionChanges ? null : acceptedPlacement.PositionReference,
        };

    public bool IsDirty => Placement != acceptedPlacement;

    public bool HasPositionChanges =>
        HorizontalAnchor != acceptedPlacement.Horizontal
        || HorizontalOffset != acceptedPlacement.HorizontalOffset
        || VerticalAnchor != acceptedPlacement.Vertical
        || VerticalOffset != acceptedPlacement.VerticalOffset;

    public LegacyOverlayPlacement ApplyOpacityTo(LegacyOverlayPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return placement with { Opacity = Placement.Opacity };
    }

    public void ResetToDefault()
    {
        LegacyOverlayPlacement placement = definition.DefaultPlacement;
        HorizontalAnchor = placement.Horizontal;
        HorizontalOffset = placement.HorizontalOffset;
        VerticalAnchor = placement.Vertical;
        VerticalOffset = placement.VerticalOffset;
        UseCustomOpacity = placement.Opacity is not null;
        if (placement.Opacity is not null)
        {
            CustomOpacityPercent = placement.Opacity.Value * 100d;
        }
    }

    public void AcceptChanges()
    {
        acceptedPlacement = Placement;
        OnPropertyChanged(nameof(IsDirty));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(IsDirty));
        changed();
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
