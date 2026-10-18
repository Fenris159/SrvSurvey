using System.ComponentModel;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationViewModel
{
    private Action? wishListOpener;
    private readonly WorkspaceCommand wishListCommand;
    private readonly HashSet<string> shoppingProjectIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly WorkspaceCommand searchMarketsCommand;
    private ColonizationShoppingSession? shoppingSession;
    private ColonizationShoppingViewModel? shoppingWorkspace;
    private int shoppingSelectionVersion;
    private IReadOnlyList<ColonizationFleetCarrier>? shoppingCarriers;

    // Imported cache data can be displayed before Raven responds, but must not trim saved purchases.
    private bool hasCurrentShoppingProjects;

    internal void SetWishListOpener(Action? opener) => wishListOpener = opener;

    public ColonizationWishListViewModel WishList { get; }

    public ICommand OpenWishListCommand => wishListCommand;

    public ColonizationShoppingViewModel? MarketSearchWorkspace => shoppingWorkspace;

    private int selectedWorkspaceTabIndex;

    public int SelectedWorkspaceTabIndex
    {
        get => selectedWorkspaceTabIndex;
        set
        {
            int selected = Math.Clamp(value, 0, 3);
            if (selectedWorkspaceTabIndex == selected)
            {
                return;
            }
            selectedWorkspaceTabIndex = selected;
            OnPropertyChanged();
            EnsureMarketSearchWorkspace();
        }
    }

    private void EnsureMarketSearchWorkspace(long? preferredCarrierMarketId = null)
    {
        if (selectedWorkspaceTabIndex == 1 && CanSearchMarkets && shoppingWorkspace is null)
        {
            CreateShopping(
                Projects.Where(row => row.IsShoppingSelected).Select(row => row.Project).ToArray(),
                preferredCarrierMarketId: preferredCarrierMarketId
            );
        }
    }

    private void CloseMarketSearchWorkspace()
    {
        shoppingWorkspace?.Dispose();
        shoppingWorkspace = null;
        OnPropertyChanged(nameof(MarketSearchWorkspace));
    }

    private void ReconcileWishList()
    {
        IReadOnlyList<ColonizationShoppingNeed> needs = ColonizationShopping.Aggregate(
            Projects.Where(row => shoppingProjectIds.Contains(row.Project.BuildId)).Select(row => row.Project)
        );
        if (shoppingSession?.Search is { EffectiveUseCarrierDifference: true } carrierSearch)
        {
            if (
                hasCurrentShoppingProjects
                && carrierSearch.SelectedCarrierMarketId > 0
                && !LinkedFleetCarriers.Any(carrier => carrier.MarketId == carrierSearch.SelectedCarrierMarketId)
            )
            {
                carrierSearch = carrierSearch with { SelectedCarrierMarketId = 0 };
                shoppingSession = shoppingSession with { Search = carrierSearch };
                SaveShoppingSession("The updated carrier selection could not be saved to disk.");
            }
            needs = ColonizationShopping.SubtractCarrierCargo(
                needs,
                LinkedFleetCarriers,
                carrierSearch.SelectedCarrierMarketId
            );
        }
        if (hasCurrentShoppingProjects && shoppingSession?.Search is { } search)
        {
            var reconciled = new ColonizationShoppingPlan(needs, search.Quotes, search.WishList);
            IReadOnlyList<ColonizationWishListItem> allocations = reconciled.Snapshot();
            if (!search.WishList.SequenceEqual(allocations))
            {
                shoppingSession = shoppingSession with { Search = search with { WishList = allocations } };
                SaveShoppingSession("The updated ShoppingList could not be saved to disk.");
            }
        }
        WishList.Apply(shoppingSession?.Search, needs);
    }

    private void ReconcileShoppingCarriers()
    {
        if (!hasCurrentShoppingProjects || ReferenceEquals(shoppingCarriers, LinkedFleetCarriers))
        {
            return;
        }
        shoppingCarriers = LinkedFleetCarriers;
        shoppingWorkspace?.UpdateCarriers(LinkedFleetCarriers);
        ReconcileWishList();
    }

    private void RemoveWishListItems(IReadOnlyList<ColonizationWishListItem> items)
    {
        if (shoppingWorkspace?.RemoveWishListItems(items) == true)
        {
            return;
        }
        if (shoppingSession?.Search is not { } search || CommanderName is null)
        {
            return;
        }
        var keys = items.Select(item => item.Key()).ToHashSet();
        shoppingSession = shoppingSession with
        {
            Search = search with { WishList = search.WishList.Where(item => !keys.Contains(item.Key())).ToArray() },
        };
        ReconcileWishList();
        SaveShoppingSession("The ShoppingList was updated but could not be saved to disk.");
    }

    public bool CanSearchMarkets =>
        IsEnabled && hasCurrentShoppingProjects && Projects.Any(row => row.IsShoppingSelected);

    public ICommand SearchMarketsCommand => searchMarketsCommand;

    public string SearchMarketsButtonText => shoppingSession?.Search is not null ? "Reopen Search" : "Search Markets";

    /// <summary>Opens the embedded search for the selected project tags without changing overlay visibility.</summary>
    public void OpenShopping()
    {
        SelectedWorkspaceTabIndex = 1;
        EnsureMarketSearchWorkspace();
    }

    internal Func<IColonizationMarketSearch> MarketSearchFactory { get; set; } =
        static () => new ColonizationMarketSearchClient();

    internal ColonizationShoppingViewModel CreateShopping(
        IReadOnlyList<ColonizationProject> selected,
        IColonizationMarketSearch? marketSearch = null,
        long? preferredCarrierMarketId = null
    )
    {
        if (!hasCurrentShoppingProjects)
        {
            throw new InvalidOperationException("Refresh projects before opening the shopping workspace.");
        }
        string? commander = CommanderName;
        int version = shoppingSelectionVersion;
        string[] ids = selected.Select(project => project.BuildId).ToArray();
        bool matches =
            shoppingSession is not null
            && new HashSet<string>(shoppingSession.ProjectIds, StringComparer.OrdinalIgnoreCase).SetEquals(ids);
        var model = new ColonizationShoppingViewModel(
            selected,
            () => recovery.CurrentSystemName,
            marketSearch ?? MarketSearchFactory(),
            ownsProvidedSearch: marketSearch is null,
            restoredState: matches ? shoppingSession?.Search : null,
            saveState: state =>
            {
                // An obsolete search session must not overwrite a changed project selection.
                if (commander is null || version != shoppingSelectionVersion)
                {
                    return;
                }
                var session = new ColonizationShoppingSession(ids, state);
                if (string.Equals(commander, CommanderName, StringComparison.OrdinalIgnoreCase))
                {
                    shoppingSession = session;
                    if (planningCarrierMarketId != state.SelectedCarrierMarketId)
                    {
                        planningCarrierMarketId = state.SelectedCarrierMarketId;
                        OnPropertyChanged(nameof(SelectedPlanningCarrierMarketId));
                        OnPropertyChanged(nameof(SelectedPlanningCarrier));
                        SavePlanningCarrier();
                        UpdateCommodityPlan();
                    }
                    ReconcileWishList();
                    OnPropertyChanged(nameof(SearchMarketsButtonText));
                }
                settingsStore.SaveShopping(commander, session);
            },
            refreshProjects: token => RefreshShoppingProjectsAsync(commander, version, ids, token)
        )
        {
            OpenWishListCommand = wishListCommand,
        };
        shoppingWorkspace = model;
        model.UpdateCarriers(LinkedFleetCarriers, preferredCarrierMarketId ?? planningCarrierMarketId);
        model.SaveWorkspace();
        OnPropertyChanged(nameof(MarketSearchWorkspace));
        return model;
    }

    private void RestoreShoppingSelection()
    {
        shoppingSelectionVersion++;
        CloseMarketSearchWorkspace();
        localFocusProjectId = CommanderName is null ? null : settingsStore.LoadLocalProjectFocus(CommanderName);
        shoppingSession = CommanderName is null ? null : settingsStore.LoadShopping(CommanderName);
        plannedSystems.Clear();
        authoritativePlanningPrefixes.Clear();
        planningSystemEditVersions.Clear();
        localPlanningDrafts.Clear();
        latestPlanningSnapshots.Clear();
        if (CommanderName is not null)
        {
            foreach (ColonizationSystemRecord system in settingsStore.LoadPlannedSystems(CommanderName))
            {
                plannedSystems[system.Name] = system;
            }
        }
        hiddenPlannedProjectIds.Clear();
        if (CommanderName is not null)
        {
            hiddenPlannedProjectIds.UnionWith(settingsStore.LoadHiddenPlannedProjects(CommanderName));
        }
        RestorePlanningCarrier();
        shoppingProjectIds.Clear();
        shoppingProjectIds.UnionWith(shoppingSession?.ProjectIds ?? []);
        ReconcileWishList();
        OnPropertyChanged(nameof(SearchMarketsButtonText));
    }

    private void SaveShoppingSelection()
    {
        ColonizationShoppingState? state =
            shoppingProjectIds.Count == 0 ? null : shoppingWorkspace?.CaptureState() ?? shoppingSession?.Search;
        shoppingSelectionVersion++;
        CloseMarketSearchWorkspace();
        shoppingSession = new ColonizationShoppingSession(shoppingProjectIds.ToArray(), state);
        ReconcileWishList();
        OnPropertyChanged(nameof(SearchMarketsButtonText));
        SaveShoppingSession("The shopping selection could not be saved to disk.");
        EnsureMarketSearchWorkspace(state?.SelectedCarrierMarketId);
    }

    private void SaveShoppingSession(string failureMessage)
    {
        if (CommanderName is null || shoppingSession is null)
        {
            return;
        }
        try
        {
            settingsStore.SaveShopping(CommanderName, shoppingSession);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StatusMessage = failureMessage;
        }
    }

    private void ReconcileShoppingSelection(IEnumerable<string> projectIds)
    {
        int previousCount = shoppingProjectIds.Count;
        shoppingProjectIds.IntersectWith(
            projectIds.Concat(
                shoppingProjectIds.Where(id =>
                    id.StartsWith(ColonizationPlannedProjects.BuildIdPrefix, StringComparison.Ordinal)
                    && !authoritativePlanningPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal))
                )
            )
        );
        if (previousCount != shoppingProjectIds.Count)
        {
            SaveShoppingSelection();
        }
    }

    private void OnShoppingSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is ColonizationProjectRowViewModel row && args.PropertyName == nameof(row.IsShoppingSelected))
        {
            if (row.IsShoppingSelected)
            {
                shoppingProjectIds.Add(row.Project.BuildId);
            }
            else
            {
                shoppingProjectIds.Remove(row.Project.BuildId);
            }

            SaveShoppingSelection();
            OnPropertyChanged(nameof(CanSearchMarkets));
            searchMarketsCommand.Refresh();
        }
    }

    private async Task<IReadOnlyList<ColonizationProject>> RefreshShoppingProjectsAsync(
        string? commander,
        int selectionVersion,
        IReadOnlyList<string> ids,
        CancellationToken token
    )
    {
        if (
            !IsEnabled
            || commander is null
            || !string.Equals(commander, CommanderName, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException("The shopping commander is no longer active.");
        }
        Task<bool> refresh;
        if (!IsBusy)
        {
            refresh = StartProjectRefresh(token);
        }
        else if (projectRefreshTask is { IsCompleted: false })
        {
            refresh = projectRefreshTask;
        }
        else
        {
            throw new InvalidDataException("The project workspace is busy. Try refreshing again when it finishes.");
        }
        bool succeeded = await refresh.WaitAsync(token);
        if (
            !succeeded
            || !IsEnabled
            || selectionVersion != shoppingSelectionVersion
            || !string.Equals(commander, CommanderName, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException("Current project needs could not be refreshed.");
        }
        return Projects
            .Where(row => ids.Contains(row.Project.BuildId, StringComparer.OrdinalIgnoreCase))
            .Select(row => row.Project)
            .ToArray();
    }
}
