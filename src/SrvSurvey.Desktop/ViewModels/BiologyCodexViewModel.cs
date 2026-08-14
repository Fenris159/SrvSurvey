using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BiologyCodexViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SystemSurveyViewModel survey;
    private readonly ExobiologyReferenceCatalog catalog;
    private readonly BiologyPredictionEvaluator evaluator;
    private readonly Func<string?> commanderNameProvider;
    private readonly DelegateCommand previousBodyCommand;
    private readonly DelegateCommand nextBodyCommand;
    private readonly DelegateCommand previousOrganismCommand;
    private readonly DelegateCommand nextOrganismCommand;
    private readonly AsyncCommand openWindowCommand;
    private readonly AsyncCommand openSubmitImageCommand;
    private readonly AsyncCommand openCanonnRegionsCommand;
    private readonly AsyncCommand openBioforgeCommand;
    private readonly AsyncCommand openCanonnSignalsCommand;
    private readonly AsyncCommand openSpanshCommand;
    private IReadOnlyList<BiologyCodexBodyViewModel> bodies = [];
    private BiologyCodexBodyViewModel? selectedBody;
    private BiologyCodexOrganismViewModel? selectedOrganism;
    private Func<Task<bool>>? windowOpener;
    private Func<Uri, Task<bool>>? uriLauncher;
    private string launchStatus = string.Empty;
    private bool disposed;

    public BiologyCodexViewModel(
        SystemSurveyViewModel survey,
        ExobiologyReferenceCatalog catalog,
        BiologyCriteriaCatalog criteriaCatalog,
        Func<string?>? commanderNameProvider = null)
    {
        this.survey = survey ?? throw new ArgumentNullException(nameof(survey));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        evaluator = new BiologyPredictionEvaluator(
            criteriaCatalog ?? throw new ArgumentNullException(nameof(criteriaCatalog)));
        this.commanderNameProvider = commanderNameProvider ?? (() => null);
        previousBodyCommand = new DelegateCommand(
            () => MoveBody(-1),
            () => Bodies.Count > 1);
        nextBodyCommand = new DelegateCommand(
            () => MoveBody(1),
            () => Bodies.Count > 1);
        previousOrganismCommand = new DelegateCommand(
            () => MoveOrganism(-1),
            () => SelectedBody?.Organisms.Count > 1);
        nextOrganismCommand = new DelegateCommand(
            () => MoveOrganism(1),
            () => SelectedBody?.Organisms.Count > 1);
        openWindowCommand = new AsyncCommand(
            OpenWindowAsync,
            () => windowOpener is not null && HasSystem);
        openSubmitImageCommand = new AsyncCommand(
            OpenSubmitImageAsync,
            CanOpenOrganismLink);
        openCanonnRegionsCommand = new AsyncCommand(
            OpenCanonnRegionsAsync,
            CanOpenOrganismLink);
        openBioforgeCommand = new AsyncCommand(
            OpenBioforgeAsync,
            CanOpenOrganismLink);
        openCanonnSignalsCommand = new AsyncCommand(
            OpenCanonnSignalsAsync,
            CanOpenSystemLink);
        openSpanshCommand = new AsyncCommand(
            OpenSpanshAsync,
            CanOpenSystemLink);
        survey.PropertyChanged += OnSurveyPropertyChanged;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SystemName => survey.Snapshot.SystemName ?? "No biological system";

    public long? SystemAddress => survey.Snapshot.SystemAddress;

    public bool HasSystemAddress => SystemAddress is > 0;

    public string SystemAddressText => SystemAddressFormatter.Format(SystemAddress);

    public IReadOnlyList<BiologyCodexBodyViewModel> Bodies
    {
        get => bodies;
        private set
        {
            if (SetField(ref bodies, value))
            {
                OnPropertyChanged(nameof(HasSystem));
                OnPropertyChanged(nameof(EmptyStateText));
            }
        }
    }

    public bool HasSystem => SystemAddress is not null && Bodies.Count > 0;

    public string EmptyStateText => SystemAddress is null
        ? "Enter a system to browse biological Codex entries."
        : (Bodies.Count == 0) switch
        {
            true => "No biological signals have been reported in this system.",
            false => "No confirmed or predicted organisms are available for this body."
        };

    public BiologyCodexBodyViewModel? SelectedBody
    {
        get => selectedBody;
        set => SelectBody(value, null);
    }

    public BiologyCodexOrganismViewModel? SelectedOrganism
    {
        get => selectedOrganism;
        set
        {
            if (!SetField(ref selectedOrganism, value))
            {
                return;
            }

            RaiseSelectedOrganismProperties();
            RaiseCommands();
        }
    }

    public bool HasSelectedOrganism => SelectedOrganism is not null;

    public string BodyPositionText => SelectedBody is null
        ? "No biological body selected"
        : $"Body {Bodies.IndexOf(SelectedBody) + 1:N0} of {Bodies.Count:N0}";

    public string OrganismPositionText => SelectedBody is null
        || SelectedOrganism is null
            ? "No organism selected"
            : $"Entry {SelectedBody.Organisms.IndexOf(SelectedOrganism) + 1:N0} "
                + $"of {SelectedBody.Organisms.Count:N0}";

    public string SelectedTitle => SelectedOrganism?.DisplayName
        ?? "No organism available";

    public string SelectedEntryId => SelectedOrganism is null
        ? string.Empty
        : $"Entry ID {SelectedOrganism.EntryId}";

    public string SelectedDiscoveryStatus => SelectedOrganism?.StatusText
        ?? string.Empty;

    public string SelectedSampleDistance => SelectedOrganism?.SampleDistanceText
        ?? string.Empty;

    public string SelectedReward => SelectedOrganism?.RewardText
        ?? string.Empty;

    public string SelectedTemperatureRange =>
        SelectedOrganism?.TemperatureRangeText ?? string.Empty;

    public string SelectedTemperatureWarning =>
        SelectedOrganism?.TemperatureWarningText ?? string.Empty;

    public bool HasTemperatureWarning => !string.IsNullOrWhiteSpace(
        SelectedTemperatureWarning);

    public bool HasSelectedImage => SelectedOrganism?.HasImage == true;

    public string SelectedImageUrl => SelectedOrganism?.ImageUrl ?? string.Empty;

    public string SelectedImageCredit => SelectedOrganism?.ImageCreditText
        ?? string.Empty;

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

    public ICommand PreviousBodyCommand => previousBodyCommand;

    public ICommand NextBodyCommand => nextBodyCommand;

    public ICommand PreviousOrganismCommand => previousOrganismCommand;

    public ICommand NextOrganismCommand => nextOrganismCommand;

    public ICommand OpenWindowCommand => openWindowCommand;

    public ICommand OpenSubmitImageCommand => openSubmitImageCommand;

    public ICommand OpenCanonnRegionsCommand => openCanonnRegionsCommand;

    public ICommand OpenBioforgeCommand => openBioforgeCommand;

    public ICommand OpenCanonnSignalsCommand => openCanonnSignalsCommand;

    public ICommand OpenSpanshCommand => openSpanshCommand;

    public void SetWindowOpener(Func<Task<bool>>? opener)
    {
        windowOpener = opener;
        openWindowCommand.RaiseCanExecuteChanged();
    }

    public void SetUriLauncher(Func<Uri, Task<bool>>? launcher)
    {
        uriLauncher = launcher;
        RaiseCommands();
    }

    public Task<bool> OpenEntryAsync(long entryId)
    {
        var body = Bodies.FirstOrDefault(candidate =>
            candidate.Organisms.Any(organism => organism.EntryId == entryId));
        if (body is not null)
        {
            SelectBody(body, entryId);
        }

        return OpenWindowAsync();
    }

    public Task<bool> OpenSubmitImageAsync()
    {
        if (SelectedOrganism is not { } organism)
        {
            return Task.FromResult(false);
        }

        var commander = commanderNameProvider() ?? string.Empty;
        var uri = new Uri(
            WellKnownUris.CodexMissingForm.AbsoluteUri
                + "?entry.987977054=" + Uri.EscapeDataString(commander)
                + "&entry.1282362439="
                + Uri.EscapeDataString(organism.DisplayName)
                + "&entry.468337930=" + organism.EntryId);
        return LaunchUriAsync(uri, "image submission form");
    }

    public Task<bool> OpenCanonnRegionsAsync()
    {
        return SelectedOrganism is { } organism
            ? LaunchUriAsync(
                new Uri(
                    WellKnownUris.CanonnCodexRegionsEntryPrefix
                        + organism.EntryId
                        + "&hud_category=Biology"),
                "Canonn Codex Regions")
            : Task.FromResult(false);
    }

    public Task<bool> OpenBioforgeAsync()
    {
        return SelectedOrganism is { } organism
            ? LaunchUriAsync(
                new Uri(
                    WellKnownUris.CanonnBioforgeEntryPrefix
                        + Uri.EscapeDataString(organism.DisplayName)),
                "Bioforge")
            : Task.FromResult(false);
    }

    public Task<bool> OpenCanonnSignalsAsync()
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
            new Uri(WellKnownUris.SpanshSystemPrefix + SystemAddress),
            "Spansh");
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
            or nameof(SystemSurveyViewModel.CurrentStatus)
            or nameof(SystemSurveyViewModel.DisableBioPredictions))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var previousBodyId = SelectedBody?.BodyId;
        var previousEntryId = SelectedOrganism?.EntryId;
        var snapshot = survey.Snapshot;
        var nextBodies = snapshot.Bodies
            .Where(body => body.BiologicalSignalCount > 0)
            .OrderBy(body => body.BodyId)
            .Select(CreateBody)
            .ToArray();
        Bodies = nextBodies;
        OnPropertyChanged(nameof(SystemName));
        OnPropertyChanged(nameof(SystemAddress));
        OnPropertyChanged(nameof(HasSystemAddress));
        OnPropertyChanged(nameof(SystemAddressText));

        var preferredBodyId = previousBodyId ?? ResolveCurrentBodyId(snapshot);
        var nextBody = nextBodies.FirstOrDefault(body =>
                body.BodyId == preferredBodyId)
            ?? nextBodies.FirstOrDefault();
        SelectBody(nextBody, previousEntryId);
        RaiseCommands();
    }

    private BiologyCodexBodyViewModel CreateBody(SystemScanBodySnapshot body)
    {
        var entries = new Dictionary<long, BiologyCodexOrganismViewModel>();
        var inputs = BiologyPredictionContextBuilder.Build(
            survey.Snapshot,
            body.BodyId);
        AddObservedOrganisms(body, inputs, entries);
        AddPredictedOrganisms(body, inputs, entries);
        return new BiologyCodexBodyViewModel(
            body.BodyId,
            body.Name,
            body.ShortName,
            body.BiologicalSignalCount,
            entries.Values.ToArray());
    }

    private void AddObservedOrganisms(
        SystemScanBodySnapshot body,
        BiologyPredictionInputs? inputs,
        Dictionary<long, BiologyCodexOrganismViewModel> entries)
    {
        foreach (var organism in body.Organisms)
        {
            var reference = ResolveOrganismReference(organism);
            if (reference is null)
            {
                continue;
            }

            entries[reference.EntryId] = CreateOrganism(
                body,
                reference,
                ResolveObservedDiscoveryStatus(organism),
                inputs);
        }
    }

    private ExobiologyReference? ResolveOrganismReference(
        SystemOrganismSnapshot organism)
    {
        return organism.EntryId is { } entryId
            ? catalog.FindByEntryId(entryId)
            : catalog.FindByVariant(organism.Variant)
                ?? catalog.FindBySpecies(organism.Species);
    }

    private static BiologyCodexDiscoveryStatus ResolveObservedDiscoveryStatus(
        SystemOrganismSnapshot organism)
    {
        if (organism.IsAnalyzed)
        {
            return BiologyCodexDiscoveryStatus.Analyzed;
        }

        return organism.IsScanned
            ? BiologyCodexDiscoveryStatus.Confirmed
            : BiologyCodexDiscoveryStatus.Reported;
    }

    private void AddPredictedOrganisms(
        SystemScanBodySnapshot body,
        BiologyPredictionInputs? inputs,
        Dictionary<long, BiologyCodexOrganismViewModel> entries)
    {
        if (survey.DisableBioPredictions || inputs is null)
        {
            return;
        }

        var result = evaluator.Evaluate(inputs.Context, inputs.Knowledge);
        foreach (var prediction in result.PredictionDetails)
        {
            var reference = catalog.FindByDisplayName(prediction.Name);
            if (reference is null || entries.ContainsKey(reference.EntryId))
            {
                continue;
            }

            entries.Add(
                reference.EntryId,
                CreateOrganism(
                    body,
                    reference,
                    BiologyCodexDiscoveryStatus.Predicted,
                    inputs));
        }
    }

    private BiologyCodexOrganismViewModel CreateOrganism(
        SystemScanBodySnapshot body,
        ExobiologyReference reference,
        BiologyCodexDiscoveryStatus status,
        BiologyPredictionInputs? inputs)
    {
        BiologyCriteriaClause? temperatureClause = null;
        if (inputs is not null && !string.IsNullOrWhiteSpace(reference.DisplayName))
        {
            temperatureClause = evaluator.Evaluate(
                    inputs.Context,
                    inputs.Knowledge,
                    reference.DisplayName)
                .TargetClauses.FirstOrDefault(clause =>
                    clause.Property == "temp"
                    && clause.Operator == BiologyCriteriaOperator.Range);
        }

        var temperatureRange = FormatTemperatureRange(temperatureClause);
        var temperatureWarning = FormatTemperatureWarning(
            body,
            temperatureClause);
        var genusName = ExobiologyReferenceCatalog.GetGenusName(reference);
        return new BiologyCodexOrganismViewModel(
            reference.EntryId,
            reference.DisplayName ?? reference.VariantName,
            status,
            ExobiologyReferenceCatalog.GetSampleDistanceMeters(genusName),
            reference.Reward,
            temperatureRange,
            temperatureWarning,
            reference.ImageUrl,
            reference.ImageCommander,
            reference.GetLegacyLocalImageName());
    }

    private string FormatTemperatureWarning(
        SystemScanBodySnapshot body,
        BiologyCriteriaClause? clause)
    {
        var status = survey.CurrentStatus;
        if (status?.OnFoot != true
            || status.Temperature <= 0
            || clause is null
            || !IsCurrentBody(body, status))
        {
            return string.Empty;
        }

        if (clause.Maximum is { } maximum && status.Temperature > maximum)
        {
            return $"Current temperature {status.Temperature:N1} K is too hot.";
        }

        return clause.Minimum is { } minimum && status.Temperature < minimum
            ? $"Current temperature {status.Temperature:N1} K is too cold."
            : $"Current temperature {status.Temperature:N1} K is within range.";
    }

    private int? ResolveCurrentBodyId(SystemScanSnapshot snapshot)
    {
        var status = survey.CurrentStatus;
        var body = !string.IsNullOrWhiteSpace(status?.BodyName)
            ? snapshot.Bodies.FirstOrDefault(candidate => string.Equals(
                candidate.Name,
                status.BodyName,
                StringComparison.OrdinalIgnoreCase))
            : null;
        return body?.BodyId ?? snapshot.CurrentBodyId ?? snapshot.LastDetailedBodyId;
    }

    private static bool IsCurrentBody(
        SystemScanBodySnapshot body,
        Core.Journal.EliteStatus status)
    {
        return !string.IsNullOrWhiteSpace(status.BodyName)
            && string.Equals(
                body.Name,
                status.BodyName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatTemperatureRange(BiologyCriteriaClause? clause)
    {
        if (clause is null)
        {
            return "Temperature criteria unavailable";
        }

        return (clause.Minimum, clause.Maximum) switch
        {
            ({ } minimum, { } maximum) =>
                $"{minimum:N0}–{maximum:N0} K temperature range",
            ({ } minimum, null) => $"At least {minimum:N0} K",
            (null, { } maximum) => $"At most {maximum:N0} K",
            _ => "Temperature criteria unavailable",
        };
    }

    private void SelectBody(
        BiologyCodexBodyViewModel? value,
        long? preferredEntryId)
    {
        var bodyChanged = SetField(ref selectedBody, value, nameof(SelectedBody));
        var organism = preferredEntryId is { } entryId
            ? value?.Organisms.FirstOrDefault(candidate =>
                candidate.EntryId == entryId)
            : null;
        organism ??= value?.Organisms is { Count: > 0 } organisms
            ? organisms[0]
            : null;
        SelectedOrganism = organism;
        if (bodyChanged)
        {
            OnPropertyChanged(nameof(BodyPositionText));
            OnPropertyChanged(nameof(OrganismPositionText));
            OnPropertyChanged(nameof(EmptyStateText));
        }

        RaiseCommands();
    }

    private void MoveBody(int delta)
    {
        if (SelectedBody is null || Bodies.Count == 0)
        {
            return;
        }

        var index = Bodies.IndexOf(SelectedBody);
        SelectedBody = Bodies[(index + delta + Bodies.Count) % Bodies.Count];
    }

    private void MoveOrganism(int delta)
    {
        if (SelectedBody is not { Organisms.Count: > 0 } body
            || SelectedOrganism is null)
        {
            return;
        }

        var index = body.Organisms.IndexOf(SelectedOrganism);
        SelectedOrganism = body.Organisms[
            (index + delta + body.Organisms.Count) % body.Organisms.Count];
    }

    private Task<bool> OpenWindowAsync()
    {
        return windowOpener?.Invoke() ?? Task.FromResult(false);
    }

    private bool CanOpenOrganismLink()
    {
        return uriLauncher is not null && SelectedOrganism is not null;
    }

    private bool CanOpenSystemLink()
    {
        return uriLauncher is not null && HasSystem;
    }

    private async Task<bool> LaunchUriAsync(Uri uri, string label)
    {
        if (uriLauncher is null)
        {
            LaunchStatus = $"{label} is unavailable on this platform.";
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

    private void RaiseSelectedOrganismProperties()
    {
        OnPropertyChanged(nameof(HasSelectedOrganism));
        OnPropertyChanged(nameof(OrganismPositionText));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedEntryId));
        OnPropertyChanged(nameof(SelectedDiscoveryStatus));
        OnPropertyChanged(nameof(SelectedSampleDistance));
        OnPropertyChanged(nameof(SelectedReward));
        OnPropertyChanged(nameof(SelectedTemperatureRange));
        OnPropertyChanged(nameof(SelectedTemperatureWarning));
        OnPropertyChanged(nameof(HasTemperatureWarning));
        OnPropertyChanged(nameof(HasSelectedImage));
        OnPropertyChanged(nameof(SelectedImageUrl));
        OnPropertyChanged(nameof(SelectedImageCredit));
    }

    private void RaiseCommands()
    {
        previousBodyCommand.RaiseCanExecuteChanged();
        nextBodyCommand.RaiseCanExecuteChanged();
        previousOrganismCommand.RaiseCanExecuteChanged();
        nextOrganismCommand.RaiseCanExecuteChanged();
        openWindowCommand.RaiseCanExecuteChanged();
        openSubmitImageCommand.RaiseCanExecuteChanged();
        openCanonnRegionsCommand.RaiseCanExecuteChanged();
        openBioforgeCommand.RaiseCanExecuteChanged();
        openCanonnSignalsCommand.RaiseCanExecuteChanged();
        openSpanshCommand.RaiseCanExecuteChanged();
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

    private sealed class DelegateCommand(Action execute, Func<bool> canExecute)
        : ICommand
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

public sealed record BiologyCodexBodyViewModel(
    int BodyId,
    string Name,
    string ShortName,
    int BiologicalSignalCount,
    IReadOnlyList<BiologyCodexOrganismViewModel> Organisms)
{
    public string SignalCountText => BiologicalSignalCount == 1
        ? "1 biological signal"
        : $"{BiologicalSignalCount:N0} biological signals";

    public string DisplayName => Organisms.Count == 0
        ? $"{Name} · no entries"
        : $"{Name} · {Organisms.Count:N0} entries";
}

public sealed record BiologyCodexOrganismViewModel(
    long EntryId,
    string DisplayName,
    BiologyCodexDiscoveryStatus Status,
    int SampleDistanceMeters,
    long Reward,
    string TemperatureRangeText,
    string TemperatureWarningText,
    string? ImageUrl,
    string? ImageCommander,
    string? LocalImageName)
{
    public string StatusText => Status.ToString();

    public string SampleDistanceText =>
        $"{SampleDistanceMeters:N0} m minimum sample separation";

    public string RewardText => Reward > 0
        ? $"{Reward:N0} CR base reward"
        : "Reward unavailable";

    public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);

    public string ImageCreditText => string.IsNullOrWhiteSpace(ImageCommander)
        ? "Canonn Codex reference image"
        : $"Reference image by CMDR {ImageCommander}";
}

public enum BiologyCodexDiscoveryStatus
{
    Predicted,
    Reported,
    Confirmed,
    Analyzed,
}

internal static class BiologyCodexListExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> source, T value)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(source[index], value))
            {
                return index;
            }
        }

        return -1;
    }
}
