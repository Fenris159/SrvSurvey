using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BoxelSurveyStatsViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly (BoxelPlanetClass Class, string Code, string DisplayName)[] DisplayOrder =
    [
        (BoxelPlanetClass.Earthlike, "ELW", "Earth-like world"),
        (BoxelPlanetClass.WaterWorld, "WW", "Water world"),
        (BoxelPlanetClass.HighMetalContent, "HMC", "High metal content"),
        (BoxelPlanetClass.MetalRich, "MR", "Metal rich"),
        (BoxelPlanetClass.AmmoniaWorld, "AW", "Ammonia world"),
        (BoxelPlanetClass.Rocky, "RB", "Rocky body"),
        (BoxelPlanetClass.Icy, "IB", "Icy body"),
        (BoxelPlanetClass.RockyIce, "RIB", "Rocky ice body"),
        (BoxelPlanetClass.SudarskyI, "GG1", "Sudarsky class I"),
        (BoxelPlanetClass.SudarskyII, "GG2", "Sudarsky class II"),
        (BoxelPlanetClass.SudarskyIII, "GG3", "Sudarsky class III"),
        (BoxelPlanetClass.SudarskyIV, "GG4", "Sudarsky class IV"),
        (BoxelPlanetClass.SudarskyV, "GG5", "Sudarsky class V"),
        (BoxelPlanetClass.GasGiantWaterLife, "GGWL", "Gas giant with water life"),
        (BoxelPlanetClass.GasGiantAmmoniaLife, "GGAL", "Gas giant with ammonia life"),
        (BoxelPlanetClass.HeliumRichGasGiant, "HRGG", "Helium-rich gas giant"),
        (BoxelPlanetClass.WaterGiant, "WG", "Water giant"),
        (BoxelPlanetClass.WaterGiantWithLife, "WG Life", "Water giant with life"),
        (BoxelPlanetClass.HeliumGasGiant, "HGG", "Helium gas giant"),
    ];

    private readonly BoxelSurveyStatsCoordinator coordinator;
    private readonly BoxelSurveyStatsSettingsStore settingsStore;
    private readonly BoxelSearchViewModel? search;
    private readonly List<string> focusedPrefixes = [];
    private BoxelSurveyStatsPreferences preferences;
    private char selectedMassCode = 'c';
    private string? selectedPrefix;
    private bool isDetailVisible;
    private bool showSearchRollup;
    private bool isBusy;
    private string statusMessage = string.Empty;
    private string detailTitle = "Select a boxel";
    private string heliumText = BoxelSurveyAverageFormatter.Placeholder;
    private string visitedText = BoxelSurveyAverageFormatter.Placeholder;
    private string completenessText = BoxelSurveyAverageFormatter.Placeholder;
    private string valueText = BoxelSurveyAverageFormatter.Placeholder;
    private string minAveragesText = "10";
    private string minExportText = "5";
    private IReadOnlyList<BoxelSurveyBrowserRowViewModel> browserRows = [];
    private IReadOnlyList<BoxelSurveyClassRowViewModel> classRows = [];
    private BoxelSurveyBoxelSnapshot? detail;

    public BoxelSurveyStatsViewModel(
        BoxelSurveyStatsCoordinator coordinator,
        BoxelSurveyStatsSettingsStore settingsStore,
        BoxelSearchViewModel? search = null)
    {
        this.coordinator = coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        this.search = search;
        preferences = settingsStore.Load();
        ApplyPreferences();
        minAveragesText = preferences.MinSystemsForAverages.ToString(
            CultureInfo.CurrentCulture);
        minExportText = preferences.MinSystemsForExport.ToString(
            CultureInfo.CurrentCulture);
        MassCodes = CreateMassCodes();
        SelectMassCodeCommand = new RelayCommand(parameter =>
        {
            if (parameter is char massCode)
            {
                SelectedMassCode = massCode;
            }
            else if (parameter is string text
                && text.Length == 1)
            {
                SelectedMassCode = text[0];
            }
        });
        OpenRowCommand = new AsyncCommand(OpenRowAsync);
        BackCommand = new RelayCommand(_ =>
        {
            IsDetailVisible = false;
            RefreshBrowser();
        });
        RefreshCommand = new AsyncCommand(RefreshAsync);
        RebuildCommand = new AsyncCommand(RebuildAsync);
        ExportCommand = new AsyncCommand(ExportAsync);
        coordinator.Changed += OnCoordinatorChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? RebuildRequested;

    public event EventHandler? ExportRequested;

    public ICommand SelectMassCodeCommand { get; }

    public ICommand OpenRowCommand { get; }

    public ICommand BackCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand RebuildCommand { get; }

    public ICommand ExportCommand { get; }

    public IReadOnlyList<BoxelSurveyMassCodeOption> MassCodes { get; }

    public IReadOnlyList<BoxelSurveyBrowserRowViewModel> BrowserRows
    {
        get => browserRows;
        private set => SetField(ref browserRows, value);
    }

    public IReadOnlyList<BoxelSurveyClassRowViewModel> ClassRows
    {
        get => classRows;
        private set => SetField(ref classRows, value);
    }

    public IReadOnlyList<BoxelSurveyIndexEntry> RecentEntries
        => coordinator.RecentEntries();

    public char SelectedMassCode
    {
        get => selectedMassCode;
        set
        {
            var massCode = char.ToLowerInvariant(value);
            if (!BoxelAddress.IsValidMassCode(massCode)
                || !SetField(ref selectedMassCode, massCode))
            {
                return;
            }

            focusedPrefixes.Clear();
            RefreshMassCodes();
            RefreshBrowser();
        }
    }

    public bool IsDetailVisible
    {
        get => isDetailVisible;
        private set
        {
            if (SetField(ref isDetailVisible, value))
            {
                OnPropertyChanged(nameof(IsBrowserVisible));
            }
        }
    }

    public bool IsBrowserVisible => !IsDetailVisible;

    public bool CanShowSearchRollup =>
        (focusedPrefixes.Count > 1
            || (search?.IsActive == true && search.SearchPrefixes.Count > 0));

    public bool ShowSearchRollup
    {
        get => showSearchRollup;
        set
        {
            if (SetField(ref showSearchRollup, value))
            {
                _ = RefreshDetailAsync();
            }
        }
    }

    public bool TreatNavBeaconAsFullyScanned
    {
        get => preferences.TreatNavBeaconAsFullyScanned;
        set
        {
            if (preferences.TreatNavBeaconAsFullyScanned == value)
            {
                return;
            }

            preferences = preferences with { TreatNavBeaconAsFullyScanned = value };
            SavePreferences();
            _ = RefreshDetailAsync();
        }
    }

    public string MinAveragesText
    {
        get => minAveragesText;
        set
        {
            if (!SetField(ref minAveragesText, value)
                || !int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.CurrentCulture,
                    out var parsed))
            {
                return;
            }

            preferences = preferences with
            {
                MinSystemsForAverages = Math.Clamp(parsed, 1, 1000),
            };
            SavePreferences();
            _ = RefreshDetailAsync();
        }
    }

    public string MinExportText
    {
        get => minExportText;
        set
        {
            if (!SetField(ref minExportText, value)
                || !int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.CurrentCulture,
                    out var parsed))
            {
                return;
            }

            preferences = preferences with
            {
                MinSystemsForExport = Math.Clamp(parsed, 1, 1000),
            };
            SavePreferences();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool IsBusy
    {
        get => isBusy;
        private set => SetField(ref isBusy, value);
    }

    public string DetailTitle
    {
        get => detailTitle;
        private set => SetField(ref detailTitle, value);
    }

    public string HeliumText
    {
        get => heliumText;
        private set => SetField(ref heliumText, value);
    }

    public string VisitedText
    {
        get => visitedText;
        private set => SetField(ref visitedText, value);
    }

    public string CompletenessText
    {
        get => completenessText;
        private set => SetField(ref completenessText, value);
    }

    public string ValueText
    {
        get => valueText;
        private set => SetField(ref valueText, value);
    }

    public string? SelectedPrefix => selectedPrefix;

    public BoxelSurveyBoxelSnapshot? Detail => detail;

    public BoxelSurveyStatsPreferences Preferences => preferences;

    public BoxelSurveyStatsCoordinator Coordinator => coordinator;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var current = coordinator.Current?.Prefix;
        if (!string.IsNullOrWhiteSpace(current))
        {
            await OpenPrefixAsync(current, cancellationToken).ConfigureAwait(false);
            return;
        }

        IsDetailVisible = false;
        RefreshBrowser();
    }

    public async Task FocusPrefixesAsync(
        IEnumerable<string> prefixes,
        char? massCode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefixes);
        focusedPrefixes.Clear();
        focusedPrefixes.AddRange(
            prefixes.Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .Distinct(StringComparer.Ordinal));
        coordinator.SetRetainPrefixes(focusedPrefixes);
        if (massCode is { } code && BoxelAddress.IsValidMassCode(code))
        {
            selectedMassCode = char.ToLowerInvariant(code);
            RefreshMassCodes();
        }

        OnPropertyChanged(nameof(CanShowSearchRollup));
        var first = focusedPrefixes.FirstOrDefault();
        if (first is not null)
        {
            await OpenPrefixAsync(first, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            IsDetailVisible = false;
            RefreshBrowser();
        }
    }

    public async Task OpenPrefixAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        selectedPrefix = prefix;
        IsDetailVisible = true;
        await RefreshDetailAsync(cancellationToken).ConfigureAwait(false);
        RefreshBrowser();
    }

    public async Task RefreshAsync()
    {
        if (IsDetailVisible)
        {
            await RefreshDetailAsync().ConfigureAwait(false);
        }

        RefreshBrowser();
        OnPropertyChanged(nameof(RecentEntries));
    }

    public Task RebuildAsync()
    {
        RebuildRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task ExportAsync()
    {
        ExportRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void ReportStatus(string message)
    {
        StatusMessage = message;
        OnPropertyChanged(nameof(HasStatusMessage));
    }

    public void Dispose()
    {
        coordinator.Changed -= OnCoordinatorChanged;
    }

    private async Task OpenRowAsync()
    {
        // CommandParameter is supplied through the button; Avalonia binds it
        // separately from ICommand.Execute in some hosts, so OpenPrefix is
        // also invoked from the window code-behind.
    }

    internal async Task OpenRowAsync(object? parameter)
    {
        if (parameter is BoxelSurveyBrowserRowViewModel row)
        {
            await OpenPrefixAsync(row.Prefix).ConfigureAwait(false);
        }
        else if (parameter is string prefix)
        {
            await OpenPrefixAsync(prefix).ConfigureAwait(false);
        }
    }

    private async Task RefreshDetailAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selectedPrefix))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var snapshot = showSearchRollup
                ? await coordinator.RollupAsync(RollupPrefixes(), cancellationToken)
                    .ConfigureAwait(false)
                : await coordinator.GetAsync(selectedPrefix, cancellationToken)
                    .ConfigureAwait(false)
                    ?? BoxelSurveyBoxelSnapshot.Empty;
            detail = snapshot;
            var format = new BoxelSurveyAverageFormat(preferences.MinSystemsForAverages);
            DetailTitle = showSearchRollup
                ? selectedPrefix + " · saved search"
                : snapshot.Prefix;
            HeliumText = FormatHelium(snapshot.MinHeliumPercent, snapshot.MaxHeliumPercent);
            VisitedText = string.Create(
                CultureInfo.CurrentCulture,
                $"{snapshot.Visited} / {snapshot.ImpliedPopulation} visited");
            CompletenessText = string.Create(
                CultureInfo.CurrentCulture,
                $"FSS complete {snapshot.FssCompleteCount}    FSS bodies {snapshot.FssDiscoveryBodyCountSum} ({FormatAverage(snapshot.BodyAverage)})");
            ValueText = string.Create(
                CultureInfo.CurrentCulture,
                $"{FormatCredits(snapshot.CurrentValue)} as scanned   ·   {FormatCredits(snapshot.MappedPotentialValue)} if mapped");
            ClassRows = BuildClassRows(snapshot, format);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private IReadOnlyList<string> RollupPrefixes()
    {
        if (focusedPrefixes.Count > 0)
        {
            return focusedPrefixes;
        }

        return search?.SearchPrefixes ?? [selectedPrefix ?? string.Empty];
    }

    private void RefreshBrowser()
    {
        var index = coordinator.Index.ToDictionary(
            entry => entry.Prefix,
            StringComparer.Ordinal);
        var roots = focusedPrefixes.Count > 0
            ? focusedPrefixes.ToArray()
            : index.Values
                .Where(entry => entry.MassCode == selectedMassCode)
                .Select(entry => entry.Prefix)
                .Order(StringComparer.Ordinal)
                .ToArray();
        var rows = new List<BoxelSurveyBrowserRowViewModel>();
        foreach (var prefix in roots)
        {
            rows.Add(CreateRow(prefix, index, indent: 0));
            if (!TryParsePrefix(prefix, out var boxel))
            {
                continue;
            }

            foreach (var child in boxel.Children)
            {
                rows.Add(CreateRow(child.Prefix, index, indent: 1));
            }
        }

        BrowserRows = rows;
        OnPropertyChanged(nameof(RecentEntries));
    }

    private BoxelSurveyBrowserRowViewModel CreateRow(
        string prefix,
        IReadOnlyDictionary<string, BoxelSurveyIndexEntry> index,
        int indent)
    {
        index.TryGetValue(prefix, out var entry);
        var visited = entry?.VisitedSystemCount ?? 0;
        var implied = entry?.ImpliedPopulation ?? 0;
        var helium = FormatHelium(entry?.MinHeliumPercent, entry?.MaxHeliumPercent);
        var status = entry is null || visited == 0 ? "not visited" : string.Empty;
        return new BoxelSurveyBrowserRowViewModel(
            prefix,
            string.Create(CultureInfo.CurrentCulture, $"{visited} / {implied}"),
            helium,
            status,
            indent,
            string.Equals(prefix, selectedPrefix, StringComparison.Ordinal));
    }

    private IReadOnlyList<BoxelSurveyClassRowViewModel> BuildClassRows(
        BoxelSurveyBoxelSnapshot snapshot,
        BoxelSurveyAverageFormat format)
    {
        var rows = new List<BoxelSurveyClassRowViewModel>(DisplayOrder.Length + 1);
        foreach (var (classified, code, displayName) in DisplayOrder)
        {
            var counts = snapshot.CountsOf(classified);
            var showTf = BoxelPlanetClassifier.ShowsTerraformableColumn(classified);
            var showLand = BoxelPlanetClassifier.ShowsLandableColumns(classified);
            rows.Add(new BoxelSurveyClassRowViewModel(
                code,
                displayName,
                counts.Count,
                BoxelSurveyAverageFormatter.Format(counts.Count, snapshot.Visited, format),
                showTf
                    ? FormatSlice(counts.Terraformable)
                    : BoxelSurveyAverageFormatter.Placeholder,
                showLand
                    ? FormatSlice(counts.Landable)
                    : BoxelSurveyAverageFormatter.Placeholder,
                showLand
                    ? FormatSlice(counts.Atmospheric)
                    : BoxelSurveyAverageFormatter.Placeholder,
                showTf,
                showLand));
        }

        rows.Add(new BoxelSurveyClassRowViewModel(
            "TF+",
            "TF (other)",
            snapshot.OtherTerraformableCount,
            BoxelSurveyAverageFormatter.Format(
                snapshot.OtherTerraformableCount,
                snapshot.Visited,
                format),
            FormatSlice(snapshot.OtherTerraformableCount),
            BoxelSurveyAverageFormatter.Placeholder,
            BoxelSurveyAverageFormatter.Placeholder,
            true,
            false));
        return rows;
    }

    private void ApplyPreferences()
    {
        coordinator.State.TreatNavBeaconAsFullyScanned =
            preferences.TreatNavBeaconAsFullyScanned;
    }

    private void SavePreferences()
    {
        settingsStore.Save(preferences);
        ApplyPreferences();
    }

    private void RefreshMassCodes()
    {
        foreach (var option in MassCodes)
        {
            option.IsSelected = option.MassCode == selectedMassCode;
        }
    }

    private IReadOnlyList<BoxelSurveyMassCodeOption> CreateMassCodes()
    {
        var options = new ObservableCollection<BoxelSurveyMassCodeOption>();
        for (var massCode = BoxelAddress.MinimumMassCode;
             massCode <= BoxelAddress.MaximumMassCode;
             massCode++)
        {
            options.Add(new BoxelSurveyMassCodeOption(
                massCode,
                massCode == selectedMassCode));
        }

        return options;
    }

    private void OnCoordinatorChanged(object? sender, EventArgs eventArgs)
    {
        _ = RefreshAsync();
    }

    private static bool TryParsePrefix(string prefix, out BoxelAddress boxel)
    {
        boxel = null!;
        if (!BoxelAddress.TryParse(prefix + "0", out var parsed) || parsed is null)
        {
            return false;
        }

        boxel = parsed;
        return true;
    }

    private static string FormatHelium(double? min, double? max)
    {
        if (min is null && max is null)
        {
            return BoxelSurveyAverageFormatter.Placeholder;
        }

        var low = min ?? max ?? 0;
        var high = max ?? min ?? 0;
        return string.Create(CultureInfo.CurrentCulture, $"HE {low:0.#}–{high:0.#}%");
    }

    private static string FormatAverage(double? value)
        => value is null
            ? BoxelSurveyAverageFormatter.Placeholder
            : string.Create(CultureInfo.CurrentCulture, $"{value.Value:0.#} / sys");

    private static string FormatCredits(long value)
    {
        if (Math.Abs(value) >= 1_000_000)
        {
            return string.Create(
                CultureInfo.CurrentCulture,
                $"{value / 1_000_000d:0.0} M CR");
        }

        return string.Create(CultureInfo.CurrentCulture, $"{value:N0} CR");
    }

    private static string FormatSlice(int count)
        => count == 0
            ? BoxelSurveyAverageFormatter.Placeholder
            : count.ToString(CultureInfo.CurrentCulture);

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
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute(parameter);
    }

    private sealed class AsyncCommand(Func<Task> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public async void Execute(object? parameter) => await execute().ConfigureAwait(true);
    }
}

public sealed class BoxelSurveyMassCodeOption : INotifyPropertyChanged
{
    private bool isSelected;

    public BoxelSurveyMassCodeOption(char massCode, bool isSelected)
    {
        MassCode = massCode;
        Label = char.ToString(massCode);
        this.isSelected = isSelected;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public char MassCode { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed record BoxelSurveyBrowserRowViewModel(
    string Prefix,
    string Glance,
    string Helium,
    string Status,
    int Indent,
    bool IsSelected)
{
    public string IndentMargin => Indent == 0 ? "0" : "24,0,0,0";
}

public sealed record BoxelSurveyClassRowViewModel(
    string Code,
    string DisplayName,
    int Count,
    string Average,
    string Terraformable,
    string Landable,
    string Atmospheric,
    bool ShowsTerraformable,
    bool ShowsLandable);
