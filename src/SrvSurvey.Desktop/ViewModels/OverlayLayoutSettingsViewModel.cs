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
    private OverlayPlacementEditorViewModel? selectedOverlay;
    private string statusMessage = string.Empty;
    private bool hasLoadError;

    public OverlayLayoutSettingsViewModel(
        LegacyOverlayLayoutStore store,
        LegacyOverlayLayout activeLayout)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.activeLayout = activeLayout
            ?? throw new ArgumentNullException(nameof(activeLayout));
        saveCommand = new DelegateCommand(Save, () => IsDirty && !hasLoadError);
        resetSelectedCommand = new DelegateCommand(
            ResetSelected,
            () => SelectedOverlay is not null);
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

    private void Reload()
    {
        var layout = store.Load();
        hasLoadError = layout.Error is not null;
        var inheritedOpacity = (layout.DefaultOpacity ?? 1d) * 100d;
        Overlays = OverlayLayoutCatalog.Supported
            .Select(definition => new OverlayPlacementEditorViewModel(
                definition,
                layout.Placements.GetValueOrDefault(
                    definition.Name,
                    definition.DefaultPlacement),
                inheritedOpacity,
                OnEditorChanged))
            .ToArray();
        SelectedOverlay = Overlays.FirstOrDefault();
        StatusMessage = layout.Error
            ?? "Opacity overrides are ready. Changes apply to visible overlays after Save.";
        OnPropertyChanged(nameof(Overlays));
        OnEditorChanged();
    }

    private void Save()
    {
        var dirty = Overlays
            .Where(overlay => overlay.IsDirty)
            .ToArray();
        if (dirty.Length == 0 || hasLoadError)
        {
            return;
        }

        try
        {
            var latest = store.Load();
            if (latest.Error is not null)
            {
                throw new InvalidDataException(latest.Error);
            }

            var changed = dirty.ToDictionary(
                overlay => overlay.Name,
                overlay => overlay.HasPositionChanges
                    ? overlay.Placement
                    : overlay.ApplyOpacityTo(
                        latest.Placements.GetValueOrDefault(
                            overlay.Name,
                            overlay.Placement)),
                StringComparer.Ordinal);
            var result = store.Save(changed);
            var updated = store.Load();
            if (updated.Error is not null)
            {
                throw new InvalidDataException(updated.Error);
            }

            activeLayout.ReplaceWith(updated);
            foreach (var overlay in Overlays)
            {
                overlay.AcceptChanges();
            }

            StatusMessage = $"Saved {result.UpdatedPlacementCount:N0} overlay setting(s). "
                + "Visible overlays update immediately."
                + (result.BackupPath is null
                    ? string.Empty
                    : $" Previous layout backup: {result.BackupPath}");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException)
        {
            StatusMessage = "The overlay layout was not changed: " + exception.Message;
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
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private sealed class DelegateCommand(Action execute, Func<bool>? canExecute = null)
        : ICommand
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
        Action changed)
    {
        this.definition = definition
            ?? throw new ArgumentNullException(nameof(definition));
        this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
        acceptedPlacement = placement
            ?? throw new ArgumentNullException(nameof(placement));
        horizontalAnchor = placement.Horizontal;
        horizontalOffset = placement.HorizontalOffset;
        verticalAnchor = placement.Vertical;
        verticalOffset = placement.VerticalOffset;
        useCustomOpacity = placement.Opacity is not null;
        customOpacityPercent = (placement.Opacity * 100d)
            ?? Math.Clamp(inheritedOpacityPercent, 0, 100);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static IReadOnlyList<LegacyHorizontalAnchor> HorizontalAnchors { get; } =
        Enum.GetValues<LegacyHorizontalAnchor>();

    public static IReadOnlyList<LegacyVerticalAnchor> VerticalAnchors { get; } =
        Enum.GetValues<LegacyVerticalAnchor>();

    public IReadOnlyList<LegacyHorizontalAnchor> HorizontalAnchorOptions =>
        HorizontalAnchors;

    public IReadOnlyList<LegacyVerticalAnchor> VerticalAnchorOptions =>
        VerticalAnchors;

    public string Name => definition.Name;

    public string DisplayName => definition.DisplayName;

    public string Description => Name;

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

    public LegacyOverlayPlacement Placement => new(
        HorizontalAnchor,
        HorizontalOffset,
        VerticalAnchor,
        VerticalOffset,
        UseCustomOpacity ? CustomOpacityPercent / 100d : null);

    public bool IsDirty => Placement != acceptedPlacement;

    public bool HasPositionChanges =>
        HorizontalAnchor != acceptedPlacement.Horizontal
        || HorizontalOffset != acceptedPlacement.HorizontalOffset
        || VerticalAnchor != acceptedPlacement.Vertical
        || VerticalOffset != acceptedPlacement.VerticalOffset;

    public LegacyOverlayPlacement ApplyOpacityTo(
        LegacyOverlayPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return placement with { Opacity = Placement.Opacity };
    }

    public void ResetToDefault()
    {
        var placement = definition.DefaultPlacement;
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
        OnPropertyChanged(nameof(IsDirty));
        changed();
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
