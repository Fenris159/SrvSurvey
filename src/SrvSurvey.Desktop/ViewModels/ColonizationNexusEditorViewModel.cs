using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Keeps a creation acknowledgement so failed list updates can be finished without a second create.</summary>
public sealed partial class ColonizationNexusEditorViewModel : WorkspaceObservable, IDisposable
{
    private readonly IRavenColonialNexusWriter writer;
    private readonly string apiKey;
    private readonly Func<bool> isCurrent;
    private readonly Func<ColonizationNexus, Task> onCreated;
    private readonly CancellationTokenSource lifetime = new();
    private readonly WorkspaceCommand addCarrierCommand;
    private bool disposed;
    private bool isBusy;
    private bool completed;
    private bool uncertainCreation;
    private string name = string.Empty;
    private string notes = string.Empty;
    private string carrierQuery = string.Empty;
    private bool isPrivate = true;
    private string status = "Add entries to their lists before creating the Nexus. Systems follow the order shown.";
    private ColonizationNexus? created;
    private IReadOnlyList<ColonizationNexusCarrierMatch> carrierOptions;
    private ColonizationNexusCarrierMatch? selectedCarrier;

    public ColonizationNexusEditorViewModel(
        IRavenColonialNexusWriter writer,
        string commander,
        string apiKey,
        Func<bool> isCurrent,
        Func<ColonizationNexus, Task> onCreated,
        IReadOnlyList<ColonizationFleetCarrier> carriers,
        ColonizationNexus? existing = null
    )
    {
        this.writer = writer;
        Commander = commander;
        Owner = existing?.Owner ?? commander;
        IsEditing = existing is not null;
        Systems.MinimumRows = IsEditing ? 1 : 0;
        created = existing;
        this.apiKey = apiKey;
        this.isCurrent = isCurrent;
        this.onCreated = onCreated;
        carrierOptions = carriers
            .Where(carrier => carrier.MarketId > 0)
            .DistinctBy(carrier => carrier.MarketId)
            .Select(carrier => new ColonizationNexusCarrierMatch
            {
                MarketId = carrier.MarketId,
                Name = carrier.Name,
                CarrierName = carrier.DisplayName,
            })
            .ToArray();
        selectedCarrier = carrierOptions.Count > 0 ? carrierOptions[0] : null;
        addCarrierCommand = new WorkspaceCommand(
            AddCarrier,
            () =>
                CanEdit
                && SelectedCarrier is not null
                && Carriers.All(carrier => carrier.MarketId != SelectedCarrier.MarketId)
        );
        RemoveCarrierCommand = new WorkspaceParameterCommand(value =>
        {
            if (CanEdit && value is ColonizationNexusCarrierMatch carrier)
            {
                Carriers.Remove(carrier);
                addCarrierCommand.Refresh();
            }
        });
        Systems.PropertyChanged += EntriesChanged;
        Commanders.PropertyChanged += EntriesChanged;
        if (existing is not null)
        {
            name = existing.Name;
            notes = existing.Notes ?? string.Empty;
            isPrivate = !existing.Open;
            Systems.Load(string.Join('\n', existing.Systems.Select(system => system.Name)));
            Commanders.Load(
                string.Join(
                    '\n',
                    existing.Commanders.Where(member =>
                        !string.Equals(member, Owner, StringComparison.OrdinalIgnoreCase)
                    )
                )
            );
            foreach (ColonizationFleetCarrier carrier in existing.FleetCarriers)
            {
                Carriers.Add(
                    new()
                    {
                        MarketId = carrier.MarketId,
                        Name = carrier.Name,
                        CarrierName = carrier.DisplayName,
                    }
                );
            }
            status = "Edit the Nexus details, then choose Save changes.";
        }
    }

    public event Action? CloseRequested;
    public string Commander { get; }
    public string Owner { get; }
    public bool IsEditing { get; }
    public string? NexusId => created?.Id;
    public string Title => IsEditing ? "Edit Nexus" : "Create Nexus";
    public SpanshSystemListViewModel Systems { get; } = new();

