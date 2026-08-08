using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayThemeSettingsViewModel : INotifyPropertyChanged
{
    private const string CategoryGeneral = "General";
    private const string CategoryExobiology = "Exobiology";
    private const string CategoryColonisation = "Colonisation";
    private const string CategoryHumanSettlements = "Human settlements";
    private const string CategoryGuardian = "Guardian";

    private static readonly IReadOnlyList<OverlayThemeColorDefinition> Definitions =
    [
        new(CategoryGeneral, "orange", "Primary accent"),
        new(CategoryGeneral, "orangeDark", "Primary accent (dim)"),
        new(CategoryGeneral, "cyan", "Secondary accent"),
        new(CategoryGeneral, "cyanDark", "Secondary accent (dim)"),
        new(CategoryGeneral, "red", "Danger"),
        new(CategoryGeneral, "redDark", "Danger (dim)"),
        new(CategoryGeneral, "yellow", "Warning"),
        new(CategoryGeneral, "green", "Success"),
        new(CategoryGeneral, "greenDark", "Success (dim)"),
        new(CategoryGeneral, "white", "Primary text"),
        new(CategoryGeneral, "black", "Background"),
        new(CategoryGeneral, "menuGold", "Menu gold"),
        new(CategoryGeneral, "grey", "Muted text"),
        new(CategoryExobiology, "bio.confirmed", "Confirmed reward PIP"),
        new(CategoryExobiology, "bio.confirmedDim", "Analyzed reward PIP"),
        new(CategoryExobiology, "bio.potential", "Possible reward segment"),
        new(CategoryExobiology, "bio.prediction", "Predicted reward PIP"),
        new(
            CategoryExobiology,
            "bio.predictionPotential",
            "Predicted possible segment"),
        new(
            CategoryExobiology,
            "bio.gold",
            "First-discovery candidate PIP"),
        new(
            CategoryExobiology,
            "bio.goldDark",
            "First-discovery candidate (analyzed)"),
        new(CategoryExobiology, "bio.unknown", "Unknown reward frame"),
        new(CategoryExobiology, "bio.unknownGlyph", "Unknown reward question mark"),
        new(CategoryExobiology, "bio.hatch", "Prediction hatch lines"),
        new(CategoryExobiology, "bio.empty", "Empty reward segment"),
        new(CategoryExobiology, "bio.white", "Biology labels and values"),
        new(CategoryColonisation, "colonise.surplus", "Surplus"),
        new(CategoryColonisation, "colonise.surplusDark", "Surplus (dim)"),
        new(CategoryColonisation, "colonise.deficit", "Deficit"),
        new(CategoryColonisation, "colonise.deficitDark", "Deficit (dim)"),
        new(CategoryColonisation, "colonise.highlight", "Highlight"),
        new(CategoryColonisation, "colonise.item", "Item"),
        new(CategoryColonisation, "colonise.itemDark", "Item (dim)"),
        new(
            CategoryColonisation,
            "colonise.rowHighlight",
            "Commodity row fill (colour + alpha)"),
        new(CategoryHumanSettlements, "fcz.checkpoint", "Checkpoint"),
        new(CategoryHumanSettlements, "fcz.checkpointLocal", "Local checkpoint"),
        new(CategoryHumanSettlements, "fcz.powerPost", "Power post"),
        new(CategoryGuardian, "guardian.background", "Background"),
        new(CategoryGuardian, "guardian.surface", "Surface / choice fill"),
        new(CategoryGuardian, "guardian.header", "Header / title"),
        new(CategoryGuardian, "guardian.primary", "Primary accent"),
        new(CategoryGuardian, "guardian.primaryDark", "Primary accent (dim)"),
        new(CategoryGuardian, "guardian.secondary", "Live / destination"),
        new(CategoryGuardian, "guardian.secondaryDark", "Live / destination (dim)"),
        new(CategoryGuardian, "guardian.text", "Body text"),
        new(CategoryGuardian, "guardian.muted", "Muted / detail text"),
        new(CategoryGuardian, "guardian.warning", "Warning / alignment"),
        new(CategoryGuardian, "guardian.success", "Success / present"),
        new(CategoryGuardian, "guardian.danger", "Danger / missing"),
    ];

    private readonly LegacyOverlayThemeStore activeStore;
    private readonly OverlayThemeStateStore stateStore;
    private readonly RavenThemeService? themeService;
    private readonly DelegateCommand applyCommand;
    private readonly DelegateCommand previewCommand;
    private readonly DelegateCommand saveStateCommand;
    private readonly DelegateCommand loadStateCommand;
    private readonly DelegateCommand deleteStateCommand;
    private IReadOnlyList<OverlayThemeCategoryViewModel> categories = [];
    private IReadOnlyList<string> savedStates = [];
    private string? selectedSavedState;
    private string stateName = string.Empty;
    private string statusMessage = string.Empty;

    public OverlayThemeSettingsViewModel(
        LegacyOverlayThemeStore activeStore,
        OverlayThemeStateStore stateStore,
        RavenThemeService? themeService = null,
        LegacyOverlayTheme? initialTheme = null)
    {
        this.activeStore = activeStore
            ?? throw new ArgumentNullException(nameof(activeStore));
        this.stateStore = stateStore
            ?? throw new ArgumentNullException(nameof(stateStore));
        this.themeService = themeService;
        applyCommand = new DelegateCommand(Apply, () => CanApply);
        previewCommand = new DelegateCommand(Preview, () => CanPreview);
        saveStateCommand = new DelegateCommand(SaveState, () => CanSaveState);
        loadStateCommand = new DelegateCommand(
            LoadState,
            () => SelectedSavedState is not null);
        deleteStateCommand = new DelegateCommand(
            DeleteState,
            () => CanDeleteSelectedState);
        ApplyCommand = applyCommand;
        PreviewCommand = previewCommand;
        SaveStateCommand = saveStateCommand;
        LoadStateCommand = loadStateCommand;
        DeleteStateCommand = deleteStateCommand;
        RestoreDefaultsCommand = new DelegateCommand(RestoreDefaults);
        ReloadActiveCommand = new DelegateCommand(ReloadActive);

        var theme = initialTheme ?? themeService?.CurrentOverlayTheme ?? activeStore.Load();
        ReplaceEditors(theme.Colors, acceptChanges: true);
        RefreshSavedStates(
            OverlayThemePresetCatalog.FindMatching(theme.Colors)?.Name);
        StatusMessage = theme.Error
            ?? "Overlay colours are independent from the application theme."
                + " Imported theme.json colours are active until you apply changes here.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<OverlayThemeCategoryViewModel> Categories
    {
        get => categories;
        private set
        {
            categories = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<string> SavedStates
    {
        get => savedStates;
        private set
        {
            savedStates = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSavedStates));
        }
    }

    public bool HasSavedStates => SavedStates.Count > 0;

    public string? SelectedSavedState
    {
        get => selectedSavedState;
        set => SetSelectedSavedState(value, loadBuiltInPreset: true);
    }

    public string StateName
    {
        get => stateName;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(stateName, normalized, StringComparison.Ordinal))
            {
                return;
            }

            stateName = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSaveState));
            saveStateCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (string.Equals(statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            statusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => StatusMessage.Length > 0;

    public bool IsDirty => Categories.SelectMany(category => category.Colors)
        .Any(color => color.IsDirty);

    public bool HasValidationErrors => Categories.SelectMany(category => category.Colors)
        .Any(color => color.HasValidationError);

    public bool CanApply => IsDirty && !HasValidationErrors;

    public bool CanPreview => themeService is not null && !HasValidationErrors;

    public bool CanSaveState => !string.IsNullOrWhiteSpace(StateName)
        && StateName.Trim().Length <= 80
        && !OverlayThemePresetCatalog.TryGet(StateName.Trim(), out _)
        && !HasValidationErrors;

    public bool CanDeleteSelectedState => SelectedSavedState is not null
        && !OverlayThemePresetCatalog.TryGet(SelectedSavedState, out _);

    public ICommand ApplyCommand { get; }

    public ICommand PreviewCommand { get; }

    public ICommand SaveStateCommand { get; }

    public ICommand LoadStateCommand { get; }

    public ICommand DeleteStateCommand { get; }

    public ICommand RestoreDefaultsCommand { get; }

    public ICommand ReloadActiveCommand { get; }

    private void Apply()
    {
        try
        {
            var theme = CreateDraftTheme();
            var result = activeStore.Save(theme);
            themeService?.ApplyOverlayTheme(theme);
            foreach (var editor in Categories.SelectMany(category => category.Colors))
            {
                editor.AcceptChanges();
            }

            StatusMessage = "Applied overlay colours to theme.json and all open overlays."
                + (result.BackupPath is null
                    ? string.Empty
                    : $" Previous theme backup: {result.BackupPath}");
            OnEditorsChanged();
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException)
        {
            StatusMessage = "The overlay theme was not changed: " + exception.Message;
        }
    }

    private void Preview()
    {
        try
        {
            themeService?.ApplyOverlayTheme(CreateDraftTheme());
            StatusMessage = "Refreshed all open overlays and position previews with unsaved colours. Apply to keep them, or discard changes to restore theme.json.";
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or InvalidOperationException
                or ArgumentException)
        {
            StatusMessage = "The overlay preview was not refreshed: "
                + exception.Message;
        }
    }

    private void SaveState()
    {
        if (OverlayThemePresetCatalog.TryGet(StateName.Trim(), out _))
        {
            StatusMessage = $"'{StateName.Trim()}' is a built-in overlay theme name."
                + " Choose another name for a saved state.";
            return;
        }

        try
        {
            var result = stateStore.SaveState(StateName, CreateDraftTheme().Colors);
            RefreshSavedStates(result.StateName);
            StateName = result.StateName;
            StatusMessage = result.ReplacedExisting
                ? $"Updated saved overlay theme state '{result.StateName}'."
                : $"Saved overlay theme state '{result.StateName}'.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException)
        {
            StatusMessage = "The overlay theme state was not saved: " + exception.Message;
        }
    }

    private void LoadState()
    {
        var selected = SelectedSavedState;
        if (selected is null)
        {
            return;
        }

        if (OverlayThemePresetCatalog.TryGet(selected, out var preset))
        {
            LoadBuiltInPreset(preset, updateSelection: true);
            Preview();
            return;
        }

        var collection = stateStore.Load();
        var state = collection.States.SingleOrDefault(candidate => string.Equals(
            candidate.Name,
            selected,
            StringComparison.Ordinal));
        if (state is null)
        {
            StatusMessage = collection.Error
                ?? $"Saved overlay theme state '{selected}' was not found.";
            RefreshSavedStates();
            return;
        }

        ReplaceEditors(state.Colors, acceptChanges: false);
        StateName = state.Name;
        Preview();
    }

    private void DeleteState()
    {
        var selected = SelectedSavedState;
        if (selected is null)
        {
            return;
        }

        try
        {
            _ = stateStore.DeleteState(selected);
            RefreshSavedStates();
            StatusMessage = $"Deleted saved overlay theme state '{selected}'."
                + " The active in-game theme was not changed.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or ArgumentException
                or KeyNotFoundException)
        {
            StatusMessage = "The saved overlay theme state was not deleted: "
                + exception.Message;
        }
    }

    private void RestoreDefaults()
    {
        LoadBuiltInPreset(
            OverlayThemePresetCatalog.Default,
            updateSelection: true);
    }

    private void ReloadActive()
    {
        var theme = activeStore.Load();
        ReplaceEditors(theme.Colors, acceptChanges: true);
        SetSelectedSavedState(
            OverlayThemePresetCatalog.FindMatching(theme.Colors)?.Name,
            loadBuiltInPreset: false);
        themeService?.ApplyOverlayTheme(theme);
        StatusMessage = theme.Error
            ?? "Reloaded the active theme.json colours, discarded editor changes, and refreshed open overlays.";
    }

    private LegacyOverlayTheme CreateDraftTheme()
    {
        if (HasValidationErrors)
        {
            throw new InvalidDataException(
                "Correct invalid colour values before saving or applying.");
        }

        return new LegacyOverlayTheme(
            Categories.SelectMany(category => category.Colors).ToDictionary(
                editor => editor.Key,
                editor => editor.Color,
                StringComparer.Ordinal),
            IsCustom: true,
            Error: null);
    }

    private void ReplaceEditors(
        IReadOnlyDictionary<string, Color> colors,
        bool acceptChanges)
    {
        var definitions = Definitions.ToList();
        var knownKeys = definitions.Select(definition => definition.Key)
            .ToHashSet(StringComparer.Ordinal);
        definitions.AddRange(colors.Keys
            .Where(key => !knownKeys.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(key => new OverlayThemeColorDefinition(
                "Custom / imported",
                key,
                key)));

        Categories = definitions
            .GroupBy(definition => definition.Category, StringComparer.Ordinal)
            .Select(group => new OverlayThemeCategoryViewModel(
                group.Key,
                group.Select(definition =>
                {
                    var initialColor = colors.TryGetValue(
                        definition.Key,
                        out var configuredColor)
                            ? configuredColor
                            : LegacyOverlayThemeStore.CreateDefault().GetColor(
                                definition.Key);
                    var editor = new OverlayThemeColorEditorViewModel(
                        definition,
                        initialColor,
                        OnEditorsChanged);
                    if (!acceptChanges)
                    {
                        editor.MarkDirty();
                    }

                    return editor;
                }).ToArray()))
            .ToArray();
        OnEditorsChanged();
    }

    private void RefreshSavedStates(string? select = null)
    {
        var collection = stateStore.Load();
        SavedStates = OverlayThemePresetCatalog.Presets
            .Select(preset => preset.Name)
            .Concat(collection.States
                .Select(state => state.Name)
                .Where(name => !OverlayThemePresetCatalog.TryGet(name, out _)))
            .ToArray();
        var requested = select ?? SelectedSavedState;
        SetSelectedSavedState(
            requested is not null && SavedStates.Contains(requested)
                ? requested
                : null,
            loadBuiltInPreset: false);
        if (collection.Error is not null)
        {
            StatusMessage = collection.Error;
        }
    }

    private void SetSelectedSavedState(
        string? value,
        bool loadBuiltInPreset)
    {
        if (string.Equals(selectedSavedState, value, StringComparison.Ordinal))
        {
            if (loadBuiltInPreset
                && OverlayThemePresetCatalog.TryGet(value, out var currentPreset))
            {
                LoadBuiltInPreset(currentPreset, updateSelection: false);
            }

            return;
        }

        selectedSavedState = value;
        OnPropertyChanged(nameof(SelectedSavedState));
        OnPropertyChanged(nameof(CanDeleteSelectedState));
        loadStateCommand.RaiseCanExecuteChanged();
        deleteStateCommand.RaiseCanExecuteChanged();
        if (loadBuiltInPreset
            && OverlayThemePresetCatalog.TryGet(value, out var preset))
        {
            LoadBuiltInPreset(preset, updateSelection: false);
        }
    }

    private void LoadBuiltInPreset(
        OverlayThemePreset preset,
        bool updateSelection)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (updateSelection)
        {
            SetSelectedSavedState(preset.Name, loadBuiltInPreset: false);
        }

        ReplaceEditors(preset.Colors, acceptChanges: false);
        StateName = string.Empty;
        StatusMessage = $"Loaded the built-in '{preset.Name}' overlay theme."
            + " Choose Apply to use it in-game.";
    }

    private void OnEditorsChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasValidationErrors));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanSaveState));
        applyCommand.RaiseCanExecuteChanged();
        previewCommand.RaiseCanExecuteChanged();
        saveStateCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

