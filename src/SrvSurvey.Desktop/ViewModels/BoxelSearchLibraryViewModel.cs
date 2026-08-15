using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class BoxelSearchLibraryViewModel : INotifyPropertyChanged
{
    private readonly BoxelSearchViewModel boxelSearch;
    private readonly AsyncCommand refreshCommand;
    private readonly AsyncCommand openSelectedCommand;
    private readonly AsyncCommand confirmDeleteCommand;
    private readonly AsyncCommand saveRenameCommand;
    private readonly AsyncCommand saveNotesCommand;
    private readonly RelayCommand requestDeleteCommand;
    private readonly RelayCommand cancelDialogCommand;
    private readonly RelayCommand sortNameCommand;
    private readonly RelayCommand sortDateCommand;
    private readonly RelayCommand sortModifiedCommand;
    private readonly RelayCommand sortProgressCommand;
    private bool isBusy;
    private bool favoritesFirst = true;
    private bool isDeleteConfirmationVisible;
    private bool isRenameVisible;
    private bool isNotesVisible;
    private BoxelSearchLibraryItemViewModel? editingSearch;
    private string renameDraft = string.Empty;
    private string notesDraft = string.Empty;
    private string statusMessage = "Loading saved boxel searches…";
    private BoxelSearchLibrarySortColumn sortColumn =
        BoxelSearchLibrarySortColumn.Name;
    private bool sortAscending = true;

    public BoxelSearchLibraryViewModel(BoxelSearchViewModel boxelSearch)
    {
        this.boxelSearch = boxelSearch
            ?? throw new ArgumentNullException(nameof(boxelSearch));
        refreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        openSelectedCommand = new AsyncCommand(
            OpenSelectedAsync,
            () => SelectedSearch is not null && !IsBusy && !IsDialogVisible);
        requestDeleteCommand = new RelayCommand(
            RequestDelete,
            () => SelectedSearch is not null && !IsBusy && !IsDialogVisible);
        confirmDeleteCommand = new AsyncCommand(
            ConfirmDeleteAsync,
            () => SelectedSearch is not null
                && IsDeleteConfirmationVisible
                && !IsBusy);
        saveRenameCommand = new AsyncCommand(
            SaveRenameAsync,
            () => EditingSearch is not null
                && !string.IsNullOrWhiteSpace(RenameDraft)
                && IsRenameVisible
                && !IsBusy);
        saveNotesCommand = new AsyncCommand(
            SaveNotesAsync,
            () => EditingSearch is not null && IsNotesVisible && !IsBusy);
        cancelDialogCommand = new RelayCommand(
            CloseDialogs,
            () => IsDialogVisible);
        sortNameCommand = new RelayCommand(SortByName, () => !IsBusy);
        sortDateCommand = new RelayCommand(SortByDate, () => !IsBusy);
        sortModifiedCommand = new RelayCommand(SortByModified, () => !IsBusy);
        sortProgressCommand = new RelayCommand(SortByProgress, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? SearchOpened;

    public event EventHandler<BoxelSurveyStatsFocusRequest>? StatisticsRequested;

    public ObservableCollection<BoxelSearchLibraryItemViewModel> Searches { get; } = [];

    public BoxelSearchLibraryItemViewModel? SelectedSearch =>
        Searches.FirstOrDefault(search => search.IsSelected);

    public bool HasSearches => Searches.Count > 0;

    public bool HasSelection => SelectedSearch is not null;

    public string SelectionSummary =>
        $"{Searches.Count:N0} saved search{(Searches.Count == 1 ? string.Empty : "es")}";

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
                Reorder();
            }
        }
    }

    public bool IsDeleteConfirmationVisible
    {
        get => isDeleteConfirmationVisible;
        private set => SetDialogField(ref isDeleteConfirmationVisible, value);
    }

    public bool IsRenameVisible
    {
        get => isRenameVisible;
        private set => SetDialogField(ref isRenameVisible, value);
    }

    public bool IsNotesVisible
    {
        get => isNotesVisible;
        private set => SetDialogField(ref isNotesVisible, value);
    }

    public bool IsDialogVisible => IsDeleteConfirmationVisible
        || IsRenameVisible
        || IsNotesVisible;

    public BoxelSearchLibraryItemViewModel? EditingSearch
    {
        get => editingSearch;
        private set
        {
            if (SetField(ref editingSearch, value))
            {
                OnPropertyChanged(nameof(EditingSearchName));
            }
        }
    }

    public string EditingSearchName => EditingSearch?.Name ?? string.Empty;

    public string RenameDraft
    {
        get => renameDraft;
        set
        {
            if (SetField(ref renameDraft, value))
            {
                saveRenameCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NotesDraft
    {
        get => notesDraft;
        set => SetField(ref notesDraft, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public string DeleteConfirmationText => SelectedSearch is { } selected
        ? $"Delete '{selected.Name}'? It will be moved to recovery storage."
        : "Delete the selected saved search?";

    public string NameSortIndicator => GetSortIndicator(
        BoxelSearchLibrarySortColumn.Name);

    public string DateSortIndicator => GetSortIndicator(
        BoxelSearchLibrarySortColumn.Created);

    public string ModifiedSortIndicator => GetSortIndicator(
        BoxelSearchLibrarySortColumn.Modified);

    public string ProgressSortIndicator => GetSortIndicator(
        BoxelSearchLibrarySortColumn.Progress);

    public ICommand RefreshCommand => refreshCommand;

    public ICommand OpenSelectedCommand => openSelectedCommand;

    public ICommand RequestDeleteCommand => requestDeleteCommand;

    public ICommand ConfirmDeleteCommand => confirmDeleteCommand;

    public ICommand SaveRenameCommand => saveRenameCommand;

    public ICommand SaveNotesCommand => saveNotesCommand;

    public ICommand CancelDialogCommand => cancelDialogCommand;

    public ICommand SortNameCommand => sortNameCommand;

    public ICommand SortDateCommand => sortDateCommand;

    public ICommand SortModifiedCommand => sortModifiedCommand;

    public ICommand SortProgressCommand => sortProgressCommand;

    public async Task RefreshAsync()
    {
        try
        {
            IsBusy = true;
            var entries = await boxelSearch.ListSavedSearchesAsync();
            var knownPrefixes = boxelSearch.SurveyStats?.Index
                .Select(entry => entry.Prefix)
                .ToHashSet(StringComparer.Ordinal)
                ?? [];
            Searches.Clear();
            foreach (var entry in entries)
            {
                var item = new BoxelSearchLibraryItemViewModel(
                    entry,
                    SelectOnly,
                    ToggleFavoriteAsync,
                    OpenRename,
                    OpenNotes,
                    OpenStatistics);
                item.SetCanOpenStatistics(
                    entry.Prefixes.Any(knownPrefixes.Contains));
                Searches.Add(item);
            }

            Reorder();
            StatusMessage = GetLoadedSearchStatus(Searches.Count);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The saved boxel searches could not be loaded: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseState();
        }
    }

    private async Task OpenSelectedAsync()
    {
        if (SelectedSearch is not { } selected)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await boxelSearch.ResumeSavedSearchAsync(selected.FileName);
            StatusMessage = $"Opened {selected.Name}.";
            SearchOpened?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The saved boxel search could not be opened: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleFavoriteAsync(BoxelSearchLibraryItemViewModel search)
    {
        try
        {
            IsBusy = true;
            var saved = await boxelSearch.SetSavedSearchFavoriteAsync(
                search.FileName,
                !search.IsFavorite);
            search.SetFavorite(saved.IsFavorite);
            search.SetUpdatedAt(saved.UpdatedAt);
            Reorder();
            StatusMessage = saved.IsFavorite
                ? $"Added {search.Name} to favorites."
                : $"Removed {search.Name} from favorites.";
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

    private void OpenStatistics(BoxelSearchLibraryItemViewModel search)
    {
        if (!search.CanOpenStatistics)
        {
            return;
        }

        StatisticsRequested?.Invoke(
            this,
            new BoxelSurveyStatsFocusRequest(search.Prefixes, search.LowMassCode));
    }

    private void OpenRename(BoxelSearchLibraryItemViewModel search)
    {
        EditingSearch = search;
        RenameDraft = search.Name;
        IsRenameVisible = true;
    }

    private void OpenNotes(BoxelSearchLibraryItemViewModel search)
    {
        EditingSearch = search;
        NotesDraft = search.Notes ?? string.Empty;
        IsNotesVisible = true;
    }

    private async Task SaveRenameAsync()
    {
        if (EditingSearch is not { } search)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var saved = await boxelSearch.RenameSavedSearchAsync(
                search.FileName,
                RenameDraft);
            search.SetName(saved.Name);
            search.SetUpdatedAt(saved.UpdatedAt);
            CloseDialogs();
            Reorder();
            StatusMessage = $"Renamed saved search to {saved.Name}.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The saved search could not be renamed: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveNotesAsync()
    {
        if (EditingSearch is not { } search)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var saved = await boxelSearch.SaveSavedSearchNotesAsync(
                search.FileName,
                NotesDraft);
            search.SetNotes(saved.Notes);
            search.SetUpdatedAt(saved.UpdatedAt);
            CloseDialogs();
            StatusMessage = $"Saved notes for {search.Name}.";
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The notes could not be saved: "
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

    private async Task ConfirmDeleteAsync()
    {
        if (SelectedSearch is not { } selected)
        {
            return;
        }

        CloseDialogs();
        try
        {
            IsBusy = true;
            await boxelSearch.DeleteSavedSearchAsync(selected.FileName);
            Searches.Remove(selected);
            StatusMessage = $"Moved {selected.Name} to recovery storage.";
            RaiseState();
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            StatusMessage = "The saved search could not be deleted: "
                + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SelectOnly(BoxelSearchLibraryItemViewModel selected)
    {
        if (selected.IsSelected)
        {
            foreach (var search in Searches.Where(search => !ReferenceEquals(search, selected)))
            {
                search.SetSelected(false);
            }
        }

        RaiseState();
    }

    private void SortByName()
    {
        ApplySort(BoxelSearchLibrarySortColumn.Name, defaultAscending: true);
    }

    private void SortByDate()
    {
        ApplySort(BoxelSearchLibrarySortColumn.Created, defaultAscending: false);
    }

    private void SortByModified()
    {
        ApplySort(BoxelSearchLibrarySortColumn.Modified, defaultAscending: false);
    }

    private void SortByProgress()
    {
        ApplySort(BoxelSearchLibrarySortColumn.Progress, defaultAscending: false);
    }

    private void ApplySort(
        BoxelSearchLibrarySortColumn column,
        bool defaultAscending)
    {
        if (sortColumn == column)
        {
            sortAscending = !sortAscending;
        }
        else
        {
            sortColumn = column;
            sortAscending = defaultAscending;
        }

        OnPropertyChanged(nameof(NameSortIndicator));
        OnPropertyChanged(nameof(DateSortIndicator));
        OnPropertyChanged(nameof(ModifiedSortIndicator));
        OnPropertyChanged(nameof(ProgressSortIndicator));
        Reorder();
    }

    private string GetSortIndicator(BoxelSearchLibrarySortColumn column)
    {
        if (sortColumn != column)
        {
            return string.Empty;
        }

        return sortAscending ? "▲" : "▼";
    }

    private static string GetLoadedSearchStatus(int count)
    {
        if (count == 0)
        {
            return "No saved boxel searches yet.";
        }

        var pluralSuffix = count == 1 ? string.Empty : "es";
        return $"Loaded {count:N0} saved boxel search{pluralSuffix}.";
    }

    private void Reorder()
    {
        IEnumerable<BoxelSearchLibraryItemViewModel> source = Searches;
        IOrderedEnumerable<BoxelSearchLibraryItemViewModel>? favorites = null;
        if (FavoritesFirst)
        {
            favorites = source.OrderByDescending(search => search.IsFavorite);
        }

        Func<BoxelSearchLibraryItemViewModel, object> key = sortColumn switch
        {
            BoxelSearchLibrarySortColumn.Created => search => search.CreatedAt,
            BoxelSearchLibrarySortColumn.Modified => search => search.UpdatedAt,
            BoxelSearchLibrarySortColumn.Progress => search => search.ProgressFraction,
            _ => search => search.Name,
        };
        var ordered = sortAscending
            ? favorites?.ThenBy(key) ?? source.OrderBy(key)
            : favorites?.ThenByDescending(key) ?? source.OrderByDescending(key);
        var target = ordered
            .ThenBy(search => search.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var index = 0; index < target.Length; index++)
        {
            var currentIndex = Searches.IndexOf(target[index]);
            if (currentIndex != index)
            {
                Searches.Move(currentIndex, index);
            }
        }

        RaiseState();
    }

    private void CloseDialogs()
    {
        IsDeleteConfirmationVisible = false;
        IsRenameVisible = false;
        IsNotesVisible = false;
        EditingSearch = null;
        RenameDraft = string.Empty;
        NotesDraft = string.Empty;
    }

    private void SetDialogField(ref bool field, bool value)
    {
        if (SetField(ref field, value))
        {
            OnPropertyChanged(nameof(IsDialogVisible));
            RaiseCommands();
        }
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(HasSearches));
        OnPropertyChanged(nameof(SelectedSearch));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(DeleteConfirmationText));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        refreshCommand.RaiseCanExecuteChanged();
        openSelectedCommand.RaiseCanExecuteChanged();
        requestDeleteCommand.RaiseCanExecuteChanged();
        confirmDeleteCommand.RaiseCanExecuteChanged();
        saveRenameCommand.RaiseCanExecuteChanged();
        saveNotesCommand.RaiseCanExecuteChanged();
        cancelDialogCommand.RaiseCanExecuteChanged();
        sortNameCommand.RaiseCanExecuteChanged();
        sortDateCommand.RaiseCanExecuteChanged();
        sortModifiedCommand.RaiseCanExecuteChanged();
        sortProgressCommand.RaiseCanExecuteChanged();
        foreach (var search in Searches)
        {
            search.RaiseCanExecuteChanged();
        }
    }

    private static bool IsExpectedException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException
            or System.Text.Json.JsonException;
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

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        private bool isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !isExecuting && canExecute();

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

    private sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
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

public sealed class BoxelSearchLibraryItemViewModel : INotifyPropertyChanged
{
    private readonly Action<BoxelSearchLibraryItemViewModel> selectionChanged;
    private bool isSelected;
    private string name;
    private string? notes;
    private bool isFavorite;
    private DateTimeOffset updatedAt;
    private bool canOpenStatistics;

    public BoxelSearchLibraryItemViewModel(
        SavedBoxelSearchCatalogEntry entry,
        Action<BoxelSearchLibraryItemViewModel> selectionChanged,
        Func<BoxelSearchLibraryItemViewModel, Task> toggleFavorite,
        Action<BoxelSearchLibraryItemViewModel> rename,
        Action<BoxelSearchLibraryItemViewModel> editNotes,
        Action<BoxelSearchLibraryItemViewModel>? openStatistics = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        this.selectionChanged = selectionChanged;
        name = entry.Name;
        notes = entry.Notes;
        isFavorite = entry.IsFavorite;
        CreatedAt = entry.CreatedAt;
        updatedAt = entry.UpdatedAt;
        CompletedSystems = entry.CompletedSystems;
        TotalSystems = entry.TotalSystems;
        HasUncountedBoxels = entry.HasUncountedBoxels;
        FileName = entry.FileName;
        FilePath = entry.FilePath;
        TopBoxelPrefix = entry.TopBoxelPrefix;
        LowMassCode = entry.LowMassCode;
        Prefixes = entry.Prefixes;
        ToggleFavoriteCommand = new AsyncCommand(() => toggleFavorite(this));
        RenameCommand = new RelayCommand(() => rename(this));
        EditNotesCommand = new RelayCommand(() => editNotes(this));
        OpenStatisticsCommand = new RelayCommand(
            () => openStatistics?.Invoke(this),
            () => canOpenStatistics);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetField(ref isSelected, value))
            {
                selectionChanged(this);
            }
        }
    }

    public string Name => name;

    public string? Notes => notes;

    public string NotesDisplay => string.IsNullOrWhiteSpace(Notes)
        ? "No notes"
        : Notes;

    public bool IsFavorite => isFavorite;

    public string FavoriteGlyph => IsFavorite ? "★" : "☆";

    public DateTimeOffset CreatedAt { get; }

    public string CreatedAtText => CreatedAt.ToLocalTime().ToString("g");

    public DateTimeOffset UpdatedAt => updatedAt;

    public string UpdatedAtText => UpdatedAt.ToLocalTime().ToString("g");

    public int CompletedSystems { get; }

    public int TotalSystems { get; }

    public bool HasUncountedBoxels { get; }

    public double ProgressFraction => TotalSystems == 0
        ? 0
        : (double)CompletedSystems / TotalSystems;

    public string ProgressText => HasUncountedBoxels
        ? $"{CompletedSystems:N0} of {TotalSystems:N0} known systems complete; audit for the full total"
        : $"{CompletedSystems:N0} of {TotalSystems:N0} systems complete";

    public string FileName { get; }

    public string FilePath { get; }

    public string? TopBoxelPrefix { get; }

    public char LowMassCode { get; }

    public IReadOnlyList<string> Prefixes { get; }

    public bool CanOpenStatistics => canOpenStatistics;

    public ICommand ToggleFavoriteCommand { get; }

    public ICommand RenameCommand { get; }

    public ICommand EditNotesCommand { get; }

    public ICommand OpenStatisticsCommand { get; }

    public void SetSelected(bool selected)
    {
        SetField(ref isSelected, selected, nameof(IsSelected));
    }

    public void SetFavorite(bool favorite)
    {
        if (SetField(ref isFavorite, favorite, nameof(IsFavorite)))
        {
            OnPropertyChanged(nameof(FavoriteGlyph));
        }
    }

    public void SetName(string value)
    {
        SetField(ref name, value, nameof(Name));
    }

    public void SetNotes(string? value)
    {
        if (SetField(ref notes, value, nameof(Notes)))
        {
            OnPropertyChanged(nameof(NotesDisplay));
        }
    }

    public void SetUpdatedAt(DateTimeOffset value)
    {
        if (SetField(ref updatedAt, value, nameof(UpdatedAt)))
        {
            OnPropertyChanged(nameof(UpdatedAtText));
        }
    }

    public void SetCanOpenStatistics(bool value)
    {
        if (SetField(ref canOpenStatistics, value, nameof(CanOpenStatistics)))
        {
            ((RelayCommand)OpenStatisticsCommand).RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        ((AsyncCommand)ToggleFavoriteCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RenameCommand).RaiseCanExecuteChanged();
        ((RelayCommand)EditNotesCommand).RaiseCanExecuteChanged();
        ((RelayCommand)OpenStatisticsCommand).RaiseCanExecuteChanged();
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

        public bool CanExecute(object? parameter) => !isExecuting;

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
        Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

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

public enum BoxelSearchLibrarySortColumn
{
    Name,
    Created,
    Modified,
    Progress,
}

public sealed record BoxelSurveyStatsFocusRequest(
    IReadOnlyList<string> Prefixes,
    char LowMassCode);
