using System.ComponentModel;
using System.Diagnostics;
using SrvSurvey.Core.Storage;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Desktop.Platform;

internal enum ApplicationUpdateStartupMode
{
    Normal,
    Apply,
    Confirm,
    Result,
}

internal sealed record ApplicationUpdateStartup(
    ApplicationUpdateStartupMode Mode,
    string? PlanPath,
    IReadOnlyList<string> ApplicationArguments
);

internal sealed class ApplicationUpdateHandoffService : IApplicationUpdateHandoff
{
    private static readonly TimeSpan HelperReadyTimeout = TimeSpan.FromSeconds(30);
    private readonly ReleaseInstallationPlanStore planStore;
    private readonly Func<ProcessStartInfo, Process?> startProcess;

    public ApplicationUpdateHandoffService()
        : this(new ReleaseInstallationPlanStore(), startInfo => Process.Start(startInfo)) { }

    internal ApplicationUpdateHandoffService(
        ReleaseInstallationPlanStore planStore,
        Func<ProcessStartInfo, Process?> startProcess
    )
    {
        this.planStore = planStore;
        this.startProcess = startProcess;
    }

    Task<ApplicationUpdateHandoffResult> IApplicationUpdateHandoff.StartHelperAttemptAsync(
        string dataDirectory,
        ReleaseInstallationPreparation preparation,
        string stagedEntryPoint,
        CancellationToken cancellationToken
    )
    {
        return StartHelperAttemptAsync(dataDirectory, preparation, stagedEntryPoint, cancellationToken);
    }

    private async Task<ApplicationUpdateHandoffResult> StartHelperAttemptAsync(
        string dataDirectory,
        ReleaseInstallationPreparation preparation,
        string stagedEntryPoint,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedEntryPoint);
        string helperPath = Path.GetFullPath(stagedEntryPoint);
        if (!File.Exists(helperPath))
        {
            return new ApplicationUpdateHandoffResult(
                ApplicationUpdateHandoffStatus.NotStarted,
                null,
                new FileNotFoundException("The staged update helper entry point was not found.", helperPath)
            );
        }

        ReleaseInstallationHandoffPlan? plan = null;
        Process? helper = null;
        bool helperStarted = false;
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            plan = await planStore
                .CreateAsync(
                    dataDirectory,
                    preparation,
                    currentProcess.Id,
                    currentProcess.StartTime.ToUniversalTime(),
                    cancellationToken
                )
                .ConfigureAwait(false);
            ProcessStartInfo startInfo = CreateHelperStartInfo(
                helperPath,
                plan.PlanPath,
                preparation.RequiresElevation,
                preparation.Kind == ReleaseInstallationKind.AppImage
            );
            helper = startProcess(startInfo);
            if (helper is null)
            {
                return new ApplicationUpdateHandoffResult(
                    ApplicationUpdateHandoffStatus.NotStarted,
                    plan,
                    new InvalidOperationException("The staged SrvSurvey update helper did not start.")
                );
            }

            helperStarted = true;
            if (preparation.RequiresElevation)
            {
                await WaitForHelperReadyAsync(plan, helper, cancellationToken).ConfigureAwait(false);
            }

