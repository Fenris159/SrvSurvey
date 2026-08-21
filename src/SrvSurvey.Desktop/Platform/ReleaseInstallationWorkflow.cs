using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Desktop.Platform;

internal enum ReleaseInstallationCapabilityStatus
{
    Supported,
    Unpackaged,
    ReadOnlyAppImage,
}

internal sealed record ReleaseInstallationCapability(
    ReleaseInstallationCapabilityStatus Status)
{
    public bool CanInstall => Status == ReleaseInstallationCapabilityStatus.Supported;

    public static ReleaseInstallationCapability Detect(
        string installationDirectory,
        bool isAppImage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        if (isAppImage)
        {
            return new ReleaseInstallationCapability(
                ReleaseInstallationCapabilityStatus.ReadOnlyAppImage);
        }

        return new ReleaseInstallationCapability(
            File.Exists(Path.Combine(
                Path.GetFullPath(installationDirectory),
                "release-package.json"))
                ? ReleaseInstallationCapabilityStatus.Supported
                : ReleaseInstallationCapabilityStatus.Unpackaged);
    }
}

internal enum ReleaseInstallationCheckpoint
{
    BeforeDownload,
    BeforeHandoff,
}

internal enum ReleaseInstallationWorkflowStage
{
    None,
    ScanningInstances,
    AwaitingInstanceConfirmation,
    ClosingInstances,
    Downloading,
    ValidatingArchive,
    Staging,
    PreparingRollback,
    StartingHelper,
    AwaitingApplicationExit,
}

internal enum ReleaseInstallationWorkflowStatus
{
    Rejected,
    Cancelled,
    Failed,
    HandoffStarted,
    CleanupFailed,
    OwnershipUnresolved,
}

internal enum ReleaseInstallationRejectionReason
{
    None,
    Unsupported,
    Busy,
    InstancesDeclined,
}

internal enum ReleaseInstallationCleanupStatus
{
    NotRequired,
    Succeeded,
    Failed,
    Transferred,
}

internal sealed record ReleaseInstallationRequest(
    ReleaseVersion Version,
    CrossPlatformReleasePackage Package);

internal sealed record ReleaseInstallationWorkflowProgress(
    ReleaseInstallationWorkflowStage Stage)
{
    public ReleaseInstallationCheckpoint? Checkpoint { get; init; }

    public ApplicationInstanceScan? InstanceScan { get; init; }

    public long DownloadedBytes { get; init; }

    public long TotalBytes { get; init; }

    public int StagedFileCount { get; init; }

    public bool RequiresElevation { get; init; }

    public Exception? Error { get; init; }
}

internal sealed record ReleaseInstallationWorkflowResult(
    ReleaseInstallationWorkflowStatus Status,
    ReleaseInstallationWorkflowStage Stage,
    ReleaseInstallationCleanupStatus CleanupStatus,
    ReleaseInstallationRejectionReason RejectionReason =
        ReleaseInstallationRejectionReason.None,
    Exception? Error = null,
    Exception? CleanupError = null,
    ReleaseInstallationHandoffPlan? HandoffPlan = null);

internal interface IReleaseInstallationWorkflow
{
    ReleaseInstallationCapability Capability { get; }

