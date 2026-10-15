using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Reuses one system autocomplete entry to build an ordered list of route destinations.</summary>
public sealed class SpanshSystemListViewModel : WorkspaceObservable
{
    private readonly WorkspaceCommand commitCommand;
    private readonly WorkspaceCommand cancelEditCommand;
    private string draft = "";
    private bool showRefuel;
    private bool synchronizing;
    private SpanshDestinationViewModel? editing;

    public SpanshSystemListViewModel()
    {
        commitCommand = new WorkspaceCommand(Commit, () => Draft.Trim().Length > 0);
        cancelEditCommand = new WorkspaceCommand(CancelEdit, () => IsEditing);
    }

    public ObservableCollection<SpanshDestinationViewModel> Rows { get; } = [];
    public ICommand CommitCommand => commitCommand;
    public ICommand CancelEditCommand => cancelEditCommand;
    public string Value => string.Join('\n', Rows.Select(row => row.Name));
    public string RefuelValue => string.Join('\n', Rows.Where(row => row.Refuel).Select(row => row.Name));
    public bool HasRows => Rows.Count > 0;
    public bool IsEditing => editing is not null;
    public bool HasPendingEntry => IsEditing || Draft.Trim().Length > 0;
    public string CommitLabel => IsEditing ? "Save" : "+ Add";
    public string Draft
    {
        get => draft;
        set
        {
            if (Set(ref draft, value ?? ""))
            {
                commitCommand.Refresh();
            }
        }
    }
    public bool ShowRefuel
    {
        get => showRefuel;
        internal set
        {
            if (Set(ref showRefuel, value))
            {
                foreach (SpanshDestinationViewModel row in Rows)
                {
                    row.Refresh();
                }
            }
        }
    }

    internal void Load(string text)
    {
        string[] names = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (Rows.Select(row => row.Name).SequenceEqual(names))
        {
            return;
        }
        string[] refuel = Rows.Where(row => row.Refuel).Select(row => row.Name).ToArray();
        synchronizing = true;
        Rows.Clear();
        foreach (string name in names)
        {
            Rows.Add(
                new SpanshDestinationViewModel(this, name)
                {
                    Refuel = refuel.Contains(name, StringComparer.OrdinalIgnoreCase),
                }
            );
        }
        synchronizing = false;
        CancelEdit();
        Publish();
    }

    internal void SetRefuel(string text)
    {
        string[] names = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        synchronizing = true;
        foreach (SpanshDestinationViewModel row in Rows)
        {
            row.Refuel = names.Contains(row.Name, StringComparer.OrdinalIgnoreCase);
        }
        synchronizing = false;
    }

    private void Commit()
    {
        string name = Draft.Trim();
        if (name.Length == 0)
        {
            return;
        }
        if (editing is null)
        {
            Rows.Add(new SpanshDestinationViewModel(this, name));
        }
        else
        {
            editing.Rename(name);
        }
        CancelEdit();
        Publish();
    }

    internal void Edit(SpanshDestinationViewModel row)
    {
        editing = row;
        Draft = row.Name;
        RefreshEditing();
    }

    private void CancelEdit()
    {
        editing = null;
        Draft = "";
        RefreshEditing();
    }

    private void RefreshEditing()
    {
        Changed(nameof(IsEditing));
        Changed(nameof(CommitLabel));
        cancelEditCommand.Refresh();
    }

    internal void Remove(SpanshDestinationViewModel row)
    {
        if (ReferenceEquals(editing, row))
        {
            CancelEdit();
        }
        Rows.Remove(row);
        Publish();
    }

    internal void Move(SpanshDestinationViewModel row, int offset)
    {
        int index = Rows.IndexOf(row);
        int target = index + offset;
        if (index >= 0 && target >= 0 && target < Rows.Count)
        {
            Rows.Move(index, target);
            Publish();
        }
    }

    internal void Publish()
    {
        if (synchronizing)
        {
            return;
        }
        foreach (SpanshDestinationViewModel row in Rows)
        {
            row.Refresh();
        }
        Changed(nameof(Value));
        Changed(nameof(RefuelValue));
        Changed(nameof(HasRows));
    }
}

public sealed class SpanshDestinationViewModel : WorkspaceObservable
{
    private readonly SpanshSystemListViewModel owner;
    private readonly WorkspaceCommand upCommand;
    private readonly WorkspaceCommand downCommand;
    private string name;
    private bool refuel;

    internal SpanshDestinationViewModel(SpanshSystemListViewModel owner, string name)
    {
        this.owner = owner;
        this.name = name;
        EditCommand = new WorkspaceCommand(() => owner.Edit(this));
        RemoveCommand = new WorkspaceCommand(() => owner.Remove(this));
        upCommand = new WorkspaceCommand(() => owner.Move(this, -1), () => owner.Rows.IndexOf(this) > 0);
        downCommand = new WorkspaceCommand(
            () => owner.Move(this, 1),
            () => owner.Rows.IndexOf(this) < owner.Rows.Count - 1
        );
    }

    public string Name => name;
    public string Number =>
        (owner.Rows.IndexOf(this) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
    public bool ShowRefuel => owner.ShowRefuel;
    public ICommand EditCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand UpCommand => upCommand;
    public ICommand DownCommand => downCommand;
    public bool Refuel
    {
        get => refuel;
        set
        {
            if (Set(ref refuel, value))
            {
                owner.Publish();
            }
        }
    }

    internal void Rename(string value) => Set(ref name, value, nameof(Name));

    internal void Refresh()
    {
        Changed(nameof(Number));
        Changed(nameof(ShowRefuel));
        upCommand.Refresh();
        downCommand.Refresh();
    }
}
