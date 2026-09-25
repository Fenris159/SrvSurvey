using System.Collections.ObjectModel;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MiningChipBoxViewModel : WorkspaceObservable
{
    private readonly IReadOnlyList<string> choices;
    private readonly string fallback;
    private string query = "";
    private bool open;
    private IReadOnlyList<string> suggestions = [];

    public MiningChipBoxViewModel(string title, IReadOnlyList<string> choices, string initial)
    {
        Title = title;
        this.choices = choices;
        fallback = initial;
        if (initial.Length > 0)
        {
            Selected.Add(initial);
        }

        RefreshSuggestions();
    }

    public string Title { get; }
    public ObservableCollection<string> Selected { get; } = [];
    public string Query
    {
        get => query;
        set
        {
            if (Set(ref query, value))
            {
                RefreshSuggestions();
            }
        }
    }
    public bool Open
    {
        get => open;
        set => Set(ref open, value);
    }
    public IReadOnlyList<string> Suggestions => suggestions;

    private void RefreshSuggestions()
    {
        suggestions = choices
            .Where(choice =>
                !Selected.Contains(choice, StringComparer.OrdinalIgnoreCase)
                && (query.Length == 0 || choice.Contains(query, StringComparison.OrdinalIgnoreCase))
            )
            .Take(8)
            .ToArray();
        Changed(nameof(Suggestions));
    }

    public void Add(string value)
    {
        if (!choices.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (
            value.Equals("Any", StringComparison.OrdinalIgnoreCase)
            || value.Equals("All", StringComparison.OrdinalIgnoreCase)
        )
        {
            Selected.Clear();
        }
        else
        {
            RemoveToken("Any");
            RemoveToken("All");
        }

        if (!Selected.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            Selected.Add(value);
        }

        Query = "";
        Open = false;
        RefreshSuggestions();
        Changed(nameof(Selected));
    }

    public void Remove(string value)
    {
        RemoveToken(value);
        if (Selected.Count == 0 && fallback.Length > 0)
        {
            Selected.Add(fallback);
        }

        RefreshSuggestions();
        Changed(nameof(Selected));
    }

    private void RemoveToken(string value)
    {
        string? existing = Selected.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Selected.Remove(existing);
        }
    }
}
