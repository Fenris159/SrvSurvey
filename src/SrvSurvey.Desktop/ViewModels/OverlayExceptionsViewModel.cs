using System.Windows.Input;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class OverlayExceptionsViewModel
{
    private readonly OverlayWindowRegistry registry;
    private string boarded = "unknown";
    public IReadOnlyList<OverlayExceptionCategoryViewModel> Categories { get; }

    public OverlayExceptionsViewModel(OverlayVehicleSettingsStore store, OverlayWindowRegistry? registry = null)
    {
        this.registry = registry ?? OverlayWindowRegistry.Shared;
        string? migrationError = null;
        try
        {
            store.MigrateFiregroupsCategory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            migrationError = ex.Message;
        }
        Categories = Enum.GetValues<OverlaySettingsCategory>()
            .Select(category => new OverlayExceptionCategoryViewModel(category, store, Apply))
            .ToArray();
        if (migrationError is not null)
        {
            ForCategory(OverlaySettingsCategory.Firegroups).Status =
                "Firegroups exceptions were inherited for this session but could not be saved: " + migrationError;
        }

        Apply();
    }

    public OverlayExceptionCategoryViewModel ForCategory(OverlaySettingsCategory category) =>
        Categories.Single(c => c.Category == category);

    public void UpdateBoardedVehicle(JournalSessionState journal, EliteStatus? status)
    {
        var next = OverlayVehicleCatalog.Resolve(journal, status);
        if (next == boarded)
        {
            return;
        }

        boarded = next;
        Apply();
    }

    private void Apply() =>
        registry.SetVehicleExcludedPlotters(
            OverlayLayoutCatalog
                .Supported.Where(panel =>
                    panel.SettingsCategories.Any(category => !ForCategory(category).Allows(boarded))
                )
                .Select(panel => panel.Name)
        );
}

public sealed class OverlayExceptionCategoryViewModel : WorkspaceObservable
{
    private readonly OverlayVehicleSettingsStore store;
    private readonly Action apply;
    private bool batching;
    private string status = "";
    public OverlaySettingsCategory Category { get; }
    public string Title =>
        $"{(Category == OverlaySettingsCategory.Global ? "Status & utilities" : OverlaySettingsCategoryCatalog.All.Single(c => c.Category == Category).DisplayName)} overlay exceptions";
    public IReadOnlyList<OverlayExceptionGroupViewModel> Groups { get; }
    public IEnumerable<OverlayExceptionEntryViewModel> Entries => Groups.SelectMany(g => g.Entries);
    public string Status
    {
        get => status;
        internal set => Set(ref status, value);
    }
    public ICommand CheckAllCommand { get; }
    public ICommand UncheckAllCommand { get; }

    public OverlayExceptionCategoryViewModel(
        OverlaySettingsCategory category,
        OverlayVehicleSettingsStore store,
        Action apply
    )
    {
        Category = category;
        this.store = store;
        this.apply = apply;
        var saved = store.Load(category);
        Groups = OverlayVehicleCatalog
            .ForCategory(category)
            .GroupBy(v => v.Group)
            .Select(group => new OverlayExceptionGroupViewModel(
                group.Key,
                group.Select(v => new OverlayExceptionEntryViewModel(v, saved?.Contains(v.Id) ?? true, Save)).ToArray()
            ))
            .ToArray();
        CheckAllCommand = new WorkspaceCommand(() => SetAll(true));
        UncheckAllCommand = new WorkspaceCommand(() => SetAll(false));
    }

    public bool Allows(string id) => Entries.Any(e => e.Definition.Id == id && e.IsAllowed);

    private void SetAll(bool allowed)
    {
        batching = true;
        try
        {
            foreach (var entry in Entries)
            {
                entry.IsAllowed = allowed;
            }
        }
        finally
        {
            batching = false;
        }
        Save();
    }

    private void Save()
    {
        if (batching)
        {
            return;
        }

        apply();
        try
        {
            store.Save(Category, Entries.Where(e => e.IsAllowed).Select(e => e.Definition.Id));
            Status = "";
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Status = "Exceptions changed for this session but could not be saved: " + exception.Message;
        }
    }
}

public sealed record OverlayExceptionGroupViewModel(string Name, IReadOnlyList<OverlayExceptionEntryViewModel> Entries);

public sealed class OverlayExceptionEntryViewModel(OverlayVehicleDefinition definition, bool allowed, Action save)
    : WorkspaceObservable
{
    private bool isAllowed = allowed;
    public OverlayVehicleDefinition Definition { get; } = definition;
    public string Name => Definition.Name;
    public bool IsAllowed
    {
        get => isAllowed;
        set
        {
            if (Set(ref isAllowed, value))
            {
                save();
            }
        }
    }
}
