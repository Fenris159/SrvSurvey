using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class JournalPostProcessorViewModel : INotifyPropertyChanged
{
    private readonly CommanderProfileCatalog commanderCatalog;
    private readonly JournalHistoryAnalyzer analyzer;
    private readonly LegacySystemBiologyAnalyzer systemBiologyAnalyzer;
    private readonly HistoricalSystemRebuildService systemRebuildService;
    private readonly CommanderCodexJournalImporter codexImporter;
    private readonly AsyncCommand analyzeCommand;
    private readonly AsyncCommand analyzeSystemsCommand;
    private readonly AsyncCommand rebuildSystemsCommand;
    private readonly AsyncCommand rebuildCodexCommand;
    private readonly AsyncCommand refreshCommandersCommand;
    private readonly DelegateCommand cancelCommand;
    private readonly DelegateCommand setBeginningCommand;
    private IReadOnlyList<JournalPostProcessorCommanderViewModel> commanders = [];
    private JournalPostProcessorCommanderViewModel? selectedCommander;
    private IReadOnlyList<JournalPostProcessorStatisticViewModel> statistics = [];
    private IReadOnlyList<JournalPostProcessorSpeciesViewModel> systemSpecies = [];
    private DateTimeOffset startDate;
    private string statusMessage = "Refresh commanders to prepare historical journal analysis.";
    private string trailblazersSummary = string.Empty;
    private string systemAnalysisSummary = string.Empty;
    private double progressValue;
    private double progressMaximum = 1;
    private bool isBusy;
    private bool codexRebuildConfirmed;
    private bool systemRebuildConfirmed;
    private CancellationTokenSource? operationCancellation;

    public JournalPostProcessorViewModel(
        CommanderProfileCatalog commanderCatalog,
        JournalHistoryAnalyzer analyzer,
        LegacySystemBiologyAnalyzer systemBiologyAnalyzer,
        HistoricalSystemRebuildService systemRebuildService,
        CommanderCodexJournalImporter codexImporter)
    {
        this.commanderCatalog = commanderCatalog
            ?? throw new ArgumentNullException(nameof(commanderCatalog));
        this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        this.systemBiologyAnalyzer = systemBiologyAnalyzer
            ?? throw new ArgumentNullException(nameof(systemBiologyAnalyzer));
        this.systemRebuildService = systemRebuildService
            ?? throw new ArgumentNullException(nameof(systemRebuildService));
        this.codexImporter = codexImporter
            ?? throw new ArgumentNullException(nameof(codexImporter));
        var localNow = DateTimeOffset.Now;
        startDate = new DateTimeOffset(
            localNow.Date.AddDays(-7),
            localNow.Offset);
        analyzeCommand = new AsyncCommand(AnalyzeAsync, CanRun);
        analyzeSystemsCommand = new AsyncCommand(AnalyzeSystemsAsync, CanRun);
        rebuildSystemsCommand = new AsyncCommand(
            RebuildSystemsAsync,
            CanRebuildSystems);
        rebuildCodexCommand = new AsyncCommand(
            RebuildCodexAsync,
            CanRebuildCodex);
        refreshCommandersCommand = new AsyncCommand(
            RefreshCommandersAsync,
            () => !IsBusy);
        cancelCommand = new DelegateCommand(Cancel, () => IsBusy);
        setBeginningCommand = new DelegateCommand(
            SetBeginningOfTime,
            () => !IsBusy);
        AnalyzeCommand = analyzeCommand;
        AnalyzeSystemsCommand = analyzeSystemsCommand;
        RebuildSystemsCommand = rebuildSystemsCommand;
        RebuildCodexCommand = rebuildCodexCommand;
        RefreshCommandersCommand = refreshCommandersCommand;
        CancelCommand = cancelCommand;
        SetBeginningCommand = setBeginningCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand AnalyzeCommand { get; }

    public ICommand AnalyzeSystemsCommand { get; }

    public ICommand RebuildSystemsCommand { get; }

    public ICommand RebuildCodexCommand { get; }

    public ICommand RefreshCommandersCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand SetBeginningCommand { get; }

    public IReadOnlyList<JournalPostProcessorCommanderViewModel> Commanders
    {
        get => commanders;
        private set => SetField(ref commanders, value);
    }

    public JournalPostProcessorCommanderViewModel? SelectedCommander
    {
        get => selectedCommander;
        set
        {
            if (SetField(ref selectedCommander, value))
            {
                CodexRebuildConfirmed = false;
                SystemRebuildConfirmed = false;
                RaiseCommandStates();
            }
        }
    }

    public DateTimeOffset StartDate
    {
        get => startDate;
        set
        {
            var normalized = value < JournalHistoryAnalyzer.EliteReleaseDate
                ? JournalHistoryAnalyzer.EliteReleaseDate
                : value > DateTimeOffset.Now
                    ? DateTimeOffset.Now
                    : value;
            if (SetField(ref startDate, normalized))
            {
                CodexRebuildConfirmed = false;
                SystemRebuildConfirmed = false;
            }
        }
    }

    public IReadOnlyList<JournalPostProcessorStatisticViewModel> Statistics
    {
        get => statistics;
        private set => SetField(ref statistics, value);
    }

    public IReadOnlyList<JournalPostProcessorSpeciesViewModel> SystemSpecies
    {
        get => systemSpecies;
        private set => SetField(ref systemSpecies, value);
    }

    public string TrailblazersSummary
    {
        get => trailblazersSummary;
        private set => SetField(ref trailblazersSummary, value);
    }

    public string SystemAnalysisSummary
    {
        get => systemAnalysisSummary;
        private set => SetField(ref systemAnalysisSummary, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public double ProgressValue
    {
        get => progressValue;
        private set => SetField(ref progressValue, value);
    }

    public double ProgressMaximum
    {
        get => progressMaximum;
        private set => SetField(ref progressMaximum, Math.Max(1, value));
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool CodexRebuildConfirmed
    {
        get => codexRebuildConfirmed;
        set
        {
            if (SetField(ref codexRebuildConfirmed, value))
            {
                rebuildCodexCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool SystemRebuildConfirmed
    {
        get => systemRebuildConfirmed;
        set
        {
            if (SetField(ref systemRebuildConfirmed, value))
            {
                rebuildSystemsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task RefreshCommandersAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var currentId = SelectedCommander?.FrontierId;
            var result = await commanderCatalog.LoadAsync();
            Commanders = result.Profiles
                .Select(profile => new JournalPostProcessorCommanderViewModel(
                    profile.FrontierId,
                    profile.CommanderName))
                .ToArray();
            SelectedCommander = Commanders.FirstOrDefault(commander =>
                    string.Equals(
                        commander.FrontierId,
                        currentId,
                        StringComparison.OrdinalIgnoreCase))
                ?? Commanders.FirstOrDefault();
            StatusMessage = result.Warnings.Count > 0
                ? $"Found {Commanders.Count:N0} commander profile(s). "
                    + string.Join(" ", result.Warnings)
                : Commanders.Count == 0
                    ? "No commander profiles were found. Import the original profile first."
                    : $"Choose one of {Commanders.Count:N0} commander profile(s) and a start date.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Commander profiles could not be loaded: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectCommander(string? frontierId)
    {
        if (string.IsNullOrWhiteSpace(frontierId))
        {
            return;
        }

        var match = Commanders.FirstOrDefault(commander => string.Equals(
            commander.FrontierId,
            frontierId,
            StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            SelectedCommander = match;
        }
    }

    public async Task AnalyzeAsync()
    {
        if (!CanRun() || SelectedCommander is null)
        {
            return;
        }

        operationCancellation = new CancellationTokenSource();
        try
        {
            IsBusy = true;
            Statistics = [];
            TrailblazersSummary = string.Empty;
            ProgressValue = 0;
            ProgressMaximum = 1;
            StatusMessage = "Scanning historical journals without changing profile data...";
            var progress = new Progress<JournalHistoryAnalysisProgress>(value =>
            {
                ProgressMaximum = value.TotalFileCount;
                ProgressValue = value.ProcessedFileCount;
                StatusMessage = $"Analyzing journal {value.ProcessedFileCount:N0} of "
                    + $"{value.TotalFileCount:N0}: {value.CurrentFile}";
            });
            var result = await analyzer.AnalyzeAsync(
                SelectedCommander.FrontierId,
                StartDate,
                progress,
                operationCancellation.Token);
            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Historical journal analysis was cancelled; no profile data changed.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "Historical journals could not be analyzed: "
                + exception.Message;
        }
        finally
        {
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    public async Task RebuildCodexAsync()
    {
        if (!CanRebuildCodex() || SelectedCommander is null)
        {
            StatusMessage =
                "Select a commander and confirm the all-history Codex merge first.";
            return;
        }

        operationCancellation = new CancellationTokenSource();
        try
        {
            IsBusy = true;
            ProgressValue = 0;
            ProgressMaximum = 1;
            StatusMessage = "Merging Commander Codex first discoveries from journal history...";
            var progress = new Progress<CommanderCodexJournalImportProgress>(value =>
            {
                ProgressMaximum = value.TotalFileCount;
                ProgressValue = value.ProcessedFileCount;
                StatusMessage = $"Merging Codex journal {value.ProcessedFileCount:N0} of "
                    + $"{value.TotalFileCount:N0}: {value.CurrentFile}";
            });
            var result = await codexImporter.ImportAsync(
                SelectedCommander.FrontierId,
                progress,
                operationCancellation.Token);
            ProgressMaximum = Math.Max(1, result.JournalFileCount);
            ProgressValue = result.JournalFileCount;
            StatusMessage = $"Scanned {result.JournalFileCount:N0} journal(s) and "
                + $"{result.DiscoveryEventCount:N0} Codex event(s); merged "
                + $"{result.ChangedEntryCount:N0} earlier global/regional first(s)."
                + (result.MalformedLineCount > 0
                    ? $" Ignored {result.MalformedLineCount:N0} malformed line(s)."
                    : string.Empty)
                + (result.Warnings.Count > 0
                    ? " " + string.Join(" ", result.Warnings)
                    : string.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusMessage =
                "Commander Codex rebuilding was cancelled. Completed atomic merges remain valid.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            StatusMessage = "Commander Codex rebuilding could not finish: "
                + exception.Message;
        }
        finally
        {
            CodexRebuildConfirmed = false;
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    public async Task AnalyzeSystemsAsync()
    {
        if (!CanRun() || SelectedCommander is null)
        {
            return;
        }

        operationCancellation = new CancellationTokenSource();
        try
        {
            IsBusy = true;
            SystemSpecies = [];
            SystemAnalysisSummary = string.Empty;
            ProgressValue = 0;
            ProgressMaximum = 1;
            StatusMessage =
                "Reading copied system files without changing them...";
            var progress = new Progress<LegacySystemBiologyAnalysisProgress>(value =>
            {
                ProgressMaximum = value.TotalFileCount;
                ProgressValue = value.ProcessedFileCount;
                StatusMessage = $"Reading system file {value.ProcessedFileCount:N0} of "
                    + $"{value.TotalFileCount:N0}: {value.CurrentFile}";
            });
            var result = await systemBiologyAnalyzer.AnalyzeAsync(
                SelectedCommander.FrontierId,
                progress,
                operationCancellation.Token);
            SystemSpecies = result.Species
                .Select(species => new JournalPostProcessorSpeciesViewModel(
                    species.Name,
                    species.Count,
                    FormatAtmospheres(species.AtmosphereCompositions)))
                .ToArray();
            ProgressMaximum = Math.Max(1, result.CandidateFileCount);
            ProgressValue = result.CandidateFileCount;
            SystemAnalysisSummary = $"Read {result.ProcessedFileCount:N0} of "
                + $"{result.CandidateFileCount:N0} system file(s), "
                + $"{result.BodyCount:N0} bodies, and {result.OrganismCount:N0} organisms; "
                + $"found {result.Species.Count:N0} localized species."
                + (result.Warnings.Count > 0
                    ? $" {result.Warnings.Count:N0} file warning(s) are shown in the status."
                    : string.Empty);
            StatusMessage = SystemAnalysisSummary
                + (result.Warnings.Count > 0
                    ? " " + string.Join(" ", result.Warnings)
                    : string.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusMessage =
                "System-file analysis was cancelled; no system or profile data changed.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException)
        {
            StatusMessage = "System files could not be analyzed: "
                + exception.Message;
        }
        finally
        {
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    public async Task RebuildSystemsAsync()
    {
        if (!CanRebuildSystems() || SelectedCommander is null)
        {
            StatusMessage =
                "Select a commander and confirm the verified historical system rebuild first.";
            return;
        }

        operationCancellation = new CancellationTokenSource();
        try
        {
            IsBusy = true;
            ProgressValue = 0;
            ProgressMaximum = 1;
            StatusMessage =
                "Reconstructing exploration history before creating verified backups...";
            var progress = new Progress<HistoricalSystemRebuildProgress>(value =>
            {
                ProgressMaximum = value.TotalCount;
                ProgressValue = value.ProcessedCount;
                StatusMessage = $"{value.Stage}: {value.ProcessedCount:N0} of "
                    + $"{value.TotalCount:N0}"
                    + (string.IsNullOrWhiteSpace(value.CurrentFile)
                        ? string.Empty
                        : $" - {value.CurrentFile}");
            });
            var result = await systemRebuildService.RebuildAsync(
                SelectedCommander.FrontierId,
                SelectedCommander.CommanderName,
                StartDate,
                progress,
                operationCancellation.Token);
            ProgressMaximum = Math.Max(1, result.CandidateJournalFileCount);
            ProgressValue = result.CandidateJournalFileCount;
            StatusMessage = $"Replayed {result.AppliedExplorationEventCount:N0} exploration "
                + $"event(s) into {result.ReconstructedSystemCount:N0} system(s); updated "
                + $"{result.UpdatedSystemFileCount:N0} and created "
                + $"{result.CreatedSystemFileCount:N0} system file(s)."
                + (string.IsNullOrWhiteSpace(result.BackupDirectory)
                    ? " No system files required activation."
                    : $" Verified backup: {result.BackupDirectory}")
                + (result.SkippedRecentFileCount > 0
                    ? $" Skipped {result.SkippedRecentFileCount:N0} recent active journal(s)."
                    : string.Empty)
                + (result.SkippedLegacyFileCount > 0
                    ? $" Skipped {result.SkippedLegacyFileCount:N0} pre-Odyssey journal(s); their Codex firsts remain available through the separate Codex merge."
                    : string.Empty)
                + (result.Warnings.Count > 0
                    ? " " + string.Join(" ", result.Warnings)
                    : string.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusMessage =
                "Historical system reconstruction was cancelled before activation; active system files did not change.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            StatusMessage = "Historical system reconstruction could not finish: "
                + exception.Message;
        }
        finally
        {
            SystemRebuildConfirmed = false;
            operationCancellation.Dispose();
            operationCancellation = null;
            IsBusy = false;
        }
    }

    public void Cancel()
    {
        operationCancellation?.Cancel();
        StatusMessage = "Stopping after the current journal operation...";
    }

    public void SetBeginningOfTime()
    {
        StartDate = JournalHistoryAnalyzer.EliteReleaseDate;
    }

    private bool CanRun() => !IsBusy && SelectedCommander is not null;

    private bool CanRebuildCodex() => CanRun() && CodexRebuildConfirmed;

    private bool CanRebuildSystems() => CanRun() && SystemRebuildConfirmed;

    private static string FormatAtmospheres(
        IReadOnlyList<LegacyAtmosphereCompositionSummary> atmospheres)
    {
        return atmospheres.Count == 0
            ? "No atmosphere composition recorded"
            : string.Join(
                "; ",
                atmospheres.Select(atmosphere =>
                    $"{(string.IsNullOrEmpty(atmosphere.Components) ? "Empty composition" : atmosphere.Components)} x{atmosphere.Count:N0}"));
    }

    private void ApplyResult(JournalHistoryAnalysisResult result)
    {
        var value = result.Statistics;
        Statistics =
        [
            new("Jumps", value.JumpCount.ToString("N0")),
            new("Distance (ly)", value.JumpDistanceLy.ToString("N0")),
            new("Bodies approached", value.BodyApproachCount.ToString("N0")),
            new("Organisms analyzed", value.OrganismAnalysisCount.ToString("N0")),
            new("Cargo bought", value.CargoBought.ToString("N0")),
            new("Cargo sold", value.CargoSold.ToString("N0")),
            new("Cargo transferred", value.CargoTransferred.ToString("N0")),
            new("Cargo collected", value.CargoCollected.ToString("N0")),
            new("Cargo contributed", value.CargoContributed.ToString("N0")),
            new("Docked", value.DockedCount.ToString("N0")),
            new("Touchdowns", value.TouchdownCount.ToString("N0")),
            new("Deaths", value.DeathCount.ToString("N0")),
        ];
        var before = result.Trailblazers.Before;
        var after = result.Trailblazers.After;
        TrailblazersSummary = string.Format(
            CultureInfo.CurrentCulture,
            "Trailblazers cargo - before: {0:N0} bought / {1:N0} sold / {2:N0} transferred; after: {3:N0} / {4:N0} / {5:N0}.",
            before.Bought,
            before.Sold,
            before.Transferred,
            after.Bought,
            after.Sold,
            after.Transferred);
        ProgressMaximum = Math.Max(1, result.CandidateFileCount);
        ProgressValue = result.CandidateFileCount;
        StatusMessage = $"Analyzed {result.ProcessedFileCount:N0} matching journal(s) "
            + $"and {result.ParsedEventCount:N0} event(s)."
            + (result.SkippedCommanderFileCount > 0
                ? $" Skipped {result.SkippedCommanderFileCount:N0} other-commander journal(s)."
                : string.Empty)
            + (result.SkippedRecentActiveFileCount > 0
                ? $" Skipped {result.SkippedRecentActiveFileCount:N0} recent active journal(s)."
                : string.Empty)
            + (result.MalformedLineCount > 0
                ? $" Ignored {result.MalformedLineCount:N0} malformed line(s)."
                : string.Empty)
            + (result.Warnings.Count > 0
                ? " " + string.Join(" ", result.Warnings)
                : string.Empty);
    }

    private void RaiseCommandStates()
    {
        analyzeCommand.RaiseCanExecuteChanged();
        analyzeSystemsCommand.RaiseCanExecuteChanged();
        rebuildSystemsCommand.RaiseCanExecuteChanged();
        rebuildCodexCommand.RaiseCanExecuteChanged();
        refreshCommandersCommand.RaiseCanExecuteChanged();
        cancelCommand.RaiseCanExecuteChanged();
        setBeginningCommand.RaiseCanExecuteChanged();
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

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class DelegateCommand(
        Action execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter) => execute();

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        private bool running;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !running && canExecute();

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            running = true;
            RaiseCanExecuteChanged();
            try
            {
                await execute();
            }
            finally
            {
                running = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record JournalPostProcessorCommanderViewModel(
    string FrontierId,
    string CommanderName)
{
    public string DisplayName => $"{CommanderName} ({FrontierId})";
}

public sealed record JournalPostProcessorStatisticViewModel(
    string Name,
    string Value);

public sealed record JournalPostProcessorSpeciesViewModel(
    string Name,
    int Count,
    string AtmosphereSummary)
{
    public string CountText => $"{Count:N0} observation(s)";
}