            return new ApplicationUpdateHandoffResult(ApplicationUpdateHandoffStatus.Started, plan);
        }
        catch (Exception exception) when (IsHandoffFailure(exception))
        {
            return new ApplicationUpdateHandoffResult(
                helperStarted
                    ? ApplicationUpdateHandoffStatus.StartedReadinessUnconfirmed
                    : ApplicationUpdateHandoffStatus.NotStarted,
                plan,
                exception
            );
        }
        finally
        {
            helper?.Dispose();
        }
    }

    internal static ProcessStartInfo CreateHelperStartInfo(
        string stagedEntryPoint,
        string planPath,
        bool requiresElevation = false,
        bool extractAppImage = false
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedEntryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(planPath);
        string fullEntryPoint = Path.GetFullPath(stagedEntryPoint);
        var startInfo = new ProcessStartInfo
        {
            FileName = fullEntryPoint,
            WorkingDirectory = Path.GetDirectoryName(fullEntryPoint)!,
            UseShellExecute = requiresElevation && OperatingSystem.IsWindows(),
            Verb = requiresElevation && OperatingSystem.IsWindows() ? "runas" : string.Empty,
            ArgumentList = { ApplicationUpdateBootstrap.ApplyArgument, Path.GetFullPath(planPath) },
        };
        if (extractAppImage)
        {
            startInfo.Environment["APPIMAGE_EXTRACT_AND_RUN"] = "1";
        }

        return startInfo;
    }

    private async Task WaitForHelperReadyAsync(
        ReleaseInstallationHandoffPlan plan,
        Process helper,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HelperReadyTimeout);
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                if (await planStore.IsHelperReadyAsync(plan, timeout.Token).ConfigureAwait(false))
                {
                    return;
                }

                if (helper.HasExited)
                {
                    throw new InvalidOperationException(
                        "The elevated update helper exited before validating the installation."
                    );
                }

                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The elevated update helper did not become ready in time.");
        }
    }

    private static bool IsHandoffFailure(Exception exception)
    {
        return exception
            is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or Win32Exception
                or OperationCanceledException
                or PlatformNotSupportedException;
    }
}

internal static class ApplicationUpdateBootstrap
{
    internal const string ApplyArgument = "--apply-update";
    internal const string ConfirmArgument = "--confirm-update";
    internal const string ResultArgument = "--update-result";
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReplacementExitTimeout = TimeSpan.FromSeconds(15);
    private static string? pendingConfirmationPlanPath;
    private static string? pendingOutcomePlanPath;

    public static ApplicationUpdateStartup ParseStartupArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ParsedStartupArguments parsed = ParseInternalPaths(arguments);
        int internalModeCount =
            (parsed.ApplyPath is null ? 0 : 1)
            + (parsed.ConfirmPath is null ? 0 : 1)
            + (parsed.ResultPath is null ? 0 : 1);
        if (internalModeCount > 1)
        {
            throw new InvalidDataException("Internal update helper modes cannot be combined.");
        }

        if (parsed.ApplyPath is not null)
        {
            if (parsed.ApplicationArguments.Count != 0)
            {
                throw new InvalidDataException("The update helper does not accept application arguments.");
            }

            return new ApplicationUpdateStartup(ApplicationUpdateStartupMode.Apply, parsed.ApplyPath, []);
        }

        if (parsed.ConfirmPath is not null)
        {
            return new ApplicationUpdateStartup(
                ApplicationUpdateStartupMode.Confirm,
                parsed.ConfirmPath,
                parsed.ApplicationArguments
            );
        }

        if (parsed.ResultPath is not null)
        {
            return new ApplicationUpdateStartup(
                ApplicationUpdateStartupMode.Result,
                parsed.ResultPath,
                parsed.ApplicationArguments
            );
        }

