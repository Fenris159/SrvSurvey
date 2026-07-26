using System.Diagnostics;
using SrvSurvey.Core.Storage;
using SrvSurvey.Core.Updates;

namespace SrvSurvey.Desktop.Platform;

internal enum ApplicationUpdateStartupMode
{
    Normal,
    Apply,
    Confirm,
}

internal sealed record ApplicationUpdateStartup(
    ApplicationUpdateStartupMode Mode,
    string? PlanPath,
    IReadOnlyList<string> ApplicationArguments);

public sealed class ApplicationUpdateHandoffService
    : IApplicationUpdateHandoffService
{
    private readonly ReleaseInstallationPlanStore planStore;
    private readonly Func<ProcessStartInfo, Process?> startProcess;

    public ApplicationUpdateHandoffService()
        : this(
            new ReleaseInstallationPlanStore(),
            startInfo => Process.Start(startInfo))
    {
    }

    internal ApplicationUpdateHandoffService(
        ReleaseInstallationPlanStore planStore,
        Func<ProcessStartInfo, Process?> startProcess)
    {
        this.planStore = planStore;
        this.startProcess = startProcess;
    }

    public async Task<ReleaseInstallationHandoffPlan> StartHelperAsync(
        string dataDirectory,
        ReleaseInstallationPreparation preparation,
        string stagedEntryPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedEntryPoint);
        var helperPath = Path.GetFullPath(stagedEntryPoint);
        if (!File.Exists(helperPath))
        {
            throw new FileNotFoundException(
                "The staged update helper entry point was not found.",
                helperPath);
        }

        using var currentProcess = Process.GetCurrentProcess();
        var plan = await planStore.CreateAsync(
                dataDirectory,
                preparation,
                currentProcess.Id,
                currentProcess.StartTime.ToUniversalTime(),
                cancellationToken)
            .ConfigureAwait(false);
        var startInfo = CreateHelperStartInfo(helperPath, plan.PlanPath);
        using var helper = startProcess(startInfo)
            ?? throw new InvalidOperationException(
                "The staged SrvSurvey update helper did not start.");
        return plan;
    }

    internal static ProcessStartInfo CreateHelperStartInfo(
        string stagedEntryPoint,
        string planPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedEntryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(planPath);
        var fullEntryPoint = Path.GetFullPath(stagedEntryPoint);
        return new ProcessStartInfo
        {
            FileName = fullEntryPoint,
            WorkingDirectory = Path.GetDirectoryName(fullEntryPoint)!,
            UseShellExecute = false,
            ArgumentList =
            {
                ApplicationUpdateBootstrap.ApplyArgument,
                Path.GetFullPath(planPath),
            },
        };
    }
}

public interface IApplicationUpdateHandoffService
{
    Task<ReleaseInstallationHandoffPlan> StartHelperAsync(
        string dataDirectory,
        ReleaseInstallationPreparation preparation,
        string stagedEntryPoint,
        CancellationToken cancellationToken = default);
}

internal static class ApplicationUpdateBootstrap
{
    internal const string ApplyArgument = "--apply-update";
    internal const string ConfirmArgument = "--confirm-update";
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReplacementExitTimeout = TimeSpan.FromSeconds(15);
    private static string? pendingConfirmationPlanPath;

    public static ApplicationUpdateStartup ParseStartupArguments(
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string? applyPath = null;
        string? confirmPath = null;
        var applicationArguments = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument is not (ApplyArgument or ConfirmArgument))
            {
                applicationArguments.Add(argument);
                continue;
            }

            if (index + 1 >= arguments.Count
                || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                throw new InvalidDataException(
                    $"The internal update argument '{argument}' has no plan path.");
            }

