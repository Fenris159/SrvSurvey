using System.Net;
using System.Windows.Input;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Owns authenticated Nexus navigation and discards requests superseded by profile or selection changes.</summary>
public sealed partial class ColonizationNexusViewModel : WorkspaceObservable, IDisposable
{
    private readonly IRavenColonialNexusReader? reader;
    private readonly Action<ColonizationProject> openProject;
    private readonly ColonizationBuildCatalog catalog;
    private readonly WorkspaceCommand refreshCommand;
    private readonly WorkspaceCommand privacyCommand;
    private Action? cancelRequest;
    private bool enabled;
    private bool disposed;
    private bool isBusy;
    private string? commander;
    private string? apiKey;
    private string statusMessage = "Enable Colonization and configure this commander's Raven API key in Settings.";
    private IReadOnlyList<ColonizationNexusSummary> nexuses = [];
    private ColonizationNexusSummary? selectedNexus;
    private ColonizationNexusDetailViewModel? detail;

    public ColonizationNexusViewModel(
        IRavenColonialNexusReader? reader,
        ColonizationBuildCatalog catalog,
        Action<ColonizationProject> openProject
    )
    {
        this.reader = reader;
        this.catalog = catalog;
        this.openProject = openProject;
        editCommand = new WorkspaceCommand(() => editOpener?.Invoke(), () => CanEditSelected && editOpener is not null);
        refreshCommand = new WorkspaceCommand(Activate, () => CanLoad && !IsBusy);
        privacyCommand = new WorkspaceCommand(() => ActiveTask = TogglePrivacyAsync(), () => CanEditSelected);
        createCommand = new WorkspaceCommand(
            () => createOpener?.Invoke(),
            () => CanLoad && reader is IRavenColonialNexusWriter && createOpener is not null
        );
        RemoveSystemCommand = new WorkspaceParameterCommand(value =>
        {
            if (value is ColonizationNexusScope system)
            {
                RequestSystemRemoval(system);
            }
        });
        confirmSystemRemovalCommand = new WorkspaceCommand(
            () => ActiveTask = ConfirmSystemRemovalAsync(),
            () => IsSystemRemovalVisible && CanRemoveSystems
        );
        CancelSystemRemovalCommand = new WorkspaceCommand(ClearSystemRemoval, () => !IsBusy);
    }