public sealed record OverlayThemeCategoryViewModel(
    string Name,
    IReadOnlyList<OverlayThemeColorEditorViewModel> Colors);

public sealed class OverlayThemeColorEditorViewModel : INotifyPropertyChanged
{
    private readonly Action changed;
    private Color acceptedColor;
    private Color color;
    private string hexValue;
    private string validationMessage = string.Empty;
    private bool forceDirty;

    public OverlayThemeColorEditorViewModel(
        OverlayThemeColorDefinition definition,
        Color initialColor,
        Action changed)
    {
        Definition = definition;
        this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
        acceptedColor = initialColor;
        color = initialColor;
        hexValue = LegacyOverlayThemeStore.FormatHtmlColor(initialColor);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayThemeColorDefinition Definition { get; }

    public string Key => Definition.Key;

    public string DisplayName => Definition.DisplayName;

    public string HexValue
    {
        get => hexValue;
        set
        {
            var updated = value?.Trim() ?? string.Empty;
            if (string.Equals(hexValue, updated, StringComparison.Ordinal))
            {
                return;
            }

            hexValue = updated;
            if (LegacyOverlayThemeStore.TryParseHtmlColor(updated, out var parsed))
            {
                color = parsed;
                validationMessage = string.Empty;
            }
            else
            {
                validationMessage = "Use #RRGGBB or #RRGGBBAA.";
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(Color));
            OnPropertyChanged(nameof(OpacityPercent));
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(HasValidationError));
            OnPropertyChanged(nameof(IsDirty));
            changed();
        }
    }

