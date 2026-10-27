using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>One carrier's explicit manifest edits, isolated from automatic journal cargo synchronization.</summary>
public sealed partial class ColonizationCarrierManifestViewModel : WorkspaceObservable, IDisposable
{
    private readonly IRavenColonialCarrierManifestClient client;
    private readonly string commander;
    private readonly string apiKey;
    private readonly Func<bool> isCurrentContext;
    private readonly Action<ColonizationFleetCarrier> saved;
    private readonly ColonizationLocalCarrierCargo localCargo;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<CarrierManifestRowViewModel> rows = [];
    private ColonizationFleetCarrier? carrier;
    private CarrierManifestChoice? selectedCarrier;
    private bool isBusy;
    private bool disposed;
    private bool alphabetical;
    private bool textMode;
    private string systemName = string.Empty;
    private string bulkText = string.Empty;
    private string status = string.Empty;
    private ColonizationManifestCommodity? newCommodity;
    private bool hideNonColonyCommodities;

    public bool HideNonColonyCommodities
    {
        get => hideNonColonyCommodities;
        set
        {
            if (Set(ref hideNonColonyCommodities, value))
            {
                RebuildGroups();
            }
        }
    }

    public ColonizationCarrierManifestViewModel(
        IRavenColonialCarrierManifestClient client,
        string commander,
        string apiKey,
        Func<bool> isCurrentContext,
        Action<ColonizationFleetCarrier> saved,
        ColonizationLocalCarrierCargo? localCargo = null,
        ColonizationCarrierRefreshCooldown? refreshCooldown = null
    )
    {
        this.client = client;
        this.commander = commander;
        this.apiKey = apiKey;
        this.isCurrentContext = isCurrentContext;
        this.saved = saved;
        this.localCargo = localCargo ?? new ColonizationLocalCarrierCargo();
        this.refreshCooldown = refreshCooldown ?? new ColonizationCarrierRefreshCooldown();
        this.localCargo.Changed += RefreshJournalQuantities;
        SortCommand = new WorkspaceCommand(ToggleSort);
        ToggleTextCommand = new WorkspaceCommand(ToggleText);
    }

    public long PreferredCarrierMarketId { get; init; }

