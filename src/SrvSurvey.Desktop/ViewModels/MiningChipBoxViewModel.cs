using System.Collections.ObjectModel;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MiningChipBoxViewModel : WorkspaceObservable
{
    private readonly HashSet<string> exclusive;
    private IReadOnlyList<string> choices;
    private string fallback;
    private string query = "";
    private bool open;
    private IReadOnlyList<string> suggestions = [];

    public MiningChipBoxViewModel(
        string title,
        IReadOnlyList<string> choices,
        string initial,
        IReadOnlyList<string>? exclusive = null
    )
    {
        Title = title;
        this.choices = choices;
        fallback = initial;
        this.exclusive = new HashSet<string>(exclusive ?? ["Any", "All"], StringComparer.OrdinalIgnoreCase);
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

        if (exclusive.Contains(value))
        {
            Selected.Clear();
        }
        else
        {
            foreach (string token in exclusive)
            {
                RemoveToken(token);
            }
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

    public void ReplaceChoices(IReadOnlyList<string> next, string nextFallback)
    {
        choices = next;
        fallback = nextFallback;
        foreach (
            string selected in Selected
                .Where(item => !choices.Contains(item, StringComparer.OrdinalIgnoreCase))
                .ToArray()
        )
        {
            Selected.Remove(selected);
        }

        if (Selected.Count == 0 && fallback.Length > 0)
        {
            Selected.Add(fallback);
        }

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