    public Color Color
    {
        get => color;
        set
        {
            var formatted = LegacyOverlayThemeStore.FormatHtmlColor(value);
            if (color == value
                && string.Equals(hexValue, formatted, StringComparison.Ordinal)
                && !HasValidationError)
            {
                return;
            }

            color = value;
            hexValue = formatted;
            validationMessage = string.Empty;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HexValue));
            OnPropertyChanged(nameof(OpacityPercent));
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(HasValidationError));
            OnPropertyChanged(nameof(IsDirty));
            changed();
        }
    }

    /// <summary>
    /// Separate 0–100 opacity control that only mutates the alpha channel,
    /// leaving RGB from the colour picker / hex field intact.
    /// </summary>
    public int OpacityPercent
    {
        get => (int)Math.Round(color.A * 100d / 255d);
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            var alpha = (byte)Math.Round(clamped * 255d / 100d);
            if (color.A == alpha)
            {
                return;
            }

            Color = Color.FromArgb(alpha, color.R, color.G, color.B);
        }
    }

    public string ValidationMessage => validationMessage;

    public bool HasValidationError => ValidationMessage.Length > 0;

    public bool IsDirty => forceDirty || color != acceptedColor;

    public void AcceptChanges()
    {
        acceptedColor = color;
        forceDirty = false;
        OnPropertyChanged(nameof(IsDirty));
    }

    public void MarkDirty()
    {
        forceDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record OverlayThemeColorDefinition(
    string Category,
    string Key,
    string DisplayName);