    Task<ReleaseInstallationWorkflowResult> ExecuteAsync(
        ReleaseInstallationRequest request,
        IProgress<ReleaseInstallationWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

internal delegate Task<bool> ConfirmReleaseInstallationInstances(
    ApplicationInstanceScan scan,
    ReleaseInstallationCheckpoint checkpoint,
    CancellationToken cancellationToken);

internal delegate Task RequestReleaseInstallationShutdown(
    CancellationToken cancellationToken);

internal enum ApplicationUpdateHandoffStatus
{
    NotStarted,
    Started,
    StartedReadinessUnconfirmed,
}

internal sealed record ApplicationUpdateHandoffResult(
    ApplicationUpdateHandoffStatus Status,
    ReleaseInstallationHandoffPlan? Plan,
    Exception? Error = null);

internal interface IApplicationUpdateHandoff
{
    Task<ApplicationUpdateHandoffResult> StartHelperAttemptAsync(
        string dataDirectory,
        ReleaseInstallationPreparation preparation,
        string stagedEntryPoint,
        CancellationToken cancellationToken = default);
}

internal interface IReleaseInstallationOutcomeMonitor
{
    Task<ReleaseInstallationOutcome?> WaitForOutcomeAsync(
        ReleaseInstallationHandoffPlan plan,
        CancellationToken cancellationToken = default);
}

internal sealed record ReleaseInstallationWorkflowAdapters(
    IReleasePackageDownloadService DownloadService,
    IReleasePackageStagingService StagingService,
    IReleaseInstallationPreparer InstallationPreparer,
    IApplicationUpdateHandoff Handoff,
    IApplicationInstanceManager InstanceManager,
    ConfirmReleaseInstallationInstances ConfirmInstances);

internal sealed record ReleaseInstallationWorkflowContext(
    string DataDirectory,
    string InstallationDirectory,
    IReadOnlyList<string> StartupArguments,
    RequestReleaseInstallationShutdown RequestShutdown,
    bool IsAppImage,
    Action<string>? Log = null);

internal sealed record ReleaseInstallationWorkflowSeams(
    IReleaseInstallationOutcomeMonitor OutcomeMonitor,
    Func<bool> IsCurrentProcessRunning,
    Func<string, bool> PathExists,
    ReleaseInstallationCapability Capability);

internal sealed class ReleaseInstallationWorkflow : IReleaseInstallationWorkflow
{
    private readonly IReleasePackageDownloadService downloadService;
    private readonly IReleasePackageStagingService stagingService;
    private readonly IReleaseInstallationPreparer installationPreparer;
    private readonly IApplicationUpdateHandoff handoff;
    private readonly IApplicationInstanceManager instanceManager;
    private readonly ConfirmReleaseInstallationInstances confirmInstances;
    private readonly RequestReleaseInstallationShutdown requestShutdown;
    private readonly IReleaseInstallationOutcomeMonitor outcomeMonitor;
    private readonly Func<bool> isCurrentProcessRunning;
    private readonly Func<string, bool> pathExists;
    private readonly string dataDirectory;
    private readonly string installationDirectory;
    private readonly IReadOnlyList<string> startupArguments;
    private int executionActive;
    private bool retryBlocked;

    internal ReleaseInstallationWorkflow(
        ReleaseInstallationWorkflowAdapters adapters,
        ReleaseInstallationWorkflowContext context)
        : this(
            adapters,
            context,
            new ReleaseInstallationWorkflowSeams(
                new ReleaseInstallationOutcomeMonitor(log: context.Log),
                () => true,
                path => Directory.Exists(path) || File.Exists(path),
                ReleaseInstallationCapability.Detect(
                    context.InstallationDirectory,
                    context.IsAppImage)))
    {
    }

    internal ReleaseInstallationWorkflow(
        ReleaseInstallationWorkflowAdapters adapters,
        ReleaseInstallationWorkflowContext context,
        ReleaseInstallationWorkflowSeams seams)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(seams);
        downloadService = adapters.DownloadService
            ?? throw new ArgumentNullException(nameof(adapters.DownloadService));
        stagingService = adapters.StagingService
            ?? throw new ArgumentNullException(nameof(adapters.StagingService));
        installationPreparer = adapters.InstallationPreparer
            ?? throw new ArgumentNullException(nameof(adapters.InstallationPreparer));
        handoff = adapters.Handoff
            ?? throw new ArgumentNullException(nameof(adapters.Handoff));
        instanceManager = adapters.InstanceManager
            ?? throw new ArgumentNullException(nameof(adapters.InstanceManager));
        confirmInstances = adapters.ConfirmInstances
            ?? throw new ArgumentNullException(nameof(adapters.ConfirmInstances));
        requestShutdown = context.RequestShutdown
            ?? throw new ArgumentNullException(nameof(context.RequestShutdown));
        outcomeMonitor = seams.OutcomeMonitor
            ?? throw new ArgumentNullException(nameof(seams.OutcomeMonitor));
        isCurrentProcessRunning = seams.IsCurrentProcessRunning
            ?? throw new ArgumentNullException(nameof(seams.IsCurrentProcessRunning));
        pathExists = seams.PathExists
            ?? throw new ArgumentNullException(nameof(seams.PathExists));
        Capability = seams.Capability
            ?? throw new ArgumentNullException(nameof(seams.Capability));
        ArgumentException.ThrowIfNullOrWhiteSpace(context.DataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.InstallationDirectory);
        dataDirectory = Path.GetFullPath(context.DataDirectory);
        installationDirectory = Path.GetFullPath(context.InstallationDirectory);
        startupArguments = context.StartupArguments?.ToArray()
            ?? throw new ArgumentNullException(nameof(context.StartupArguments));
    }

    public ReleaseInstallationCapability Capability { get; }

    public async Task<ReleaseInstallationWorkflowResult> ExecuteAsync(
        ReleaseInstallationRequest request,
        IProgress<ReleaseInstallationWorkflowProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Package);
        if (!Capability.CanInstall)
        {
            return Rejected(ReleaseInstallationRejectionReason.Unsupported);
        }

        if (retryBlocked
            || Interlocked.CompareExchange(ref executionActive, 1, 0) != 0)
        {
            return Rejected(ReleaseInstallationRejectionReason.Busy);
        }

        var state = new WorkflowExecutionState();
        try
        {
            return await RunWorkflowAsync(
                    request,
                    progress,
                    state,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            !state.OwnershipTransferred)
        {
            return await FinishBeforeHandoffAsync(
                    ReleaseInstallationWorkflowStatus.Cancelled,
                    state.Stage,
                    state.Preparation,
                    error: exception)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            !state.OwnershipTransferred && IsOperationalFailure(exception))
        {
            return await FinishBeforeHandoffAsync(
                    ReleaseInstallationWorkflowStatus.Failed,
                    state.Stage,
                    state.Preparation,
                    error: exception)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (!state.OwnershipTransferred)
        {
            await CleanupBeforeRethrowAsync(state.Preparation, exception)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (
            state.OwnershipTransferred && IsOperationalFailure(exception))
        {
            retryBlocked = true;
            return OwnershipUnresolved(
                state.Stage,
                exception,
                state.Plan ?? throw new UnreachableException(
                    "Transferred update ownership has no installation plan."));
        }
        catch when (state.OwnershipTransferred)
        {
            retryBlocked = true;
            throw;
        }
        finally
        {
            if (!retryBlocked)
            {
                Interlocked.Exchange(ref executionActive, 0);
            }
        }
    }

    private async Task<ReleaseInstallationWorkflowResult> RunWorkflowAsync(
        ReleaseInstallationRequest request,
        IProgress<ReleaseInstallationWorkflowProgress>? progress,
        WorkflowExecutionState state,
        CancellationToken cancellationToken)
    {
        state.Stage = ReleaseInstallationWorkflowStage.ScanningInstances;
        if (!await CloseOtherInstancesAsync(
                ReleaseInstallationCheckpoint.BeforeDownload,
                progress,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return Rejected(
                ReleaseInstallationRejectionReason.InstancesDeclined,
                state.Stage);
        }

        var prepared = await PrepareCandidateAsync(
                request,
                progress,
                state,
                cancellationToken)
            .ConfigureAwait(false);
        state.Stage = ReleaseInstallationWorkflowStage.ScanningInstances;
        if (!await CloseOtherInstancesAsync(
                ReleaseInstallationCheckpoint.BeforeHandoff,
                progress,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return await FinishBeforeHandoffAsync(
                    ReleaseInstallationWorkflowStatus.Rejected,
                    state.Stage,
                    prepared.Preparation,
                    ReleaseInstallationRejectionReason.InstancesDeclined)
                .ConfigureAwait(false);
        }

        return await StartHelperAndShutdownAsync(
                prepared,
                progress,
                state,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PreparedReleaseInstallation> PrepareCandidateAsync(
        ReleaseInstallationRequest request,
        IProgress<ReleaseInstallationWorkflowProgress>? progress,
        WorkflowExecutionState state,
        CancellationToken cancellationToken)
    {
        state.Stage = ReleaseInstallationWorkflowStage.Downloading;
        progress?.Report(new ReleaseInstallationWorkflowProgress(state.Stage));
        var download = await downloadService.DownloadAsync(
                request.Version,
                request.Package,
                dataDirectory,
                new DownloadProgressAdapter(progress),
                cancellationToken)
            .ConfigureAwait(false);

        state.Stage = ReleaseInstallationWorkflowStage.ValidatingArchive;
        progress?.Report(new ReleaseInstallationWorkflowProgress(state.Stage));
        state.Stage = ReleaseInstallationWorkflowStage.Staging;
        progress?.Report(new ReleaseInstallationWorkflowProgress(state.Stage));
        var staged = await stagingService.StageAsync(
                request.Version,
                request.Package,
                download.ArchivePath,
                dataDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        state.Stage = ReleaseInstallationWorkflowStage.PreparingRollback;
        progress?.Report(new ReleaseInstallationWorkflowProgress(state.Stage)
        {
            StagedFileCount = staged.FileCount,
        });
        state.Preparation = await installationPreparer.PrepareAsync(
                request.Version,
                request.Package.RuntimeIdentifier,
                staged.ReadyDirectory,
                staged.ManifestSha256,
                installationDirectory,
                startupArguments,
                cancellationToken)
            .ConfigureAwait(false);
        return new PreparedReleaseInstallation(
            state.Preparation,
            staged.EntryPointPath);
    }

    private async Task<ReleaseInstallationWorkflowResult>
        StartHelperAndShutdownAsync(
            PreparedReleaseInstallation prepared,
            IProgress<ReleaseInstallationWorkflowProgress>? progress,
            WorkflowExecutionState state,
            CancellationToken cancellationToken)
    {
        state.Stage = ReleaseInstallationWorkflowStage.StartingHelper;
        progress?.Report(new ReleaseInstallationWorkflowProgress(state.Stage)
        {
            RequiresElevation = prepared.Preparation.RequiresElevation,
        });
        var handoffResult = await handoff.StartHelperAttemptAsync(
                dataDirectory,
                prepared.Preparation,
                prepared.EntryPointPath,
                cancellationToken)
            .ConfigureAwait(false);
        if (handoffResult.Status == ApplicationUpdateHandoffStatus.NotStarted)
        {
            return await FinishBeforeHandoffAsync(
                    ReleaseInstallationWorkflowStatus.Failed,
                    state.Stage,
                    prepared.Preparation,
                    error: handoffResult.Error)
                .ConfigureAwait(false);
        }

        state.TransferOwnership(handoffResult.Plan
            ?? throw new UnreachableException(
                "A started update handoff has no installation plan."));
        state.Stage = ReleaseInstallationWorkflowStage.AwaitingApplicationExit;
        progress?.Report(new ReleaseInstallationWorkflowProgress(state.Stage)
        {
            Error = handoffResult.Error,
        });
        var shutdownError = await TryRequestShutdownAsync().ConfigureAwait(false);
        if (!isCurrentProcessRunning())
        {
            return new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.HandoffStarted,
                state.Stage,
                ReleaseInstallationCleanupStatus.Transferred,
                Error: handoffResult.Error ?? shutdownError,
                HandoffPlan: state.Plan);
        }

        return await ObserveTransferredOutcomeAsync(
                prepared.Preparation,
                state,
                handoffResult.Error ?? shutdownError)
            .ConfigureAwait(false);
    }

    private async Task<Exception?> TryRequestShutdownAsync()
    {
        try
        {
            await requestShutdown(CancellationToken.None).ConfigureAwait(false);
            return null;
        }
        catch (Exception exception) when (IsOperationalFailure(exception))
        {
            return exception;
        }
    }

    private async Task<ReleaseInstallationWorkflowResult>
        ObserveTransferredOutcomeAsync(
            ReleaseInstallationPreparation preparation,
            WorkflowExecutionState state,
            Exception? handoffError)
    {
        try
        {
            var outcome = await outcomeMonitor.WaitForOutcomeAsync(
                    state.Plan ?? throw new UnreachableException(
                        "Transferred update ownership has no installation plan."),
                    CancellationToken.None)
                .ConfigureAwait(false);
            return InterpretTransferredOutcome(
                preparation,
                state,
                outcome,
                handoffError);
        }
        catch (Exception exception) when (IsOperationalFailure(exception))
        {
            retryBlocked = true;
            return OwnershipUnresolved(
                state.Stage,
                exception,
                state.Plan ?? throw new UnreachableException(
                    "Transferred update ownership has no installation plan."));
        }
    }

    private ReleaseInstallationWorkflowResult InterpretTransferredOutcome(
        ReleaseInstallationPreparation preparation,
        WorkflowExecutionState state,
        ReleaseInstallationOutcome? outcome,
        Exception? handoffError)
    {
        var plan = state.Plan ?? throw new UnreachableException(
            "Transferred update ownership has no installation plan.");
        if (outcome is null)
        {
            retryBlocked = true;
            return OwnershipUnresolved(state.Stage, handoffError, plan);
        }

        if (outcome.Status != ReleaseInstallationOutcomeStatus.Aborted)
        {
            retryBlocked = true;
            return OwnershipUnresolved(
                state.Stage,
                new InvalidOperationException(
                    "The helper completed an installation while its parent remained active."),
                plan);
        }

        var helperError = new InvalidOperationException(
            outcome.Error ?? "The update helper aborted the installation.");
        if (pathExists(preparation.CandidateDirectory))
        {
            retryBlocked = true;
            return new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.CleanupFailed,
                state.Stage,
                ReleaseInstallationCleanupStatus.Failed,
                Error: helperError,
                CleanupError: new IOException(
                    "The prepared update candidate still exists after helper abort."),
                HandoffPlan: plan);
        }

        return new ReleaseInstallationWorkflowResult(
            ReleaseInstallationWorkflowStatus.Failed,
            state.Stage,
            ReleaseInstallationCleanupStatus.Succeeded,
            Error: helperError,
            HandoffPlan: plan);
    }

    private async Task CleanupBeforeRethrowAsync(
        ReleaseInstallationPreparation? preparation,
        Exception exception)
    {
        if (preparation is null)
        {
            return;
        }

        try
        {
            await installationPreparer.AbortAsync(
                    preparation,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception cleanupError)
        {
            retryBlocked = true;
            throw new AggregateException(
                "The installation operation and candidate cleanup both failed.",
                exception,
                cleanupError);
        }
    }

    private async Task<bool> CloseOtherInstancesAsync(
        ReleaseInstallationCheckpoint checkpoint,
        IProgress<ReleaseInstallationWorkflowProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ReleaseInstallationWorkflowProgress(
            ReleaseInstallationWorkflowStage.ScanningInstances)
        {
            Checkpoint = checkpoint,
        });
        var scan = await instanceManager.ScanOtherInstancesAsync(cancellationToken)
            .ConfigureAwait(false);
        if (scan.TotalCount == 0)
        {
            return true;
        }

        progress?.Report(new ReleaseInstallationWorkflowProgress(
            ReleaseInstallationWorkflowStage.AwaitingInstanceConfirmation)
        {
            Checkpoint = checkpoint,
            InstanceScan = scan,
        });
        if (!await confirmInstances(scan, checkpoint, cancellationToken)
            .ConfigureAwait(false))
        {
            return false;
        }

        progress?.Report(new ReleaseInstallationWorkflowProgress(
            ReleaseInstallationWorkflowStage.ClosingInstances)
        {
            Checkpoint = checkpoint,
            InstanceScan = scan,
        });
        await instanceManager.CloseOtherInstancesAsync(cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private async Task<ReleaseInstallationWorkflowResult> FinishBeforeHandoffAsync(
        ReleaseInstallationWorkflowStatus status,
        ReleaseInstallationWorkflowStage stage,
        ReleaseInstallationPreparation? preparation,
        ReleaseInstallationRejectionReason rejectionReason =
            ReleaseInstallationRejectionReason.None,
        Exception? error = null)
    {
        if (preparation is null)
        {
            return new ReleaseInstallationWorkflowResult(
                status,
                stage,
                ReleaseInstallationCleanupStatus.NotRequired,
                rejectionReason,
                error);
        }

        try
        {
            await installationPreparer.AbortAsync(
                    preparation,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return new ReleaseInstallationWorkflowResult(
                status,
                stage,
                ReleaseInstallationCleanupStatus.Succeeded,
                rejectionReason,
                error);
        }
        catch (Exception cleanupError) when (IsOperationalFailure(cleanupError))
        {
            retryBlocked = true;
            return new ReleaseInstallationWorkflowResult(
                ReleaseInstallationWorkflowStatus.CleanupFailed,
                stage,
                ReleaseInstallationCleanupStatus.Failed,
                rejectionReason,
                error,
                cleanupError);
        }
    }

    private static ReleaseInstallationWorkflowResult Rejected(
        ReleaseInstallationRejectionReason reason,
        ReleaseInstallationWorkflowStage stage = ReleaseInstallationWorkflowStage.None)
    {
        return new ReleaseInstallationWorkflowResult(
            ReleaseInstallationWorkflowStatus.Rejected,
            stage,
            ReleaseInstallationCleanupStatus.NotRequired,
            reason);
    }

    private static ReleaseInstallationWorkflowResult OwnershipUnresolved(
        ReleaseInstallationWorkflowStage stage,
        Exception? error,
        ReleaseInstallationHandoffPlan plan)
    {
        return new ReleaseInstallationWorkflowResult(
            ReleaseInstallationWorkflowStatus.OwnershipUnresolved,
            stage,
            ReleaseInstallationCleanupStatus.Transferred,
            Error: error,
            HandoffPlan: plan);
    }

    private static bool IsOperationalFailure(Exception exception)
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

    private sealed record PreparedReleaseInstallation(
        ReleaseInstallationPreparation Preparation,
        string EntryPointPath);

    private sealed class WorkflowExecutionState
    {
        public ReleaseInstallationWorkflowStage Stage { get; set; }

        public ReleaseInstallationPreparation? Preparation { get; set; }

        public ReleaseInstallationHandoffPlan? Plan { get; private set; }

        public bool OwnershipTransferred { get; private set; }

        public void TransferOwnership(ReleaseInstallationHandoffPlan plan)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            OwnershipTransferred = true;
        }
    }

    private sealed class DownloadProgressAdapter(
        IProgress<ReleaseInstallationWorkflowProgress>? progress)
        : IProgress<ReleasePackageDownloadProgress>
    {
        public void Report(ReleasePackageDownloadProgress value)
        {
            progress?.Report(new ReleaseInstallationWorkflowProgress(
                ReleaseInstallationWorkflowStage.Downloading)
            {
                DownloadedBytes = value.DownloadedBytes,
                TotalBytes = value.TotalBytes,
            });
        }
    }
}

internal sealed class ReleaseInstallationOutcomeMonitor(
    ReleaseInstallationPlanStore? planStore = null,
    TimeProvider? timeProvider = null,
    TimeSpan? pollInterval = null,
    TimeSpan? timeout = null,
    Action<string>? log = null) : IReleaseInstallationOutcomeMonitor
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2.25);
    private readonly ReleaseInstallationPlanStore store = planStore
        ?? new ReleaseInstallationPlanStore();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan interval = pollInterval ?? DefaultPollInterval;
    private readonly TimeSpan maximumWait = timeout ?? DefaultTimeout;

    public async Task<ReleaseInstallationOutcome?> WaitForOutcomeAsync(
        ReleaseInstallationHandoffPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var startedAt = clock.GetUtcNow();
        while (clock.GetUtcNow() - startedAt < maximumWait)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(plan.OutcomePath))
            {
                return await store.ReadOutcomeAsync(plan, cancellationToken)
                    .ConfigureAwait(false);
            }

            await Task.Delay(interval, clock, cancellationToken)
                .ConfigureAwait(false);
        }

        log?.Invoke(
            $"Update helper outcome was not available for request "
            + $"{plan.Preparation.RequestId:N} within {maximumWait}.");
        return null;
    }
}
