using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class ReleaseUpdateViewModel : INotifyPropertyChanged
{
    private readonly IReleaseUpdateService service;
    private readonly Version currentVersion;
    private readonly AsyncCommand checkCommand;
    private readonly AsyncCommand openReleaseCommand;
    private readonly AsyncCommand installCommand;
    private Func<Uri, Task<bool>>? uriLauncher;
    private InstallerContext? installer;
    private Uri? releaseUri;
    private CrossPlatformReleasePackage? releasePackage;
    private Version? releaseVersion;
    private string latestVersion = "Not checked";
    private string statusMessage = "Update status has not been checked.";
    private string installProgressText = string.Empty;
    private bool isChecking;
    private bool isUpdateAvailable;
    private bool isInstalling;
    private bool installConfirmed;
    private double installProgressPercent;

    public ReleaseUpdateViewModel(
        IReleaseUpdateService service,
        Version currentVersion)
    {
        this.service = service;
        this.currentVersion = currentVersion;
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
        CheckCommand = checkCommand;
        OpenReleaseCommand = openReleaseCommand;
        InstallCommand = installCommand;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CheckCommand { get; }

    public ICommand OpenReleaseCommand { get; }

    public ICommand InstallCommand { get; }

    public string CurrentVersion => FormatVersion(currentVersion);

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

    public bool IsUpdateAvailable
    {
        get => isUpdateAvailable;
        private set
        {
            if (SetField(ref isUpdateAvailable, value))
            {
                OnPropertyChanged(nameof(IsCurrent));
                OnPropertyChanged(nameof(ShowInstallUnavailable));
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

    public bool CanInstallCurrentInstallation => installer?.IsPackaged == true;

    public bool ShowInstallUnavailable =>
        IsUpdateAvailable && !CanInstallCurrentInstallation;

    public bool CanInstall => IsUpdateAvailable
        && CanInstallCurrentInstallation
        && releasePackage is not null
        && releaseVersion is not null
        && InstallConfirmed
        && !IsChecking
        && !IsInstalling;

    public bool IsCurrent => !IsUpdateAvailable;

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

    public void ConfigureInstaller(
        IReleasePackageDownloadService downloadService,
        IReleasePackageStagingService stagingService,
        IReleaseInstallationPreparer installationPreparer,
        IApplicationUpdateHandoffService handoffService,
        string dataDirectory,
        string installationDirectory,
        IReadOnlyList<string> startupArguments,
        Func<Task> shutdown)
    {
        ArgumentNullException.ThrowIfNull(downloadService);
        ArgumentNullException.ThrowIfNull(stagingService);
        ArgumentNullException.ThrowIfNull(installationPreparer);
        ArgumentNullException.ThrowIfNull(handoffService);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        ArgumentNullException.ThrowIfNull(startupArguments);
        ArgumentNullException.ThrowIfNull(shutdown);
        var fullInstallationDirectory = Path.GetFullPath(installationDirectory);
        installer = new InstallerContext(
            downloadService,
            stagingService,
            installationPreparer,
            handoffService,
            Path.GetFullPath(dataDirectory),
            fullInstallationDirectory,
            startupArguments.ToArray(),
            shutdown,
            File.Exists(Path.Combine(
                fullInstallationDirectory,
                "release-package.json")));
        OnPropertyChanged(nameof(CanInstallCurrentInstallation));
        OnPropertyChanged(nameof(ShowInstallUnavailable));
        installCommand.RaiseCanExecuteChanged();
    }

    public async Task CheckAsync()
    {
        if (IsChecking || IsInstalling)
        {
            return;
        }

        IsChecking = true;
        try
        {
            var result = await service.CheckAsync(currentVersion);
            releaseUri = result.ReleaseUri;
            releasePackage = result.Package;
            releaseVersion = result.IsUpdateAvailable
                ? result.LatestVersion
                : null;
            InstallConfirmed = false;
            LatestVersion = FormatVersion(result.LatestVersion);
            IsUpdateAvailable = result.IsUpdateAvailable;
            StatusMessage = result.IsUpdateAvailable
                ? CanInstallCurrentInstallation
                    ? $"SrvSurvey {LatestVersion} is available. Confirm the guarded install when ready."
                    : $"SrvSurvey {LatestVersion} is available. This development or unpackaged build cannot replace itself; use Open releases."
                : $"SrvSurvey {CurrentVersion} is current with the published release index.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            releaseUri = null;
            releasePackage = null;
            releaseVersion = null;
            InstallConfirmed = false;
            LatestVersion = "Unavailable";
            IsUpdateAvailable = false;
            StatusMessage = "The update check was unavailable: "
                + exception.Message
                + " The installation and profile were not changed.";
        }
        finally
        {
            IsChecking = false;
            openReleaseCommand.RaiseCanExecuteChanged();
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
                : "Automatic installation is available only from a checksum-indexed SrvSurvey package. No files were changed.";
            return;
        }

        IsInstalling = true;
        InstallProgressPercent = 0;
        InstallProgressText = "Starting verified package download...";
        StatusMessage =
            "Downloading into the update cache. The installation and profile are still untouched.";
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
            var download = await installer.DownloadService.DownloadAsync(
                releaseVersion,
                releasePackage,
                installer.DataDirectory,
                progress);
            progress.Close();
            InstallProgressPercent = 100;
            InstallProgressText = "Download hash verified; validating archive files...";
            StatusMessage =
                "The package hash is valid. Extracting to an isolated staging directory.";
            var staged = await installer.StagingService.StageAsync(
                releaseVersion,
                releasePackage,
                download.ArchivePath,
                installer.DataDirectory);
            InstallProgressText =
                $"Verified {staged.FileCount:N0} staged files; preparing rollback...";
            StatusMessage =
                "The staged package is valid. Preparing a same-volume candidate without changing the running installation.";
            var preparation = await installer.InstallationPreparer.PrepareAsync(
                releaseVersion,
                releasePackage.RuntimeIdentifier,
                staged.ReadyDirectory,
                staged.ManifestSha256,
                installer.InstallationDirectory,
                installer.StartupArguments);
            InstallProgressText =
                "Rollback candidate verified; starting external helper...";
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

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is HttpRequestException
            or IOException
            or InvalidDataException
            or JsonException
            or TaskCanceledException
            or InvalidOperationException
            or PlatformNotSupportedException;
    }

    private static string FormatVersion(Version version)
    {
        return version.Revision > 0
            ? version.ToString(4)
            : version.Build >= 0
                ? version.ToString(3)
                : version.ToString(2);
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

    private sealed record InstallerContext(
        IReleasePackageDownloadService DownloadService,
        IReleasePackageStagingService StagingService,
        IReleaseInstallationPreparer InstallationPreparer,
        IApplicationUpdateHandoffService HandoffService,
        string DataDirectory,
        string InstallationDirectory,
        IReadOnlyList<string> StartupArguments,
        Func<Task> Shutdown,
        bool IsPackaged);

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