        return new ApplicationUpdateStartup(ApplicationUpdateStartupMode.Normal, null, parsed.ApplicationArguments);
    }

    private static ParsedStartupArguments ParseInternalPaths(IReadOnlyList<string> arguments)
    {
        string? applyPath = null;
        string? confirmPath = null;
        string? resultPath = null;
        var applicationArguments = new List<string>();
        int index = 0;
        while (index < arguments.Count)
        {
            string argument = arguments[index++];
            if (argument is not (ApplyArgument or ConfirmArgument or ResultArgument))
            {
                applicationArguments.Add(argument);
                continue;
            }

            if (index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                throw new InvalidDataException($"The internal update argument '{argument}' has no plan path.");
            }

            string planPath = arguments[index++];
            AssignInternalPath(argument, planPath, ref applyPath, ref confirmPath, ref resultPath);
        }

        return new ParsedStartupArguments(applyPath, confirmPath, resultPath, applicationArguments);
    }

    private static void AssignInternalPath(
        string argument,
        string planPath,
        ref string? applyPath,
        ref string? confirmPath,
        ref string? resultPath
    )
    {
        if (argument == ApplyArgument)
        {
            if (applyPath is not null)
            {
                throw new InvalidDataException("The internal update apply argument was repeated.");
            }

            applyPath = planPath;
            return;
        }

        if (argument == ConfirmArgument)
        {
            if (confirmPath is not null)
            {
                throw new InvalidDataException("The internal update confirmation argument was repeated.");
            }

            confirmPath = planPath;
            return;
        }

        if (resultPath is not null)
        {
            throw new InvalidDataException("The internal update result argument was repeated.");
        }

        resultPath = planPath;
    }

    private sealed record ParsedStartupArguments(
        string? ApplyPath,
        string? ConfirmPath,
        string? ResultPath,
        IReadOnlyList<string> ApplicationArguments
    );

    public static void SetPendingConfirmation(string? planPath)
    {
        pendingConfirmationPlanPath = string.IsNullOrWhiteSpace(planPath) ? null : Path.GetFullPath(planPath);
    }

    public static void SetPendingOutcome(string? planPath)
    {
        pendingOutcomePlanPath = string.IsNullOrWhiteSpace(planPath) ? null : Path.GetFullPath(planPath);
    }

    public static async Task<ReleaseInstallationOutcome?> ConsumePendingOutcomeAsync(
        AppDataPaths paths,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(paths);
        string? planPath = pendingOutcomePlanPath;
        if (planPath is null)
        {
            return null;
        }

        var store = new ReleaseInstallationPlanStore();
        ReleaseInstallationHandoffPlan plan = await store
            .LoadAsync(paths.DataDirectory, planPath, cancellationToken)
            .ConfigureAwait(false);
        ValidateConfirmationProcess(
            plan,
            AppContext.BaseDirectory,
            Environment.ProcessPath,
            Environment.GetEnvironmentVariable("APPIMAGE")
        );

        ReleaseInstallationOutcome outcome = await store
            .ReadOutcomeAsync(plan, cancellationToken)
            .ConfigureAwait(false);
        pendingOutcomePlanPath = null;
        return outcome;
    }

    public static async Task<ReleaseInstallationHandoffPlan?> ConfirmPendingHealthyAsync(
        AppDataPaths paths,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(paths);
        string? planPath = pendingConfirmationPlanPath;
        if (planPath is null)
        {
            return null;
        }

        var store = new ReleaseInstallationPlanStore();
        ReleaseInstallationHandoffPlan plan = await store
            .LoadAsync(paths.DataDirectory, planPath, cancellationToken)
            .ConfigureAwait(false);
        ValidateConfirmationProcess(
            plan,
            AppContext.BaseDirectory,
            Environment.ProcessPath,
            Environment.GetEnvironmentVariable("APPIMAGE")
        );
        await store.WriteHealthMarkerAsync(plan, cancellationToken).ConfigureAwait(false);
        pendingConfirmationPlanPath = null;
        return plan;
    }

    public static Task<int> RunHelperAsync(string planPath, CancellationToken cancellationToken = default)
    {
        var paths = AppDataPaths.ResolveCurrent();
        return RunHelperAsync(paths.DataDirectory, planPath, cancellationToken: cancellationToken);
    }

    internal static async Task<int> RunHelperAsync(
        string dataDirectory,
        string planPath,
        TimeSpan? parentExitTimeout = null,
        CancellationToken cancellationToken = default
    )
    {
        if (parentExitTimeout is { } configuredTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(configuredTimeout, TimeSpan.Zero);
        }

        var store = new ReleaseInstallationPlanStore();
        ReleaseInstallationHandoffPlan? plan = null;
        Process? validatedParent = null;
        try
        {
            plan = await store.LoadAsync(dataDirectory, planPath, cancellationToken).ConfigureAwait(false);
            if (plan.Preparation.RequiresElevation)
            {
                validatedParent = OpenValidatedParentProcess(plan);
                await ReleaseInstallationPlanStore
                    .WriteHelperReadyMarkerAsync(plan, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await ApplyHelperPlanAsync(
                    store,
                    plan,
                    validatedParent,
                    parentExitTimeout ?? ParentExitTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
                        or TaskCanceledException
            )
        {
            await HandleHelperFailureAsync(store, plan, exception).ConfigureAwait(false);
            return 2;
        }
        finally
        {
            validatedParent?.Dispose();
        }
    }

    private static async Task<int> ApplyHelperPlanAsync(
        ReleaseInstallationPlanStore store,
        ReleaseInstallationHandoffPlan plan,
        Process? validatedParent,
        TimeSpan parentExitTimeout,
        CancellationToken cancellationToken
    )
    {
        await WaitForParentExitAsync(plan, validatedParent, cancellationToken, parentExitTimeout).ConfigureAwait(false);
        var transaction = new ReleaseInstallationTransaction();
        ReleaseInstallationResult result = await transaction
            .ApplyAsync(
                plan.Preparation,
                (entryPoint, arguments, token) => LaunchAndConfirmAsync(store, plan, entryPoint, arguments, token),
                cancellationToken
            )
            .ConfigureAwait(false);
        ReleaseInstallationOutcomeStatus status =
            result.Status == ReleaseInstallationStatus.Installed
                ? ReleaseInstallationOutcomeStatus.Installed
                : ReleaseInstallationOutcomeStatus.RolledBack;
        if (
            result.Status == ReleaseInstallationStatus.Installed
            && plan.Preparation.Kind == ReleaseInstallationKind.AppImage
            && result.BackupDirectory is not null
        )
        {
            TryDeleteFile(result.BackupDirectory);
            if (!File.Exists(result.BackupDirectory))
            {
                result = result with { BackupDirectory = null };
            }
        }

        await store
            .WriteOutcomeAsync(
                plan,
                new ReleaseInstallationOutcome(
                    status,
                    plan.Preparation.RequestId,
                    plan.Preparation.Version,
                    DateTimeOffset.UtcNow,
                    result.BackupDirectory,
                    result.FailedDirectory,
                    result.Error
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
        if (result.Status == ReleaseInstallationStatus.RolledBack)
        {
            StartWithoutConfirmation(
                Path.Combine(plan.Preparation.InstallationDirectory, plan.Preparation.EntryPoint),
                plan.Preparation.StartupArguments,
                plan.PlanPath
            );
            return 3;
        }

        return 0;
    }

    private static async Task HandleHelperFailureAsync(
        ReleaseInstallationPlanStore store,
        ReleaseInstallationHandoffPlan? plan,
        Exception exception
    )
    {
        if (plan is null)
        {
            return;
        }

        string error = exception.Message;
        if (exception is UpdateParentStillRunningException)
        {
            Exception? cleanupError = await AbortTimedOutCandidateAsync(plan.Preparation).ConfigureAwait(false);
            if (cleanupError is not null)
            {
                error += " Candidate cleanup also failed: " + cleanupError.Message;
            }
        }

        try
        {
            await store
                .WriteOutcomeAsync(
                    plan,
                    new ReleaseInstallationOutcome(
                        ReleaseInstallationOutcomeStatus.Aborted,
                        plan.Preparation.RequestId,
                        plan.Preparation.Version,
                        DateTimeOffset.UtcNow,
                        PathExists(plan.Preparation.BackupDirectory) ? plan.Preparation.BackupDirectory : null,
                        PathExists(plan.Preparation.FailedDirectory) ? plan.Preparation.FailedDirectory : null,
                        error
                    ),
                    CancellationToken.None
                )
                .ConfigureAwait(false);
        }
        catch (Exception outcomeException)
            when (outcomeException is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The primary update failure remains the actionable result.
        }

        TryRestartOriginalInstallation(plan, exception);
    }

    private static async Task<Exception?> AbortTimedOutCandidateAsync(ReleaseInstallationPreparation preparation)
    {
        try
        {
            if (preparation.Kind == ReleaseInstallationKind.AppImage)
            {
                await new AppImageReleaseInstallationPreparer()
                    .AbortAsync(preparation, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                await new ReleaseInstallationPreparer()
                    .AbortAsync(preparation, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            if (Directory.Exists(preparation.CandidateDirectory) || File.Exists(preparation.CandidateDirectory))
            {
                return new IOException("The prepared update candidate could not be removed.");
            }

            return null;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or InvalidOperationException
            )
        {
            return exception;
        }
    }

    private static void TryRestartOriginalInstallation(ReleaseInstallationHandoffPlan plan, Exception exception)
    {
        string originalEntryPoint = Path.Combine(plan.Preparation.InstallationDirectory, plan.Preparation.EntryPoint);
        if (
            exception is UpdateParentStillRunningException or OperationCanceledException
            || IsParentStillRunning(plan)
            || !File.Exists(originalEntryPoint)
        )
        {
            return;
        }

        try
        {
            StartWithoutConfirmation(originalEntryPoint, plan.Preparation.StartupArguments, plan.PlanPath);
        }
        catch (Exception launchException)
            when (launchException is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The original failure is returned when recovery cannot launch.
        }
    }

    internal static ProcessStartInfo CreateReplacementStartInfo(
        string entryPoint,
        IReadOnlyList<string> arguments,
        string? confirmationPlanPath
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        ArgumentNullException.ThrowIfNull(arguments);
        string fullEntryPoint = Path.GetFullPath(entryPoint);
        var startInfo = new ProcessStartInfo
        {
            FileName = fullEntryPoint,
            WorkingDirectory = Path.GetDirectoryName(fullEntryPoint)!,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (confirmationPlanPath is not null)
        {
            startInfo.ArgumentList.Add(ConfirmArgument);
            startInfo.ArgumentList.Add(Path.GetFullPath(confirmationPlanPath));
        }

        return startInfo;
    }

    internal static void ValidateConfirmationProcess(
        ReleaseInstallationHandoffPlan plan,
        string baseDirectory,
        string? processPath,
        string? appImagePath = null
    )
    {
        ArgumentNullException.ThrowIfNull(plan);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string expectedDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(plan.Preparation.InstallationDirectory)
        );
        string currentDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        string expectedProcess = Path.Combine(expectedDirectory, plan.Preparation.EntryPoint);
        if (plan.Preparation.Kind == ReleaseInstallationKind.AppImage)
        {
            if (
                string.IsNullOrWhiteSpace(appImagePath)
                || !string.Equals(Path.GetFullPath(appImagePath), expectedProcess, StringComparison.Ordinal)
            )
            {
                throw new InvalidDataException("Update health confirmation did not come from the installed AppImage.");
            }

            return;
        }

        if (
            !string.Equals(expectedDirectory, currentDirectory, comparison)
            || processPath is null
            || !string.Equals(Path.GetFullPath(processPath), expectedProcess, comparison)
        )
        {
            throw new InvalidDataException("Update health confirmation did not come from the installed replacement.");
        }
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Retain the verified rollback image when the file system refuses cleanup.
        }
    }

    internal static async Task WaitForParentExitAsync(
        ReleaseInstallationHandoffPlan plan,
        Process? validatedParent,
        CancellationToken cancellationToken,
        TimeSpan? parentExitTimeout = null
    )
    {
        if (parentExitTimeout is { } configuredTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(configuredTimeout, TimeSpan.Zero);
        }

        Process? parent = validatedParent;
        bool disposeParent = false;
        try
        {
            if (parent is null)
            {
                parent = Process.GetProcessById(plan.ParentProcessId);
                disposeParent = true;
                long actualStartTicks = parent.StartTime.ToUniversalTime().Ticks;
                if (Math.Abs(actualStartTicks - plan.ParentProcessStartTimeUtcTicks) > TimeSpan.FromSeconds(1).Ticks)
                {
                    return;
                }
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(parentExitTimeout ?? ParentExitTimeout);
            try
            {
                await parent.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new UpdateParentStillRunningException();
            }
        }
        catch (ArgumentException)
        {
            // The parent process already exited before it could be inspected.
        }
        finally
        {
            if (disposeParent)
            {
                parent?.Dispose();
            }
        }
    }

    internal static bool IsParentStillRunning(ReleaseInstallationHandoffPlan plan)
    {
        try
        {
            using var parent = Process.GetProcessById(plan.ParentProcessId);
            return Math.Abs(parent.StartTime.ToUniversalTime().Ticks - plan.ParentProcessStartTimeUtcTicks)
                <= TimeSpan.FromSeconds(1).Ticks;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    internal static Process OpenValidatedParentProcess(ReleaseInstallationHandoffPlan plan)
    {
        Process? parent = null;
        try
        {
            parent = Process.GetProcessById(plan.ParentProcessId);
            long actualStartTicks = parent.StartTime.ToUniversalTime().Ticks;
            string? actualPath = parent.MainModule?.FileName;
            ValidateElevatedParentProcess(plan, actualStartTicks, actualPath);

            return parent;
        }
        catch (InvalidDataException)
        {
            parent?.Dispose();
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            parent?.Dispose();
            throw new InvalidDataException(
                "The elevated update helper could not validate the installed SrvSurvey process.",
                exception
            );
        }
    }

    internal static void ValidateElevatedParentProcess(
        ReleaseInstallationHandoffPlan plan,
        long actualStartTimeUtcTicks,
        string? actualProcessPath
    )
    {
        ArgumentNullException.ThrowIfNull(plan);
        const string expectedEntryPoint = "SrvSurvey.Desktop.exe";
        string expectedPath = Path.GetFullPath(
            Path.Combine(plan.Preparation.InstallationDirectory, plan.Preparation.EntryPoint)
        );
        if (
            plan.Preparation.RuntimeIdentifier != "win-x64"
            || !string.Equals(plan.Preparation.EntryPoint, expectedEntryPoint, StringComparison.Ordinal)
            || Math.Abs(actualStartTimeUtcTicks - plan.ParentProcessStartTimeUtcTicks) > TimeSpan.FromSeconds(1).Ticks
            || actualProcessPath is null
            || !string.Equals(Path.GetFullPath(actualProcessPath), expectedPath, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidDataException(
                "The elevated update request did not come from the installed SrvSurvey process."
            );
        }
    }

    private static async Task<bool> LaunchAndConfirmAsync(
        ReleaseInstallationPlanStore store,
        ReleaseInstallationHandoffPlan plan,
        string entryPoint,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        ProcessStartInfo startInfo = CreateReplacementStartInfo(entryPoint, arguments, plan.PlanPath);
        using Process replacement =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("The replacement SrvSurvey process did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HealthTimeout);
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                if (await store.IsHealthConfirmedAsync(plan, timeout.Token).ConfigureAwait(false))
                {
                    return true;
                }

                if (replacement.HasExited)
                {
                    return false;
                }

                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Health confirmation timed out; rollback follows below.
        }

        await StopReplacementAsync(replacement).ConfigureAwait(false);
        return false;
    }

    private static async Task StopReplacementAsync(Process replacement)
    {
        if (replacement.HasExited)
        {
            return;
        }

        replacement.Kill(entireProcessTree: true);
        using var timeout = new CancellationTokenSource(ReplacementExitTimeout);
        try
        {
            await replacement.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new IOException("The unhealthy replacement process did not exit for rollback.");
        }
    }

    private static void StartWithoutConfirmation(
        string entryPoint,
        IReadOnlyList<string> arguments,
        string resultPlanPath
    )
    {
        ProcessStartInfo startInfo = CreateReplacementStartInfo(entryPoint, arguments, confirmationPlanPath: null);
        startInfo.ArgumentList.Add(ResultArgument);
        startInfo.ArgumentList.Add(Path.GetFullPath(resultPlanPath));
        using Process process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("SrvSurvey could not restart after update rollback.");
    }

    private sealed class UpdateParentStillRunningException()
        : InvalidOperationException("The running SrvSurvey process did not exit before the update timeout.");
}
