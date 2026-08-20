using System.ComponentModel;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;
using static SrvSurvey.Desktop.Tests.JournalEventEnvelopeTestParser;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class BoxelSearchLibraryViewModelTests : IAsyncLifetime
{
    private readonly List<BoxelSearchSession> sessions = [];
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-BoxelLibraryTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SelectingAnotherSearchClearsThePreviousSelection()
    {
        var store = new SavedBoxelSearchStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        var snapshot = new BoxelSearchSnapshot
        {
            Active = true,
            TopBoxel = top,
            Current = top,
            CurrentCount = 2,
            ProgressByPrefix = new Dictionary<string, int>
            {
                [top.Prefix] = 2,
            },
        };
        await store.CreateAsync("F123", "First", null, snapshot);
        await store.CreateAsync("F123", "Second", null, snapshot);
        var boxel = CreateBoxel(
            new CommanderProfileStore(temporaryDirectory),
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new EmptyResolver(),
            savedSearchStore: store);
        await boxel.LoadProfileAsync("F123", "Drew", true, BoxelSearchSnapshot.Empty);
        var library = new BoxelSearchLibraryViewModel(boxel.Session, boxel.SurveyStats);
        await library.RefreshAsync();

        library.Searches[0].IsSelected = true;
        library.Searches[1].IsSelected = true;

        Assert.False(library.Searches[0].IsSelected);
        Assert.True(library.Searches[1].IsSelected);
        Assert.Same(library.Searches[1], library.SelectedSearch);
    }

    [Fact]
    public async Task LibraryCommandsManageSavedSearchesAndKeepStateConsistent()
    {
        var (store, boxel, library) = await CreateLibraryAsync(
            ("Zulu", null, 1, 4),
            ("Alpha", "Original notes", 3, 4));
        var propertyChanges = new List<string?>();
        var renameDialogVisibleWhenCompleted = true;
        var notesDialogVisibleWhenCompleted = true;
        library.PropertyChanged += (_, eventArgs) =>
        {
            propertyChanges.Add(eventArgs.PropertyName);
            if (eventArgs.PropertyName == nameof(library.StatusMessage))
            {
                if (string.Equals(
                    library.StatusMessage,
                    "Renamed saved search to Renamed search.",
                    StringComparison.Ordinal))
                {
                    renameDialogVisibleWhenCompleted = library.IsDialogVisible;
                }
                else if (string.Equals(
                    library.StatusMessage,
                    "Saved notes for Renamed search.",
                    StringComparison.Ordinal))
                {
                    notesDialogVisibleWhenCompleted = library.IsDialogVisible;
                }
            }
        };

        Assert.True(library.HasSearches);
        Assert.False(library.HasSelection);
        Assert.Equal("2 saved searches", library.SelectionSummary);
        Assert.Equal("▲", library.NameSortIndicator);
        Assert.Empty(library.DateSortIndicator);
        Assert.Empty(library.ModifiedSortIndicator);
        Assert.Empty(library.ProgressSortIndicator);
        Assert.False(library.OpenSelectedCommand.CanExecute(null));
        Assert.False(library.RequestDeleteCommand.CanExecute(null));

        library.Searches[0].IsSelected = true;
        var selected = Assert.IsType<BoxelSearchLibraryItemViewModel>(
            library.SelectedSearch);
        Assert.True(library.HasSelection);
        Assert.Contains(selected.Name, library.DeleteConfirmationText);
        Assert.True(library.OpenSelectedCommand.CanExecute(null));
        Assert.True(library.RequestDeleteCommand.CanExecute(null));

        await ExecuteAndWaitAsync(
            selected.ToggleFavoriteCommand,
            () => library.StatusMessage.StartsWith("Added ", StringComparison.Ordinal));
        Assert.True(selected.IsFavorite);
        Assert.Equal("★", selected.FavoriteGlyph);
        Assert.True(selected.UpdatedAt >= selected.CreatedAt);

        selected.RenameCommand.Execute(null);
        Assert.True(library.IsRenameVisible);
        Assert.True(library.IsDialogVisible);
        Assert.Same(selected, library.EditingSearch);
        Assert.Equal(selected.Name, library.EditingSearchName);
        Assert.False(library.OpenSelectedCommand.CanExecute(null));
        library.RenameDraft = "Renamed search";
        Assert.True(library.SaveRenameCommand.CanExecute(null));
        await ExecuteAndWaitAsync(
            library.SaveRenameCommand,
            () => string.Equals(
                library.StatusMessage,
                "Renamed saved search to Renamed search.",
                StringComparison.Ordinal));
        Assert.Equal("Renamed search", selected.Name);
        Assert.False(library.IsDialogVisible);
        Assert.False(renameDialogVisibleWhenCompleted);
        Assert.Null(library.EditingSearch);
        Assert.Empty(library.EditingSearchName);

        selected.EditNotesCommand.Execute(null);
        Assert.True(library.IsNotesVisible);
        Assert.Equal("Original notes", library.NotesDraft);
        library.NotesDraft = "Updated notes";
        Assert.True(library.SaveNotesCommand.CanExecute(null));
        await ExecuteAndWaitAsync(
            library.SaveNotesCommand,
            () => string.Equals(
                library.StatusMessage,
                "Saved notes for Renamed search.",
                StringComparison.Ordinal));
        Assert.Equal("Updated notes", selected.Notes);
        Assert.Equal("Updated notes", selected.NotesDisplay);
        Assert.False(notesDialogVisibleWhenCompleted);

        selected.EditNotesCommand.Execute(null);
        Assert.True(library.CancelDialogCommand.CanExecute(null));
        library.CancelDialogCommand.Execute(null);
        Assert.False(library.IsDialogVisible);
        Assert.Empty(library.NotesDraft);

        library.SortDateCommand.Execute(null);
        Assert.Equal("▼", library.DateSortIndicator);
        library.SortDateCommand.Execute(null);
        Assert.Equal("▲", library.DateSortIndicator);
        library.SortModifiedCommand.Execute(null);
        Assert.Equal("▼", library.ModifiedSortIndicator);
        library.SortProgressCommand.Execute(null);
        Assert.Equal("▼", library.ProgressSortIndicator);
        Assert.Equal(3, library.Searches[0].CompletedSystems);
        library.SortNameCommand.Execute(null);
        Assert.Equal("▲", library.NameSortIndicator);
        library.SortNameCommand.Execute(null);
        Assert.Equal("▼", library.NameSortIndicator);
        library.FavoritesFirst = false;
        Assert.False(library.FavoritesFirst);

        var opened = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        library.SearchOpened += (_, _) => opened.TrySetResult();
        await ExecuteAndWaitAsync(
            library.OpenSelectedCommand,
            () => opened.Task.IsCompleted);
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("Opened Renamed search.", library.StatusMessage);

        var toDelete = library.Searches.Single(search =>
            !ReferenceEquals(search, selected));
        toDelete.IsSelected = true;
        library.RequestDeleteCommand.Execute(null);
        Assert.True(library.IsDeleteConfirmationVisible);
        Assert.True(library.ConfirmDeleteCommand.CanExecute(null));
        await ExecuteAndWaitAsync(
            library.ConfirmDeleteCommand,
            () => library.Searches.Count == 1);
        Assert.Equal("Moved Zulu to recovery storage.", library.StatusMessage);
        Assert.Equal("1 saved search", library.SelectionSummary);
        Assert.Single(await store.ListAsync("F123"));

        Assert.Contains(nameof(library.HasSearches), propertyChanges);
        Assert.Contains(nameof(library.SelectedSearch), propertyChanges);
        Assert.Contains(nameof(library.IsDeleteConfirmationVisible), propertyChanges);
        Assert.Contains(nameof(library.IsDialogVisible), propertyChanges);
    }

    [Fact]
    public async Task EmptyLibraryAndLibraryItemDisplayValuesAreExplicit()
    {
        var (_, _, library) = await CreateLibraryAsync();

        Assert.False(library.HasSearches);
        Assert.Equal("0 saved searches", library.SelectionSummary);
        Assert.Equal("No saved boxel searches yet.", library.StatusMessage);
        Assert.Equal(
            "Delete the selected saved search?",
            library.DeleteConfirmationText);

        var entry = new SavedBoxelSearchCatalogEntry(
            "Empty",
            null,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            0,
            0,
            true,
            "empty.json",
            "C:\\empty.json");
        var selectedCount = 0;
        var item = new BoxelSearchLibraryItemViewModel(
            entry,
            _ => selectedCount++,
            _ => Task.CompletedTask,
            _ => { },
            _ => { });
        var changed = new List<string?>();
        item.PropertyChanged += (_, eventArgs) => changed.Add(eventArgs.PropertyName);

        Assert.Equal("No notes", item.NotesDisplay);
        Assert.Equal("☆", item.FavoriteGlyph);
        Assert.Equal(0, item.ProgressFraction);
        Assert.Contains("audit for the full total", item.ProgressText);
        Assert.NotEmpty(item.CreatedAtText);
        Assert.NotEmpty(item.UpdatedAtText);
        item.IsSelected = true;
        item.IsSelected = true;
        item.SetFavorite(true);
        item.SetNotes("Notes");
        item.SetName("Changed");
        item.SetUpdatedAt(item.UpdatedAt.AddMinutes(1));
        item.SetSelected(false);
        item.RaiseCanExecuteChanged();

        Assert.Equal(1, selectedCount);
        Assert.Equal("Changed", item.Name);
        Assert.Equal("Notes", item.NotesDisplay);
        Assert.Equal("★", item.FavoriteGlyph);
        Assert.Contains(nameof(item.NotesDisplay), changed);
        Assert.Contains(nameof(item.UpdatedAtText), changed);

        Assert.Throws<ArgumentNullException>(() =>
            new BoxelSearchLibraryViewModel(null!));
        Assert.Throws<ArgumentNullException>(() =>
            new BoxelSearchLibraryItemViewModel(
                null!,
                _ => { },
                _ => Task.CompletedTask,
                _ => { },
                _ => { }));
    }

    [Fact]
    public async Task ExternalLibraryChangesPreserveSelectionAndOpenDrafts()
    {
        var (_, boxel, library) = await CreateLibraryAsync(
            ("Alpha", null, 0, 1),
            ("Zulu", null, 0, 1));
        var selected = library.Searches.Single(search => search.Name == "Alpha");
        var changed = library.Searches.Single(search => search.Name == "Zulu");
        selected.IsSelected = true;
        selected.RenameCommand.Execute(null);
        library.RenameDraft = "Draft name";

        await boxel.Session.ExecuteAsync(
            new SetSavedBoxelSearchFavorite(changed.FileName, true));

        Assert.True(library.IsRenameVisible);
        Assert.Equal("Draft name", library.RenameDraft);
        Assert.Equal(selected.FileName, library.SelectedSearch?.FileName);

        var refreshCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
        {
            if (eventArgs.PropertyName == nameof(library.IsBusy)
                && !library.IsBusy)
            {
                refreshCompleted.TrySetResult();
            }
        }

        library.PropertyChanged += OnPropertyChanged;
        try
        {
            library.CancelDialogCommand.Execute(null);
            await refreshCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            library.PropertyChanged -= OnPropertyChanged;
        }

        Assert.Equal(selected.FileName, library.SelectedSearch?.FileName);
        Assert.True(library.Searches
            .Single(search => search.FileName == changed.FileName)
            .IsFavorite);
    }

    [Fact]
    public async Task MissingSavedFilesReportEachLibraryOperationWithoutCrashing()
    {
        var (_, _, library) = await CreateLibraryAsync(
            ("Open", null, 0, 1),
            ("Favorite", null, 0, 1),
            ("Rename", null, 0, 1),
            ("Notes", null, 0, 1),
            ("Delete", null, 0, 1));

        var open = library.Searches.Single(search => search.Name == "Open");
        open.IsSelected = true;
        File.Delete(open.FilePath);
        await ExecuteAndWaitAsync(
            library.OpenSelectedCommand,
            () => library.StatusMessage.Contains(
                "could not be opened",
                StringComparison.Ordinal));

        var favorite = library.Searches.Single(search => search.Name == "Favorite");
        File.Delete(favorite.FilePath);
        await ExecuteAndWaitAsync(
            favorite.ToggleFavoriteCommand,
            () => library.StatusMessage.Contains(
                "favorite could not be updated",
                StringComparison.Ordinal));

        var rename = library.Searches.Single(search => search.Name == "Rename");
        rename.RenameCommand.Execute(null);
        library.RenameDraft = "Replacement";
        File.Delete(rename.FilePath);
        await ExecuteAndWaitAsync(
            library.SaveRenameCommand,
            () => library.StatusMessage.Contains(
                "could not be renamed",
                StringComparison.Ordinal));
        Assert.True(library.IsRenameVisible);
        library.CancelDialogCommand.Execute(null);

        var notes = library.Searches.Single(search => search.Name == "Notes");
        notes.EditNotesCommand.Execute(null);
        library.NotesDraft = "Replacement notes";
        File.Delete(notes.FilePath);
        await ExecuteAndWaitAsync(
            library.SaveNotesCommand,
            () => library.StatusMessage.Contains(
                "notes could not be saved",
                StringComparison.Ordinal));
        Assert.True(library.IsNotesVisible);
        library.CancelDialogCommand.Execute(null);

        var delete = library.Searches.Single(search => search.Name == "Delete");
        delete.IsSelected = true;
        library.RequestDeleteCommand.Execute(null);
        File.Delete(delete.FilePath);
        await ExecuteAndWaitAsync(
            library.ConfirmDeleteCommand,
            () => library.StatusMessage.Contains(
                "could not be deleted",
                StringComparison.Ordinal));
        Assert.Contains(delete, library.Searches);
    }

    [Fact]
    public async Task StatisticsShortcutEnablesWhenPrefixExistsInIndex()
    {
        var store = new SavedBoxelSearchStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        await store.CreateAsync(
            "F123",
            "Helium run",
            null,
            new BoxelSearchSnapshot
            {
                Active = true,
                TopBoxel = top,
                Current = top,
                CurrentCount = 1,
                LowMassCode = 'c',
                ProgressByPrefix = new Dictionary<string, int>
                {
                    [top.Prefix] = 1,
                },
            });
        using var coordinator = new BoxelSurveyStatsCoordinator(
            new BoxelSurveyStatsStore(temporaryDirectory),
            TimeSpan.FromHours(1));
        await coordinator.SwitchCommanderAsync("F123");
        await coordinator.ApplyJournalEventsAsync(
        [
            Parse(
                """{"timestamp":"2026-07-10T12:00:00Z","event":"FSDJump","StarSystem":"Praea Euq IL-P c5-0","SystemAddress":2001}"""),
        ]);
        await coordinator.FlushAsync();

        var boxel = CreateBoxel(
            new CommanderProfileStore(temporaryDirectory),
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new EmptyResolver(),
            savedSearchStore: store,
            surveyStats: coordinator);
        await boxel.LoadProfileAsync("F123", "Drew", true, BoxelSearchSnapshot.Empty);
        var library = new BoxelSearchLibraryViewModel(boxel.Session, boxel.SurveyStats);
        BoxelSurveyStatsFocusRequest? requested = null;
        library.StatisticsRequested += (_, request) => requested = request;
        await library.RefreshAsync();

        var item = Assert.Single(library.Searches);
        Assert.Equal(top.Prefix, item.TopBoxelPrefix);
        Assert.Equal('c', item.LowMassCode);
        Assert.True(item.CanOpenStatistics);
        Assert.True(item.OpenStatisticsCommand.CanExecute(null));
        item.OpenStatisticsCommand.Execute(null);
        Assert.NotNull(requested);
        Assert.Contains(top.Prefix, requested!.Prefixes);
        Assert.Equal('c', requested.LowMassCode);
    }

    private async Task<(
        SavedBoxelSearchStore Store,
        BoxelSearchViewModel Boxel,
        BoxelSearchLibraryViewModel Library)> CreateLibraryAsync(
        params (string Name, string? Notes, int Completed, int Total)[] searches)
    {
        var store = new SavedBoxelSearchStore(temporaryDirectory);
        var top = BoxelAddress.Parse("Praea Euq IL-P c5-0");
        foreach (var search in searches)
        {
            var snapshot = new BoxelSearchSnapshot
            {
                Active = true,
                TopBoxel = top,
                Current = top,
                CurrentCount = search.Total,
                ProgressByPrefix = new Dictionary<string, int>
                {
                    [top.Prefix] = search.Total,
                },
                CompletedSystems = Enumerable.Range(0, search.Completed)
                    .Select(index => $"{top.Prefix}{index}")
                    .ToArray(),
            };
            await store.CreateAsync("F123", search.Name, search.Notes, snapshot);
        }

        var boxel = CreateBoxel(
            new CommanderProfileStore(temporaryDirectory),
            new LegacySystemDataReader(temporaryDirectory),
            new EmptyBoxelStore(temporaryDirectory),
            new EmptyResolver(),
            savedSearchStore: store);
        await boxel.LoadProfileAsync(
            "F123",
            "Drew",
            true,
            BoxelSearchSnapshot.Empty);
        var library = new BoxelSearchLibraryViewModel(boxel.Session, boxel.SurveyStats);
        await library.RefreshAsync();
        return (store, boxel, library);
    }

    private static async Task ExecuteAndWaitAsync(
        System.Windows.Input.ICommand command,
        Func<bool> completed)
    {
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        var timeout = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!completed() && DateTimeOffset.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(completed());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        foreach (var session in sessions.AsEnumerable().Reverse())
        {
            await session.DisposeAsync();
        }

        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private BoxelSearchViewModel CreateBoxel(
        CommanderProfileStore profileStore,
        LegacySystemDataReader localSystemReader,
        EmptyBoxelStore emptyBoxelStore,
        IBoxelSystemResolver systemResolver,
        SavedBoxelSearchStore? savedSearchStore = null,
        BoxelSurveyStatsCoordinator? surveyStats = null)
    {
        var viewModel = BoxelSearchViewModelTestFactory.Create(
            profileStore,
            localSystemReader,
            emptyBoxelStore,
            systemResolver,
            out var session,
            savedSearchStore: savedSearchStore,
            surveyStats: surveyStats);
        sessions.Add(session);
        return viewModel;
    }

    private sealed class EmptyResolver : IBoxelSystemResolver
    {
        public Task<IReadOnlyList<BoxelSystemObservation>> SearchAsync(
            BoxelAddress boxel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BoxelSystemObservation>>([]);
        }
    }
}