            var planPath = arguments[++index];
            if (argument == ApplyArgument)
            {
                if (applyPath is not null)
                {
                    throw new InvalidDataException(
                        "The internal update apply argument was repeated.");
                }

                applyPath = planPath;
            }
            else
            {
                if (confirmPath is not null)
                {
                    throw new InvalidDataException(
                        "The internal update confirmation argument was repeated.");
                }

                confirmPath = planPath;
            }
        }

        if (applyPath is not null && confirmPath is not null)
        {
            throw new InvalidDataException(
                "Update apply and confirmation modes cannot be combined.");
        }

        if (applyPath is not null)
        {
            if (applicationArguments.Count != 0)
            {
                throw new InvalidDataException(
                    "The update helper does not accept application arguments.");
            }

            return new ApplicationUpdateStartup(
                ApplicationUpdateStartupMode.Apply,
                applyPath,
                []);
        }

        if (confirmPath is not null)
        {
            return new ApplicationUpdateStartup(
                ApplicationUpdateStartupMode.Confirm,
                confirmPath,
                applicationArguments);
        }

        return new ApplicationUpdateStartup(
            ApplicationUpdateStartupMode.Normal,
            null,
            applicationArguments);
    }

    public static void SetPendingConfirmation(string? planPath)
    {
        pendingConfirmationPlanPath = string.IsNullOrWhiteSpace(planPath)
            ? null
            : Path.GetFullPath(planPath);
    }

    public static async Task<bool> ConfirmPendingHealthyAsync(
        AppDataPaths paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var planPath = pendingConfirmationPlanPath;
        if (planPath is null)
        {
            return false;
        }

        var store = new ReleaseInstallationPlanStore();
        var plan = await store.LoadAsync(
                paths.DataDirectory,
                planPath,
                cancellationToken)
            .ConfigureAwait(false);
        ValidateConfirmationProcess(
            plan,
            AppContext.BaseDirectory,
            Environment.ProcessPath);
        await store.WriteHealthMarkerAsync(plan, cancellationToken)
            .ConfigureAwait(false);
        pendingConfirmationPlanPath = null;
        return true;
    }

    public static async Task<int> RunHelperAsync(
        string planPath,
        CancellationToken cancellationToken = default)
    {
        var paths = AppDataPaths.ResolveCurrent();
        var store = new ReleaseInstallationPlanStore();
        ReleaseInstallationHandoffPlan? plan = null;
        try
        {
            plan = await store.LoadAsync(
                    paths.DataDirectory,
                    planPath,
                    cancellationToken)
                .ConfigureAwait(false);
            await WaitForParentExitAsync(plan, cancellationToken)
                .ConfigureAwait(false);
            var transaction = new ReleaseInstallationTransaction();
            var result = await transaction.ApplyAsync(
                    plan.Preparation,
                    (entryPoint, arguments, token) =>
                        LaunchAndConfirmAsync(
                            store,
                            plan,
                            entryPoint,
                            arguments,
                            token),
                    cancellationToken)
                .ConfigureAwait(false);
            var status = result.Status == ReleaseInstallationStatus.Installed
                ? ReleaseInstallationOutcomeStatus.Installed
                : ReleaseInstallationOutcomeStatus.RolledBack;
            await store.WriteOutcomeAsync(
                    plan,
                    new ReleaseInstallationOutcome(
                        status,
                        plan.Preparation.RequestId,
                        plan.Preparation.Version,
                        DateTimeOffset.UtcNow,
                        result.BackupDirectory,
                        result.FailedDirectory,
                        result.Error),
                    cancellationToken)
                .ConfigureAwait(false);
            if (result.Status == ReleaseInstallationStatus.RolledBack)
            {
                StartWithoutConfirmation(
                    Path.Combine(
                        plan.Preparation.InstallationDirectory,
                        plan.Preparation.EntryPoint),
                    plan.Preparation.StartupArguments);
                return 3;
            }

            return 0;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException
                or TaskCanceledException)
        {
            if (plan is not null)
            {
                try
                {
                    await store.WriteOutcomeAsync(
                            plan,
                            new ReleaseInstallationOutcome(
                                ReleaseInstallationOutcomeStatus.Aborted,
                                plan.Preparation.RequestId,
                                plan.Preparation.Version,
                                DateTimeOffset.UtcNow,
                                Directory.Exists(plan.Preparation.BackupDirectory)
                                    ? plan.Preparation.BackupDirectory
                                    : null,
                                Directory.Exists(plan.Preparation.FailedDirectory)
                                    ? plan.Preparation.FailedDirectory
                                    : null,
                                exception.Message),
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception outcomeException) when (
                    outcomeException is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException)
                {
                }

                var originalEntryPoint = Path.Combine(
                    plan.Preparation.InstallationDirectory,
                    plan.Preparation.EntryPoint);
                if (exception is not (
                        UpdateParentStillRunningException
                        or OperationCanceledException)
                    && File.Exists(originalEntryPoint))
                {
                    try
                    {
                        StartWithoutConfirmation(
                            originalEntryPoint,
                            plan.Preparation.StartupArguments);
                    }
                    catch (Exception launchException) when (
                        launchException is IOException
                            or UnauthorizedAccessException
                            or InvalidOperationException)
                    {
                    }
                }
            }

            return 2;
        }
    }

    internal static ProcessStartInfo CreateReplacementStartInfo(
        string entryPoint,
        IReadOnlyList<string> arguments,
        string? confirmationPlanPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        ArgumentNullException.ThrowIfNull(arguments);
        var fullEntryPoint = Path.GetFullPath(entryPoint);
        var startInfo = new ProcessStartInfo
        {
            FileName = fullEntryPoint,
            WorkingDirectory = Path.GetDirectoryName(fullEntryPoint)!,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
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
        string? processPath)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var expectedDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(plan.Preparation.InstallationDirectory));
        var currentDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(baseDirectory));
        var expectedProcess = Path.Combine(
            expectedDirectory,
            plan.Preparation.EntryPoint);
        if (!string.Equals(expectedDirectory, currentDirectory, comparison)
            || processPath is null
            || !string.Equals(
                Path.GetFullPath(processPath),
                expectedProcess,
                comparison))
        {
            throw new InvalidDataException(
                "Update health confirmation did not come from the installed replacement.");
        }
    }

    private static async Task WaitForParentExitAsync(
        ReleaseInstallationHandoffPlan plan,
        CancellationToken cancellationToken)
    {
        Process? parent = null;
        try
        {
            parent = Process.GetProcessById(plan.ParentProcessId);
            var actualStartTicks = parent.StartTime.ToUniversalTime().Ticks;
            if (Math.Abs(
                    actualStartTicks - plan.ParentProcessStartTimeUtcTicks)
                > TimeSpan.FromSeconds(1).Ticks)
            {
                return;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(ParentExitTimeout);
            try
            {
                await parent.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                throw new UpdateParentStillRunningException();
            }
        }
        catch (ArgumentException)
        {
        }
        finally
        {
            parent?.Dispose();
        }
    }

    private static async Task<bool> LaunchAndConfirmAsync(
        ReleaseInstallationPlanStore store,
        ReleaseInstallationHandoffPlan plan,
        string entryPoint,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateReplacementStartInfo(
            entryPoint,
            arguments,
            plan.PlanPath);
        using var replacement = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "The replacement SrvSurvey process did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(HealthTimeout);
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                if (await store.IsHealthConfirmedAsync(plan, timeout.Token)
                    .ConfigureAwait(false))
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
            throw new IOException(
                "The unhealthy replacement process did not exit for rollback.");
        }
    }

    private static void StartWithoutConfirmation(
        string entryPoint,
        IReadOnlyList<string> arguments)
    {
        var startInfo = CreateReplacementStartInfo(
            entryPoint,
            arguments,
            confirmationPlanPath: null);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "SrvSurvey could not restart after update rollback.");
    }

    private sealed class UpdateParentStillRunningException()
        : InvalidOperationException(
            "The running SrvSurvey process did not exit before the update timeout.");
}
