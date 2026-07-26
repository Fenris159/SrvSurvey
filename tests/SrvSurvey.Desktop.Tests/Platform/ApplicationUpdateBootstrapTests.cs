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
            Path.Combine(parent, $".SrvSurvey-update-{requestId:N}"),
            Path.Combine(parent, $".SrvSurvey-backup-{requestId:N}"),
            Path.Combine(parent, $".SrvSurvey-failed-{requestId:N}"),
            "SrvSurvey.Desktop.exe",
            new string('a', 64),
            new string('b', 64),
            []);
        return new ReleaseInstallationHandoffPlan(
            Path.Combine(planDirectory, "plan.json"),
            Path.Combine(planDirectory, "health.json"),
            Path.Combine(planDirectory, "outcome.json"),
            DateTimeOffset.UtcNow,
            123,
            DateTimeOffset.UtcNow.UtcTicks,
            new string('c', 64),
            preparation);
    }
}
