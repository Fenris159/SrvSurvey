using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Routes;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class SpanshSearchViewModel : WorkspaceObservable, IDisposable
{
    private const string RefuelDestinations = "refuel_destinations";
    private readonly RouteWorkspaceViewModel route;
    private readonly ISpanshPlannerClient client;
    private readonly CurrentShipExportViewModel currentShip;
    private readonly Dictionary<SpanshPlannerDefinition, IReadOnlyList<SpanshPlannerFieldViewModel>> forms = [];
    private SpanshPlannerDefinition selectedPlanner;
    private IReadOnlyList<SpanshPlannerFieldViewModel> fields = [];
    private Action? cancelSearch;
    private bool useCurrentShip = true;
    private bool isBusy;
    private string status = "Choose a planner and search to load its route into the itinerary.";

    /// <summary>
    /// Connects planner forms to one route workspace and the current equipped ship.
    /// </summary>
    public SpanshSearchViewModel(
        RouteWorkspaceViewModel route,
        ISpanshPlannerClient client,
        CurrentShipExportViewModel currentShip
    )
    {
        this.route = route;
        this.client = client;
        this.currentShip = currentShip;
        Planners = route.IsFleetCarrierWorkspace
            ? SpanshPlannerCatalog.CarrierPlanners
            : SpanshPlannerCatalog.ShipPlanners;
        selectedPlanner = Planners[0];
        route.PropertyChanged += OnRouteChanged;
        currentShip.PropertyChanged += OnShipChanged;
        SelectForm();
    }

    public string Title => route.IsFleetCarrierWorkspace ? "Spansh Search · Fleet carrier" : "Spansh Search";
    public IReadOnlyList<SpanshPlannerDefinition> Planners { get; }
    public SpanshPlannerDefinition SelectedPlanner
    {
        get => selectedPlanner;
        set
        {
            if (!IsBusy && Planners.Contains(value) && Set(ref selectedPlanner, value))
            {
                SelectForm();
            }
        }
    }
    public IReadOnlyList<SpanshPlannerFieldViewModel> Fields => fields;
    public bool IsGalaxy => SelectedPlanner.Kind == SpanshRouteKind.Galaxy;
    public bool UseCurrentShip
    {
        get => useCurrentShip;
        set
        {
            if (Set(ref useCurrentShip, value))
            {
                UpdateShipBuild();
            }
        }
    }
    public string ShipDescription => currentShip.ShipDescription;
    public string ShipStatus => currentShip.CanExport ? "Current equipped ship build is ready." : currentShip.Status;
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (Set(ref isBusy, value))
            {
                Changed(nameof(CanSearch));
            }
        }
    }
    public bool CanSearch =>
        !IsBusy && route.CanSearchSpansh && (!IsGalaxy || !UseCurrentShip || currentShip.CanExport);
    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }

    /// <summary>
    /// Owns the request cancellation source until submission and result import have finished.
    /// </summary>
    public async Task<bool> SearchAsync()
    {
        if (!CanSearch)
        {
            return false;
        }
        using var operation = new CancellationTokenSource();
        cancelSearch = operation.Cancel;
        try
        {
            if (fields.Any(field => field.IsVisible && field.SystemList?.HasPendingEntry == true))
            {
                throw new ArgumentException("Add or save the system entry before searching.");
            }
            SpanshPlannerRequest request = SelectedPlanner.CreateRequest(Values());
            IsBusy = true;
            bool loaded = await route.SearchSpanshAsync(client, request, operation.Token);
            Status = route.StatusMessage;
            return loaded;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            Status = exception.Message;
            return false;
        }
        finally
        {
            cancelSearch = null;
            IsBusy = false;
        }
    }

    /// <summary>
    /// Invokes the active request's non-owning cancellation handle.
    /// </summary>
    public void Cancel() => cancelSearch?.Invoke();

    /// <summary>
    /// Cancels pending work and releases workspace and ship-change subscriptions.
    /// </summary>
    public void Dispose()
    {
        Cancel();
        route.PropertyChanged -= OnRouteChanged;
        currentShip.PropertyChanged -= OnShipChanged;
    }

    /// <summary>
    /// Collects current field values by their original Spansh API keys.
    /// </summary>
    private Dictionary<string, string> Values() =>
        fields.ToDictionary(field => field.Definition.Key, field => field.Value);

    /// <summary>
    /// Restores a planner's retained form or initializes its defaults and subscriptions.
    /// </summary>
    private void SelectForm()
    {
        if (!forms.TryGetValue(SelectedPlanner, out IReadOnlyList<SpanshPlannerFieldViewModel>? form))
        {
            form = SelectedPlanner.Fields.Select(field => new SpanshPlannerFieldViewModel(field)).ToArray();
            SpanshPlannerFieldViewModel? source = form.FirstOrDefault(field =>
                field.Definition.Type == SpanshPlannerFieldType.System
            );
            if (source is not null && route.CurrentSystem != "—")
            {
                source.Value = route.CurrentSystem;
            }
            foreach (SpanshPlannerFieldViewModel field in form)
            {
                field.PropertyChanged += OnFieldChanged;
                if (field.Definition.Key == "destinations" && field.SystemList is not null)
                {
                    field.SystemList.PropertyChanged += OnCarrierDestinationsChanged;
                }
            }
            forms.Add(SelectedPlanner, form);
        }
        fields = form;
        UpdateVisibility();
        UpdateShipBuild();
        Changed(nameof(Fields));
        Changed(nameof(IsGalaxy));
        Changed(nameof(CanSearch));
    }

    /// <summary>
    /// Follows the live equipped-ship SLEF while automatic ship selection is enabled.
    /// </summary>
    private void UpdateShipBuild()
    {
        SpanshPlannerFieldViewModel? build = fields.FirstOrDefault(field =>
            field.Definition.Type == SpanshPlannerFieldType.ShipBuild
        );
        if (build is not null)
        {
            build.IsReadOnly = UseCurrentShip;
            if (UseCurrentShip)
            {
                build.Value = currentShip.SlefJson;
            }
        }
        Changed(nameof(CanSearch));
        Changed(nameof(ShipDescription));
        Changed(nameof(ShipStatus));
    }

    /// <summary>
    /// Updates conditional fields and search availability after an input changes.
    /// </summary>
    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpanshPlannerFieldViewModel.Value))
        {
            if (sender is SpanshPlannerFieldViewModel { Definition.Key: RefuelDestinations } refuel)
            {
                CarrierDestinationList?.SetRefuel(refuel.Value);
            }
            UpdateVisibility();
        }
    }

    /// <summary>
    /// Reconciles conditional controls, carrier capacity and per-destination refuel toggles.
    /// </summary>
    private void UpdateVisibility()
    {
        Dictionary<string, string> values = Values();
        foreach (SpanshPlannerFieldViewModel field in fields)
        {
            field.IsVisible = SpanshPlannerDefinition.IsVisible(field.Definition, values);
            if (field.Definition.Key is "capacity_used" or "tritium_stored")
            {
                field.SetCarrierCapacity(
                    values.GetValueOrDefault("carrier_type") == "Squadron Carrier" ? 60000 : 25000
                );
            }
        }
        SpanshPlannerFieldViewModel? refuel = fields.FirstOrDefault(field =>
            field.Definition.Key == RefuelDestinations
        );
        if (refuel is not null && CarrierDestinationList is { } destinations)
        {
            destinations.ShowRefuel = refuel.IsVisible;
            refuel.IsVisible = false;
        }
    }

    private SpanshSystemListViewModel? CarrierDestinationList =>
        fields.FirstOrDefault(entry => entry.Definition.Key == "destinations")?.SystemList;

    /// <summary>
    /// Keeps API destination and refuelling parameters synchronized with the shared picker.
    /// </summary>
    private void OnCarrierDestinationsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpanshSystemListViewModel.RefuelValue) && sender is SpanshSystemListViewModel list)
        {
            SpanshPlannerFieldViewModel? refuel = fields.FirstOrDefault(field =>
                field.Definition.Key == RefuelDestinations
            );
            refuel?.Value = list.RefuelValue;
        }
    }

    /// <summary>
    /// Refreshes automatic ship-build inputs when export readiness changes.
    /// </summary>
    private void OnShipChanged(object? sender, PropertyChangedEventArgs e) => UpdateShipBuild();

    /// <summary>
    /// Refreshes availability and cancels work when the commander context changes.
    /// </summary>
    private void OnRouteChanged(object? sender, PropertyChangedEventArgs e)
    {
        Changed(nameof(CanSearch));
        if (e.PropertyName == nameof(RouteWorkspaceViewModel.CurrentSystem) && route.CurrentSystem != "—")
        {
            SpanshPlannerFieldViewModel? source = fields.FirstOrDefault(field => field.IsSystem);
            if (source is not null && source.Value.Length == 0)
            {
                source.Value = route.CurrentSystem;
            }
        }
        if (IsBusy && e.PropertyName == nameof(RouteWorkspaceViewModel.StatusMessage))
        {
            Status = route.StatusMessage;
        }
    }
}