    public ICommand RefreshCommand => refreshCommand;
    public ICommand TogglePrivacyCommand => privacyCommand;
    public bool IsPublic => Detail?.Nexus.Open == true;
    public string PrivacyHint =>
        IsPublic
            ? "Public Nexus. Click to make the entire Nexus private."
            : "Private Nexus. Click to make the entire Nexus public.";
    public Task ActiveTask { get; private set; } = Task.CompletedTask;
    public IReadOnlyList<ColonizationNexusSummary> Nexuses
    {
        get => nexuses;
        private set => Set(ref nexuses, value);
    }
    public ColonizationNexusDetailViewModel? Detail
    {
        get => detail;
        private set
        {
            if (Set(ref detail, value))
            {
                Changed(nameof(HasDetail));
                Changed(nameof(IsPublic));
                Changed(nameof(PrivacyHint));
                editCommand.Refresh();
                privacyCommand.Refresh();
                RefreshSystemRemoval();
            }
        }
    }
    public bool HasDetail => Detail is not null;
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (Set(ref isBusy, value))
            {
                refreshCommand.Refresh();
                editCommand.Refresh();
                privacyCommand.Refresh();
                RefreshSystemRemoval();
            }
        }
    }
    public string StatusMessage
    {
        get => statusMessage;
        private set => Set(ref statusMessage, value);
    }
    private bool CanLoad => !disposed && enabled && reader is not null && commander is not null && apiKey is not null;

    public ColonizationNexusSummary? SelectedNexus
    {
        get => selectedNexus;
        set
        {
            if (value is not null && Nexuses.Contains(value) && Set(ref selectedNexus, value))
            {
                ClearSystemRemoval();
                Detail = null;
                ActiveTask = LoadAsync(reloadIndex: false, CancellationToken.None);
            }
        }
    }

    public void SetContext(bool consent, string? commanderName, string? key)
    {
        if (disposed)
        {
            return;
        }
        string? name = string.IsNullOrWhiteSpace(commanderName) ? null : commanderName.Trim();
        string? credential = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        if (enabled == consent && commander == name && apiKey == credential)
        {
            return;
        }
        CancelRequest();
        ClearSystemRemoval();
        contextVersion++;
        ContextChanged?.Invoke();
        enabled = consent;
        commander = name;
        apiKey = credential;
        Nexuses = [];
        selectedNexus = null;
        Changed(nameof(SelectedNexus));
        Detail = null;
        StatusMessage = CanLoad
            ? "Open Nexus or select Refresh Nexus to load your projects."
            : "Enable Colonization and configure this commander's Raven API key in Settings.";
        refreshCommand.Refresh();
        editCommand.Refresh();
        createCommand.Refresh();
        privacyCommand.Refresh();
    }

    internal void ReportLinkFailure() => StatusMessage = "Could not open Raven Colonial in your browser.";

    public void Activate() => ActiveTask = RefreshAsync();

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(reloadIndex: true, cancellationToken);

    private async Task LoadAsync(bool reloadIndex, CancellationToken cancellationToken)
    {
        if (!CanLoad)
        {
            return;
        }
        CancelRequest();
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Action cancel = session.Cancel;
        cancelRequest = cancel;
        IsBusy = true;
        StatusMessage = "Loading Nexus projects from Raven Colonial...";
        string name = commander!;
        string key = apiKey!;
        try
        {
            if (reloadIndex)
            {
                await LoadIndexAsync(name, key, cancel, session.Token);
            }
            session.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(cancel))
            {
                return;
            }
            if (selectedNexus is null)
            {
                StatusMessage = "No Nexus projects are linked to this commander.";
                return;
            }
            ColonizationNexus? snapshot = await reader!.GetNexusAsync(selectedNexus.Id, name, key, session.Token);
            session.Token.ThrowIfCancellationRequested();
            if (!IsCurrent(cancel))
            {
                return;
            }
            Detail = snapshot is null
                ? null
                : MakeDetail(
                    snapshot,
                    Detail?.SelectedScope.SystemAddress ?? 0,
                    Detail?.SelectedSystem?.SystemAddress ?? 0
                );
            StatusMessage = snapshot is null
                ? "This Nexus is no longer available, or access has changed. Refresh the Nexus list to try again."
                : $"Updated from Raven Colonial at {DateTimeOffset.Now:t}.";
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested)
        {
            if (IsCurrent(cancel))
            {
                StatusMessage = "Nexus refresh cancelled. Select Refresh Nexus to try again.";
            }
        }
        catch (Exception exception)
            when (exception
                    is HttpRequestException
                        or IOException
                        or InvalidDataException
                        or OverflowException
                        or OperationCanceledException
            )
        {
            if (IsCurrent(cancel))
            {
                StatusMessage = DescribeFailure(exception);
            }
        }
        finally
        {
            if (IsCurrent(cancel))
            {
                cancelRequest = null;
                IsBusy = false;
            }
        }
    }

    private async Task LoadIndexAsync(string name, string key, Action cancel, CancellationToken token)
    {
        IReadOnlyList<ColonizationNexusSummary> entries = await reader!.GetCommanderNexusesAsync(name, key, token);
        token.ThrowIfCancellationRequested();
        if (!IsCurrent(cancel))
        {
            return;
        }
        Nexuses = entries;
        ColonizationNexusSummary? selection =
            entries.FirstOrDefault(entry => entry.Id == selectedNexus?.Id) ?? (entries.Count > 0 ? entries[0] : null);
        if (selection?.Id != Detail?.Nexus.Id)
        {
            Detail = null;
        }
        selectedNexus = selection;
        Changed(nameof(SelectedNexus));
    }

    private string DescribeFailure(Exception exception)
    {
        if (
            exception is RavenColonialServiceException
            {
                StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            }
        )
        {
            return "Raven Colonial denied access. Check this commander's Raven API key and Nexus membership.";
        }
        return Detail is null
            ? "Nexus data could not be loaded. Check your connection and select Refresh Nexus to retry."
            : "Nexus refresh failed. The previous snapshot remains displayed; select Refresh Nexus to retry.";
    }

    private bool IsCurrent(Action cancel) => !disposed && ReferenceEquals(cancelRequest, cancel);

    private void CancelRequest()
    {
        Action? cancel = cancelRequest;
        cancelRequest = null;
        cancel?.Invoke();
        IsBusy = false;
    }

    public void Dispose()
    {
        ClearSystemRemoval();
        disposed = true;
        contextVersion++;
        ContextChanged?.Invoke();
        CancelRequest();
        apiKey = null;
        Nexuses = [];
        Detail = null;
        refreshCommand.Refresh();
        editCommand.Refresh();
        createCommand.Refresh();
        privacyCommand.Refresh();
    }
}
