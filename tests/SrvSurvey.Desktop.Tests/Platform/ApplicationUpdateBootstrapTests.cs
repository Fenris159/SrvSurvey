using System.Diagnostics;
using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationUpdateBootstrapTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-update-bootstrap-tests-{Guid.NewGuid():N}");

    [Fact]
    public void ConfirmationModeStripsOnlyInternalArguments()
    {
        var startup = ApplicationUpdateBootstrap.ParseStartupArguments(
        [
            "--journal-directory",
            "C:\\Elite Journals",
            ApplicationUpdateBootstrap.ConfirmArgument,
            "C:\\Data\\plan.json",
            "--frontier-id",
            "F123",
        ]);

        Assert.Equal(ApplicationUpdateStartupMode.Confirm, startup.Mode);
        Assert.Equal("C:\\Data\\plan.json", startup.PlanPath);
        Assert.Equal(
            [
                "--journal-directory",
                "C:\\Elite Journals",
                "--frontier-id",
                "F123",
            ],
            startup.ApplicationArguments);
    }

    [Fact]
    public void ApplyModeRequiresOnlyOnePlanArgument()
    {
        var startup = ApplicationUpdateBootstrap.ParseStartupArguments(
        [
            ApplicationUpdateBootstrap.ApplyArgument,
            "C:\\Data\\plan.json",
        ]);

        Assert.Equal(ApplicationUpdateStartupMode.Apply, startup.Mode);
        Assert.Empty(startup.ApplicationArguments);
        Assert.Equal("C:\\Data\\plan.json", startup.PlanPath);
    }

    [Fact]
    public void ResultModeStripsPlanAndPreservesApplicationArguments()
    {
        var startup = ApplicationUpdateBootstrap.ParseStartupArguments(
        [
            "--frontier-id",
            "F123",
            ApplicationUpdateBootstrap.ResultArgument,
            "C:\\Data\\plan.json",
        ]);

        Assert.Equal(ApplicationUpdateStartupMode.Result, startup.Mode);
        Assert.Equal("C:\\Data\\plan.json", startup.PlanPath);
        Assert.Equal(["--frontier-id", "F123"], startup.ApplicationArguments);
    }

    [Theory]
    [InlineData("--apply-update")]
    [InlineData("--apply-update", "plan.json", "--frontier-id", "F123")]
    [InlineData("--apply-update", "one.json", "--confirm-update", "two.json")]
    [InlineData("--confirm-update", "one.json", "--confirm-update", "two.json")]
    public void InvalidInternalArgumentsAreRejected(params string[] arguments)
    {
        Assert.Throws<InvalidDataException>(() =>
            ApplicationUpdateBootstrap.ParseStartupArguments(arguments));
    }

    [Fact]
    public void HelperAndReplacementStartInfoUseArgumentLists()
    {
        var helper = ApplicationUpdateHandoffService.CreateHelperStartInfo(
            Path.Combine(temporaryDirectory, "staged", "SrvSurvey.Desktop.exe"),
            Path.Combine(temporaryDirectory, "plans", "plan.json"));
        var replacement = ApplicationUpdateBootstrap.CreateReplacementStartInfo(
            Path.Combine(temporaryDirectory, "install", "SrvSurvey.Desktop.exe"),
            ["--journal-directory", "C:\\Elite Journals"],
            Path.Combine(temporaryDirectory, "plans", "plan.json"));

        Assert.False(helper.UseShellExecute);
        Assert.Equal(
            [ApplicationUpdateBootstrap.ApplyArgument, Path.GetFullPath(
                Path.Combine(temporaryDirectory, "plans", "plan.json"))],
            helper.ArgumentList);
        Assert.False(replacement.UseShellExecute);
        Assert.Equal(
            [
                "--journal-directory",
                "C:\\Elite Journals",
                ApplicationUpdateBootstrap.ConfirmArgument,
                Path.GetFullPath(Path.Combine(
                    temporaryDirectory,
                    "plans",
                    "plan.json")),
            ],
            replacement.ArgumentList);
    }

    [Fact]
    public void ProtectedWindowsHelperUsesRunAsVerb()
    {
        var helper = ApplicationUpdateHandoffService.CreateHelperStartInfo(
            Path.Combine(temporaryDirectory, "staged", "SrvSurvey.Desktop.exe"),
            Path.Combine(temporaryDirectory, "plans", "plan.json"),
            requiresElevation: true);

        Assert.Equal(OperatingSystem.IsWindows(), helper.UseShellExecute);
        Assert.Equal(OperatingSystem.IsWindows() ? "runas" : string.Empty, helper.Verb);
        Assert.Equal(
            [ApplicationUpdateBootstrap.ApplyArgument, Path.GetFullPath(
                Path.Combine(temporaryDirectory, "plans", "plan.json"))],
            helper.ArgumentList);
    }

    [Fact]
    public void ElevatedHelperValidatesInstalledParentProcess()
    {
        var plan = CreatePlan();
        var expectedPath = Path.Combine(
            plan.Preparation.InstallationDirectory,
            plan.Preparation.EntryPoint);
        ApplicationUpdateBootstrap.ValidateElevatedParentProcess(
            plan,
            plan.ParentProcessStartTimeUtcTicks,
            expectedPath);

        var tampered = plan with
        {
            Preparation = plan.Preparation with
            {
                InstallationDirectory = Path.Combine(temporaryDirectory, "wrong"),
            },
        };
        Assert.Throws<InvalidDataException>(() =>
            ApplicationUpdateBootstrap.ValidateElevatedParentProcess(
                tampered,
                tampered.ParentProcessStartTimeUtcTicks,
                expectedPath));
    }

    [Fact]
    public void HealthConfirmationRequiresInstalledProcessAndBaseDirectory()
    {
        var plan = CreatePlan();
        var expectedProcess = Path.Combine(
            plan.Preparation.InstallationDirectory,
            plan.Preparation.EntryPoint);

        ApplicationUpdateBootstrap.ValidateConfirmationProcess(
            plan,
            plan.Preparation.InstallationDirectory,
            expectedProcess);
        Assert.Throws<InvalidDataException>(() =>
            ApplicationUpdateBootstrap.ValidateConfirmationProcess(
                plan,
                Path.Combine(temporaryDirectory, "staged"),
                expectedProcess));
        Assert.Throws<InvalidDataException>(() =>
            ApplicationUpdateBootstrap.ValidateConfirmationProcess(
                plan,
                plan.Preparation.InstallationDirectory,
                Path.Combine(temporaryDirectory, "wrong.exe")));
    }

    [Fact]
    public async Task HandoffWritesPlanBeforeStartingStagedHelper()
    {
        var stagedEntryPoint = Path.Combine(
            temporaryDirectory,
            "staged",
            "SrvSurvey.Desktop.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedEntryPoint)!);
        await File.WriteAllTextAsync(stagedEntryPoint, "helper");
        ProcessStartInfo? captured = null;
        var service = new ApplicationUpdateHandoffService(
            new ReleaseInstallationPlanStore(),
            startInfo =>
            {
                captured = startInfo;
                return Process.GetCurrentProcess();
            });
        var preparation = CreatePlan().Preparation;

        var plan = await service.StartHelperAsync(
            temporaryDirectory,
            preparation,
            stagedEntryPoint);

        Assert.True(File.Exists(plan.PlanPath));
        Assert.NotNull(captured);
        Assert.Equal(Path.GetFullPath(stagedEntryPoint), captured.FileName);
        Assert.Equal(
            [ApplicationUpdateBootstrap.ApplyArgument, plan.PlanPath],
            captured.ArgumentList);
        var loaded = await new ReleaseInstallationPlanStore().LoadAsync(
            temporaryDirectory,
            plan.PlanPath);
        Assert.Equal(preparation.RequestId, loaded.Preparation.RequestId);
    }

    [Fact]
    public async Task ElevatedHandoffWaitsForHelperReadyMarker()
    {
        var stagedEntryPoint = Path.Combine(
            temporaryDirectory,
            "staged-elevated",
            "SrvSurvey.Desktop.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedEntryPoint)!);
        await File.WriteAllTextAsync(stagedEntryPoint, "helper");
        var store = new ReleaseInstallationPlanStore();
        ProcessStartInfo? captured = null;
        var service = new ApplicationUpdateHandoffService(
            store,
            startInfo =>
            {
                captured = startInfo;
                var planPath = startInfo.ArgumentList[1];
                _ = Task.Run(async () =>
                {
                    var loaded = await store.LoadAsync(
                        temporaryDirectory,
                        planPath);
                    await ReleaseInstallationPlanStore.WriteHelperReadyMarkerAsync(
                        loaded);
                });
                return Process.GetCurrentProcess();
            });
        var preparation = CreatePlan().Preparation with
        {
            RequiresElevation = true,
        };

        var plan = await service.StartHelperAsync(
            temporaryDirectory,
            preparation,
            stagedEntryPoint);

        Assert.NotNull(captured);
        Assert.Equal(OperatingSystem.IsWindows(), captured.UseShellExecute);
        Assert.True(await store.IsHelperReadyAsync(plan));
    }

    [Fact]
    public void ElevatedHelperRejectsAParentFromAnotherExecutable()
    {
        using var current = Process.GetCurrentProcess();
        var plan = CreatePlan() with
        {
            ParentProcessId = current.Id,
            ParentProcessStartTimeUtcTicks = current.StartTime.ToUniversalTime().Ticks,
        };

        var exception = Assert.Throws<InvalidDataException>(() =>
            ApplicationUpdateBootstrap.OpenValidatedParentProcess(plan));

        Assert.Contains(
            "did not come from the installed SrvSurvey process",
            exception.Message);
    }

    [Fact]
    public void ElevatedHelperRejectsAMissingParentProcess()
    {
        var plan = CreatePlan() with
        {
            ParentProcessId = int.MaxValue,
        };

        var exception = Assert.Throws<InvalidDataException>(() =>
            ApplicationUpdateBootstrap.OpenValidatedParentProcess(plan));

        Assert.Contains("could not validate", exception.Message);
    }

    [Fact]
    public void ParentIdentityCheckRecognizesCurrentStaleAndMissingProcesses()
    {
        using var current = Process.GetCurrentProcess();
        var currentPlan = CreatePlan() with
        {
            ParentProcessId = current.Id,
            ParentProcessStartTimeUtcTicks = current.StartTime.ToUniversalTime().Ticks,
        };

        Assert.True(ApplicationUpdateBootstrap.IsParentStillRunning(currentPlan));
        Assert.False(ApplicationUpdateBootstrap.IsParentStillRunning(
            currentPlan with
            {
                ParentProcessStartTimeUtcTicks =
                    currentPlan.ParentProcessStartTimeUtcTicks
                    + TimeSpan.FromSeconds(2).Ticks,
            }));
        Assert.False(ApplicationUpdateBootstrap.IsParentStillRunning(
            currentPlan with { ParentProcessId = int.MaxValue }));
    }

    [Fact]
    public async Task ParentWaitReturnsForStaleAndMissingProcesses()
    {
        using var current = Process.GetCurrentProcess();
        var currentPlan = CreatePlan() with
        {
            ParentProcessId = current.Id,
            ParentProcessStartTimeUtcTicks =
                current.StartTime.ToUniversalTime().Ticks
                + TimeSpan.FromSeconds(2).Ticks,
        };

        var staleException = await Record.ExceptionAsync(() =>
            ApplicationUpdateBootstrap.WaitForParentExitAsync(
                currentPlan,
                validatedParent: null,
                CancellationToken.None));
        var missingException = await Record.ExceptionAsync(() =>
            ApplicationUpdateBootstrap.WaitForParentExitAsync(
                currentPlan with { ParentProcessId = int.MaxValue },
                validatedParent: null,
                CancellationToken.None));

        Assert.Null(staleException);
        Assert.Null(missingException);
    }

    [Fact]
    public async Task ParentTimeoutAbortsTheInactivePreparedCandidate()
    {
        using var current = Process.GetCurrentProcess();
        var store = new ReleaseInstallationPlanStore();
        var preparation = CreatePlan().Preparation;
        Directory.CreateDirectory(preparation.CandidateDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(preparation.CandidateDirectory, "candidate.txt"),
            "candidate");
        var plan = await store.CreateAsync(
            temporaryDirectory,
            preparation,
            current.Id,
            current.StartTime.ToUniversalTime());

        var exitCode = await ApplicationUpdateBootstrap.RunHelperAsync(
            temporaryDirectory,
            plan.PlanPath,
            CancellationToken.None,
            TimeSpan.Zero);

        Assert.Equal(2, exitCode);
        Assert.False(Directory.Exists(preparation.CandidateDirectory));
        var outcome = await store.ReadOutcomeAsync(plan);
        Assert.Equal(ReleaseInstallationOutcomeStatus.Aborted, outcome.Status);
        Assert.Contains("did not exit", outcome.Error);
        Assert.DoesNotContain("cleanup also failed", outcome.Error);
    }

    [Fact]
    public async Task ElevatedHelperFailureWritesAnAbortedOutcome()
    {
        using var current = Process.GetCurrentProcess();
        var store = new ReleaseInstallationPlanStore();
        var preparation = CreatePlan().Preparation with
        {
            RequiresElevation = true,
        };
        var plan = await store.CreateAsync(
            temporaryDirectory,
            preparation,
            current.Id,
            current.StartTime.ToUniversalTime());

        var exitCode = await ApplicationUpdateBootstrap.RunHelperAsync(
            temporaryDirectory,
            plan.PlanPath);

        Assert.Equal(2, exitCode);
        var outcome = await store.ReadOutcomeAsync(plan);
        Assert.NotNull(outcome);
        Assert.Equal(ReleaseInstallationOutcomeStatus.Aborted, outcome.Status);
        Assert.Contains(
            "did not come from the installed SrvSurvey process",
            outcome.Error);
    }

    [Fact]
    public async Task HelperFailureBeforePlanLoadReturnsAnErrorCode()
    {
        var exitCode = await ApplicationUpdateBootstrap.RunHelperAsync(
            Path.Combine(temporaryDirectory, "missing-plan.json"));

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ElevatedHandoffRejectsAHelperThatExitsBeforeValidation()
    {
        var stagedEntryPoint = Path.Combine(
            temporaryDirectory,
            "staged-exited",
            "SrvSurvey.Desktop.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedEntryPoint)!);
        await File.WriteAllTextAsync(stagedEntryPoint, "helper");
        var service = new ApplicationUpdateHandoffService(
            new ReleaseInstallationPlanStore(),
            _ => StartExitedProcess());
        var preparation = CreatePlan().Preparation with
        {
            RequiresElevation = true,
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartHelperAsync(
                temporaryDirectory,
                preparation,
                stagedEntryPoint));

        Assert.Contains("exited before validating", exception.Message);
    }

    [Fact]
    public async Task TypedHandoffPreservesOwnershipWhenStartedHelperIsUnconfirmed()
    {
        var stagedEntryPoint = Path.Combine(
            temporaryDirectory,
            "staged-unconfirmed",
            "SrvSurvey.Desktop.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedEntryPoint)!);
        await File.WriteAllTextAsync(stagedEntryPoint, "helper");
        IApplicationUpdateHandoff handoff = new ApplicationUpdateHandoffService(
            new ReleaseInstallationPlanStore(),
            _ => StartExitedProcess());
        var preparation = CreatePlan().Preparation with
        {
            RequiresElevation = true,
        };

        var result = await handoff.StartHelperAttemptAsync(
            temporaryDirectory,
            preparation,
            stagedEntryPoint);

        Assert.Equal(
            ApplicationUpdateHandoffStatus.StartedReadinessUnconfirmed,
            result.Status);
        Assert.NotNull(result.Plan);
        var error = Assert.IsType<InvalidOperationException>(result.Error);
        Assert.Contains("exited before validating", error.Message);
    }

    public void Dispose()
    {
        ApplicationUpdateBootstrap.SetPendingConfirmation(null);
        ApplicationUpdateBootstrap.SetPendingOutcome(null);
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private ReleaseInstallationHandoffPlan CreatePlan()
    {
        var requestId = Guid.NewGuid();
        var parent = Path.Combine(temporaryDirectory, "install-parent");
        var installation = Path.Combine(parent, "SrvSurvey");
        var planDirectory = Path.Combine(
            temporaryDirectory,
            "updates",
            "install-plans",
            requestId.ToString("N"));
        var preparation = new ReleaseInstallationPreparation(
            requestId,
            new Version(2, 0, 95, 23),
            "win-x64",
            installation,
            Path.Combine(temporaryDirectory, "ready"),
            Path.Combine(parent, $".SrvSurvey-update-{requestId:N}"),
            Path.Combine(parent, $".SrvSurvey-backup-{requestId:N}"),
            Path.Combine(parent, $".SrvSurvey-failed-{requestId:N}"),
            "SrvSurvey.Desktop.exe",
            new string('a', 64),
            new string('b', 64),
            false,
            []);
        return new ReleaseInstallationHandoffPlan(
            Path.Combine(planDirectory, "plan.json"),
            Path.Combine(planDirectory, "helper-ready.json"),
            Path.Combine(planDirectory, "health.json"),
            Path.Combine(planDirectory, "outcome.json"),
            DateTimeOffset.UtcNow,
            123,
            DateTimeOffset.UtcNow.UtcTicks,
            new string('c', 64),
            preparation);
    }

    private static Process StartExitedProcess()
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c exit 0")
            : new ProcessStartInfo("/bin/sh", "-c true");
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Test process did not start.");
        process.WaitForExit();
        return process;
    }
}