public sealed class SpanshPlannerFieldViewModel : WorkspaceObservable
{
    private string value;
    private bool isVisible = true;
    private bool isReadOnly;
    private decimal? carrierCapacity;

    /// <summary>
    /// Creates the appropriate editable state for one planner field definition.
    /// </summary>
    public SpanshPlannerFieldViewModel(SpanshPlannerField definition)
    {
        Definition = definition;
        value = definition.Default;
        RadioChoices = Choices.Select(choice => new SpanshRadioChoiceViewModel(this, choice)).ToArray();
        if (IsSystems)
        {
            SystemList = new SpanshSystemListViewModel();
            SystemList.PropertyChanged += OnSystemListChanged;
            SystemList.Load(value);
        }
    }

    public SpanshPlannerField Definition { get; }
    public SpanshSystemListViewModel? SystemList { get; }
    public IReadOnlyList<SpanshRadioChoiceViewModel> RadioChoices { get; }

    /// <summary>
    /// Copies committed destination-list changes into the field's submission value.
    /// </summary>
    private void OnSystemListChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpanshSystemListViewModel.Value))
        {
            Value = SystemList!.Value;
        }
    }

    public string Label => Definition.Label;
    public bool IsRequired => Definition.Required;
    public string Help => Definition.Help;
    public bool HasHelp => Help.Length > 0;
    public IReadOnlyList<SpanshPlannerChoice> Choices => Definition.Choices ?? [];
    public SpanshPlannerChoice? SelectedChoice
    {
        get => Choices.FirstOrDefault(choice => choice.Value == Value);
        set
        {
            if (value is not null)
            {
                Value = value.Value;
            }
        }
    }
    public decimal? Number
    {
        get =>
            decimal.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number)
                ? number
                : null;
        set => Value = value?.ToString(CultureInfo.InvariantCulture) ?? "";
    }
    public decimal Minimum => (decimal)(Definition.Minimum ?? 0);
    public decimal Maximum => carrierCapacity ?? (Definition.Maximum is { } max ? (decimal)max : decimal.MaxValue);

    /// <summary>
    /// Updates numeric bounds for the selected carrier type.
    /// </summary>
    internal void SetCarrierCapacity(decimal value)
    {
        if (Set(ref carrierCapacity, value))
        {
            Changed(nameof(Maximum));
        }
    }

    public bool IsSystem => Definition.Type == SpanshPlannerFieldType.System;
    public bool IsBoolean => Definition.Type == SpanshPlannerFieldType.Boolean;
    public bool IsChoice => Definition.Type == SpanshPlannerFieldType.Choice;
    public bool IsRadioChoice => IsChoice && Definition.Key is "supercharge_multiplier" or "carrier_type";
    public bool IsDropdownChoice => IsChoice && !IsRadioChoice;
    public bool IsNumber => Definition.Type == SpanshPlannerFieldType.Number;
    public bool IsSystems => Definition.Type == SpanshPlannerFieldType.Systems;
    public bool IsText => Definition.Type == SpanshPlannerFieldType.ShipBuild;
    public bool IsMultiline => IsText;
    public bool IsVisible
    {
        get => isVisible;
        internal set => Set(ref isVisible, value);
    }
    public bool IsReadOnly
    {
        get => isReadOnly;
        internal set => Set(ref isReadOnly, value);
    }
    public string Value
    {
        get => value;
        set
        {
            value ??= "";
            if (Set(ref this.value, value))
            {
                SystemList?.Load(value);
                Changed(nameof(IsChecked));
                Changed(nameof(Number));
                Changed(nameof(SelectedChoice));
                foreach (SpanshRadioChoiceViewModel choice in RadioChoices)
                {
                    choice.Refresh();
                }
            }
        }
    }
    public bool IsChecked
    {
        get => Value == "1";
        set => Value = value ? "1" : "0";
    }
}

public sealed class SpanshRadioChoiceViewModel(SpanshPlannerFieldViewModel owner, SpanshPlannerChoice choice)
    : WorkspaceObservable
{
    public string Name => choice.Name;
    public string Group => owner.Definition.Key;
    public bool IsChecked
    {
        get => owner.Value == choice.Value;
        set
        {
            if (value)
            {
                owner.Value = choice.Value;
            }
        }
    }

    /// <summary>
    /// Notifies radio-choice bindings after the parent field's selection changes.
    /// </summary>
    internal void Refresh() => Changed(nameof(IsChecked));
}
