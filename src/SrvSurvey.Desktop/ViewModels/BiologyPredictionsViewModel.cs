using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BiologyPredictionsViewModel : INotifyPropertyChanged, IDisposable
{
    private const string PredictionUnavailable = "Prediction unavailable";

    private readonly SystemSurveyViewModel survey;
    private readonly BiologyPredictionsSettingsStore settingsStore;
    private readonly AsyncCommand openWindowCommand;
    private readonly AsyncCommand openCanonnCommand;
    private readonly AsyncCommand openSpanshCommand;
    private readonly AsyncCommand openEdsmCommand;
    private readonly DelegateCommand expandAllCommand;
    private readonly DelegateCommand collapseAllCommand;
    private Func<Task<bool>>? windowOpener;
    private Func<Uri, Task<bool>>? uriLauncher;
    private IReadOnlyList<BiologyPredictionBodyViewModel> bodies = [];
    private bool currentBodyOnly;
    private int rowSize;
    private string systemName = "No biological signals";
    private long? systemAddress;
    private string scanProgress = "Waiting for a biological system scan.";
    private string confirmedReward = "0 CR";
    private string estimatedReward = PredictionUnavailable;
    private string firstFootfallEstimate = PredictionUnavailable;
    private string statusMessage = "Scan a system with biological signals to begin.";
    private string launchStatus = string.Empty;
    private bool disposed;

    public BiologyPredictionsViewModel(
        SystemSurveyViewModel survey,
        BiologyPredictionsSettingsStore settingsStore)
    {
        this.survey = survey ?? throw new ArgumentNullException(nameof(survey));
        this.settingsStore = settingsStore
            ?? throw new ArgumentNullException(nameof(settingsStore));
        var preferences = settingsStore.Load();
        currentBodyOnly = preferences.CurrentBodyOnly;
        rowSize = preferences.RowSize;
        RowSizeOptions =
        [
            new BiologyPredictionRowSizeOption(1, "Compact"),
            new BiologyPredictionRowSizeOption(2, "Comfortable"),
            new BiologyPredictionRowSizeOption(3, "Large"),
        ];
        openWindowCommand = new AsyncCommand(
            OpenWindowAsync,
            () => windowOpener is not null && HasSystem);
        openCanonnCommand = new AsyncCommand(
            OpenCanonnAsync,
            CanOpenExternalLink);
        openSpanshCommand = new AsyncCommand(
            OpenSpanshAsync,
            CanOpenExternalLink);
        openEdsmCommand = new AsyncCommand(
            OpenEdsmAsync,
            CanOpenExternalLink);
        expandAllCommand = new DelegateCommand(
            ExpandAll,
            () => Bodies.Count > 0);
        collapseAllCommand = new DelegateCommand(
            CollapseAll,
            () => Bodies.Count > 0);
        survey.PropertyChanged += OnSurveyPropertyChanged;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<BiologyPredictionRowSizeOption> RowSizeOptions { get; }

    public IReadOnlyList<BiologyPredictionBodyViewModel> Bodies
    {
        get => bodies;
        private set
        {
            if (SetField(ref bodies, value))
            {
                OnPropertyChanged(nameof(HasBodies));
                OnPropertyChanged(nameof(HasSystem));
                RaiseCommands();
            }
        }
    }

    public bool HasBodies => Bodies.Count > 0;

    public bool HasSystem => SystemAddress is not null && HasBodies;

    public string SystemName
    {
        get => systemName;
        private set => SetField(ref systemName, value);
    }

    public long? SystemAddress
    {
        get => systemAddress;
        private set
        {
            if (SetField(ref systemAddress, value))
            {
                OnPropertyChanged(nameof(HasSystem));
                OnPropertyChanged(nameof(HasSystemAddress));
                OnPropertyChanged(nameof(SystemAddressText));
                RaiseCommands();
            }
        }
    }

    public bool HasSystemAddress => SystemAddress is > 0;

    public string SystemAddressText => SystemAddress is > 0
        ? $"id64 {SystemAddress.Value}"
        : string.Empty;

    public string ScanProgress
    {
        get => scanProgress;
        private set => SetField(ref scanProgress, value);
    }

    public string ConfirmedReward
    {
        get => confirmedReward;
        private set => SetField(ref confirmedReward, value);
    }

    public string EstimatedReward
    {
        get => estimatedReward;
        private set => SetField(ref estimatedReward, value);
    }

    public string FirstFootfallEstimate
    {
        get => firstFootfallEstimate;
        private set => SetField(ref firstFootfallEstimate, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string LaunchStatus
    {
        get => launchStatus;
        private set
        {
            if (SetField(ref launchStatus, value))
            {
                OnPropertyChanged(nameof(HasLaunchStatus));
            }
        }
    }

    public bool HasLaunchStatus => !string.IsNullOrWhiteSpace(LaunchStatus);

    public bool CurrentBodyOnly
    {
        get => currentBodyOnly;
        set
        {
            if (!SetField(ref currentBodyOnly, value))
            {
                return;
            }

            SavePreferences();
            ApplyExpansionMode();
        }
    }

    public int RowSize
    {
        get => rowSize;
        private set
        {
            var normalized = Math.Clamp(value, 1, 3);
            if (!SetField(ref rowSize, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedRowSize));
            SavePreferences();
            Refresh();
        }
    }

    public BiologyPredictionRowSizeOption SelectedRowSize
    {
        get => RowSizeOptions.Single(option => option.Value == RowSize);
        set
        {
            if (value is not null)
            {
                RowSize = value.Value;
            }
        }
    }

    public ICommand OpenWindowCommand => openWindowCommand;

    public ICommand OpenCanonnCommand => openCanonnCommand;

    public ICommand OpenSpanshCommand => openSpanshCommand;

    public ICommand OpenEdsmCommand => openEdsmCommand;

    public ICommand ExpandAllCommand => expandAllCommand;

    public ICommand CollapseAllCommand => collapseAllCommand;

    public void SetWindowOpener(Func<Task<bool>>? opener)
    {
        windowOpener = opener;
        openWindowCommand.RaiseCanExecuteChanged();
    }

    public void SetUriLauncher(Func<Uri, Task<bool>>? launcher)
    {
        uriLauncher = launcher;
        openCanonnCommand.RaiseCanExecuteChanged();
        openSpanshCommand.RaiseCanExecuteChanged();
        openEdsmCommand.RaiseCanExecuteChanged();
    }

    public Task<bool> OpenCanonnAsync()
    {
        return LaunchUriAsync(
            new Uri(
                WellKnownUris.CanonnSignalsSystemPrefix
                    + Uri.EscapeDataString(SystemName)),
            "Canonn Signals");
    }

    public Task<bool> OpenSpanshAsync()
    {
        return LaunchUriAsync(
            new Uri(
                WellKnownUris.SpanshSystemPrefix
                    + SystemAddress?.ToString(CultureInfo.InvariantCulture)),
            "Spansh");
    }

    public Task<bool> OpenEdsmAsync()
    {
        return LaunchUriAsync(
            new Uri(
                WellKnownUris.EdsmSystemById64Prefix
                    + SystemAddress?.ToString(CultureInfo.InvariantCulture)),
            "EDSM");
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        survey.PropertyChanged -= OnSurveyPropertyChanged;
        windowOpener = null;
        uriLauncher = null;
    }

    private void OnSurveyPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SystemSurveyViewModel.Snapshot)
            or nameof(SystemSurveyViewModel.BiologySurvey)
            or nameof(SystemSurveyViewModel.CurrentBiologyDiscoveryContext)
            or nameof(SystemSurveyViewModel.DisableBioPredictions))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var expandedState = Bodies.ToDictionary(
            body => body.BodyId,
            body => body.IsExpanded);
        var snapshot = survey.Snapshot;
        var overview = BiologySurveyViewModel.CreateSystemOverview(
            snapshot,
            survey.CurrentStatus,
            new BiologySurveySystemOverviewOptions(survey.DisableBioPredictions)
            {
                RewardThresholds = survey.BiologyRewardThresholds,
                PredictionEvaluator = survey.BiologyPredictionEvaluator,
                ReferenceCatalog = survey.BiologyReferenceCatalog,
                HighlightRegionalFirsts = survey.HighlightRegionalFirsts,
                DiscoveryContext = survey.CurrentBiologyDiscoveryContext,
            });
        if (overview is null)
        {
            SystemName = snapshot.SystemName ?? "No biological signals";
            SystemAddress = snapshot.SystemAddress;
            ScanProgress = "Waiting for a biological system scan.";
            ConfirmedReward = "0 CR";
            EstimatedReward = PredictionUnavailable;
            FirstFootfallEstimate = PredictionUnavailable;
            StatusMessage = "Scan a system with biological signals to begin.";
            Bodies = [];
            return;
        }

        var rowFontSize = RowSize switch
        {
            1 => 11d,
            3 => 15d,
            _ => 13d,
        };
        var rowVerticalPadding = RowSize switch
        {
            1 => 5d,
            3 => 10d,
            _ => 7d,
        };
        var bodyRows = overview.Bodies.Select(row =>
        {
            var body = snapshot.Bodies.Single(candidate =>
                candidate.BodyId == row.BodyId);
            var detail = BiologySurveyViewModel.CreateBodyDetail(
                snapshot,
                body.BodyId,
                survey.CurrentExobiology,
                new BiologySurveyBodyDetailOptions(
                    survey.HighlightRegionalFirsts,
                    survey.DimAnalyzedOrganisms,
                    survey.HideGeoCountInBioSystem,
                    survey.DisableBioPredictions)
                {
                    DiscoveryContext = survey.CurrentBiologyDiscoveryContext,
                    RewardThresholds = survey.BiologyRewardThresholds,
                    PredictionEvaluator = survey.BiologyPredictionEvaluator,
                    ReferenceCatalog = survey.BiologyReferenceCatalog,
                })!;
            var isExpanded = expandedState.GetValueOrDefault(
                body.BodyId,
                !CurrentBodyOnly || row.IsCurrentBody);
            return new BiologyPredictionBodyViewModel(
                new BiologyPredictionBodyOptions
                {
                    BodyId = body.BodyId,
                    Name = body.Name,
                    DistanceText = FormatDistance(body.DistanceFromArrivalLs),
                    ProgressText = row.ProgressText,
                    RewardText = detail.RewardSummary,
                    IsFirstFootfall = body.IsFirstFootfall,
                    IsCurrent = row.IsCurrentBody,
                    IsDestination = row.IsDestination,
                    RequiresDss = detail.RequiresDss,
                    PredictionStatus = detail.PredictionStatus,
                    Organisms = detail.Organisms.Select(organism =>
                        BiologyPredictionOrganismViewModel.Create(
                            organism,
                            rowFontSize,
                            rowVerticalPadding)).ToArray(),
                    IsExpanded = isExpanded,
                });
        }).ToArray();

        SystemName = overview.Heading;
        SystemAddress = snapshot.SystemAddress;
        ScanProgress = overview.ProgressText;
        ConfirmedReward = FormatCredits(CalculateConfirmedReward(snapshot));
        EstimatedReward = string.IsNullOrWhiteSpace(overview.RewardSummary)
            ? PredictionUnavailable
            : overview.RewardSummary;
        FirstFootfallEstimate = CalculateFirstFootfallEstimate(
            snapshot,
            overview.Bodies);
        var predictedBodyCountLabel = bodyRows.Length == 1
            ? "body."
            : "bodies.";
        var predictionStatus = bodyRows.Any(body => body.HasPredictionStatus)
            ? "Some bodies still need complete planet or parent-star scans."
            : $"Exact criteria evaluated for {bodyRows.Length:N0} biological "
                + predictedBodyCountLabel;
        StatusMessage = survey.DisableBioPredictions
            ? "Exact predictions are disabled in system-survey settings."
            : predictionStatus;
        Bodies = bodyRows;
        if (CurrentBodyOnly)
        {
            ApplyExpansionMode();
        }
    }

    private void ApplyExpansionMode()
    {
        if (!CurrentBodyOnly)
        {
            foreach (var body in Bodies)
            {
                body.IsExpanded = true;
            }

            return;
        }

        var preferredBodyId = survey.CurrentStatus?.GuiFocus == GuiFocus.Fss
            ? survey.Snapshot.LastDetailedBodyId
            : survey.Snapshot.CurrentBodyId;
        if (preferredBodyId is null)
        {
            preferredBodyId = Bodies.FirstOrDefault(body => body.IsCurrent)?.BodyId;
        }

        foreach (var body in Bodies)
        {
            body.IsExpanded = body.BodyId == preferredBodyId;
        }
    }

    private void ExpandAll()
    {
        if (CurrentBodyOnly)
        {
            CurrentBodyOnly = false;
        }

        foreach (var body in Bodies)
        {
            body.IsExpanded = true;
        }
    }

    private void CollapseAll()
    {
        foreach (var body in Bodies)
        {
            body.IsExpanded = false;
        }
    }

    private async Task<bool> OpenWindowAsync()
    {
        return windowOpener is not null && await windowOpener();
    }

    private bool CanOpenExternalLink()
    {
        return uriLauncher is not null && HasSystem;
    }

    private async Task<bool> LaunchUriAsync(Uri uri, string label)
    {
        if (uriLauncher is null || !HasSystem)
        {
            LaunchStatus = $"{label} is unavailable until a system is loaded.";
            return false;
        }

        try
        {
            var launched = await uriLauncher(uri);
            LaunchStatus = launched
                ? $"Opened {label}."
                : $"The platform could not open {label}.";
            return launched;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException)
        {
            LaunchStatus = $"{label} could not be opened: {exception.Message}";
            return false;
        }
    }

    private void SavePreferences()
    {
        settingsStore.Save(new BiologyPredictionsPreferences(
            CurrentBodyOnly,
            RowSize));
    }

    private void RaiseCommands()
    {
        openWindowCommand.RaiseCanExecuteChanged();
        openCanonnCommand.RaiseCanExecuteChanged();
        openSpanshCommand.RaiseCanExecuteChanged();
        openEdsmCommand.RaiseCanExecuteChanged();
        expandAllCommand.RaiseCanExecuteChanged();
        collapseAllCommand.RaiseCanExecuteChanged();
    }

    private static long CalculateConfirmedReward(SystemScanSnapshot snapshot)
    {
        return snapshot.Bodies.Sum((SystemScanBodySnapshot body) =>
        {
            long reward = 0;
            var bonus = body.IsFirstFootfall ? 5 : 1;
            foreach (var organism in body.Organisms)
            {
                if (!organism.IsAnalyzed)
                {
                    continue;
                }

                reward += (organism.Reward ?? 0) * bonus;
            }
            return reward;
        });
    }

    private static string CalculateFirstFootfallEstimate(
        SystemScanSnapshot snapshot,
        IReadOnlyList<BiologyBodyRowViewModel> rows)
    {
        var minimum = 0L;
        var maximum = 0L;
        var hasUnknown = false;
        foreach (var row in rows)
        {
            var body = snapshot.Bodies.Single(candidate =>
                candidate.BodyId == row.BodyId);
            var multiplier = body.IsFirstFootfall ? 5 : 1;
            minimum += row.MinimumReward * multiplier;
            maximum += row.MaximumReward * multiplier;
            hasUnknown |= row.HasUnknownReward;
        }

        return FormatRewardRange(minimum, maximum, hasUnknown);
    }

    private static string FormatRewardRange(
        long minimum,
        long maximum,
        bool hasUnknown)
    {
        var range = minimum == maximum
            ? FormatCredits(minimum)
            : $"{FormatCredits(minimum)} - {FormatCredits(maximum)}";
        return hasUnknown ? range + " + pending" : range;
    }

    private static string FormatCredits(long value)
    {
        return value switch
        {
            >= 1_000_000 => $"{value / 1_000_000d:N2} M CR",
            >= 1_000 => $"{value / 1_000d:N1} K CR",
            _ => $"{value:N0} CR",
        };
    }

    private static string FormatDistance(double distanceFromArrivalLs)
    {
        return distanceFromArrivalLs switch
        {
            >= 100_000 => $"{distanceFromArrivalLs / 1_000d:N1} K LS",
            > 0 => $"{distanceFromArrivalLs:N0} LS",
            _ => "Arrival distance unknown",
        };
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

    private sealed class DelegateCommand(
        Action execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return canExecute();
        }

        public void Execute(object? parameter)
        {
            execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class AsyncCommand(
        Func<Task<bool>> execute,
        Func<bool> canExecute) : ICommand
    {
        private bool isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !isExecuting && canExecute();
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await execute();
            }
            finally
            {
                isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record BiologyPredictionRowSizeOption(int Value, string Label);

public sealed class BiologyPredictionBodyOptions
{
    public required int BodyId { get; init; }

    public required string Name { get; init; }

    public required string DistanceText { get; init; }

    public required string ProgressText { get; init; }

    public required string RewardText { get; init; }

    public required bool IsFirstFootfall { get; init; }

    public required bool IsCurrent { get; init; }

    public required bool IsDestination { get; init; }

    public required bool RequiresDss { get; init; }

    public required string PredictionStatus { get; init; }

    public required IReadOnlyList<BiologyPredictionOrganismViewModel> Organisms
    {
        get;
        init;
    }

    public required bool IsExpanded { get; init; }
}

public sealed class BiologyPredictionBodyViewModel : INotifyPropertyChanged
{
    private bool isExpanded;

    public BiologyPredictionBodyViewModel(BiologyPredictionBodyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        BodyId = options.BodyId;
        Name = options.Name;
        DistanceText = options.DistanceText;
        ProgressText = options.ProgressText;
        RewardText = options.RewardText;
        IsFirstFootfall = options.IsFirstFootfall;
        IsCurrent = options.IsCurrent;
        IsDestination = options.IsDestination;
        RequiresDss = options.RequiresDss;
        PredictionStatus = options.PredictionStatus;
        Organisms = options.Organisms;
        isExpanded = options.IsExpanded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int BodyId { get; }

    public string Name { get; }

    public string DistanceText { get; }

    public string ProgressText { get; }

    public string RewardText { get; }

    public bool HasReward => !string.IsNullOrWhiteSpace(RewardText);

    public bool IsFirstFootfall { get; }

    public bool IsCurrent { get; }

    public bool IsDestination { get; }

    public bool RequiresDss { get; }

    public string PredictionStatus { get; }

    public bool HasPredictionStatus => !string.IsNullOrWhiteSpace(
        PredictionStatus);

    public IReadOnlyList<BiologyPredictionOrganismViewModel> Organisms { get; }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
            {
                return;
            }

            isExpanded = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
}

public sealed class BiologyPredictionOrganismViewModel
{
    public string DisplayName { get; init; } = string.Empty;

    public string GenusName { get; init; } = string.Empty;

    public string SampleDistanceText { get; init; } = string.Empty;

    public string RewardText { get; init; } = string.Empty;

    public bool IsAnalyzed { get; init; }

    public bool IsCommanderFirst { get; init; }

    public bool IsRegionalFirst { get; init; }

    public bool IsGlobalRegionalFirst { get; init; }

    public bool IsHighlightedFirst { get; init; }

    public bool IsCurrentSample { get; init; }

    public bool IsPrediction { get; init; }

    public bool IsGenusIdentified { get; init; }

    public bool IsUnknown { get; init; }

    public double RowOpacity { get; init; }

    public double RowFontSize { get; init; }

    public double RowVerticalPadding { get; init; }

    public long Reward { get; init; }

    public double RewardBucketOneMillions { get; init; }

    public double RewardBucketTwoMillions { get; init; }

    public double RewardBucketThreeMillions { get; init; }

    public bool HasSampleDistance => !string.IsNullOrWhiteSpace(
        SampleDistanceText);

    public static BiologyPredictionOrganismViewModel Create(
        BiologyOrganismRowViewModel source,
        double rowFontSize,
        double rowVerticalPadding)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new BiologyPredictionOrganismViewModel
        {
            DisplayName = source.DisplayName,
            GenusName = source.GenusName,
            SampleDistanceText = source.SampleDistanceText,
            RewardText = source.RewardText,
            IsAnalyzed = source.IsAnalyzed,
            IsCommanderFirst = source.IsCommanderFirst,
            IsRegionalFirst = source.IsRegionalFirst,
            IsGlobalRegionalFirst = source.IsGlobalRegionalFirst,
            IsHighlightedFirst = source.IsHighlightedFirst,
            IsCurrentSample = source.IsCurrentSample,
            IsPrediction = source.IsPrediction,
            IsGenusIdentified = source.IsGenusIdentified,
            IsUnknown = source.IsUnknown,
            RowOpacity = source.RowOpacity,
            RowFontSize = rowFontSize,
            RowVerticalPadding = rowVerticalPadding,
            Reward = source.Reward,
            RewardBucketOneMillions = source.RewardBucketOneMillions,
            RewardBucketTwoMillions = source.RewardBucketTwoMillions,
            RewardBucketThreeMillions = source.RewardBucketThreeMillions,
        };
    }
}
