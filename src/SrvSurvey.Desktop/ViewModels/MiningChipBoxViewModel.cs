using System.Collections.ObjectModel;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record MiningChipTag(string Name, string Label);

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
        Selected.CollectionChanged += (_, _) => PublishTags();
        if (initial.Length > 0)
        {
            Selected.Add(initial);
        }

        RefreshSuggestions();
        PublishTags();
    }

    public string Title { get; }
    public string Prompt =>
        Title switch
        {
            "Mining type" => "Type to add mining types...",
            "System state" => "Type to add system states...",
            _ => "Type to add minerals/metals...",
        };
    public ObservableCollection<string> Selected { get; } = [];
    public IReadOnlyList<MiningChipTag> Tags { get; private set; } = [];
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
                && (
                    query.Length == 0
                    || choice.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || ChipLabel(choice).Contains(query, StringComparison.OrdinalIgnoreCase)
                )
            )
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

    public string ChipLabel(string name)
    {
        if (Title is "Mining type" or "System state" || IsWordToken(name))
        {
            return name;
        }

        return MiningCommodityCode.Abbreviate(name);
    }

    private void PublishTags()
    {
        Tags = Selected.Select(name => new MiningChipTag(name, ChipLabel(name))).ToArray();
        Changed(nameof(Tags));
    }

    private static bool IsWordToken(string name) =>
        name.Equals("Default", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Any", StringComparison.OrdinalIgnoreCase)
        || name.Equals("All", StringComparison.OrdinalIgnoreCase)
        || name.Equals("None", StringComparison.OrdinalIgnoreCase);

    private void RemoveToken(string value)
    {
        string? existing = Selected.FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Selected.Remove(existing);
        }
    }
}
