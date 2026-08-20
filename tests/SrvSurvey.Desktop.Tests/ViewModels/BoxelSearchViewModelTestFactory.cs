using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

internal static class BoxelSearchViewModelTestFactory
{
    public static BoxelSearchViewModel Create(
        CommanderProfileStore profileStore,
        LegacySystemDataReader localSystemReader,
        EmptyBoxelStore emptyBoxelStore,
        IBoxelSystemResolver systemResolver,
        Func<string, Task>? clipboardWriter = null,
        KnownSystemAddressCatalog? knownSystems = null,
        SavedBoxelSearchStore? savedSearchStore = null,
        ISystemNameSuggestionClient? systemNameSuggestionClient = null,
        TimeSpan? systemSuggestionDelay = null,
        BoxelSurveyStatsCoordinator? surveyStats = null)
    {
        return Create(
            profileStore,
            localSystemReader,
            emptyBoxelStore,
            systemResolver,
            out _,
            clipboardWriter,
            knownSystems,
            savedSearchStore,
            systemNameSuggestionClient,
            systemSuggestionDelay,
            surveyStats);
    }

    public static BoxelSearchViewModel Create(
        CommanderProfileStore profileStore,
        LegacySystemDataReader localSystemReader,
        EmptyBoxelStore emptyBoxelStore,
        IBoxelSystemResolver systemResolver,
        out BoxelSearchSession session,
        Func<string, Task>? clipboardWriter = null,
        KnownSystemAddressCatalog? knownSystems = null,
        SavedBoxelSearchStore? savedSearchStore = null,
        ISystemNameSuggestionClient? systemNameSuggestionClient = null,
        TimeSpan? systemSuggestionDelay = null,
        BoxelSurveyStatsCoordinator? surveyStats = null)
    {
        session = new BoxelSearchSession(
            profileStore,
            localSystemReader,
            emptyBoxelStore,
            savedSearchStore
                ?? new SavedBoxelSearchStore(profileStore.ProfileDirectory),
            systemResolver,
            clipboardWriter is null
                ? null
                : new DelegateClipboard(clipboardWriter));
        return new BoxelSearchViewModel(
            session,
            knownSystems,
            systemNameSuggestionClient,
            systemSuggestionDelay,
            surveyStats);
    }

    private sealed class DelegateClipboard(Func<string, Task> writer)
        : IBoxelClipboard
    {
        public bool IsReady => true;

        public Task WriteTextAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return writer(text);
        }
    }
}
