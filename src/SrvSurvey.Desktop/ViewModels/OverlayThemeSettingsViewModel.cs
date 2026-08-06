using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media;
using SrvSurvey.Desktop.Theming;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayThemeSettingsViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<OverlayThemeColorDefinition> Definitions =
    [
        new("General", "orange", "Primary accent"),
        new("General", "orangeDark", "Primary accent (dim)"),
        new("General", "cyan", "Secondary accent"),
        new("General", "cyanDark", "Secondary accent (dim)"),
        new("General", "red", "Danger"),
        new("General", "redDark", "Danger (dim)"),
        new("General", "yellow", "Warning"),
        new("General", "green", "Success"),
        new("General", "greenDark", "Success (dim)"),
        new("General", "white", "Primary text"),
        new("General", "black", "Background"),
        new("General", "menuGold", "Menu gold"),
        new("General", "grey", "Muted text"),
        new("Exobiology", "bio.gold", "Candidate highlight"),
        new("Exobiology", "bio.goldDark", "Candidate highlight (dim)"),
        new("Exobiology", "bio.unknown", "Unknown species"),
        new("Exobiology", "bio.hatch", "Hatch fill"),
        new("Exobiology", "bio.white", "Biology text"),
        new("Exobiology", "bio.prediction", "Prediction"),
        new("Colonisation", "colonise.surplus", "Surplus"),
        new("Colonisation", "colonise.surplusDark", "Surplus (dim)"),
        new("Colonisation", "colonise.deficit", "Deficit"),
        new("Colonisation", "colonise.deficitDark", "Deficit (dim)"),
        new("Colonisation", "colonise.highlight", "Highlight"),
        new("Colonisation", "colonise.item", "Item"),
        new("Colonisation", "colonise.itemDark", "Item (dim)"),
        new("Guardian sites", "fcz.checkpoint", "Checkpoint"),
        new("Guardian sites", "fcz.checkpointLocal", "Local checkpoint"),
        new("Guardian sites", "fcz.powerPost", "Power post"),
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
        loadStateCommand = new DelegateCommand(LoadState, () => SelectedSavedState is not null);
        deleteStateCommand = new DelegateCommand(DeleteState, () => SelectedSavedState is not null);
        ApplyCommand = applyCommand;
        PreviewCommand = previewCommand;
        SaveStateCommand = saveStateCommand;
        LoadStateCommand = loadStateCommand;
        DeleteStateCommand = deleteStateCommand;
        RestoreDefaultsCommand = new DelegateCommand(RestoreDefaults);
        ReloadActiveCommand = new DelegateCommand(ReloadActive);

        var theme = initialTheme ?? themeService?.CurrentOverlayTheme ?? activeStore.Load();
        ReplaceEditors(theme.Colors, acceptChanges: true);
        RefreshSavedStates();
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
        set
        {
            if (string.Equals(selectedSavedState, value, StringComparison.Ordinal))
            {
                return;
            }

            selectedSavedState = value;
            OnPropertyChanged();
            loadStateCommand.RaiseCanExecuteChanged();
            deleteStateCommand.RaiseCanExecuteChanged();
        }
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
        && !HasValidationErrors;

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
        StatusMessage = $"Loaded '{state.Name}' into the editor. Choose Apply to use it in-game.";
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
        ReplaceEditors(
            LegacyOverlayThemeStore.CreateDefault().Colors,
            acceptChanges: false);
        StatusMessage = "Loaded the original overlay defaults into the editor."
            + " Choose Apply to use them in-game.";
    }

    private void ReloadActive()
    {
        var theme = activeStore.Load();
        ReplaceEditors(theme.Colors, acceptChanges: true);
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
        SavedStates = collection.States.Select(state => state.Name).ToArray();
        var firstSavedState = SavedStates.Count > 0
            ? SavedStates[0]
            : null;
        SelectedSavedState = select is not null && SavedStates.Contains(select)
            ? select
            : firstSavedState;
        if (collection.Error is not null)
        {
            StatusMessage = collection.Error;
        }
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
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(HasValidationError));
            OnPropertyChanged(nameof(IsDirty));
            changed();
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
