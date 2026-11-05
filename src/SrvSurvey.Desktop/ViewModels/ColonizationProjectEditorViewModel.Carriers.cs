using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationProjectEditorViewModel
{
    private bool includeCarrierLinks;
    private bool updatingCarrierOptions;
    private ColonizationCarrierSelectionViewModel? carrierSelection;

    public ColonizationCarrierSelectionViewModel CarrierSelection =>
        carrierSelection ??= new(OnCreationCarrierSelectionChanged, () => !IsBusy);

    public IReadOnlyList<ColonizationCarrierChoice> CarrierOptions { get; private set; } = [new(0, "ALL")];

    public bool IncludeCarrierLinks
    {
        get => includeCarrierLinks;
        set
        {
            if (!IsBusy && SetField(ref includeCarrierLinks, value))
            {
                ClearConfirmation();
                NotifyCarrierSelection();
            }
        }
    }

    public long SelectedCreationCarrierMarketId
    {
        get => CarrierSelection.LegacyMarketId;
        set
        {
            if (!IsBusy && !updatingCarrierOptions)
            {
                CarrierSelection.Restore(null, value);
            }
        }
    }

    public bool HasCreationCarriers => CarrierSelection.Items.Count > 0 && client is IRavenColonialProjectCarrierClient;

    public bool CanSetCarrierLinking => !IsBusy && (HasCreationCarriers || IncludeCarrierLinks);

    public bool CanChooseCreationCarrier => !IsBusy && HasCreationCarriers && IncludeCarrierLinks;

    public string CarrierLinkHint
    {
        get
        {
            if (!HasCreationCarriers)
            {
                return "Link carriers to your commander in Raven to make them available here.";
            }
            if (!IncludeCarrierLinks)
            {
                return "Carriers are linked after the build is created.";
            }
            int count = SelectedCreationCarriers().Count;
            return count == 1
                ? "Link 1 carrier after this build is created."
                : $"Link {count:N0} carriers after this build is created.";
        }
    }

    private string CarrierConfirmationSummary
    {
        get
        {
            if (!IncludeCarrierLinks)
            {
                return string.Empty;
            }
            int count = SelectedCreationCarriers().Count;
            return count == 1 ? " Link 1 carrier after creation." : $" Link {count:N0} carriers after creation.";
        }
    }

    /// <summary>Refreshes known carriers without silently expanding a removed specific selection to ALL.</summary>
    public void UpdateCarrierOptions(IReadOnlyList<ColonizationFleetCarrier> carriers)
    {
        ArgumentNullException.ThrowIfNull(carriers);
        long[] previous = CarrierSelection.CaptureSelectedIds().Order().ToArray();
        updatingCarrierOptions = true;
        try
        {
            CarrierSelection.UpdateCarriers(carriers, pruneMissing: false);
            CarrierOptions = ColonizationCarrierChoice.CreateOptions(carriers);
            OnPropertyChanged(nameof(CarrierOptions));
            OnPropertyChanged(nameof(SelectedCreationCarrierMarketId));
        }
        finally
        {
            updatingCarrierOptions = false;
        }
        if (IncludeCarrierLinks && !IsBusy && !previous.SequenceEqual(CarrierSelection.CaptureSelectedIds().Order()))
        {
            ClearConfirmation();
        }
        NotifyCarrierSelection();
    }

    private void OnCreationCarrierSelectionChanged()
    {
        if (!updatingCarrierOptions)
        {
            ClearConfirmation();
            OnPropertyChanged(nameof(SelectedCreationCarrierMarketId));
            NotifyCarrierSelection();
        }
    }

    private IReadOnlyList<ColonizationFleetCarrier> SelectedCreationCarriers() => CarrierSelection.ResolveCarriers();

    private string? CarrierSelectionError() =>
        IncludeCarrierLinks && (!HasCreationCarriers || SelectedCreationCarriers().Count == 0)
            ? "Choose an available carrier, or turn off carrier linking before reviewing this build."
            : null;

    private void ResetCarrierLinkSelection()
    {
        includeCarrierLinks = false;
        updatingCarrierOptions = true;
        try
        {
            CarrierSelection.Restore(null);
        }
        finally
        {
            updatingCarrierOptions = false;
        }
        OnPropertyChanged(nameof(IncludeCarrierLinks));
        OnPropertyChanged(nameof(SelectedCreationCarrierMarketId));
        NotifyCarrierSelection();
    }

    private void NotifyCarrierSelection()
    {
        OnPropertyChanged(nameof(HasCreationCarriers));
        OnPropertyChanged(nameof(CanSetCarrierLinking));
        OnPropertyChanged(nameof(CanChooseCreationCarrier));
        OnPropertyChanged(nameof(CarrierLinkHint));
        OnPropertyChanged(nameof(ConfirmationSummary));
    }

    /// <summary>Runs only after creation has returned a real build ID; each link failure leaves the build intact.</summary>
    private async Task<(ColonizationProject Project, string? Message)> LinkCreatedProjectCarriersAsync(
        ColonizationProject created,
        IReadOnlyList<ColonizationFleetCarrier> carriers,
        int version
    )
    {
        if (carriers.Count == 0)
        {
            return (created, null);
        }
        if (
            !ColonizationProjectCarrierLinks.CanLink(created)
            || client is not IRavenColonialProjectCarrierClient carrierClient
        )
        {
            return (created, "Carrier linking was skipped because no valid build ID was returned.");
        }
        int linked = 0;
        var failures = new List<string>();
        StatusMessage = $"Created {created.BuildName}. Linking carriers...";
        foreach (ColonizationFleetCarrier carrier in carriers)
        {
            if (version != contextVersion)
            {
                return (created, null);
            }
            try
            {
                await carrierClient.LinkProjectCarrierAsync(created.BuildId, carrier.MarketId, CancellationToken.None);
                if (version != contextVersion)
                {
                    return (created, null);
                }
                created = ColonizationProjectCarrierLinks.Apply(created, carrier, link: true);
                linked++;
            }
            catch (Exception exception)
                when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
            {
                failures.Add($"{carrier.Name}: {exception.Message}");
            }
        }
        string message = $"Carriers linked: {linked:N0} of {carriers.Count:N0}.";
        if (failures.Count > 0)
        {
            message += " " + string.Join("; ", failures) + " Retry failed links from My build projects.";
        }
        return (created, message);
    }
}