    // The route planner's ordered name-list model also handles add/edit/remove for commander names.
    public SpanshSystemListViewModel Commanders { get; } = new();
    public ObservableCollection<ColonizationNexusCarrierMatch> Carriers { get; } = [];
    public ICommand AddCarrierCommand => addCarrierCommand;
    public ICommand RemoveCarrierCommand { get; }
    public IReadOnlyList<ColonizationNexusCarrierMatch> CarrierOptions
    {
        get => carrierOptions;
        private set => Set(ref carrierOptions, value);
    }
    public ColonizationNexusCarrierMatch? SelectedCarrier
    {
        get => selectedCarrier;
        set
        {
            if (Set(ref selectedCarrier, value))
            {
                addCarrierCommand.Refresh();
            }
        }
    }
    public string Name
    {
        get => name;
        set
        {
            if (Set(ref name, value ?? string.Empty))
            {
                Changed(nameof(CanSubmit));
            }
        }
    }
    public string Notes
    {
        get => notes;
        set => Set(ref notes, value ?? string.Empty);
    }
    public bool IsPrivate
    {
        get => isPrivate;
        set => Set(ref isPrivate, value);
    }
    public string CarrierQuery
    {
        get => carrierQuery;
        set
        {
            if (Set(ref carrierQuery, value ?? string.Empty))
            {
                Changed(nameof(CanFindCarrier));
            }
        }
    }
    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (Set(ref isBusy, value))
            {
                RefreshAvailability();
            }
        }
    }
    public bool CanEdit =>
        !disposed && !IsBusy && !completed && !uncertainCreation && !IsDeleteConfirmationVisible && isCurrent();
    public bool CanEditName => CanEdit && (IsEditing || created is null);
    public bool CanFindCarrier => CanEdit && CarrierQuery.Trim().Length >= 3;
    public bool CanSubmit =>
        CanEdit
        && Systems.HasRows
        && !string.IsNullOrWhiteSpace(Name)
        && !Systems.HasPendingEntry
        && !Commanders.HasPendingEntry;
    public string SubmitLabel
    {
        get
        {
            if (IsEditing)
            {
                return "Save changes";
            }
            return created is null ? "Create Nexus" : "Finish Nexus setup";
        }
    }
    public string CloseLabel => created is not null || uncertainCreation ? "Close" : "Cancel";

    public async Task FindCarriersAsync()
    {
        if (!CanFindCarrier)
        {
            return;
        }
        IsBusy = true;
        Status = "Finding Fleet Carriers on Raven Colonial...";
        try
        {
            IReadOnlyList<ColonizationNexusCarrierMatch> matches = await writer.FindNexusCarriersAsync(
                CarrierQuery.Trim(),
                Commander,
                apiKey,
                lifetime.Token
            );
            if (!IsCurrent)
            {
                return;
            }
            CarrierOptions = matches;
            SelectedCarrier = matches.Count > 0 ? matches[0] : null;
            Status =
                matches.Count > 0
                    ? "Select a carrier and add it to the list."
                    : "No matching carriers were found. Try a name or callsign.";
        }
        catch (Exception exception) when (IsServiceFailure(exception))
        {
            if (IsCurrent)
            {
                Status = "Carrier search failed. Try again when Raven Colonial is available.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool ValidateSubmission()
    {
        if (!CanEdit)
        {
            return false;
        }
        if (!Systems.HasRows)
        {
            Status = "Keep at least one system in the Nexus. Use Delete Nexus to remove the final system.";
            return false;
        }
        if (!CanSubmit)
        {
            return false;
        }
        if (HasDuplicates(Systems) || HasDuplicates(Commanders))
        {
            Status = "Remove duplicate systems or commanders before creating the Nexus.";
            return false;
        }
        return true;
    }

    public async Task SubmitAsync()
    {
        if (!ValidateSubmission())
        {
            return;
        }
        var draft = new ColonizationNexusDraft(
            Name.Trim(),
            Notes.Trim(),
            IsPrivate,
            Systems.Rows.Select(row => row.Name).ToArray(),
            (IsEditing ? new[] { Owner } : Array.Empty<string>())
                .Concat(Commanders.Rows.Select(row => row.Name))
                .ToArray(),
            Carriers.Select(carrier => carrier.MarketId).ToArray()
        )
        {
            Owner = Owner,
        };
        IsBusy = true;
        try
        {
            if (!await CheckForConcurrentEditsAsync())
            {
                return;
            }
            if (created is null)
            {
                Status = "Creating Nexus on Raven Colonial...";
                // A lost acknowledgement is an unknown outcome; only a definitive rejection makes creation safe to retry.
                uncertainCreation = true;
                created = await writer.CreateNexusAsync(draft.Name, Commander, apiKey, lifetime.Token);
                uncertainCreation = false;
                Changed(nameof(SubmitLabel));
            }
            if (!IsCurrent)
            {
                return;
            }
            if (IsEditing && created.Name != draft.Name)
            {
                created = await writer.RenameNexusAsync(created.Id, draft.Name, Commander, apiKey, lifetime.Token);
                if (!IsCurrent)
                {
                    return;
                }
            }
            Status = "Saving Nexus systems, commanders, carriers and preferences...";
            ColonizationNexus snapshot = await writer.ConfigureNexusAsync(
                created.Id,
                draft,
                Commander,
                apiKey,
                lifetime.Token
            );
            if (!IsCurrent)
            {
                return;
            }
            completed = true;
            Status = "Nexus saved. Refreshing your Nexus projects...";
            await onCreated(snapshot);
            if (IsCurrent)
            {
                CloseRequested?.Invoke();
            }
        }
        catch (Exception exception) when (IsServiceFailure(exception))
        {
            if (IsCurrent)
            {
                ExplainFailure(exception);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> CheckForConcurrentEditsAsync()
    {
        if (!IsEditing)
        {
            return true;
        }
        if (created is not { } original || writer is not IRavenColonialNexusReader reader)
        {
            throw new InvalidDataException("The current Nexus details could not be checked.");
        }
        ColonizationNexus? latest = await reader.GetNexusAsync(original.Id, Commander, apiKey, lifetime.Token);
        if (!IsCurrent)
        {
            return false;
        }
        if (latest is null || !HasSameEditableDetails(original, latest))
        {
            Status =
                "This Nexus changed on Raven Colonial. Close this editor, refresh Nexus, and reopen it before saving.";
            return false;
        }
        return true;
    }

    private static bool HasSameEditableDetails(ColonizationNexus original, ColonizationNexus latest) =>
        original.Name == latest.Name
        && (original.Notes ?? string.Empty) == (latest.Notes ?? string.Empty)
        && original.Open == latest.Open
        && string.Equals(original.Owner, latest.Owner, StringComparison.OrdinalIgnoreCase)
        && original
            .Systems.Select(system => system.Name)
            .SequenceEqual(latest.Systems.Select(system => system.Name), StringComparer.OrdinalIgnoreCase)
        && original
            .Commanders.Append(original.Owner)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(
                latest
                    .Commanders.Append(latest.Owner)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase
            )
        && original
            .FleetCarriers.Select(carrier => carrier.MarketId)
            .Distinct()
            .Order()
            .SequenceEqual(latest.FleetCarriers.Select(carrier => carrier.MarketId).Distinct().Order());

    private void ExplainFailure(Exception exception)
    {
        if (
            exception is RavenColonialServiceException
            {
                StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            }
        )
        {
            uncertainCreation = false;
        }
        if (IsEditing)
        {
            Status =
                "Some Nexus changes could not be saved. Check the entries, your membership and connection, then choose Save changes to retry.";
        }
        else if (created is not null)
        {
            Status =
                "The Nexus was created, but some details could not be saved. Correct the entries and choose Finish Nexus setup to retry this same Nexus.";
        }
        else if (uncertainCreation)
        {
            Status =
                "Raven may have created this Nexus, but its acknowledgement was lost. Close this window and refresh your Nexus projects before trying to create it again.";
        }
        else
        {
            Status =
                "Raven Colonial rejected creation. Check the Nexus name, your Raven API key and connection, then try again.";
        }
    }

    private bool IsCurrent => !disposed && !lifetime.IsCancellationRequested && isCurrent();

    private static bool IsServiceFailure(Exception exception) =>
        exception is HttpRequestException or IOException or InvalidDataException or OperationCanceledException;

    private static bool HasDuplicates(SpanshSystemListViewModel list) =>
        list.Rows.Select(row => row.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Rows.Count;

    private void AddCarrier()
    {
        if (SelectedCarrier is { } carrier)
        {
            Carriers.Add(carrier);
            addCarrierCommand.Refresh();
        }
    }

    private void EntriesChanged(object? sender, PropertyChangedEventArgs args) => Changed(nameof(CanSubmit));

    private void RefreshAvailability()
    {
        Changed(nameof(CanEdit));
        Changed(nameof(CanEditName));
        Changed(nameof(CanFindCarrier));
        Changed(nameof(CanSubmit));
        Changed(nameof(CloseLabel));
        Changed(nameof(CanDelete));
        Changed(nameof(CanConfirmDeletion));
        addCarrierCommand.Refresh();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        lifetime.Cancel();
        lifetime.Dispose();
        Systems.PropertyChanged -= EntriesChanged;
        Commanders.PropertyChanged -= EntriesChanged;
        RefreshAvailability();
    }
}
