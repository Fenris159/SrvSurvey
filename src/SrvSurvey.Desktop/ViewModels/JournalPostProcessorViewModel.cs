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
    private readonly CommanderCodexJournalImporter codexImporter;
    private readonly AsyncCommand analyzeCommand;
    private readonly AsyncCommand rebuildCodexCommand;
    private readonly AsyncCommand refreshCommandersCommand;
    private readonly DelegateCommand cancelCommand;
    private readonly DelegateCommand setBeginningCommand;
    private IReadOnlyList<JournalPostProcessorCommanderViewModel> commanders = [];
    private JournalPostProcessorCommanderViewModel? selectedCommander;
    private IReadOnlyList<JournalPostProcessorStatisticViewModel> statistics = [];
    private DateTimeOffset startDate;
    private string statusMessage = "Refresh commanders to prepare historical journal analysis.";
    private string trailblazersSummary = string.Empty;
    private double progressValue;
    private double progressMaximum = 1;
    private bool isBusy;
    private bool codexRebuildConfirmed;
    private CancellationTokenSource? operationCancellation;

    public JournalPostProcessorViewModel(
        CommanderProfileCatalog commanderCatalog,
        JournalHistoryAnalyzer analyzer,
        CommanderCodexJournalImporter codexImporter)
    {
        this.commanderCatalog = commanderCatalog
            ?? throw new ArgumentNullException(nameof(commanderCatalog));
        this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        this.codexImporter = codexImporter
            ?? throw new ArgumentNullException(nameof(codexImporter));
        var localNow = DateTimeOffset.Now;
        startDate = new DateTimeOffset(
            localNow.Date.AddDays(-7),
            localNow.Offset);
        analyzeCommand = new AsyncCommand(AnalyzeAsync, CanRun);
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
        RebuildCodexCommand = rebuildCodexCommand;
        RefreshCommandersCommand = refreshCommandersCommand;
        CancelCommand = cancelCommand;
        SetBeginningCommand = setBeginningCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand AnalyzeCommand { get; }

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
            }
        }
    }

    public IReadOnlyList<JournalPostProcessorStatisticViewModel> Statistics
    {
        get => statistics;
        private set => SetField(ref statistics, value);
    }

    public string TrailblazersSummary
    {
        get => trailblazersSummary;
        private set => SetField(ref trailblazersSummary, value);
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
