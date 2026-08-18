using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ReleaseInstallerConfiguration
{
    public required IReleasePackageDownloadService DownloadService { get; init; }

    public required IReleasePackageStagingService StagingService { get; init; }

    public required IReleaseInstallationPreparer InstallationPreparer { get; init; }

    public required IApplicationUpdateHandoffService HandoffService { get; init; }

    public required IApplicationInstanceManager InstanceManager { get; init; }

    public required Func<ApplicationInstanceScan, Task<bool>>
        ConfirmMultipleInstances
    { get; init; }

    public required string DataDirectory { get; init; }

    public required string InstallationDirectory { get; init; }

    public required IReadOnlyList<string> StartupArguments { get; init; }

    public required Func<Task> Shutdown { get; init; }

    public string? AutomaticInstallationUnavailableReason { get; init; }

    public bool IsAppImage { get; init; }
}

public sealed class ReleaseUpdateViewModel : INotifyPropertyChanged
{
    private readonly IReleaseUpdateService service;
    private readonly ReleaseVersion currentVersion;
    private readonly ReleaseUpdateSettingsStore? settingsStore;
    private readonly AsyncCommand checkCommand;
    private readonly AsyncCommand openReleaseCommand;
    private readonly AsyncCommand installCommand;
    private readonly AsyncCommand dismissNotificationCommand;
    private readonly AsyncCommand openUpdateDiagnosticsCommand;
    private Func<Uri, Task<bool>>? uriLauncher;
    private Action? diagnosticsNavigator;
    private InstallerContext? installer;
    private Uri? releaseUri;
    private CrossPlatformReleasePackage? releasePackage;
    private ReleaseVersion? releaseVersion;
    private string releaseNotes = string.Empty;
    private ReleaseVersion? dismissedReleaseVersion;
    private string latestVersion = "Not checked";
    private string statusMessage = "Update status has not been checked.";
    private string installProgressText = string.Empty;
    private string? previousInstallationOutcomeMessage;
    private bool isChecking;
    private bool isUpdateAvailable;
    private bool isInstalling;
    private bool installConfirmed;
    private bool useDevelopmentReleases;
    private bool recheckRequested;
    private double installProgressPercent;

