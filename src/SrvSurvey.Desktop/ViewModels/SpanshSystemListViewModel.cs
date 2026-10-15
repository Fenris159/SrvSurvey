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

    /// <summary>
    /// Creates commands for adding and editing destinations through a single reusable entry.
    /// </summary>
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

    /// <summary>
    /// Restores committed destinations while retaining matching refuel selections.
    /// </summary>
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

    /// <summary>
    /// Synchronizes per-row refuel toggles from the encoded refuelling-destination list.
    /// </summary>
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

    /// <summary>
    /// Adds the current draft or saves it into the row being edited.
    /// </summary>
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

    /// <summary>
    /// Loads a selected row into the same autocomplete entry for revision.
    /// </summary>
    internal void Edit(SpanshDestinationViewModel row)
    {
        editing = row;
        Draft = row.Name;
        RefreshEditing();
    }

    /// <summary>
    /// Clears pending text and leaves the committed destination list intact.
    /// </summary>
    private void CancelEdit()
    {
        editing = null;
        Draft = "";
        RefreshEditing();
    }

    /// <summary>
    /// Updates entry labels, row controls and edit-command availability.
    /// </summary>
    private void RefreshEditing()
    {
        Changed(nameof(IsEditing));
        Changed(nameof(CommitLabel));
        cancelEditCommand.Refresh();
    }

    /// <summary>
    /// Removes a destination and refreshes ordering and refuelling parameters.
    /// </summary>
    internal void Remove(SpanshDestinationViewModel row)
    {
        if (ReferenceEquals(editing, row))
        {
            CancelEdit();
        }
        Rows.Remove(row);
        Publish();
    }

    /// <summary>
    /// Moves a destination within the ordered list when the target index exists.
    /// </summary>
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

    /// <summary>
    /// Refreshes destination numbering and notifies field and refuelling bindings.
    /// </summary>
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

    /// <summary>
    /// Connects row commands and refuelling state to the owning destination list.
    /// </summary>
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

    /// <summary>
    /// Updates the committed row name and its bound display values.
    /// </summary>
    internal void Rename(string value) => Set(ref name, value, nameof(Name));

    /// <summary>
    /// Updates row ordering, refuelling visibility and command availability.
    /// </summary>
    internal void Refresh()
    {
        Changed(nameof(Number));
        Changed(nameof(ShowRefuel));
        upCommand.Refresh();
        downCommand.Refresh();
    }
}
