using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Settlements;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class HumanSiteTemplateAuthoringViewModel
    : INotifyPropertyChanged
{
    private HumanSiteTemplateCatalog catalog;
    private readonly HumanSiteTemplateCatalogExporter exporter;
    private readonly Action previewChanged;
    private readonly DelegateCommand startCommand;
    private readonly DelegateCommand beginPolygonCommand;
    private readonly DelegateCommand addPolygonPointCommand;
    private readonly DelegateCommand endPolygonCommand;
    private readonly DelegateCommand cancelPolygonCommand;
    private readonly DelegateCommand addCircleCommand;
    private readonly DelegateCommand removePendingPathCommand;
    private readonly DelegateCommand commitBuildingCommand;
    private readonly DelegateCommand addNamedPointCommand;
    private readonly DelegateCommand addDataTerminalCommand;
    private readonly DelegateCommand addSecureDoorCommand;
    private readonly DelegateCommand removeLastNamedPointCommand;
    private readonly DelegateCommand removeLastDataTerminalCommand;
    private readonly DelegateCommand removeLastSecureDoorCommand;
    private readonly DelegateCommand removeLastBuildingCommand;
    private readonly DelegateCommand requestDiscardCommand;
    private readonly DelegateCommand confirmDiscardCommand;
    private readonly DelegateCommand cancelDiscardCommand;
    private HumanSiteTemplateAuthoringSession? session;
    private HumanSiteTemplate? activeTemplate;
    private HumanSiteMapPoint? currentOffset;
    private string? activeIdentity;
    private double relativeHeading;
    private bool? shieldsUp;
    private int securityLevel;
    private int floor = 1;
    private double circleRadius = 5;
    private string buildingName = string.Empty;
    private string namedPointName = string.Empty;
    private bool isDiscardConfirmationPending;
    private bool isBusy;
    private string statusMessage =
        "Approach an aligned settlement to author its template.";
    private string? lastExportPath;

    public HumanSiteTemplateAuthoringViewModel(
        HumanSiteTemplateCatalog catalog,
        Action previewChanged,
        HumanSiteTemplateCatalogExporter? exporter = null)
    {
        this.catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        this.previewChanged = previewChanged
            ?? throw new ArgumentNullException(nameof(previewChanged));
        this.exporter = exporter ?? new HumanSiteTemplateCatalogExporter();
        startCommand = new DelegateCommand(Start, () => CanStart);
        beginPolygonCommand = new DelegateCommand(
            BeginPolygon,
            () => CanCapturePoint && session?.IsCapturingPolygon == false);
        addPolygonPointCommand = new DelegateCommand(
            AddPolygonPoint,
            () => CanCapturePoint && session?.IsCapturingPolygon == true);
        endPolygonCommand = new DelegateCommand(
            EndPolygon,
            () => CanCapturePoint
                && session?.IsCapturingPolygon == true
                && session.PendingPolygonPoints.Count > 0);
        cancelPolygonCommand = new DelegateCommand(
            CancelPolygon,
            () => session?.IsCapturingPolygon == true && !IsBusy);
        addCircleCommand = new DelegateCommand(
            AddCircle,
            () => CanCapturePoint && session?.IsCapturingPolygon == false);
        removePendingPathCommand = new DelegateCommand(
            RemovePendingPath,
            () => session?.HasPendingBuilding == true && !IsBusy);
        commitBuildingCommand = new DelegateCommand(
            CommitBuilding,
            () => session?.HasPendingBuilding == true
                && !session.IsCapturingPolygon
                && !string.IsNullOrWhiteSpace(BuildingName)
                && !IsBusy);
        addNamedPointCommand = new DelegateCommand(
            AddNamedPoint,
            () => CanCapturePoint
                && !string.IsNullOrWhiteSpace(NamedPointName));
        addDataTerminalCommand = new DelegateCommand(
            AddDataTerminal,
            () => CanCapturePoint);
        addSecureDoorCommand = new DelegateCommand(
            AddSecureDoor,
            () => CanCapturePoint);
        removeLastNamedPointCommand = new DelegateCommand(
            RemoveLastNamedPoint,
            () => session?.Template.NamedPoints.Count > 0 && !IsBusy);
        removeLastDataTerminalCommand = new DelegateCommand(
            RemoveLastDataTerminal,
            () => session?.Template.DataTerminals.Count > 0 && !IsBusy);
        removeLastSecureDoorCommand = new DelegateCommand(
            RemoveLastSecureDoor,
            () => session?.Template.SecureDoors.Count > 0 && !IsBusy);
        removeLastBuildingCommand = new DelegateCommand(
            RemoveLastBuilding,
            () => session?.Template.Buildings.Count > 0 && !IsBusy);
        requestDiscardCommand = new DelegateCommand(
            RequestDiscard,
            () => IsAuthoring && !IsBusy);
        confirmDiscardCommand = new DelegateCommand(
            ConfirmDiscard,
            () => IsDiscardConfirmationPending && !IsBusy);
        cancelDiscardCommand = new DelegateCommand(
            CancelDiscard,
            () => IsDiscardConfirmationPending && !IsBusy);
        StartCommand = startCommand;
        BeginPolygonCommand = beginPolygonCommand;
        AddPolygonPointCommand = addPolygonPointCommand;
        EndPolygonCommand = endPolygonCommand;
        CancelPolygonCommand = cancelPolygonCommand;
        AddCircleCommand = addCircleCommand;
        RemovePendingPathCommand = removePendingPathCommand;
        CommitBuildingCommand = commitBuildingCommand;
        AddNamedPointCommand = addNamedPointCommand;
        AddDataTerminalCommand = addDataTerminalCommand;
        AddSecureDoorCommand = addSecureDoorCommand;
        RemoveLastNamedPointCommand = removeLastNamedPointCommand;
        RemoveLastDataTerminalCommand = removeLastDataTerminalCommand;
        RemoveLastSecureDoorCommand = removeLastSecureDoorCommand;
        RemoveLastBuildingCommand = removeLastBuildingCommand;
        RequestDiscardCommand = requestDiscardCommand;
        ConfirmDiscardCommand = confirmDiscardCommand;
        CancelDiscardCommand = cancelDiscardCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand StartCommand { get; }

    public ICommand BeginPolygonCommand { get; }

    public ICommand AddPolygonPointCommand { get; }

    public ICommand EndPolygonCommand { get; }

    public ICommand CancelPolygonCommand { get; }

    public ICommand AddCircleCommand { get; }

    public ICommand RemovePendingPathCommand { get; }

    public ICommand CommitBuildingCommand { get; }

    public ICommand AddNamedPointCommand { get; }

    public ICommand AddDataTerminalCommand { get; }

    public ICommand AddSecureDoorCommand { get; }

    public ICommand RemoveLastNamedPointCommand { get; }

    public ICommand RemoveLastDataTerminalCommand { get; }

    public ICommand RemoveLastSecureDoorCommand { get; }

    public ICommand RemoveLastBuildingCommand { get; }

    public ICommand RequestDiscardCommand { get; }

    public ICommand ConfirmDiscardCommand { get; }

    public ICommand CancelDiscardCommand { get; }

    public HumanSiteTemplate? PreviewTemplate =>
        session?.CreatePreviewTemplate(BuildingName);

    public bool CanStart => activeTemplate is not null
        && !IsAuthoring
        && !IsBusy;

    public bool IsAuthoring => session is not null;

    public bool CanCapturePoint => session is not null
        && currentOffset is not null
        && !IsBusy;

    public bool IsCapturingPolygon =>
        session?.IsCapturingPolygon == true;

    public bool HasPendingBuilding =>
        session?.HasPendingBuilding == true;

    public int PendingPolygonPointCount =>
        session?.PendingPolygonPoints.Count ?? 0;

    public int PendingBuildingPathCount =>
        session?.PendingBuildingPaths.Count ?? 0;

    public int BuildingCount => session?.Template.Buildings.Count
        ?? activeTemplate?.Buildings.Count
        ?? 0;

    public int NamedPointCount => session?.Template.NamedPoints.Count
        ?? activeTemplate?.NamedPoints.Count
        ?? 0;

    public int DataTerminalCount => session?.Template.DataTerminals.Count
        ?? activeTemplate?.DataTerminals.Count
        ?? 0;

    public int SecureDoorCount => session?.Template.SecureDoors.Count
        ?? activeTemplate?.SecureDoors.Count
        ?? 0;

    public string TemplateTitle => activeTemplate is null
        ? "No aligned settlement template"
        : $"{activeTemplate.Economy} #{activeTemplate.SubType} · "
            + activeTemplate.Name;

    public string CurrentOffsetText => currentOffset is { } offset
        ? $"X {offset.X:F1} m · Y {offset.Y:F1} m · door {relativeHeading:F0}°"
        : "Current commander offset is unavailable.";

    public int SecurityLevel
    {
        get => securityLevel;
        set => SetField(ref securityLevel, Math.Clamp(value, 0, 3));
    }

    public int Floor
    {
        get => floor;
        set => SetField(ref floor, Math.Clamp(value, 0, 99));
    }

    public double CircleRadius
    {
        get => circleRadius;
        set
        {
            if (SetField(ref circleRadius, Math.Clamp(value, 0.1, 10_000)))
            {
                addCircleCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string BuildingName
    {
        get => buildingName;
        set
        {
            if (SetField(ref buildingName, value ?? string.Empty))
            {
                commitBuildingCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NamedPointName
    {
        get => namedPointName;
        set
        {
            if (SetField(ref namedPointName, value ?? string.Empty))
            {
                addNamedPointCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsDiscardConfirmationPending
    {
        get => isDiscardConfirmationPending;
        private set
        {
            if (SetField(ref isDiscardConfirmationPending, value))
            {
                RaiseCommandStates();
            }
        }
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

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string? LastExportPath
    {
        get => lastExportPath;
        private set => SetField(ref lastExportPath, value);
    }

    public void UpdateContext(
        HumanSiteLiveSnapshot? site,
        HumanSiteMapPoint? commanderOffset,
        double currentRelativeHeading,
        bool currentShieldsUp)
    {
        var identity = site is null
            ? null
            : $"{site.SystemAddress}/{site.MarketId}/"
                + $"{site.Template?.Economy}/{site.Template?.SubType}";
        if (!string.Equals(identity, activeIdentity, StringComparison.Ordinal))
        {
            activeIdentity = identity;
            activeTemplate = site?.Template;
            if (session is not null)
            {
                session = null;
                IsDiscardConfirmationPending = false;
                StatusMessage = "The active settlement changed, so its unexported authoring draft was discarded.";
                previewChanged();
            }

            OnPropertyChanged(nameof(TemplateTitle));
            OnPropertyChanged(nameof(CanStart));
            RaiseCounts();
        }
        else
        {
            activeTemplate = site?.Template;
        }

        currentOffset = commanderOffset;
        relativeHeading = currentRelativeHeading;
        OnPropertyChanged(nameof(CurrentOffsetText));
        OnPropertyChanged(nameof(CanCapturePoint));
        if (shieldsUp is { } previousShields
            && previousShields != currentShieldsUp
            && session?.IsCapturingPolygon == true
            && currentOffset is { } point)
        {
            session.AddPolygonPoint(point);
            StatusMessage = $"Shield toggle captured polygon point {session.PendingPolygonPoints.Count:N0}.";
            NotifyDraftChanged();
        }

        shieldsUp = currentShieldsUp;
        RaiseCommandStates();
    }

    public async Task ExportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (session is null || IsBusy)
        {
            StatusMessage = "Start a settlement template draft before exporting.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Staging and verifying the settlement template catalog...";
        try
        {
            var updated = catalog.WithTemplate(session.Template);
            var result = await exporter.ExportAsync(
                updated,
                path,
                cancellationToken);
            catalog = updated;
            activeTemplate = session.Template;
            LastExportPath = result.Path;
            StatusMessage = result.BackupPath is null
                ? $"Exported and verified {result.TemplateCount:N0} settlement templates."
                : $"Exported and verified {result.TemplateCount:N0} settlement templates; the previous file was backed up.";
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or TaskCanceledException)
        {
            StatusMessage = "The settlement template catalog was not exported: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Start()
    {
        if (!CanStart || activeTemplate is null)
        {
            return;
        }

        session = new HumanSiteTemplateAuthoringSession(activeTemplate);
        StatusMessage = "Template draft started. Live points change only the preview until you choose an export file.";
        NotifyDraftChanged();
    }

    private void BeginPolygon()
    {
        if (currentOffset is not { } point || session is null)
        {
            return;
        }

        session.BeginPolygon(point);
        StatusMessage = "Polygon capture started. Move and add points manually or toggle shields.";
        NotifyDraftChanged();
    }

    private void AddPolygonPoint()
    {
        if (currentOffset is not { } point || session is null)
        {
            return;
        }

        session.AddPolygonPoint(point);
        StatusMessage = $"Captured polygon point {session.PendingPolygonPoints.Count:N0}.";
        NotifyDraftChanged();
    }

    private void EndPolygon()
    {
        if (currentOffset is not { } point || session is null)
        {
            return;
        }

        try
        {
            session.EndPolygon(point);
            StatusMessage = $"Added path {session.PendingBuildingPaths.Count:N0} to the pending building.";
            NotifyDraftChanged();
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void CancelPolygon()
    {
        session?.CancelPolygon();
        StatusMessage = "Polygon capture cancelled.";
        NotifyDraftChanged();
    }

    private void AddCircle()
    {
        if (currentOffset is not { } point || session is null)
        {
            return;
        }

        session.AddCircle(point, CircleRadius);
        StatusMessage = $"Added a {CircleRadius:F1} m circle to the pending building.";
        NotifyDraftChanged();
    }

    private void RemovePendingPath()
    {
        if (session?.RemoveLastPendingPath() == true)
        {
            StatusMessage = "Removed the last pending building path.";
            NotifyDraftChanged();
        }
    }

    private void CommitBuilding()
    {
        if (session is null)
        {
            return;
        }

        session.CommitBuilding(BuildingName);
        StatusMessage = $"Added building '{BuildingName.Trim()}' to the local draft.";
        BuildingName = string.Empty;
        NotifyDraftChanged();
    }

    private void AddNamedPoint()
    {
        if (session is null || currentOffset is not { } point)
        {
            return;
        }

        session.AddNamedPoint(
            NamedPointName,
            point,
            SecurityLevel,
            Floor);
        StatusMessage = $"Added '{NamedPointName.Trim()}' to the local draft.";
        NotifyDraftChanged();
    }

    private void AddDataTerminal()
    {
        if (session is null || currentOffset is not { } point)
        {
            return;
        }

        session.AddDataTerminal(point, SecurityLevel, Floor);
        StatusMessage = "Added a data terminal to the local draft.";
        NotifyDraftChanged();
    }

    private void AddSecureDoor()
    {
        if (session is null || currentOffset is not { } point)
        {
            return;
        }

        session.AddSecureDoor(
            point,
            relativeHeading,
            SecurityLevel,
            Floor);
        StatusMessage = "Added a secure door with the current relative heading.";
        NotifyDraftChanged();
    }

    private void RemoveLastNamedPoint()
    {
        if (session?.RemoveLastNamedPoint() == true)
        {
            StatusMessage = "Removed the last named point from the local draft.";
            NotifyDraftChanged();
        }
    }

    private void RemoveLastDataTerminal()
    {
        if (session?.RemoveLastDataTerminal() == true)
        {
            StatusMessage = "Removed the last data terminal from the local draft.";
            NotifyDraftChanged();
        }
    }

    private void RemoveLastSecureDoor()
    {
        if (session?.RemoveLastSecureDoor() == true)
        {
            StatusMessage = "Removed the last secure door from the local draft.";
            NotifyDraftChanged();
        }
    }

    private void RemoveLastBuilding()
    {
        if (session?.RemoveLastBuilding() == true)
        {
            StatusMessage = "Removed the last building from the local draft.";
            NotifyDraftChanged();
        }
    }

    private void RequestDiscard()
    {
        IsDiscardConfirmationPending = true;
        StatusMessage = "Confirm to discard this unexported settlement template draft.";
    }

    private void ConfirmDiscard()
    {
        session = null;
        IsDiscardConfirmationPending = false;
        StatusMessage = "The local authoring draft was discarded. No file was changed.";
        NotifyDraftChanged();
    }

    private void CancelDiscard()
    {
        IsDiscardConfirmationPending = false;
        StatusMessage = "Draft discard cancelled.";
    }

    private void NotifyDraftChanged()
    {
        OnPropertyChanged(nameof(PreviewTemplate));
        OnPropertyChanged(nameof(IsAuthoring));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanCapturePoint));
        OnPropertyChanged(nameof(IsCapturingPolygon));
        OnPropertyChanged(nameof(HasPendingBuilding));
        OnPropertyChanged(nameof(PendingPolygonPointCount));
        OnPropertyChanged(nameof(PendingBuildingPathCount));
        RaiseCounts();
        previewChanged();
        RaiseCommandStates();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(BuildingCount));
        OnPropertyChanged(nameof(NamedPointCount));
        OnPropertyChanged(nameof(DataTerminalCount));
        OnPropertyChanged(nameof(SecureDoorCount));
    }

    private void RaiseCommandStates()
    {
        startCommand.RaiseCanExecuteChanged();
        beginPolygonCommand.RaiseCanExecuteChanged();
        addPolygonPointCommand.RaiseCanExecuteChanged();
        endPolygonCommand.RaiseCanExecuteChanged();
        cancelPolygonCommand.RaiseCanExecuteChanged();
        addCircleCommand.RaiseCanExecuteChanged();
        removePendingPathCommand.RaiseCanExecuteChanged();
        commitBuildingCommand.RaiseCanExecuteChanged();
        addNamedPointCommand.RaiseCanExecuteChanged();
        addDataTerminalCommand.RaiseCanExecuteChanged();
        addSecureDoorCommand.RaiseCanExecuteChanged();
        removeLastNamedPointCommand.RaiseCanExecuteChanged();
        removeLastDataTerminalCommand.RaiseCanExecuteChanged();
        removeLastSecureDoorCommand.RaiseCanExecuteChanged();
        removeLastBuildingCommand.RaiseCanExecuteChanged();
        requestDiscardCommand.RaiseCanExecuteChanged();
        confirmDiscardCommand.RaiseCanExecuteChanged();
        cancelDiscardCommand.RaiseCanExecuteChanged();
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
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }

    private sealed class DelegateCommand(
        Action execute,
        Func<bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter))
            {
                execute();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
