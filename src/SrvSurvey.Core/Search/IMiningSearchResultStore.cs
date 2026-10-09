namespace SrvSurvey.Core.Search;

/// <summary>Completed mining search presentations, keyed by workspace and complete filter key.</summary>
public interface IMiningSearchResultStore
{
    event Action<bool>? HideIrrelevantMaterialTagsChanged;

    bool HideIrrelevantMaterialTags { get; set; }

    T? Load<T>(string workspace, string key);

    T? LoadLast<T>(string workspace);

    void Save<T>(string workspace, string key, T snapshot);
}
