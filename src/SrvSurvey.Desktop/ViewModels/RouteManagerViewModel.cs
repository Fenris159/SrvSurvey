using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class RouteManagerViewModel : INotifyPropertyChanged
{
    private readonly FollowRouteService routeService;
    private readonly RouteWorkspaceViewModel workspace;
    private readonly AsyncCommand openWorkspaceCommand;
    private readonly AsyncCommand deactivateCommand;
    private readonly AsyncCommand toggleAutoCopyCommand;
    private readonly AsyncCommand openSelectedCommand;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand confirmDeleteCommand;
    private readonly AsyncCommand saveNotesCommand;
    private readonly RelayCommand requestDeleteCommand;
    private readonly RelayCommand cancelDialogCommand;
    private readonly RelayCommand sortNameCommand;
    private readonly RelayCommand sortDateCommand;
    private readonly RelayCommand selectAllCommand;
    private readonly RelayCommand clearSelectionCommand;
    private string? frontierId;
    private bool isBusy;
    private bool isRefreshing;
    private bool refreshPending;
    private bool favoritesFirst = true;
    private RouteManagerSortColumn sortColumn = RouteManagerSortColumn.Name;
    private bool sortAscending = true;
    private bool isDeleteConfirmationVisible;
    private bool isNotesVisible;
    private RouteManagerItemViewModel? editingRoute;
    private string notesDraft = string.Empty;
    private string statusMessage = "Waiting for a commander profile.";

    public RouteManagerViewModel(
        FollowRouteService routeService,
        RouteWorkspaceViewModel workspace)
    {
        this.routeService = routeService
            ?? throw new ArgumentNullException(nameof(routeService));
        this.workspace = workspace
            ?? throw new ArgumentNullException(nameof(workspace));
        openWorkspaceCommand = new AsyncCommand(
            OpenWorkspaceAsync,
            () => HasProfile && !IsBusy);
        deactivateCommand = new AsyncCommand(
            DeactivateAsync,
            () => CanDeactivate && !IsDialogVisible);
        toggleAutoCopyCommand = new AsyncCommand(
            ToggleAutoCopyAsync,
            () => CanToggleAutoCopy && !IsDialogVisible);
        openSelectedCommand = new AsyncCommand(
            OpenSelectedAsync,
            () => HasSingleSelection && !IsBusy);
        refreshCommand = new AsyncCommand(
            RefreshAsync,
            () => HasProfile && !IsBusy);
        requestDeleteCommand = new RelayCommand(
            RequestDelete,
            () => HasSelection && !IsBusy && !IsDialogVisible);
        confirmDeleteCommand = new AsyncCommand(
            ConfirmDeleteAsync,
            () => HasSelection && !IsBusy && IsDeleteConfirmationVisible);
        cancelDialogCommand = new RelayCommand(
            CloseDialogs,
            () => IsDialogVisible);
        saveNotesCommand = new AsyncCommand(
            SaveNotesAsync,
            () => EditingRoute is not null && !IsBusy && IsNotesVisible);
        sortNameCommand = new RelayCommand(SortByName, () => !IsBusy);
        sortDateCommand = new RelayCommand(SortByDate, () => !IsBusy);
        selectAllCommand = new RelayCommand(
            SelectAll,
            () => HasRoutes && !IsBusy);
        clearSelectionCommand = new RelayCommand(
            ClearSelection,
            () => HasSelection && !IsBusy);
        workspace.CatalogChanged += OnWorkspaceCatalogChanged;
        workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RouteManagerItemViewModel> Routes { get; } = [];

    public RouteWorkspaceViewModel Workspace => workspace;

    public bool IsFleetCarrierManager => workspace.IsFleetCarrierWorkspace;

    public string PanelTitle => IsFleetCarrierManager ? "FC Routes" : "Route Manager";

    public string PanelDescription => IsFleetCarrierManager
        ? "Open the FC route workspace and organize this commander's saved fleet-carrier routes."
        : "Open the route workspace and organize this commander's saved route files.";

    public string OpenWorkspaceLabel => IsFleetCarrierManager
        ? "Open FC Route Workspace"
        : "Open Route Workspace";

    public string CurrentRouteLabel => IsFleetCarrierManager
        ? "CURRENT FC ROUTE"
        : "CURRENT ROUTE";

    public string EmptyLibraryMessage => IsFleetCarrierManager
        ? "Import a fleet-carrier route or create one in FC Route Workspace."
        : "Import a JSON route or create one in Route Workspace.";

    public bool HasProfile => !string.IsNullOrWhiteSpace(frontierId);

    public bool HasRoutes => Routes.Count > 0;

    public int SelectedCount => Routes.Count(route => route.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public bool HasSingleSelection => SelectedCount == 1;

    public bool CanImport => HasProfile && !IsBusy && !IsDialogVisible;

    public bool CanExport => HasSelection && !IsBusy && !IsDialogVisible;

    public bool CanDeactivate => workspace.HasSavedRoute
        && !workspace.IsBusy
        && !IsBusy;

    public bool AutoCopy => workspace.AutoCopy;

    public bool CanToggleAutoCopy => workspace.HasSavedRoute
        && !workspace.IsBusy
        && !IsBusy;

    public string SelectionSummary => SelectedCount == 0
        ? $"{Routes.Count:N0} saved route{(Routes.Count == 1 ? string.Empty : "s")}"
        : $"{SelectedCount:N0} of {Routes.Count:N0} selected";

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetField(ref isBusy, value))
            {
                RaiseState();
            }
        }
    }

    public bool FavoritesFirst
    {
        get => favoritesFirst;
        set
        {
            if (SetField(ref favoritesFirst, value))
            {
                ReorderRoutes();
            }
        }
    }

    public string NameSortIndicator => sortColumn == RouteManagerSortColumn.Name
        ? sortAscending ? "\u25B2" : "\u25BC"
        : string.Empty;

    public string DateSortIndicator => sortColumn == RouteManagerSortColumn.Created
        ? sortAscending ? "\u25B2" : "\u25BC"
        : string.Empty;

    public bool IsDeleteConfirmationVisible
    {
        get => isDeleteConfirmationVisible;
        private set
        {
            if (SetField(ref isDeleteConfirmationVisible, value))
            {
                OnPropertyChanged(nameof(IsDialogVisible));
                RaiseCommands();
            }
        }
    }

    public bool IsNotesVisible
    {
        get => isNotesVisible;
        private set
        {
            if (SetField(ref isNotesVisible, value))
            {
                OnPropertyChanged(nameof(IsDialogVisible));
                RaiseCommands();
            }
        }
    }

    public bool IsDialogVisible => IsDeleteConfirmationVisible || IsNotesVisible;

    public RouteManagerItemViewModel? EditingRoute
    {
        get => editingRoute;
        private set => SetField(ref editingRoute, value);
    }

    public string NotesDraft
    {
        get => notesDraft;
        set => SetField(ref notesDraft, value);
    }

    public string DeleteConfirmationText => SelectedCount == 1
        ? $"Delete '{Routes.Single(route => route.IsSelected).Name}'? The route will be moved to recovery storage."
        : $"Delete {SelectedCount:N0} selected routes? They will be moved to recovery storage.";

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public ICommand OpenWorkspaceCommand => openWorkspaceCommand;

    public ICommand DeactivateCommand => deactivateCommand;

    public ICommand ToggleAutoCopyCommand => toggleAutoCopyCommand;

    public ICommand OpenSelectedCommand => openSelectedCommand;

    public ICommand RefreshCommand => refreshCommand;

    public ICommand RequestDeleteCommand => requestDeleteCommand;

    public ICommand ConfirmDeleteCommand => confirmDeleteCommand;

    public ICommand CancelDialogCommand => cancelDialogCommand;

    public ICommand SaveNotesCommand => saveNotesCommand;

    public ICommand SortNameCommand => sortNameCommand;

    public ICommand SortDateCommand => sortDateCommand;

    public ICommand SelectAllCommand => selectAllCommand;

    public ICommand ClearSelectionCommand => clearSelectionCommand;

    public async Task UpdateContextAsync(string? nextFrontierId)
    {
        var normalized = string.IsNullOrWhiteSpace(nextFrontierId)
            ? null
            : nextFrontierId.Trim();
        if (string.Equals(
            frontierId,
            normalized,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        frontierId = normalized;
        OnPropertyChanged(nameof(HasProfile));
        CloseDialogs();
        if (frontierId is null)
        {
            Routes.Clear();
            StatusMessage = "Waiting for a commander profile.";
            RaiseState();
            return;
        }

        await RefreshAsync();
    }

    public async Task ImportAsync(IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        if (frontierId is null || filePaths.Count == 0 || !CanImport)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var imported = 0;
            foreach (var path in filePaths.Distinct(PathComparer))
            {
                await routeService.ImportAsync(frontierId, path);
                imported++;
            }

            await RefreshCoreAsync();
            StatusMessage = $"Imported {imported:N0} route file{(imported == 1 ? string.Empty : "s")}.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            await RefreshCoreAsync();
            StatusMessage = "The route import could not be completed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportSelectedAsync(string destinationDirectory)
    {
        if (frontierId is null || !CanExport)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var selected = Routes
                .Where(route => route.IsSelected)
                .Select(route => route.ToCatalogEntry())
                .ToArray();
            var exported = await routeService.ExportAsync(
                frontierId,
                selected,
                destinationDirectory);
            StatusMessage = $"Exported {exported.Count:N0} route file{(exported.Count == 1 ? string.Empty : "s")} to {Path.GetFullPath(destinationDirectory)}.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The selected routes could not be exported: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ReportFilePickerError(string operation, Exception exception)
    {
        StatusMessage = $"The route {operation} picker was unavailable: "
            + exception.Message;
    }

    private async Task OpenWorkspaceAsync()
    {
        await workspace.OpenWorkspaceAsync();
    }

    public async Task ActivateAsync(RouteManagerItemViewModel route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (frontierId is null || IsBusy || workspace.IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await workspace.ActivateSavedRouteAsync(
                route.FileName,
                route.IsLegacy,
                route.FilePath);
            StatusMessage = workspace.StatusMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeactivateAsync()
    {
        if (!CanDeactivate)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await workspace.DeactivateCurrentRouteAsync();
            StatusMessage = workspace.StatusMessage;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ToggleAutoCopyAsync()
    {
        if (!CanToggleAutoCopy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await workspace.SetAutoCopyAsync(!workspace.AutoCopy);
            StatusMessage = workspace.StatusMessage;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(AutoCopy));
        }
    }

    private async Task OpenSelectedAsync()
    {
        var selected = Routes.SingleOrDefault(route => route.IsSelected);
        if (selected is null)
        {
            return;
        }

        await workspace.LoadSavedRouteAsync(
            selected.FileName,
            selected.IsLegacy);
        await workspace.OpenWorkspaceAsync();
    }

    public async Task RefreshAsync()
    {
        if (frontierId is null)
        {
            return;
        }

        if (isRefreshing)
        {
            refreshPending = true;
            return;
        }

        try
        {
            isRefreshing = true;
            IsBusy = true;
            do
            {
                refreshPending = false;
                await RefreshCoreAsync();
            }
            while (refreshPending);

            StatusMessage = Routes.Count == 0
                ? "No saved routes. Import a JSON route or create one in the workspace."
                : $"Loaded {Routes.Count:N0} saved route{(Routes.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The route library could not be refreshed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
            isRefreshing = false;
        }
    }

    private async Task RefreshCoreAsync()
    {
        if (frontierId is null)
        {
            return;
        }

        var entries = await routeService.ListAsync(frontierId);
        var existing = Routes.ToDictionary(
            route => route.FilePath,
            PathComparer);
        var seen = new HashSet<string>(PathComparer);
        foreach (var entry in entries)
        {
            seen.Add(entry.FilePath);
            if (existing.TryGetValue(entry.FilePath, out var row))
            {
                row.Update(entry);
            }
            else
            {
                Routes.Add(new RouteManagerItemViewModel(
                    entry,
                    OnSelectionChanged,
                    ToggleFavoriteAsync,
                    OpenNotes,
                    ActivateAsync));
            }
        }

        for (var index = Routes.Count - 1; index >= 0; index--)
        {
            if (!seen.Contains(Routes[index].FilePath))
            {
                Routes.RemoveAt(index);
            }
        }

        ReorderRoutes();
        RaiseState();
    }

    public async Task ToggleFavoriteAsync(RouteManagerItemViewModel route)
    {
        if (frontierId is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var saved = await routeService.SetFavoriteAsync(
                frontierId,
                route.FileName,
                route.IsLegacy,
                !route.IsFavorite);
            route.SetFavorite(saved.IsFavorite);
            workspace.ApplyExternalFavorite(route.FilePath, saved.IsFavorite);
            ReorderRoutes();
            StatusMessage = saved.IsFavorite
                ? $"Added {route.Name} to favorites."
                : $"Removed {route.Name} from favorites.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The favorite could not be updated: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenNotes(RouteManagerItemViewModel route)
    {
        EditingRoute = route;
        NotesDraft = route.Notes ?? string.Empty;
        IsNotesVisible = true;
    }

    public async Task SaveNotesAsync()
    {
        if (frontierId is null || EditingRoute is not { } route)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var saved = await routeService.SaveNotesAsync(
                frontierId,
                route.FileName,
                route.IsLegacy,
                NotesDraft);
            route.SetNotes(saved.Notes);
            workspace.ApplyExternalNotes(route.FilePath, saved.Notes);
            StatusMessage = $"Saved notes for {route.Name}.";
            CloseDialogs();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The route notes could not be saved: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RequestDelete()
    {
        OnPropertyChanged(nameof(DeleteConfirmationText));
        IsDeleteConfirmationVisible = true;
    }

    public async Task ConfirmDeleteAsync()
    {
        if (frontierId is null)
        {
            return;
        }

        var selected = Routes.Where(route => route.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        CloseDialogs();
        try
        {
            IsBusy = true;
            var deletedLoadedRoute = selected.Any(route =>
                workspace.IsLoadedSavedRoute(route.FilePath));
            foreach (var route in selected)
            {
                await routeService.DeleteNamedAsync(
                    frontierId,
                    route.FileName,
                    route.IsLegacy);
            }

            if (deletedLoadedRoute)
            {
                await workspace.HandleLoadedRouteDeletedAsync();
            }

            await RefreshCoreAsync();
            StatusMessage = $"Moved {selected.Length:N0} route file{(selected.Length == 1 ? string.Empty : "s")} to recovery storage.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            await RefreshCoreAsync();
            StatusMessage = "The selected routes could not all be deleted: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CloseDialogs()
    {
        IsDeleteConfirmationVisible = false;
        IsNotesVisible = false;
        EditingRoute = null;
        NotesDraft = string.Empty;
    }

    private void SortByName()
    {
        if (sortColumn == RouteManagerSortColumn.Name)
        {
            sortAscending = !sortAscending;
        }
        else
        {
            sortColumn = RouteManagerSortColumn.Name;
            sortAscending = true;
        }

        RaiseSortProperties();
        ReorderRoutes();
    }

    private void SortByDate()
    {
        if (sortColumn == RouteManagerSortColumn.Created)
        {
            sortAscending = !sortAscending;
        }
        else
        {
            sortColumn = RouteManagerSortColumn.Created;
            sortAscending = false;
        }

        RaiseSortProperties();
        ReorderRoutes();
    }

    private void ReorderRoutes()
    {
        IEnumerable<RouteManagerItemViewModel> source = Routes;
        IOrderedEnumerable<RouteManagerItemViewModel>? sorted = null;
        if (FavoritesFirst)
        {
            sorted = source.OrderByDescending(route => route.IsFavorite);
        }

        Func<RouteManagerItemViewModel, object> keySelector = sortColumn switch
        {
            RouteManagerSortColumn.Created => route => route.CreatedAt,
            _ => route => route.Name,
        };
        IOrderedEnumerable<RouteManagerItemViewModel> ordered = sortAscending
            ? sorted?.ThenBy(keySelector, RouteManagerSortComparer.Instance)
                ?? source.OrderBy(keySelector, RouteManagerSortComparer.Instance)
            : sorted?.ThenByDescending(keySelector, RouteManagerSortComparer.Instance)
                ?? source.OrderByDescending(keySelector, RouteManagerSortComparer.Instance);
        var target = ordered
            .ThenBy(route => route.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < target.Length; index++)
        {
            var currentIndex = Routes.IndexOf(target[index]);
            if (currentIndex != index)
            {
                Routes.Move(currentIndex, index);
            }
        }
    }

    private void SelectAll()
    {
        foreach (var route in Routes)
        {
            route.IsSelected = true;
        }
    }

    private void ClearSelection()
    {
        foreach (var route in Routes)
        {
            route.IsSelected = false;
        }
    }

    private void OnSelectionChanged()
    {
        RaiseState();
    }

    private async void OnWorkspaceCatalogChanged(object? sender, EventArgs eventArgs)
    {
        // Route-manager operations already reconcile their own results. Ignoring
        // a nested workspace notification prevents a second refresh from racing
        // the active file operation and rebuilding UI state out of order.
        if (IsBusy)
        {
            return;
        }

        await RefreshAsync();
    }

    private void OnWorkspacePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(RouteWorkspaceViewModel.AutoCopy))
        {
            OnPropertyChanged(nameof(AutoCopy));
        }

        if (eventArgs.PropertyName is nameof(RouteWorkspaceViewModel.HasSavedRoute)
            or nameof(RouteWorkspaceViewModel.IsBusy))
        {
            OnPropertyChanged(nameof(CanDeactivate));
            OnPropertyChanged(nameof(CanToggleAutoCopy));
            deactivateCommand.RaiseCanExecuteChanged();
            toggleAutoCopyCommand.RaiseCanExecuteChanged();
        }
    }

    private void RaiseSortProperties()
    {
        OnPropertyChanged(nameof(NameSortIndicator));
        OnPropertyChanged(nameof(DateSortIndicator));
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(HasRoutes));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanDeactivate));
        OnPropertyChanged(nameof(CanToggleAutoCopy));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(DeleteConfirmationText));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        openWorkspaceCommand.RaiseCanExecuteChanged();
        deactivateCommand.RaiseCanExecuteChanged();
        toggleAutoCopyCommand.RaiseCanExecuteChanged();
        openSelectedCommand.RaiseCanExecuteChanged();
        refreshCommand.RaiseCanExecuteChanged();
        confirmDeleteCommand.RaiseCanExecuteChanged();
        saveNotesCommand.RaiseCanExecuteChanged();
        requestDeleteCommand.RaiseCanExecuteChanged();
        cancelDialogCommand.RaiseCanExecuteChanged();
        sortNameCommand.RaiseCanExecuteChanged();
        sortDateCommand.RaiseCanExecuteChanged();
        selectAllCommand.RaiseCanExecuteChanged();
        clearSelectionCommand.RaiseCanExecuteChanged();
        foreach (var route in Routes)
        {
            route.RaiseCanExecuteChanged();
        }
    }

    private static bool IsExpectedException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException;
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

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed class AsyncCommand(
        Func<Task> execute,
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

    private sealed class RelayCommand(
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

    private sealed class RouteManagerSortComparer : IComparer<object>
    {
        public static RouteManagerSortComparer Instance { get; } = new();

        public int Compare(object? first, object? second)
        {
            if (first is DateTimeOffset firstDate
                && second is DateTimeOffset secondDate)
            {
                return firstDate.CompareTo(secondDate);
            }

            return StringComparer.OrdinalIgnoreCase.Compare(
                first?.ToString(),
                second?.ToString());
        }
    }
}

public sealed class RouteManagerItemViewModel : INotifyPropertyChanged
{
    private readonly Action selectionChanged;
    private readonly Func<RouteManagerItemViewModel, Task> toggleFavorite;
    private readonly Action<RouteManagerItemViewModel> editNotes;
    private readonly Func<RouteManagerItemViewModel, Task> activate;
    private readonly AsyncCommand toggleFavoriteCommand;
    private readonly RelayCommand editNotesCommand;
    private readonly AsyncCommand activateCommand;
    private string name;
    private string fileName;
    private string filePath;
    private bool isLegacy;
    private DateTimeOffset lastModified;
    private DateTimeOffset createdAt;
    private string? notes;
    private bool isFavorite;
    private bool isSelected;

    public RouteManagerItemViewModel(
        FollowRouteCatalogEntry entry,
        Action selectionChanged,
        Func<RouteManagerItemViewModel, Task> toggleFavorite,
        Action<RouteManagerItemViewModel> editNotes,
        Func<RouteManagerItemViewModel, Task> activate)
    {
        this.selectionChanged = selectionChanged;
        this.toggleFavorite = toggleFavorite;
        this.editNotes = editNotes;
        this.activate = activate;
        name = entry.Name;
        fileName = entry.FileName;
        filePath = entry.FilePath;
        isLegacy = entry.IsLegacy;
        lastModified = entry.LastModified;
        createdAt = entry.CreatedAt;
        notes = entry.Notes;
        isFavorite = entry.IsFavorite;
        toggleFavoriteCommand = new AsyncCommand(
            () => this.toggleFavorite(this));
        editNotesCommand = new RelayCommand(() => this.editNotes(this));
        activateCommand = new AsyncCommand(() => this.activate(this));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name => name;

    public string FileName => fileName;

    public string FilePath => filePath;

    public bool IsLegacy => isLegacy;

    public DateTimeOffset LastModified => lastModified;

    public DateTimeOffset CreatedAt => createdAt;

    public string CreatedAtText => CreatedAt.LocalDateTime.ToString("g");

    public string? Notes => notes;

    public string NotesPreview => string.IsNullOrWhiteSpace(Notes)
        ? "No notes"
        : string.Join(
            " ",
            Notes.Split(
                ['\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));

    public bool IsFavorite => isFavorite;

    public string FavoriteGlyph => IsFavorite ? "\u2605" : "\u2606";

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetField(ref isSelected, value))
            {
                selectionChanged();
            }
        }
    }

    public ICommand ToggleFavoriteCommand => toggleFavoriteCommand;

    public ICommand EditNotesCommand => editNotesCommand;

    public ICommand ActivateCommand => activateCommand;

    public FollowRouteCatalogEntry ToCatalogEntry()
    {
        return new FollowRouteCatalogEntry(
            Name,
            FileName,
            FilePath,
            IsLegacy,
            LastModified,
            CreatedAt,
            Notes,
            IsFavorite);
    }

    public void Update(FollowRouteCatalogEntry entry)
    {
        SetField(ref name, entry.Name, nameof(Name));
        SetField(ref fileName, entry.FileName, nameof(FileName));
        SetField(ref filePath, entry.FilePath, nameof(FilePath));
        SetField(ref isLegacy, entry.IsLegacy, nameof(IsLegacy));
        SetField(ref lastModified, entry.LastModified, nameof(LastModified));
        if (SetField(ref createdAt, entry.CreatedAt, nameof(CreatedAt)))
        {
            OnPropertyChanged(nameof(CreatedAtText));
        }

        SetNotes(entry.Notes);
        SetFavorite(entry.IsFavorite);
    }

    public void SetNotes(string? value)
    {
        if (SetField(ref notes, value, nameof(Notes)))
        {
            OnPropertyChanged(nameof(NotesPreview));
        }
    }

    public void SetFavorite(bool value)
    {
        if (SetField(ref isFavorite, value, nameof(IsFavorite)))
        {
            OnPropertyChanged(nameof(FavoriteGlyph));
        }
    }

    public void RaiseCanExecuteChanged()
    {
        toggleFavoriteCommand.RaiseCanExecuteChanged();
        editNotesCommand.RaiseCanExecuteChanged();
        activateCommand.RaiseCanExecuteChanged();
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

    private sealed class AsyncCommand(Func<Task> execute) : ICommand
    {
        private bool isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !isExecuting;
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

    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return true;
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
}

public enum RouteManagerSortColumn
{
    Name,
    Created,
}