    public event Action? CloseRequested;
    public IReadOnlyList<CarrierManifestChoice> Carriers { get; private set; } = [];
    public CarrierManifestChoice? SelectedCarrier
    {
        get => selectedCarrier;
        set
        {
            if (!IsBusy && Set(ref selectedCarrier, value))
            {
                carrier = null;
                ReplaceRows([]);
                NotifyCarrier();
            }
        }
    }
    public string RawName => carrier?.Name ?? string.Empty;
    public string MarketId => carrier?.MarketId.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    public string Owner => carrier?.Owner ?? string.Empty;
    public string Access =>
        carrier?.Access == "SquadronAndFriends" ? "Squadron and Friends" : carrier?.Access ?? "Unknown";
    public string Notorious => carrier?.Notorious == true ? "Allowed" : "Denied";
    public bool HasCarrier => carrier is not null;
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            Set(ref isBusy, value);
            Changed(nameof(HasActivity));
            NotifyState();
        }
    }
    public bool CanEdit => HasCarrier && !IsBusy && !disposed;
    public bool CanSave =>
        CanEdit
        && !TextMode
        && rows.All(row => row.IsValid)
        && (!string.IsNullOrWhiteSpace(SystemName) || string.IsNullOrWhiteSpace(carrier?.SystemName))
        && isCurrentContext();
    public bool HasActivity => IsBusy || Status.Length > 0;
    public bool HasInvalidAmounts => rows.Any(row => !row.IsValid);
    public string Status
    {
        get => status;
        private set
        {
            Set(ref status, value);
            Changed(nameof(HasActivity));
        }
    }
    public string SystemName
    {
        get => systemName;
        set
        {
            if (Set(ref systemName, value ?? string.Empty))
            {
                NotifyState();
            }
        }
    }
    public IReadOnlyList<CarrierManifestGroup> Groups { get; private set; } = [];
    public IReadOnlyList<ColonizationManifestCommodity> AvailableCommodities { get; private set; } = [];
    public IReadOnlyList<CarrierManifestCommodityChoice> CommodityChoices { get; private set; } = [];
    public ColonizationManifestCommodity? NewCommodity
    {
        get => newCommodity;
        set => Set(ref newCommodity, value);
    }
    public string Total =>
        rows.Where(row => row.IsValid).Sum(row => (long)row.Amount).ToString("N0", CultureInfo.CurrentCulture);
    public string JournalTotal =>
        carrier is null
            ? "N/A"
            : localCargo.GetTotal(carrier, rows.Select(row => row.Key))?.ToString("N0", CultureInfo.CurrentCulture)
                ?? "N/A";
    public string SortLabel => alphabetical ? "A–Z" : "↕";
    public ICommand SortCommand { get; }
    public ICommand ToggleTextCommand { get; }
    public bool TextMode
    {
        get => textMode;
        private set
        {
            Set(ref textMode, value);
            NotifyState();
        }
    }
    public string BulkText
    {
        get => bulkText;
        set => Set(ref bulkText, value);
    }

    public Task InitializeAsync() =>
        RunAsync(async token =>
        {
            IReadOnlyList<ColonizationFleetCarrier> linked = await client.GetLinkedFleetCarriersAsync(commander, token);
            if (!IsActive)
            {
                return;
            }
            Carriers = linked
                .Where(item => item.MarketId > 0)
                .DistinctBy(item => item.MarketId)
                .Select(item => new CarrierManifestChoice(
                    item.MarketId,
                    string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name : item.DisplayName
                ))
                .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Changed(nameof(Carriers));
            selectedCarrier =
                Carriers.FirstOrDefault(choice => choice.MarketId == PreferredCarrierMarketId)
                ?? (Carriers.Count > 0 ? Carriers[0] : null);
            Changed(nameof(SelectedCarrier));
            if (selectedCarrier is null)
            {
                Status = "No Fleet Carriers are linked to this commander.";
                return;
            }
            await ReadCarrierAsync(token);
        });

    public Task LoadSelectedAsync() => RunAsync(ReadCarrierAsync);

    private async Task ReadCarrierAsync(CancellationToken token)
    {
        if (SelectedCarrier is null)
        {
            return;
        }
        ColonizationFleetCarrier? loaded = await client.GetFleetCarrierAsync(SelectedCarrier.MarketId, token);
        if (!IsActive)
        {
            return;
        }
        if (loaded is null)
        {
            Status = "This Fleet Carrier is no longer available.";
            return;
        }
        Apply(loaded);
    }

    public Task SaveAsync() =>
        CanSave
            ? RunAsync(async token =>
            {
                ColonizationFleetCarrier original = carrier!;
                Dictionary<string, int> cargo = ColonizationCarrierInventory
                    .FromRaven(original)
                    .CreateRavenReplacement(rows.Select(row => new KeyValuePair<string, int>(row.Key, row.Amount)));
                string location = SystemName.Trim();
                if (location.Length > 0 && !location.Equals(original.SystemName, StringComparison.OrdinalIgnoreCase))
                {
                    original = await client.SetFleetCarrierLocationAsync(original.MarketId, location, apiKey, token);
                }
                // A profile change or window close must stop the second write, even if a transport ignores cancellation.
                if (!IsActive)
                {
                    return;
                }
                IReadOnlyDictionary<string, int> updated = await client.ReplaceFleetCarrierCargoAsync(
                    original.MarketId,
                    cargo,
                    apiKey,
                    token
                );
                if (!IsActive)
                {
                    return;
                }
                saved(original with { Cargo = new Dictionary<string, int>(updated, StringComparer.Ordinal) });
                CloseRequested?.Invoke();
            })
            : Task.CompletedTask;

    public void AddCommodity()
    {
        if (
            !CanEdit
            || NewCommodity is not { } choice
            || rows.Any(row => row.Key.Equals(choice.Key, StringComparison.OrdinalIgnoreCase))
        )
        {
            return;
        }
        rows.Add(CreateRow(choice.Key, 0));
        NewCommodity = null;
        RebuildGroups();
    }

    private CarrierManifestRowViewModel CreateRow(string key, int amount)
    {
        var createdRow = new CarrierManifestRowViewModel(
            key,
            amount,
            () =>
            {
                Changed(nameof(Total));
                NotifyState();
            },
            row =>
            {
                if (CanEdit)
                {
                    rows.Remove(row);
                    RebuildGroups();
                }
            }
        );
        createdRow.UpdateJournalQuantity(carrier is null ? null : localCargo.GetQuantity(carrier, key));
        return createdRow;
    }

    private void AddLocalCommodities()
    {
        if (carrier is null)
        {
            return;
        }

        bool added = false;
        foreach (string key in localCargo.GetCommodities(carrier))
        {
            if (rows.Any(row => ColonizationManifestCommodity.ForKey(row.Key).Key == key))
            {
                continue;
            }

            rows.Add(CreateRow(key, 0));
            added = true;
        }
        if (added)
        {
            RebuildGroups();
        }
    }

    private void RefreshJournalQuantities()
    {
        AddLocalCommodities();
        foreach (CarrierManifestRowViewModel row in rows)
        {
            row.UpdateJournalQuantity(carrier is null ? null : localCargo.GetQuantity(carrier, row.Key));
        }
        Changed(nameof(JournalTotal));
    }

    private void Apply(ColonizationFleetCarrier loaded)
    {
        carrier = loaded;
        SystemName = loaded.SystemName ?? string.Empty;
        TextMode = false;
        ReplaceRows(loaded.Cargo);
        NotifyCarrier();
    }

    private void ReplaceRows(IEnumerable<KeyValuePair<string, int>> cargo)
    {
        rows.Clear();
        rows.AddRange(
            ColonizationCarrierInventory.NormalizeStock(cargo).Select(pair => CreateRow(pair.Key, pair.Value))
        );
        AddLocalCommodities();
        RebuildGroups();
    }

    private void ToggleSort()
    {
        alphabetical = !alphabetical;
        Changed(nameof(SortLabel));
        RebuildGroups();
    }

    private void ToggleText()
    {
        if (!CanEdit)
        {
            return;
        }
        if (!TextMode)
        {
            BulkText = string.Join(
                Environment.NewLine,
                rows.OrderBy(row => row.Name).Select(row => $"{BulkName(row)} {row.AmountText}")
            );
            Status = string.Empty;
            TextMode = true;
            return;
        }
        TextMode = false;
        var cargo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (
            string line in BulkText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        )
        {
            int separator = line.LastIndexOf(' ');
            if (
                separator < 1
                || !int.TryParse(
                    line[(separator + 1)..],
                    NumberStyles.Integer | NumberStyles.AllowThousands,
                    CultureInfo.CurrentCulture,
                    out int amount
                )
                || amount < 0
            )
            {
                Status =
                    "Text changes were not applied. Enter one commodity and a nonnegative whole-tonne amount per line.";
                return;
            }
            string name = line[..separator].Trim();
            string? key =
                rows.FirstOrDefault(row => row.Key.Equals(name, StringComparison.OrdinalIgnoreCase))?.Key
                ?? rows.FirstOrDefault(row => row.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Key
                ?? ColonizationManifestCommodity
                    .All.FirstOrDefault(item =>
                        item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                        || item.Key.Equals(name, StringComparison.OrdinalIgnoreCase)
                    )
                    ?.Key;
            if (key is null)
            {
                Status = $"Text changes were not applied. Unknown commodity: {name}. Choose a commodity from the list.";
                return;
            }
            if (!cargo.TryAdd(key, amount))
            {
                Status = "Text changes were not applied. Each commodity can appear only once.";
                return;
            }
        }
        ReplaceRows(cargo);
        Status = string.Empty;
    }

    private string BulkName(CarrierManifestRowViewModel row) =>
        rows.Count(other => other.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase)) > 1 ? row.Key : row.Name;

    private void RebuildGroups()
    {
        Groups = rows.Where(row =>
                !HideNonColonyCommodities || ColonizationManifestCommodity.ForKey(row.Key).IsColonyCommodity
            )
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(row => alphabetical ? string.Empty : row.Category)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CarrierManifestGroup(group.Key, group.ToArray()))
            .ToArray();
        AvailableCommodities = ColonizationManifestCommodity
            .All.Where(item => !HideNonColonyCommodities || item.IsColonyCommodity)
            .DistinctBy(item => item.Name)
            .Where(item =>
                rows.All(row =>
                    !ColonizationManifestCommodity
                        .ForKey(row.Key)
                        .Key.Equals(item.Key, StringComparison.OrdinalIgnoreCase)
                )
            )
            .OrderBy(item => alphabetical ? string.Empty : item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CommodityChoices = AvailableCommodities
            .GroupBy(item => alphabetical ? string.Empty : item.Category)
            .SelectMany(group =>
                (alphabetical ? [] : new[] { new CarrierManifestCommodityChoice(group.Key, null) }).Concat(
                    group.Select(item => new CarrierManifestCommodityChoice(item.Name, item))
                )
            )
            .ToArray();
        Changed(nameof(Groups));
        Changed(nameof(AvailableCommodities));
        Changed(nameof(CommodityChoices));
        Changed(nameof(Total));
        Changed(nameof(JournalTotal));
        NotifyState();
    }

    private bool IsActive => !disposed && !lifetime.IsCancellationRequested && isCurrentContext();

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy || !IsActive)
        {
            return;
        }
        IsBusy = true;
        Status = string.Empty;
        try
        {
            await operation(lifetime.Token);
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or IOException
                        or InvalidDataException
                        or UnauthorizedAccessException
                        or JsonException
                        or OperationCanceledException
            )
        {
            if (IsActive)
            {
                Status = $"The carrier operation failed: {exception.Message}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyCarrier()
    {
        foreach (
            string property in new[]
            {
                nameof(RawName),
                nameof(MarketId),
                nameof(Owner),
                nameof(Access),
                nameof(Notorious),
                nameof(HasCarrier),
            }
        )
        {
            Changed(property);
        }
        NotifyState();
    }

    private void NotifyState()
    {
        Changed(nameof(HasInvalidAmounts));
        Changed(nameof(CanEdit));
        Changed(nameof(CanSave));
        Changed(nameof(CanRefresh));
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        localCargo.Changed -= RefreshJournalQuantities;
        lifetime.Cancel();
        lifetime.Dispose();
        NotifyState();
    }
}

public sealed record CarrierManifestChoice(long MarketId, string Label);

public sealed record CarrierManifestCommodityChoice(string Name, ColonizationManifestCommodity? Commodity)
{
    public bool IsHeader => Commodity is null;
}

public sealed record CarrierManifestGroup(string Name, IReadOnlyList<CarrierManifestRowViewModel> Rows)
{
    public bool HasName => Name.Length > 0;
}

public sealed class CarrierManifestRowViewModel : WorkspaceObservable
{
    private string amountText;
    private long? journalQuantity;
    private readonly Action changed;

    internal CarrierManifestRowViewModel(
        string key,
        int amount,
        Action changed,
        Action<CarrierManifestRowViewModel> remove
    )
    {
        Key = key;
        this.changed = changed;
        amountText = amount.ToString(CultureInfo.InvariantCulture);
        RemoveCommand = new WorkspaceCommand(() => remove(this));
    }

    public string Key { get; }
    public string Name => ColonizationManifestCommodity.ForKey(Key).Name;
    public string Category => ColonizationManifestCommodity.ForKey(Key).Category;
    public long? JournalQuantity => journalQuantity;
    public string JournalAmount => journalQuantity?.ToString("N0", CultureInfo.CurrentCulture) ?? "N/A";

    internal void UpdateJournalQuantity(long? quantity)
    {
        if (Set(ref journalQuantity, quantity, nameof(JournalQuantity)))
        {
            Changed(nameof(JournalAmount));
        }
    }

    public string AmountText
    {
        get => amountText;
        set
        {
            if (Set(ref amountText, value))
            {
                Changed(nameof(IsValid));
                changed();
            }
        }
    }
    public bool IsValid =>
        int.TryParse(AmountText, NumberStyles.Integer, CultureInfo.CurrentCulture, out int amount) && amount >= 0;
    public int Amount => IsValid ? int.Parse(AmountText, CultureInfo.CurrentCulture) : 0;
    public ICommand RemoveCommand { get; }
}