    public ReleaseUpdateViewModel(
        IReleaseUpdateService service,
        ReleaseVersion currentVersion,
        ReleaseUpdateSettingsStore? settingsStore = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.currentVersion = currentVersion;
        this.settingsStore = settingsStore;
        useDevelopmentReleases = settingsStore?.LoadUseDevelopmentReleases()
            ?? true;
        checkCommand = new AsyncCommand(
            CheckAsync,
            () => !IsChecking && !IsInstalling);
        openReleaseCommand = new AsyncCommand(
            OpenReleaseAsync,
            () => IsUpdateAvailable
                && releaseUri is not null
                && uriLauncher is not null
                && !IsInstalling);
        installCommand = new AsyncCommand(
            InstallAsync,
            () => CanInstall);
        dismissNotificationCommand = new AsyncCommand(
            DismissUpdateNotificationAsync,
            () => ShouldShowUpdateNotification);
        openUpdateDiagnosticsCommand = new AsyncCommand(
            OpenUpdateDiagnosticsAsync,
            () => ShouldShowUpdateNotification
                && diagnosticsNavigator is not null);
        CheckCommand = checkCommand;
        OpenReleaseCommand = openReleaseCommand;
        InstallCommand = installCommand;
        DismissNotificationCommand = dismissNotificationCommand;
        OpenUpdateDiagnosticsCommand = openUpdateDiagnosticsCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CheckCommand { get; }

    public ICommand OpenReleaseCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand DismissNotificationCommand { get; }

    public ICommand OpenUpdateDiagnosticsCommand { get; }

    public string CurrentVersion => currentVersion.ToString();

    public string LatestVersion
    {
        get => latestVersion;
        private set => SetField(ref latestVersion, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public bool IsChecking
    {
        get => isChecking;
        private set
        {
            if (SetField(ref isChecking, value))
            {
                OnPropertyChanged(nameof(CheckButtonText));
                checkCommand.RaiseCanExecuteChanged();
                installCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool UseDevelopmentReleases
    {
        get => useDevelopmentReleases;
        set
        {
            if (!SetField(ref useDevelopmentReleases, value))
            {
                return;
            }

            dismissedReleaseVersion = null;
            LatestVersion = "Not checked";
            ClearAvailableRelease();
            RaiseChannelPropertiesChanged();
            try
            {
                settingsStore?.SaveUseDevelopmentReleases(value);
                StatusMessage = $"Switched to the {SelectedChannelName} release channel; checking now.";
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                StatusMessage = "The release channel changed for this session but could not be saved: "
                    + exception.Message;
            }

            if (IsChecking)
            {
                recheckRequested = true;
            }
            else
            {
                _ = CheckAsync();
            }
        }
    }

    public string SelectedChannelName => UseDevelopmentReleases
        ? "development"
        : "stable";

    public string ReleaseSourceDescription => UseDevelopmentReleases
        ? "Development releases are read from Fenris159/SrvSurvey, including RC builds."
        : "Stable SrvSurvey-XP releases are read from njthomson/SrvSurvey.";

    public string OpenReleaseButtonText => UseDevelopmentReleases
        ? "Open development release"
        : "Open stable release";

    public bool IsUpdateAvailable
    {
        get => isUpdateAvailable;
        private set
        {
            if (SetField(ref isUpdateAvailable, value))
            {
                OnPropertyChanged(nameof(IsCurrent));
                OnPropertyChanged(nameof(ShowInstallUnavailable));
                OnPropertyChanged(nameof(ShowGenericInstallUnavailable));
                OnPropertyChanged(nameof(ShowAppImageManualInstall));
                OnPropertyChanged(nameof(HasReleaseNotes));
                RaiseNotificationPropertiesChanged();
                openReleaseCommand.RaiseCanExecuteChanged();
                installCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsInstalling
    {
        get => isInstalling;
        private set
        {
            if (SetField(ref isInstalling, value))
            {
                OnPropertyChanged(nameof(InstallButtonText));
                checkCommand.RaiseCanExecuteChanged();
                openReleaseCommand.RaiseCanExecuteChanged();
                installCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool InstallConfirmed
    {
        get => installConfirmed;
        set
        {
            if (SetField(ref installConfirmed, value))
            {
                installCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public double InstallProgressPercent
    {
        get => installProgressPercent;
        private set => SetField(ref installProgressPercent, value);
    }

    public string InstallProgressText
    {
        get => installProgressText;
        private set => SetField(ref installProgressText, value);
    }

    public bool CanInstallCurrentInstallation => installer is
    {
        IsPackaged: true,
        AutomaticInstallationUnavailableReason: null,
    };

    public bool ShowInstallUnavailable =>
        IsUpdateAvailable && !CanInstallCurrentInstallation;

    public bool ShowGenericInstallUnavailable =>
        ShowInstallUnavailable && installer?.IsAppImage != true;

    public bool ShowAppImageManualInstall =>
        IsUpdateAvailable && installer?.IsAppImage == true;

    public static string AppImageManualInstallInstructions =>
        "Download the AppImage from this release, make it executable, replace your existing AppImage file, and launch it again.";

    public bool CanInstall => IsUpdateAvailable
        && CanInstallCurrentInstallation
        && releasePackage is not null
        && releaseVersion is not null
        && InstallConfirmed
        && !IsChecking
        && !IsInstalling;

    public bool IsCurrent => !IsUpdateAvailable;

    public string ReleaseNotes => releaseNotes;

    public bool HasReleaseNotes => IsUpdateAvailable
        && !string.IsNullOrWhiteSpace(ReleaseNotes);

    public bool ShouldShowUpdateNotification => IsUpdateAvailable
        && releaseVersion is { } available
        && dismissedReleaseVersion != available;

    public string UpdateNotificationText => releaseVersion is { } available
        ? $"SrvSurvey-XP {available} is available on the {SelectedChannelName} channel."
        : string.Empty;

    public string UpdateNotificationActionText => ShowAppImageManualInstall
        ? "Review manual update"
        : "Review update";

    public string CheckButtonText => IsChecking
        ? "Checking..."
        : "Check for updates";

    public string InstallButtonText => IsInstalling
        ? "Preparing update..."
        : "Download, verify, and install";

    public void SetUriLauncher(Func<Uri, Task<bool>>? launchUri)
    {
        uriLauncher = launchUri;
        openReleaseCommand.RaiseCanExecuteChanged();
    }

    public void SetDiagnosticsNavigator(Action? navigate)
    {
        diagnosticsNavigator = navigate;
        openUpdateDiagnosticsCommand.RaiseCanExecuteChanged();
    }

    public void ConfigureInstaller(ReleaseInstallerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configuration.DownloadService);
        ArgumentNullException.ThrowIfNull(configuration.StagingService);
        ArgumentNullException.ThrowIfNull(configuration.InstallationPreparer);
        ArgumentNullException.ThrowIfNull(configuration.HandoffService);
        ArgumentNullException.ThrowIfNull(configuration.InstanceManager);
        ArgumentNullException.ThrowIfNull(configuration.ConfirmMultipleInstances);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.DataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.InstallationDirectory);
        ArgumentNullException.ThrowIfNull(configuration.StartupArguments);
        ArgumentNullException.ThrowIfNull(configuration.Shutdown);
        var fullInstallationDirectory = Path.GetFullPath(
            configuration.InstallationDirectory);
        installer = new InstallerContext
        {
            DownloadService = configuration.DownloadService,
            StagingService = configuration.StagingService,
            InstallationPreparer = configuration.InstallationPreparer,
            HandoffService = configuration.HandoffService,
            InstanceManager = configuration.InstanceManager,
            ConfirmMultipleInstances = configuration.ConfirmMultipleInstances,
            DataDirectory = Path.GetFullPath(configuration.DataDirectory),
            InstallationDirectory = fullInstallationDirectory,
            StartupArguments = configuration.StartupArguments.ToArray(),
            Shutdown = configuration.Shutdown,
            IsPackaged = File.Exists(Path.Combine(
                fullInstallationDirectory,
                "release-package.json")),
            AutomaticInstallationUnavailableReason = string.IsNullOrWhiteSpace(
                configuration.AutomaticInstallationUnavailableReason)
                ? null
                : configuration.AutomaticInstallationUnavailableReason.Trim(),
            IsAppImage = configuration.IsAppImage,
        };
        OnPropertyChanged(nameof(CanInstallCurrentInstallation));
        OnPropertyChanged(nameof(ShowInstallUnavailable));
        OnPropertyChanged(nameof(ShowGenericInstallUnavailable));
        OnPropertyChanged(nameof(ShowAppImageManualInstall));
        RaiseNotificationPropertiesChanged();
        installCommand.RaiseCanExecuteChanged();
    }

    public void SetPreviousInstallationOutcome(
        ReleaseInstallationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        previousInstallationOutcomeMessage = outcome.Status switch
        {
            ReleaseInstallationOutcomeStatus.RolledBack =>
                $"Update {outcome.Version} was rolled back; the previous installation was restored."
                    + (string.IsNullOrWhiteSpace(outcome.Error)
                        ? string.Empty
                        : " " + outcome.Error),
            ReleaseInstallationOutcomeStatus.Aborted =>
                $"Update {outcome.Version} was aborted before completion; the active installation was preserved."
                    + (string.IsNullOrWhiteSpace(outcome.Error)
                        ? string.Empty
                        : " " + outcome.Error),
            _ =>
                $"Update {outcome.Version} installed successfully."
                    + (string.IsNullOrWhiteSpace(outcome.BackupDirectory)
                        ? string.Empty
                        : " Rollback backup: " + outcome.BackupDirectory),
        };
        StatusMessage = AppendPreviousOutcome(StatusMessage);
    }

    public async Task CheckAsync()
    {
        if (IsInstalling)
        {
            return;
        }

        if (IsChecking)
        {
            recheckRequested = true;
            return;
        }

        IsChecking = true;
        var channel = UseDevelopmentReleases
            ? ReleaseChannel.Development
            : ReleaseChannel.Stable;
        try
        {
            var result = await service.CheckAsync(currentVersion, channel);
            if (channel != (UseDevelopmentReleases
                    ? ReleaseChannel.Development
                    : ReleaseChannel.Stable))
            {
                recheckRequested = true;
                return;
            }

            releaseUri = result.ReleaseUri;
            releasePackage = result.Package;
            releaseVersion = result.IsUpdateAvailable
                ? result.LatestVersion
                : null;
            releaseNotes = result.IsUpdateAvailable
                ? result.ReleaseNotes
                : string.Empty;
            OnPropertyChanged(nameof(ReleaseNotes));
            OnPropertyChanged(nameof(HasReleaseNotes));
            InstallConfirmed = false;
            LatestVersion = result.LatestVersion?.ToString() ?? "N/A";
            IsUpdateAvailable = result.IsUpdateAvailable;
            RaiseNotificationPropertiesChanged();
            StatusMessage = AppendPreviousOutcome(GetReleaseStatus(result));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            ClearAvailableRelease();
            LatestVersion = "Unavailable";
            StatusMessage = AppendPreviousOutcome(
                "The update check was unavailable: "
                + exception.Message
                + " The installation and profile were not changed.");
        }
        finally
        {
            IsChecking = false;
            openReleaseCommand.RaiseCanExecuteChanged();
            if (recheckRequested && !IsInstalling)
            {
                recheckRequested = false;
                _ = CheckAsync();
            }
        }
    }

    public async Task InstallAsync()
    {
        if (!CanInstall
            || installer is null
            || releasePackage is null
            || releaseVersion is null)
        {
            StatusMessage = CanInstallCurrentInstallation
                ? "Confirm the guarded update installation first. No files were changed."
                : GetInstallationUnavailableMessage() + " No files were changed.";
            return;
        }

        IsInstalling = true;
        var targetVersion = releaseVersion.Value;
        InstallProgressPercent = 0;
        InstallProgressText = "Checking for other running SrvSurvey instances...";
        StatusMessage = "Checking whether another SrvSurvey instance must close before the update.";
        var handoffStarted = false;
        var progress = new GuardedProgress<ReleasePackageDownloadProgress>(value =>
        {
            InstallProgressPercent = value.TotalBytes <= 0
                ? 0
                : value.DownloadedBytes * 100d / value.TotalBytes;
            InstallProgressText = $"Downloaded {value.DownloadedBytes:N0} of "
                + $"{value.TotalBytes:N0} bytes";
        });
        try
        {
            if (!await CloseOtherInstancesBeforeUpdateAsync(installer))
            {
                InstallProgressText = "Update canceled before download.";
                return;
            }

            InstallProgressText = "Starting verified package download...";
            StatusMessage =
                "Downloading into the update cache. The installation and profile are still untouched.";
            var download = await installer.DownloadService.DownloadAsync(
                targetVersion,
                releasePackage,
                installer.DataDirectory,
                progress);
            progress.Close();
            InstallProgressPercent = 100;
            InstallProgressText = "Download hash verified; validating archive files...";
            StatusMessage =
                "The package hash is valid. Extracting to an isolated staging directory.";
            var staged = await installer.StagingService.StageAsync(
                targetVersion,
                releasePackage,
                download.ArchivePath,
                installer.DataDirectory);
            InstallProgressText =
                $"Verified {staged.FileCount:N0} staged files; preparing rollback...";
            StatusMessage =
                "The staged package is valid. Preparing a same-volume candidate without changing the running installation.";
            var preparation = await installer.InstallationPreparer.PrepareAsync(
                targetVersion,
                releasePackage.RuntimeIdentifier,
                staged.ReadyDirectory,
                staged.ManifestSha256,
                installer.InstallationDirectory,
                installer.StartupArguments);
            InstallProgressText =
                "Rechecking for SrvSurvey instances before update handoff...";
            if (!await CloseOtherInstancesBeforeUpdateAsync(installer))
            {
                await installer.InstallationPreparer.AbortAsync(preparation);
                InstallProgressText = "Update canceled before installation handoff.";
                return;
            }

            InstallProgressText = preparation.RequiresElevation
                ? "Starting elevated update helper; approve the Windows prompt..."
                : "Rollback candidate verified; starting external helper...";
            if (preparation.RequiresElevation)
            {
                StatusMessage =
                    "Windows administrator approval is required to update this protected installation. The player profile remains untouched.";
            }

            await installer.HandoffService.StartHelperAsync(
                installer.DataDirectory,
                preparation,
                staged.EntryPointPath);
            handoffStarted = true;
            InstallConfirmed = false;
            StatusMessage =
                "The verified update helper is waiting. SrvSurvey will now close; the old installation remains available for automatic rollback.";
            await installer.Shutdown();
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            progress.Close();
            StatusMessage = "The guarded update was not started: "
                + exception.Message
                + " The active installation and player profile were not changed.";
            InstallProgressText = "Update preparation stopped safely.";
        }
        finally
        {
            progress.Close();
            if (!handoffStarted)
            {
                IsInstalling = false;
            }
        }
    }

    private async Task<bool> CloseOtherInstancesBeforeUpdateAsync(
        InstallerContext currentInstaller)
    {
        var scan = await currentInstaller.InstanceManager
            .ScanOtherInstancesAsync(CancellationToken.None);
        var otherCount = scan.TotalCount;
        if (otherCount == 0)
        {
            return true;
        }

        if (scan.UnverifiedCount > 0)
        {
            StatusMessage =
                $"SrvSurvey found {otherCount:N0} matching process(es), including "
                + $"{scan.UnverifiedCount:N0} that the operating system would not let it verify. "
                + "Confirm the warning; the update will stop safely if any process remains unverified.";
        }
        else if (otherCount == 1)
        {
            StatusMessage =
                "Another SrvSurvey instance is running. Confirm whether all instances should close before updating.";
        }
        else
        {
            StatusMessage =
                $"{otherCount:N0} other SrvSurvey instances are running. Confirm whether all instances should close before updating.";
        }
        if (!await currentInstaller.ConfirmMultipleInstances(scan))
        {
            StatusMessage =
                "Update canceled. The other SrvSurvey instances remain open and no files were changed.";
            return false;
        }

        InstallProgressText = otherCount == 1
            ? "Closing the other SrvSurvey instance..."
            : $"Closing {otherCount:N0} other SrvSurvey instances...";
        StatusMessage =
            "Closing other SrvSurvey instances before continuing the update.";
        await currentInstaller.InstanceManager
            .CloseOtherInstancesAsync(CancellationToken.None);
        return true;
    }

    private async Task OpenReleaseAsync()
    {
        if (releaseUri is null || uriLauncher is null)
        {
            return;
        }

        if (!await uriLauncher(releaseUri))
        {
            StatusMessage = "The releases page could not be opened. No files were changed.";
        }
    }

    private Task DismissUpdateNotificationAsync()
    {
        dismissedReleaseVersion = releaseVersion;
        RaiseNotificationPropertiesChanged();
        return Task.CompletedTask;
    }

    private Task OpenUpdateDiagnosticsAsync()
    {
        diagnosticsNavigator?.Invoke();
        return DismissUpdateNotificationAsync();
    }

    private string GetReleaseStatus(ReleaseUpdateResult result)
    {
        if (result.LatestVersion is null)
        {
            return result.Channel == ReleaseChannel.Stable
                ? "N/A: no stable SrvSurvey-XP release is published in njthomson/SrvSurvey yet."
                : "N/A: no SrvSurvey-XP development release is published in Fenris159/SrvSurvey yet.";
        }

        if (result.IsUpdateAvailable)
        {
            return CanInstallCurrentInstallation
                ? $"SrvSurvey-XP {LatestVersion} is available. Confirm the guarded install when ready."
                : $"SrvSurvey-XP {LatestVersion} is available. {GetInstallationUnavailableMessage()}";
        }

        return $"SrvSurvey-XP {CurrentVersion} is current on the {SelectedChannelName} channel.";
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is HttpRequestException
            or IOException
            or Win32Exception
            or InvalidDataException
            or JsonException
            or TaskCanceledException
            or InvalidOperationException
            or PlatformNotSupportedException;
    }

    private void ClearAvailableRelease()
    {
        releaseUri = null;
        releasePackage = null;
        releaseVersion = null;
        releaseNotes = string.Empty;
        OnPropertyChanged(nameof(ReleaseNotes));
        OnPropertyChanged(nameof(HasReleaseNotes));
        InstallConfirmed = false;
        IsUpdateAvailable = false;
        RaiseNotificationPropertiesChanged();
        openReleaseCommand.RaiseCanExecuteChanged();
    }

    private void RaiseChannelPropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedChannelName));
        OnPropertyChanged(nameof(ReleaseSourceDescription));
        OnPropertyChanged(nameof(OpenReleaseButtonText));
        RaiseNotificationPropertiesChanged();
    }

    private void RaiseNotificationPropertiesChanged()
    {
        OnPropertyChanged(nameof(ShouldShowUpdateNotification));
        OnPropertyChanged(nameof(UpdateNotificationText));
        OnPropertyChanged(nameof(UpdateNotificationActionText));
        dismissNotificationCommand.RaiseCanExecuteChanged();
        openUpdateDiagnosticsCommand.RaiseCanExecuteChanged();
    }

    private string AppendPreviousOutcome(string message)
    {
        return previousInstallationOutcomeMessage is null
            || message.Contains(
                previousInstallationOutcomeMessage,
                StringComparison.Ordinal)
            ? message
            : message + " " + previousInstallationOutcomeMessage;
    }

    private string GetInstallationUnavailableMessage()
    {
        return installer?.AutomaticInstallationUnavailableReason
            ?? "This development or unpackaged build cannot replace itself; use Open releases.";
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class InstallerContext
    {
        public required IReleasePackageDownloadService DownloadService { get; init; }

        public required IReleasePackageStagingService StagingService { get; init; }

        public required IReleaseInstallationPreparer InstallationPreparer { get; init; }

        public required IApplicationUpdateHandoffService HandoffService { get; init; }

        public required IApplicationInstanceManager InstanceManager { get; init; }

        public required Func<ApplicationInstanceScan, Task<bool>>
            ConfirmMultipleInstances
        { get; init; }

        public required string DataDirectory { get; init; }

        public required string InstallationDirectory { get; init; }

        public required IReadOnlyList<string> StartupArguments { get; init; }

        public required Func<Task> Shutdown { get; init; }

        public bool IsPackaged { get; init; }

        public string? AutomaticInstallationUnavailableReason { get; init; }

        public bool IsAppImage { get; init; }
    }

    private sealed class GuardedProgress<T> : IProgress<T>
    {
        private readonly object gate = new();
        private readonly Action<T> report;
        private readonly Progress<T> progress;
        private bool closed;

        public GuardedProgress(Action<T> report)
        {
            this.report = report;
            progress = new Progress<T>(ReportIfOpen);
        }

        public void Report(T value)
        {
            lock (gate)
            {
                if (closed)
                {
                    return;
                }
            }

            ((IProgress<T>)progress).Report(value);
        }

        public void Close()
        {
            lock (gate)
            {
                closed = true;
            }
        }

        private void ReportIfOpen(T value)
        {
            lock (gate)
            {
                if (!closed)
                {
                    report(value);
                }
            }
        }
    }

    private sealed class AsyncCommand(
        Func<Task> execute,
        Func<bool> canExecute) : ICommand
    {
        private bool isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => !isExecuting && canExecute();

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await execute();
            }
            finally
            {
                isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
