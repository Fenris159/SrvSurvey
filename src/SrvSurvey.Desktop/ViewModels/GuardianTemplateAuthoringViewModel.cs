using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Input;
using SrvSurvey.Core.Guardian;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class GuardianTemplateAuthoringViewModel : INotifyPropertyChanged
{
    private GuardianSiteTemplateCatalog catalog;
    private readonly GuardianSiteTemplateCatalogExporter exporter;
    private readonly Action<bool> draftChanged;
    private readonly Action? pointPreviewChanged;
    private readonly DelegateCommand startCommand;
    private readonly DelegateCommand editCommand;
    private readonly DelegateCommand addMeasuredPointCommand;
    private readonly DelegateCommand applySelectedPointCommand;
    private readonly DelegateCommand removeSelectedPointCommand;
    private readonly DelegateCommand setGroupCommand;
    private readonly DelegateCommand removeSelectedGroupCommand;
    private readonly DelegateCommand requestDiscardCommand;
    private readonly DelegateCommand confirmDiscardCommand;
    private readonly DelegateCommand cancelDiscardCommand;
    private GuardianSiteTemplate? activeTemplate;
    private GuardianSiteTemplateAuthoringSession? session;
    private GuardianTemplateDraftMode draftMode;
    private GuardianSurveyMeasurement? liveMeasurement;
    private IReadOnlyList<GuardianTemplatePointViewModel> points = [];
    private GuardianTemplatePointViewModel? selectedPoint;
    private IReadOnlyList<GuardianTemplateGroupViewModel> groups = [];
    private GuardianTemplateGroupViewModel? selectedGroup;
    private string templateName = string.Empty;
    private string backgroundImage = string.Empty;
    private decimal imageOffsetX;
    private decimal imageOffsetY;
    private decimal scaleFactor = 1;
    private string newPointName = string.Empty;
    private GuardianPoiType newPointType = GuardianPoiType.Unknown;
    private string pointName = string.Empty;
    private GuardianPoiType pointType = GuardianPoiType.Unknown;
    private decimal pointAngle;
    private decimal pointDistance;
    private decimal pointRotation;
    private string groupName = string.Empty;
    private decimal groupAngle;
    private decimal groupDistance;
    private bool isDiscardConfirmationPending;
    private bool isBusy;
    private bool isLoadingSelectedPointFields;
    private bool isLoadingMetadataFields;
    private string? lastExportPath;
    private string statusMessage =
        "Select a mapped Guardian ruins or structure survey to author its master template.";

    public GuardianTemplateAuthoringViewModel(
        GuardianSiteTemplateCatalog catalog,
        Action<bool> draftChanged,
        GuardianSiteTemplateCatalogExporter? exporter = null,
        Action? pointPreviewChanged = null,
        string? defaultCatalogPath = null)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.draftChanged = draftChanged
            ?? throw new ArgumentNullException(nameof(draftChanged));
        this.exporter = exporter ?? new GuardianSiteTemplateCatalogExporter();
        this.pointPreviewChanged = pointPreviewChanged;
        DefaultCatalogPath = string.IsNullOrWhiteSpace(defaultCatalogPath)
            ? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData),
                "SrvSurvey",
                "cross-platform",
                "guardianSiteTemplates.json")
            : Path.GetFullPath(defaultCatalogPath);
        startCommand = new DelegateCommand(Start, () => CanStart);
        editCommand = new DelegateCommand(Edit, () => CanEdit);
        addMeasuredPointCommand = new DelegateCommand(
            AddMeasuredPoint,
            () => IsAuthoring && HasLiveMeasurement && !IsBusy);
        applySelectedPointCommand = new DelegateCommand(
            ApplySelectedPoint,
            () => IsAuthoring && SelectedPoint is not null && !IsBusy);
        removeSelectedPointCommand = new DelegateCommand(
            RemoveSelectedPoint,
            () => IsAuthoring && SelectedPoint is not null && !IsBusy);
        setGroupCommand = new DelegateCommand(
            SetGroup,
            () => IsAuthoring
                && !string.IsNullOrWhiteSpace(GroupName)
                && !IsBusy);
        removeSelectedGroupCommand = new DelegateCommand(
            RemoveSelectedGroup,
            () => IsAuthoring && SelectedGroup is not null && !IsBusy);
        requestDiscardCommand = new DelegateCommand(
            RequestDiscard,
            () => IsAuthoring && !IsBusy && !IsDiscardConfirmationPending);
        confirmDiscardCommand = new DelegateCommand(
            ConfirmDiscard,
            () => IsAuthoring && !IsBusy && IsDiscardConfirmationPending);
        cancelDiscardCommand = new DelegateCommand(
            CancelDiscard,
            () => IsDiscardConfirmationPending && !IsBusy);
        StartCommand = startCommand;
        EditCommand = editCommand;
        AddMeasuredPointCommand = addMeasuredPointCommand;
        ApplySelectedPointCommand = applySelectedPointCommand;
        RemoveSelectedPointCommand = removeSelectedPointCommand;
        SetGroupCommand = setGroupCommand;
        RemoveSelectedGroupCommand = removeSelectedGroupCommand;
        RequestDiscardCommand = requestDiscardCommand;
        ConfirmDiscardCommand = confirmDiscardCommand;
        CancelDiscardCommand = cancelDiscardCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand StartCommand { get; }

    public ICommand EditCommand { get; }

    public ICommand AddMeasuredPointCommand { get; }

    public ICommand ApplySelectedPointCommand { get; }

    public ICommand RemoveSelectedPointCommand { get; }

    public ICommand SetGroupCommand { get; }

    public ICommand RemoveSelectedGroupCommand { get; }

    public ICommand RequestDiscardCommand { get; }

    public ICommand ConfirmDiscardCommand { get; }

    public ICommand CancelDiscardCommand { get; }

    public IReadOnlyList<GuardianPoiType> PointTypes { get; } =
        Enum.GetValues<GuardianPoiType>();

    public GuardianSiteTemplateCatalog Catalog => catalog;

    public GuardianSiteTemplate? PreviewTemplate => BuildPreviewTemplate();

    public bool HasActiveTemplate => activeTemplate is not null;

    public bool CanStart => activeTemplate is not null && !IsAuthoring && !IsBusy;

    public bool CanEdit => CanStart;

    public bool IsAuthoring => session is not null;

    public bool IsNewMapDraft => draftMode == GuardianTemplateDraftMode.NewMap;

    public string DraftModeTitle => draftMode switch
    {
        GuardianTemplateDraftMode.NewMap => "NEW MAP DRAFT",
        GuardianTemplateDraftMode.EditCurrent => "EDIT CURRENT MAP",
        _ => "MAP DRAFT",
    };

    public string DraftDescription => draftMode switch
    {
        GuardianTemplateDraftMode.NewMap =>
            "Build a replacement shared map from scratch for this site type. Choose a background, align it, then add measured master points and group labels.",
        GuardianTemplateDraftMode.EditCurrent =>
            "Adjust the existing shared map. Its background, alignment, master points, and group labels are copied into this draft.",
        _ => string.Empty,
    };

    public string DefaultCatalogPath { get; }

    public string ManagedBackgroundDirectory => Path.Combine(
        Path.GetDirectoryName(DefaultCatalogPath)!,
        "guardian-map-images");

    public string SaveLocationText =>
        "Keep the suggested folder and file name to install these map changes for future launches. Choose another location only to export a copy.\n"
        + DefaultCatalogPath;

    public bool HasLiveMeasurement => liveMeasurement is not null;

    public string TemplateTitle => activeTemplate is null
        ? "No Guardian template selected"
        : $"{activeTemplate.SiteType} · {activeTemplate.Name}";

    public string LiveMeasurementText => liveMeasurement is { } measurement
        ? $"{measurement.Distance:N1} m · angle {measurement.Angle:N1}° · rotation {measurement.Rotation:N0}°"
        : "Live measurement unavailable; the active site must match the selected survey.";

    public IReadOnlyList<GuardianTemplatePointViewModel> Points
    {
        get => points;
        private set => SetField(ref points, value);
    }

    public GuardianTemplatePointViewModel? SelectedPoint
    {
        get => selectedPoint;
        set
        {
            if (!SetField(ref selectedPoint, value))
            {
                return;
            }

            isLoadingSelectedPointFields = true;
            try
            {
                if (value is not null)
                {
                    PointName = value.Point.Name;
                    PointType = value.Point.Type;
                    PointAngle = (decimal)value.Point.Angle;
                    PointDistance = (decimal)value.Point.Distance;
                    PointRotation = (decimal)value.Point.Rotation;
                }
            }
            finally
            {
                isLoadingSelectedPointFields = false;
            }

            OnPropertyChanged(nameof(HasSelectedPoint));
            RaiseCommandStates();
            NotifySelectedPointPreviewChanged();
        }
    }

    public bool HasSelectedPoint => SelectedPoint is not null;

    public IReadOnlyList<GuardianTemplateGroupViewModel> Groups
    {
        get => groups;
        private set => SetField(ref groups, value);
    }

    public GuardianTemplateGroupViewModel? SelectedGroup
    {
        get => selectedGroup;
        set
        {
            if (!SetField(ref selectedGroup, value))
            {
                return;
            }

            if (value is not null)
            {
                GroupName = value.Name;
                GroupAngle = (decimal)value.Location.X;
                GroupDistance = (decimal)value.Location.Y;
            }

            RaiseCommandStates();
        }
    }

    public string TemplateName
    {
        get => templateName;
        set
        {
            if (SetField(ref templateName, value ?? string.Empty))
            {
                NotifyMetadataPreviewChanged();
            }
        }
    }

    public string BackgroundImage
    {
        get => backgroundImage;
        set
        {
            if (SetField(ref backgroundImage, value ?? string.Empty))
            {
                NotifyMetadataPreviewChanged();
            }
        }
    }

    public decimal ImageOffsetX
    {
        get => imageOffsetX;
        set
        {
            if (SetField(ref imageOffsetX, value))
            {
                NotifyMetadataPreviewChanged();
            }
        }
    }

    public decimal ImageOffsetY
    {
        get => imageOffsetY;
        set
        {
            if (SetField(ref imageOffsetY, value))
            {
                NotifyMetadataPreviewChanged();
            }
        }
    }

    public decimal ScaleFactor
    {
        get => scaleFactor;
        set
        {
            if (SetField(ref scaleFactor, value))
            {
                NotifyMetadataPreviewChanged();
            }
        }
    }

    public string PointName
    {
        get => pointName;
        set
        {
            if (SetField(ref pointName, value ?? string.Empty))
            {
                NotifySelectedPointPreviewChanged();
            }
        }
    }

    public string NewPointName
    {
        get => newPointName;
        set => SetField(ref newPointName, value ?? string.Empty);
    }

    public GuardianPoiType NewPointType
    {
        get => newPointType;
        set
        {
            if (SetField(ref newPointType, value) && session is not null)
            {
                NewPointName = NextPointName(value);
            }
        }
    }

    public GuardianPoiType PointType
    {
        get => pointType;
        set
        {
            if (SetField(ref pointType, value))
            {
                NotifySelectedPointPreviewChanged();
            }
        }
    }

    public decimal PointAngle
    {
        get => pointAngle;
        set
        {
            if (SetField(ref pointAngle, value))
            {
                NotifySelectedPointPreviewChanged();
            }
        }
    }

    public decimal PointDistance
    {
        get => pointDistance;
        set
        {
            if (SetField(ref pointDistance, value))
            {
                NotifySelectedPointPreviewChanged();
            }
        }
    }

    public decimal PointRotation
    {
        get => pointRotation;
        set
        {
            if (SetField(ref pointRotation, value))
            {
                NotifySelectedPointPreviewChanged();
            }
        }
    }

    public string GroupName
    {
        get => groupName;
        set
        {
            if (SetField(ref groupName, value ?? string.Empty))
            {
                setGroupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public decimal GroupAngle
    {
        get => groupAngle;
        set => SetField(ref groupAngle, value);
    }

    public decimal GroupDistance
    {
        get => groupDistance;
        set => SetField(ref groupDistance, value);
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

    public string? LastExportPath
    {
        get => lastExportPath;
        private set => SetField(ref lastExportPath, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public void UpdateContext(
        GuardianSiteTemplate? template,
        GuardianSurveyMeasurement? measurement)
    {
        if (!string.Equals(
                activeTemplate?.SiteType,
                template?.SiteType,
                StringComparison.OrdinalIgnoreCase))
        {
            if (session is not null)
            {
                session = null;
                draftMode = GuardianTemplateDraftMode.None;
                StatusMessage = "The selected Guardian site type changed, so its unexported template draft was discarded.";
                draftChanged(false);
                NotifyDraftModeChanged();
            }

            activeTemplate = template;
            IsDiscardConfirmationPending = false;
            SelectedPoint = null;
            LoadMetadata(template);
            RefreshCollections();
            OnPropertyChanged(nameof(TemplateTitle));
            OnPropertyChanged(nameof(HasActiveTemplate));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(IsAuthoring));
        }
        else
        {
            activeTemplate = template;
        }

        liveMeasurement = measurement;
        OnPropertyChanged(nameof(HasLiveMeasurement));
        OnPropertyChanged(nameof(LiveMeasurementText));
        RaiseCommandStates();
    }

    public void SelectPoint(string? name)
    {
        SelectedPoint = string.IsNullOrWhiteSpace(name)
            ? null
            : Points.FirstOrDefault(point => string.Equals(
                point.Name,
                name,
                StringComparison.OrdinalIgnoreCase));
    }

    public async Task ExportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (session is null || IsBusy)
        {
            StatusMessage = "Start a Guardian template draft before exporting.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Staging and verifying the Guardian template catalog...";
        try
        {
            if (!TryCommitMetadata())
            {
                return;
            }

            var updated = catalog.WithTemplate(session.Template);
            var result = await exporter.ExportAsync(
                updated,
                path,
                cancellationToken);
            catalog = updated;
            activeTemplate = session.Template;
            LastExportPath = result.Path;
            var installed = string.Equals(
                Path.GetFullPath(result.Path),
                DefaultCatalogPath,
                StringComparison.OrdinalIgnoreCase);
            var action = installed
                ? "Saved and installed"
                : "Exported a copy of";
            StatusMessage = result.BackupPath is null
                ? $"{action} {result.TemplateCount:N0} verified Guardian templates."
                : $"{action} {result.TemplateCount:N0} verified Guardian templates; the previous file was backed up.";
            draftChanged(true);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or TaskCanceledException)
        {
            StatusMessage = "The Guardian template catalog was not exported: "
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

        draftMode = GuardianTemplateDraftMode.NewMap;
        session = new GuardianSiteTemplateAuthoringSession(
            activeTemplate with
            {
                BackgroundImage = string.Empty,
                ImageOffset = new GuardianMapPoint(0, 0),
                ScaleFactor = 1,
                PointsOfInterest = [],
                DestructiblePanels = [],
                ObeliskGroupNameLocations =
                    new Dictionary<string, GuardianMapPoint>(
                        StringComparer.OrdinalIgnoreCase),
            });
        LoadMetadata(session.Template);
        RefreshDraft(
            "Blank shared-map draft started. Choose a background image before saving.");
        NewPointName = NextPointName(NewPointType);
        NotifyDraftModeChanged();
    }

    public void ImportBackgroundImage(string path)
    {
        if (session is null)
        {
            StatusMessage = "Start or edit a map draft before choosing a background image.";
            return;
        }

        try
        {
            var sourcePath = Path.GetFullPath(path);
            if (!File.Exists(sourcePath))
            {
                StatusMessage = "The selected Guardian map background no longer exists.";
                return;
            }

            Directory.CreateDirectory(ManagedBackgroundDirectory);
            var siteType = SanitizeFileName(activeTemplate?.SiteType ?? "map");
            using var source = File.OpenRead(sourcePath);
            var hash = Convert.ToHexString(SHA256.HashData(source))
                .ToLowerInvariant()[..12];
            var targetPath = Path.Combine(
                ManagedBackgroundDirectory,
                $"{siteType}-{hash}-{Path.GetFileName(sourcePath)}");
            if (!string.Equals(
                    sourcePath,
                    targetPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
            }

            BackgroundImage = targetPath;
            StatusMessage =
                "Copied the background into SrvSurvey's managed Guardian map folder.";
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException)
        {
            StatusMessage = "The Guardian map background could not be imported: "
                + exception.Message;
        }
    }

    private void Edit()
    {
        if (!CanEdit || activeTemplate is null)
        {
            return;
        }

        var selectedName = SelectedPoint?.Name;
        draftMode = GuardianTemplateDraftMode.EditCurrent;
        session = new GuardianSiteTemplateAuthoringSession(activeTemplate);
        LoadMetadata(session.Template);
        RefreshDraft(
            "Existing shared map copied into an editable draft.",
            selectedName);
        NewPointName = NextPointName(NewPointType);
        NotifyDraftModeChanged();
    }

    private bool TryCommitMetadata()
    {
        if (session is null)
        {
            return false;
        }

        if (IsNewMapDraft && string.IsNullOrWhiteSpace(BackgroundImage))
        {
            StatusMessage =
                "Choose a PNG background image before saving a new map draft.";
            return false;
        }

        try
        {
            session.UpdateMetadata(
                TemplateName,
                BackgroundImage,
                new GuardianMapPoint(
                    decimal.ToDouble(ImageOffsetX),
                    decimal.ToDouble(ImageOffsetY)),
                decimal.ToDouble(ScaleFactor));
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            StatusMessage = exception.Message;
            return false;
        }
    }

    private void AddMeasuredPoint()
    {
        if (session is null || liveMeasurement is not { } measurement)
        {
            return;
        }

        try
        {
            var name = string.IsNullOrWhiteSpace(NewPointName)
                ? NextPointName(NewPointType)
                : NewPointName.Trim();
            session.AddPoint(new GuardianPointOfInterest(
                name,
                NewPointType,
                measurement.Angle,
                measurement.Distance,
                NewPointType == GuardianPoiType.Relic
                    ? -1
                    : measurement.Rotation));
            RefreshDraft($"Added measured master-template point {name}.", name);
            NewPointName = NextPointName(NewPointType);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            StatusMessage = exception.Message;
        }
    }

    private void ApplySelectedPoint()
    {
        if (session is null || SelectedPoint is not { } selected)
        {
            return;
        }

        try
        {
            var replacement = new GuardianPointOfInterest(
                PointName.Trim(),
                PointType,
                decimal.ToDouble(PointAngle),
                decimal.ToDouble(PointDistance),
                decimal.ToDouble(PointRotation));
            session.UpdatePoint(selected.Point.Name, replacement);
            RefreshDraft(
                $"Updated master-template point {replacement.Name}.",
                replacement.Name);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            StatusMessage = exception.Message;
        }
    }

    private void RemoveSelectedPoint()
    {
        if (session is null || SelectedPoint is not { } selected)
        {
            return;
        }

        try
        {
            session.RemovePoint(selected.Point.Name);
            RefreshDraft($"Removed master-template point {selected.Point.Name}.");
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void SetGroup()
    {
        try
        {
            session!.SetObeliskGroupLabel(
                GroupName,
                new GuardianMapPoint(
                    decimal.ToDouble(GroupAngle),
                    decimal.ToDouble(GroupDistance)));
            RefreshDraft($"Set obelisk group label {GroupName.Trim()}.");
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            StatusMessage = exception.Message;
        }
    }

    private void RemoveSelectedGroup()
    {
        if (session is null || SelectedGroup is not { } selected)
        {
            return;
        }

        try
        {
            session.RemoveObeliskGroupLabel(selected.Name);
            RefreshDraft($"Removed obelisk group label {selected.Name}.");
        }
        catch (InvalidOperationException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void RequestDiscard()
    {
        IsDiscardConfirmationPending = true;
        StatusMessage = "Confirm to discard the unexported Guardian template draft.";
    }

    private void ConfirmDiscard()
    {
        session = null;
        draftMode = GuardianTemplateDraftMode.None;
        IsDiscardConfirmationPending = false;
        LoadMetadata(activeTemplate);
        RefreshCollections();
        StatusMessage = "The local Guardian template draft was discarded. No file was changed.";
        NotifyDraftChanged(catalogChanged: false);
        NotifyDraftModeChanged();
    }

    private void CancelDiscard()
    {
        IsDiscardConfirmationPending = false;
        StatusMessage = "Template draft discard cancelled.";
    }

    private void RefreshDraft(string message, string? selectedName = null)
    {
        RefreshCollections(selectedName);
        StatusMessage = message;
        NotifyDraftChanged(catalogChanged: false);
    }

    private GuardianSiteTemplate? BuildPreviewTemplate()
    {
        if (session is null)
        {
            return null;
        }

        var template = BuildSelectedPointPreview() ?? session.Template;
        var previewBackground = IsNewMapDraft
            && string.IsNullOrWhiteSpace(BackgroundImage)
                ? "__guardian-map-draft-awaiting-background__.png"
                : BackgroundImage.Trim();
        var previewName = string.IsNullOrWhiteSpace(TemplateName)
            ? template.Name
            : TemplateName.Trim();
        var previewScale = ScaleFactor > 0
            ? decimal.ToDouble(ScaleFactor)
            : template.ScaleFactor;
        return template with
        {
            Name = previewName,
            BackgroundImage = previewBackground,
            ImageOffset = new GuardianMapPoint(
                decimal.ToDouble(ImageOffsetX),
                decimal.ToDouble(ImageOffsetY)),
            ScaleFactor = previewScale,
        };
    }

    private GuardianSiteTemplate? BuildSelectedPointPreview()
    {
        if (session is null || SelectedPoint is not { } selected)
        {
            return session?.Template;
        }

        try
        {
            var preview = new GuardianSiteTemplateAuthoringSession(
                session.Template);
            preview.UpdatePoint(
                selected.Point.Name,
                new GuardianPointOfInterest(
                    PointName.Trim(),
                    PointType,
                    decimal.ToDouble(PointAngle),
                    decimal.ToDouble(PointDistance),
                    decimal.ToDouble(PointRotation)));
            return preview.Template;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return session.Template;
        }
    }

    private void NotifySelectedPointPreviewChanged()
    {
        if (isLoadingSelectedPointFields || session is null)
        {
            return;
        }

        OnPropertyChanged(nameof(PreviewTemplate));
        pointPreviewChanged?.Invoke();
    }

    private void RefreshCollections(string? selectedName = null)
    {
        var template = session?.Template ?? activeTemplate;
        Points = template is null
            ? []
            : template.PointsOfInterest
                .Concat(template.DestructiblePanels)
                .OrderBy(point => point.Name, StringComparer.OrdinalIgnoreCase)
                .Select(point => new GuardianTemplatePointViewModel(point))
                .ToArray();
        Groups = template?.ObeliskGroupNameLocations
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new GuardianTemplateGroupViewModel(
                    pair.Key,
                    pair.Value))
                .ToArray()
            ?? [];
        var selectedPointName = selectedName ?? SelectedPoint?.Name;
        SelectedPoint = selectedPointName is null
            ? null
            : Points.FirstOrDefault(point => string.Equals(
                point.Point.Name,
                selectedPointName,
                StringComparison.OrdinalIgnoreCase));
        SelectedGroup = Groups.Count > 0 ? Groups[0] : null;
        OnPropertyChanged(nameof(PreviewTemplate));
    }

    private void LoadMetadata(GuardianSiteTemplate? template)
    {
        isLoadingMetadataFields = true;
        try
        {
            TemplateName = template?.Name ?? string.Empty;
            BackgroundImage = template?.BackgroundImage ?? string.Empty;
            ImageOffsetX = (decimal)(template?.ImageOffset.X ?? 0);
            ImageOffsetY = (decimal)(template?.ImageOffset.Y ?? 0);
            ScaleFactor = (decimal)(template?.ScaleFactor ?? 1);
        }
        finally
        {
            isLoadingMetadataFields = false;
        }
    }

    private void NotifyMetadataPreviewChanged()
    {
        if (session is null || isLoadingMetadataFields)
        {
            return;
        }

        OnPropertyChanged(nameof(PreviewTemplate));
        draftChanged(false);
    }

    private void NotifyDraftModeChanged()
    {
        OnPropertyChanged(nameof(IsNewMapDraft));
        OnPropertyChanged(nameof(DraftModeTitle));
        OnPropertyChanged(nameof(DraftDescription));
    }

    private string NextPointName(GuardianPoiType type)
    {
        var prefix = type switch
        {
            GuardianPoiType.Relic => "t",
            GuardianPoiType.Pylon => "py",
            GuardianPoiType.Component => "c",
            GuardianPoiType.DestructiblePanel => "d",
            GuardianPoiType.Obelisk or GuardianPoiType.BrokenObelisk => "A",
            _ => "p",
        };
        var names = (session?.Template.PointsOfInterest ?? [])
            .Concat(session?.Template.DestructiblePanels ?? [])
            .Select(point => point.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var index = 1;
        while (true)
        {
            var candidate = prefix == "A"
                ? $"A{index:00}"
                : $"{prefix}{index}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "map" : sanitized;
    }

    private void NotifyDraftChanged(bool catalogChanged)
    {
        OnPropertyChanged(nameof(PreviewTemplate));
        OnPropertyChanged(nameof(IsAuthoring));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanEdit));
        draftChanged(catalogChanged);
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        startCommand.RaiseCanExecuteChanged();
        editCommand.RaiseCanExecuteChanged();
        addMeasuredPointCommand.RaiseCanExecuteChanged();
        applySelectedPointCommand.RaiseCanExecuteChanged();
        removeSelectedPointCommand.RaiseCanExecuteChanged();
        setGroupCommand.RaiseCanExecuteChanged();
        removeSelectedGroupCommand.RaiseCanExecuteChanged();
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

public sealed record GuardianTemplatePointViewModel(
    GuardianPointOfInterest Point)
{
    public string Name => Point.Name;

    public string TypeText => Point.Type.ToString();

    public string GeometryText => $"{Point.Distance:N1} m · {Point.Angle:N1}° · rot {Point.Rotation:N1}°";
}

public sealed record GuardianTemplateGroupViewModel(
    string Name,
    GuardianMapPoint Location)
{
    public string GeometryText => $"{Location.Y:N1} m · {Location.X:N1}°";
}

public enum GuardianTemplateDraftMode
{
    None,
    NewMap,
    EditCurrent,
}
