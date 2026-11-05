using System.ComponentModel;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Retains checked carrier identities independently of their labels, ordering, and cargo updates.</summary>
public sealed class ColonizationCarrierSelectionViewModel : INotifyPropertyChanged
{
    private readonly Action? changed;
    private readonly Func<bool> canEdit;
    private IReadOnlyList<ColonizationFleetCarrier> carriers = [];
    private HashSet<long> selectedIds = [];
    private bool useAll = true;
    private bool legacySingle;

    public ColonizationCarrierSelectionViewModel(Action? changed = null, Func<bool>? canEdit = null)
    {
        this.changed = changed;
        this.canEdit = canEdit ?? (() => true);
        SelectAllCommand = new WorkspaceCommand(() => Restore(null), () => this.canEdit() && Items.Count > 0);
        ClearCommand = new WorkspaceCommand(() => Restore([]), () => this.canEdit() && Items.Count > 0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ColonizationCarrierSelectionItem> Items { get; private set; } = [];

    public ICommand SelectAllCommand { get; }
    public ICommand ClearCommand { get; }
    public bool HasNoCarriers => Items.Count == 0;

    public IReadOnlyList<long> CaptureSelectedIds() =>
        Items.Where(item => item.IsSelected).Select(item => item.MarketId).ToArray();

    public IReadOnlyList<long>? CapturePreference() => useAll || legacySingle ? null : selectedIds.Order().ToArray();

    public long LegacyMarketId => !useAll && selectedIds.Count == 1 ? selectedIds.First() : 0;
    public int SelectedCount => Items.Count(item => item.IsSelected);

    public string Summary
    {
        get
        {
            if (useAll && Items.Count > 0)
            {
                return "All carriers";
            }
            return SelectedCount switch
            {
                0 => "Select carriers",
                1 => Items.First(item => item.IsSelected).Label,
                _ => SelectedCount == Items.Count ? "All carriers" : "Multiple carriers",
            };
        }
    }

    public string SelectionNote => SelectedCount == 1 ? "1 carrier selected" : $"{SelectedCount:N0} carriers selected";

    public bool Matches(IReadOnlyList<long>? ids, long legacyMarketId = 0)
    {
        if (ids is null)
        {
            return legacyMarketId == 0 ? useAll : legacySingle && selectedIds.SetEquals([legacyMarketId]);
        }
        return !useAll && !legacySingle && selectedIds.SetEquals(ids.Where(id => id > 0));
    }

    /// <summary>Restores checked IDs, or the earlier one/ALL setting when IDs are absent.</summary>
    public void Restore(IReadOnlyList<long>? ids, long legacyMarketId = 0)
    {
        if (Matches(ids, legacyMarketId))
        {
            return;
        }
        useAll = ids is null && legacyMarketId == 0;
        legacySingle = ids is null && legacyMarketId > 0;
        selectedIds = ids?.Where(id => id > 0).ToHashSet() ?? [];
        if (ids is null && legacyMarketId > 0)
        {
            selectedIds.Add(legacyMarketId);
        }
        NotifySelection();
    }

    public void UpdateCarriers(IReadOnlyList<ColonizationFleetCarrier> updated, bool pruneMissing = true)
    {
        ArgumentNullException.ThrowIfNull(updated);
        long[] before = CaptureSelectedIds().Order().ToArray();
        carriers = updated.Where(carrier => carrier.MarketId > 0).DistinctBy(carrier => carrier.MarketId).ToArray();
        if (!useAll && pruneMissing)
        {
            selectedIds.IntersectWith(carriers.Select(carrier => carrier.MarketId));
            // Preserve fallback behavior for the legacy single-carrier preference.
            if (legacySingle && selectedIds.Count == 0)
            {
                useAll = true;
                legacySingle = false;
            }
        }
        ColonizationCarrierChoice[] options = ColonizationCarrierChoice
            .CreateOptions(carriers)
            .Where(choice => choice.MarketId > 0)
            .ToArray();
        if (!Items.Select(item => new ColonizationCarrierChoice(item.MarketId, item.Label)).SequenceEqual(options))
        {
            Items = options
                .Select(option => new ColonizationCarrierSelectionItem(this, option.MarketId, option.Label))
                .ToArray();
            PropertyChanged?.Invoke(this, new(nameof(Items)));
        }
        NotifySelection(notifyOwner: !before.SequenceEqual(CaptureSelectedIds().Order()));
    }

    public IReadOnlyList<ColonizationFleetCarrier> ResolveCarriers() =>
        carriers.Where(carrier => IsSelected(carrier.MarketId)).ToArray();

    internal bool IsSelected(long marketId) => useAll || selectedIds.Contains(marketId);

    internal void SetSelected(long marketId, bool value)
    {
        if (!canEdit() || IsSelected(marketId) == value)
        {
            return;
        }
        if (useAll)
        {
            selectedIds = Items.Select(item => item.MarketId).ToHashSet();
            useAll = false;
        }
        legacySingle = false;
        if (value)
        {
            selectedIds.Add(marketId);
        }
        else
        {
            selectedIds.Remove(marketId);
        }
        NotifySelection();
    }

    private void NotifySelection(bool notifyOwner = true)
    {
        foreach (ColonizationCarrierSelectionItem item in Items)
        {
            item.RefreshSelection();
        }
        PropertyChanged?.Invoke(this, new(nameof(Summary)));
        PropertyChanged?.Invoke(this, new(nameof(SelectionNote)));
        PropertyChanged?.Invoke(this, new(nameof(SelectedCount)));
        PropertyChanged?.Invoke(this, new(nameof(HasNoCarriers)));
        (SelectAllCommand as WorkspaceCommand)?.Refresh();
        (ClearCommand as WorkspaceCommand)?.Refresh();
        if (notifyOwner)
        {
            changed?.Invoke();
        }
    }
}

public sealed class ColonizationCarrierSelectionItem(
    ColonizationCarrierSelectionViewModel owner,
    long marketId,
    string label
) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public long MarketId { get; } = marketId;
    public string Label { get; } = label;
    public bool IsSelected
    {
        get => owner.IsSelected(MarketId);
        set => owner.SetSelected(MarketId, value);
    }

    internal void RefreshSelection() => PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
}
