using System.Windows.Input;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ColonizationSystemEditorViewModel
{
    private readonly ColonizationSettingsStore? catalogStore;
    private readonly Func<DateTimeOffset> catalogUtcNow;
    private readonly AsyncCommand refreshSystemsCommand;
    private readonly DelegateCommand useCurrentSystemCommand;
    private ColonizationSystemEditorContext liveContext = ColonizationSystemEditorContext.Unavailable;
    private ColonizationSystemCatalog? systemCatalog;
    private Action? cancelCatalogRefresh;
    private int catalogContextVersion;
    private bool catalogDisposed;
    private bool isRefreshingSystems;
    private bool hasManualSystemSelection;
    private string systemNameInput = string.Empty;
    private IReadOnlyList<string> systemNames = [];
    private string systemsStatusMessage = string.Empty;

    public ICommand RefreshSystemsCommand => refreshSystemsCommand;

    public ICommand UseCurrentSystemCommand => useCurrentSystemCommand;

    public Task SystemCatalogRefreshTask { get; private set; } = Task.CompletedTask;

    public IReadOnlyList<string> SystemNames => systemNames;

    public string SystemsStatusMessage
    {
        get => systemsStatusMessage;
        private set => SetField(ref systemsStatusMessage, value);
    }

    public bool IsRefreshingSystems
    {
        get => isRefreshingSystems;
        private set
        {
            if (SetField(ref isRefreshingSystems, value))
            {
                OnPropertyChanged(nameof(CanRefreshSystems));
                refreshSystemsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanRefreshSystems =>
        !catalogDisposed
        && !IsRefreshingSystems
        && context.IsExternalDataEnabled
        && !string.IsNullOrWhiteSpace(context.CommanderName)
        && client is IRavenColonialArchitectSystemReader;

    public bool CanUseCurrentSystem =>
        !catalogDisposed
        && !IsBusy
        && liveContext.IsExternalDataEnabled
        && (!string.IsNullOrWhiteSpace(liveContext.SystemName) || liveContext.SystemAddress is > 0);

    public string SystemNameInput
    {
        get => systemNameInput;
        set
        {
            if (SetField(ref systemNameInput, value ?? string.Empty))
            {
                hasManualSystemSelection = true;
                ApplyEditorContext(SelectedSystemContext());
            }
        }
    }

    public void UseCurrentSystem()
    {
        if (CanUseCurrentSystem)
        {
            hasManualSystemSelection = false;
            UpdateContext(liveContext);
        }
    }

    private ColonizationSystemEditorContext SelectedSystemContext()
    {
        string name = SystemNameInput.Trim();
        if (IsLoaded && string.Equals(name, context.SystemName, StringComparison.OrdinalIgnoreCase))
        {
            return liveContext with { SystemName = context.SystemName, SystemAddress = context.SystemAddress };
        }
        ColonizationArchitectSystem? match = systemCatalog?.Systems.FirstOrDefault(system =>
            string.Equals(system.Name, name, StringComparison.OrdinalIgnoreCase)
        );
        long? address = match?.SystemAddress is > 0 ? match.SystemAddress : null;
        if (string.Equals(name, liveContext.SystemName, StringComparison.OrdinalIgnoreCase))
        {
            address = liveContext.SystemAddress;
        }
        return liveContext with { SystemName = name, SystemAddress = address };
    }

    internal void ActivateSystemCatalog() => StartSystemCatalogRefresh(force: false);

    public Task RefreshSystemsAsync() => StartSystemCatalogRefresh(force: true);

    private Task StartSystemCatalogRefresh(bool force)
    {
        if (!CanRefreshSystems)
        {
            return SystemCatalogRefreshTask;
        }
        TimeSpan? age = systemCatalog is null ? null : catalogUtcNow() - systemCatalog.LastAttemptUtc;
        if (!force && age is { } elapsed && elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromDays(1))
        {
            return Task.CompletedTask;
        }
        SystemCatalogRefreshTask = RefreshSystemCatalogCoreAsync();
        return SystemCatalogRefreshTask;
    }

    private async Task RefreshSystemCatalogCoreAsync()
    {
        string commander = context.CommanderName!;
        int version = catalogContextVersion;
        using var cancellation = new CancellationTokenSource();
        Action cancel = cancellation.Cancel;
        // This request owns the source; the editor only borrows a cancellation callback.
        cancelCatalogRefresh = cancel;
        IsRefreshingSystems = true;
        SystemsStatusMessage = "Refreshing architect systems from Raven Colonial...";
        systemCatalog = new(catalogUtcNow(), systemCatalog?.RefreshedAtUtc, systemCatalog?.Systems ?? []);
        try
        {
            catalogStore?.SaveSystemCatalog(commander, systemCatalog);
            IReadOnlyList<ColonizationArchitectSystem> systems = await (
                (IRavenColonialArchitectSystemReader)client
            ).GetArchitectSystemsAsync(commander, cancellation.Token);
            if (catalogDisposed || version != catalogContextVersion)
            {
                return;
            }
            ColonizationSystemCatalog refreshed = systemCatalog with
            {
                RefreshedAtUtc = catalogUtcNow(),
                Systems = systems,
            };
            catalogStore?.SaveSystemCatalog(commander, refreshed);
            systemCatalog = refreshed;
            ApplySystemCatalog();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A different commander or consent change owns the editor now.
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or InvalidDataException
                        or OperationCanceledException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            if (!catalogDisposed && version == catalogContextVersion)
            {
                SystemsStatusMessage =
                    "Systems could not be refreshed. Cached systems remain available: " + exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(cancelCatalogRefresh, cancel))
            {
                cancelCatalogRefresh = null;
                IsRefreshingSystems = false;
            }
        }
    }

    private void RestoreSystemCatalog()
    {
        systemCatalog = liveContext.CommanderName is { } commander ? catalogStore?.LoadSystemCatalog(commander) : null;
        ApplySystemCatalog();
    }

    private void ApplySystemCatalog()
    {
        systemNames = systemCatalog?.Systems.Select(system => system.Name).ToArray() ?? [];
        OnPropertyChanged(nameof(SystemNames));
        SystemsStatusMessage = systemCatalog?.RefreshedAtUtc is { } updated
            ? $"{SystemNames.Count:N0} architect systems cached. Updated {updated.LocalDateTime:g}."
            : "Architect systems are cached for a day. Refresh Systems updates the list now.";
    }

    private void CancelSystemCatalogRefresh()
    {
        catalogContextVersion++;
        cancelCatalogRefresh?.Invoke();
        cancelCatalogRefresh = null;
        IsRefreshingSystems = false;
        if (!catalogDisposed)
        {
            ApplySystemCatalog();
        }
    }

    public void Dispose()
    {
        catalogDisposed = true;
        contextVersion++;
        sessionVersion++;
        CancelSystemCatalogRefresh();
        GC.SuppressFinalize(this);
    }
}
