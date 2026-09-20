using System.Diagnostics;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationRestartServiceTests
{
    [Fact]
    public void RestartServiceCanResolveTheCurrentLauncher()
    {
        var service = new ApplicationRestartService();

        Assert.NotNull(service);
    }

    [Fact]
    public void RestartHelperLaunchUsesTheInjectedProcessIdentityAndStarter()
    {
        ProcessStartInfo? capturedStartInfo = null;
        var service = new ApplicationRestartService(
            Path.Combine("app", "SrvSurvey.Desktop"),
            Path.Combine("app", "SrvSurvey.Desktop.dll"),
            ["--frontier-id", "F123"],
            () => (42, 638934912000000000),
            startInfo =>
            {
                capturedStartInfo = startInfo;
                return true;
            }
        );

        service.StartRestartHelper();

        Assert.NotNull(capturedStartInfo);
        Assert.Equal(ApplicationRestartService.RestartAfterProcessArgument, capturedStartInfo.ArgumentList[0]);
        Assert.Equal("42", capturedStartInfo.ArgumentList[1]);
        Assert.Equal("638934912000000000", capturedStartInfo.ArgumentList[2]);
        Assert.Equal("--frontier-id", capturedStartInfo.ArgumentList[4]);
        Assert.Equal("F123", capturedStartInfo.ArgumentList[5]);
    }

    [Fact]
    public void RestartHelperLaunchReportsWhenTheHelperCannotStart()
    {
        var service = new ApplicationRestartService(
            Path.Combine("app", "SrvSurvey.Desktop"),
            Path.Combine("app", "SrvSurvey.Desktop.dll"),
            [],
            () => (42, 638934912000000000),
            _ => false
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(service.StartRestartHelper);

        Assert.Equal("The SrvSurvey restart helper did not start.", exception.Message);
    }

    [Fact]
    public void RestartHelperEscapesTheOwningSystemdService()
    {
        ProcessStartInfo? capturedStartInfo = null;
        var service = new ApplicationRestartService(
            Path.Combine("app", "SrvSurvey.Desktop"),
            Path.Combine("app", "SrvSurvey.Desktop.dll"),
            [],
            () => (42, 638934912000000000),
            startInfo =>
            {
                capturedStartInfo = startInfo;
                return true;
            },
            isolateFromSystemdUnit: true
        );

        service.StartRestartHelper();

        Assert.NotNull(capturedStartInfo);
        Assert.Equal("/usr/bin/systemd-run", capturedStartInfo.FileName);
        Assert.Contains("--unit=srvsurvey-restart-42", capturedStartInfo.ArgumentList);
        Assert.Contains("--property=ExitType=cgroup", capturedStartInfo.ArgumentList);
        Assert.Contains(ApplicationRestartService.RestartAfterProcessArgument, capturedStartInfo.ArgumentList);
    }

    [Fact]
    public void FrameworkDependentLaunchPreservesAssemblyAndArguments()
    {
        ProcessStartInfo startInfo = ApplicationRestartService.CreateStartInfo(
            Path.Combine("runtime", "dotnet.exe"),
            Path.Combine("app", "SrvSurvey.Desktop.dll"),
            ["--journal-directory", "C:\\Elite Journals", "--frontier-id", "F123"]
        );

        Assert.Equal(Path.Combine("runtime", "dotnet.exe"), startInfo.FileName);
        Assert.Equal(
            [
                Path.Combine("app", "SrvSurvey.Desktop.dll"),
                "--journal-directory",
                "C:\\Elite Journals",
                "--frontier-id",
                "F123",
            ],
            startInfo.ArgumentList
        );
        Assert.Equal(Path.GetFullPath("app"), startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void SelfContainedLaunchDoesNotAddManagedAssembly()
    {
        ProcessStartInfo startInfo = ApplicationRestartService.CreateStartInfo(
            Path.Combine("app", "SrvSurvey.Desktop.exe"),
            Path.Combine("app", "SrvSurvey.Desktop.dll"),
            ["--frontier-id", "F123"]
        );

        Assert.Equal(["--frontier-id", "F123"], startInfo.ArgumentList);
    }

    [Fact]
    public void RestartHelperLaunchCarriesValidatedParentAndOriginalArguments()
    {
        ProcessStartInfo startInfo = ApplicationRestartService.CreateRestartHelperStartInfo(
            Path.Combine("app", "SrvSurvey.Desktop"),
            Path.Combine("app", "SrvSurvey.Desktop.dll"),
            ["--frontier-id", "F123"],
            parentProcessId: 42,
            parentProcessStartTimeUtcTicks: 638934912000000000
        );

        Assert.Equal(
            [
                ApplicationRestartService.RestartAfterProcessArgument,
                "42",
                "638934912000000000",
                "--",
                "--frontier-id",
                "F123",
            ],
            startInfo.ArgumentList
        );
    }

    [Fact]
    public void FrameworkDependentRestartHelperLaunchIncludesManagedAssembly()
    {
        ProcessStartInfo startInfo = ApplicationRestartService.CreateRestartHelperStartInfo(
            Path.Combine("runtime", "dotnet.exe"),
            Path.Combine("app", "SrvSurvey.Desktop.dll"),
            [],
            parentProcessId: 42,
            parentProcessStartTimeUtcTicks: 638934912000000000
        );

        Assert.Equal(Path.Combine("app", "SrvSurvey.Desktop.dll"), startInfo.ArgumentList[0]);
        Assert.Equal(ApplicationRestartService.RestartAfterProcessArgument, startInfo.ArgumentList[1]);
    }

    [Fact]
    public void RestartHelperWaitsForParentExitBeforeLaunchingReplacement()
    {
        var request = new ApplicationRestartRequest(42, 638934912000000000, ["--frontier-id", "F123"]);
        List<string> events = [];

        int exitCode = ApplicationRestartService.RunRestartHelper(
            request,
            (_, _) =>
            {
                events.Add("parent-exited");
                return true;
            },
            arguments =>
            {
                events.Add("replacement-started:" + string.Join(' ', arguments));
                return true;
            }
        );

        Assert.Equal(0, exitCode);
        Assert.Equal(["parent-exited", "replacement-started:--frontier-id F123"], events);
    }

    [Fact]
    public void RestartHelperDoesNotLaunchReplacementWhileParentRemainsAlive()
    {
        var request = new ApplicationRestartRequest(42, 638934912000000000, []);
        bool replacementStarted = false;

        int exitCode = ApplicationRestartService.RunRestartHelper(
            request,
            (_, _) => false,
            _ => replacementStarted = true
        );

        Assert.Equal(1, exitCode);
        Assert.False(replacementStarted);
    }

    [Fact]
    public void RestartHelperArgumentsRoundTrip()
    {
        string[] arguments =
        [
            ApplicationRestartService.RestartAfterProcessArgument,
            "42",
            "638934912000000000",
            "--",
            "--journal-directory",
            "/home/cmdr/Saved Games",
        ];

        Assert.True(
            ApplicationRestartService.TryParseRestartRequest(arguments, out ApplicationRestartRequest? request)
        );
        Assert.NotNull(request);
        Assert.Equal(42, request.ParentProcessId);
        Assert.Equal(638934912000000000, request.ParentProcessStartTimeUtcTicks);
        Assert.Equal(["--journal-directory", "/home/cmdr/Saved Games"], request.ApplicationArguments);
    }

    [Fact]
    public void InvalidRestartHelperArgumentsAreRejected()
    {
        string[][] invalidRequests =
        [
            [],
            ["--wrong", "42", "638934912000000000", "--"],
            [ApplicationRestartService.RestartAfterProcessArgument, "invalid", "638934912000000000", "--"],
            [ApplicationRestartService.RestartAfterProcessArgument, "0", "638934912000000000", "--"],
            [ApplicationRestartService.RestartAfterProcessArgument, "42", "invalid", "--"],
            [ApplicationRestartService.RestartAfterProcessArgument, "42", "0", "--"],
            [ApplicationRestartService.RestartAfterProcessArgument, "42", "638934912000000000", "invalid"],
        ];

        foreach (string[] arguments in invalidRequests)
        {
            Assert.False(ApplicationRestartService.TryParseRestartRequest(arguments, out _));
        }
    }

    [Theory]
    [InlineData()]
    [InlineData("--not-a-restart-helper")]
    public void NonRestartArgumentsAreIgnored(params string[] arguments)
    {
        int? exitCode = null;
        var error = new StringWriter();

        bool handled = ApplicationRestartService.TryRunRestartHelper(
            arguments,
            (_, _) => throw new InvalidOperationException("unexpected wait"),
            _ => throw new InvalidOperationException("unexpected launch"),
            error,
            value => exitCode = value
        );

        Assert.False(handled);
        Assert.Null(exitCode);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void InvalidRestartArgumentsSetAUsageExitCode()
    {
        int? exitCode = null;
        var error = new StringWriter();

        bool handled = ApplicationRestartService.TryRunRestartHelper(
            [ApplicationRestartService.RestartAfterProcessArgument],
            (_, _) => true,
            _ => true,
            error,
            value => exitCode = value
        );

        Assert.True(handled);
        Assert.Equal(2, exitCode);
        Assert.Contains("arguments were invalid", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    public void ValidRestartArgumentsReportTheHelperOutcome(bool parentExited, bool replacementStarted, int expected)
    {
        int? exitCode = null;
        var error = new StringWriter();

        bool handled = ApplicationRestartService.TryRunRestartHelper(
            [ApplicationRestartService.RestartAfterProcessArgument, "42", "638934912000000000", "--"],
            (_, _) => parentExited,
            _ => replacementStarted,
            error,
            value => exitCode = value
        );

        Assert.True(handled);
        Assert.Equal(expected, exitCode);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void RestartHelperReportsExpectedLaunchFailures()
    {
        int? exitCode = null;
        var error = new StringWriter();

        bool handled = ApplicationRestartService.TryRunRestartHelper(
            [ApplicationRestartService.RestartAfterProcessArgument, "42", "638934912000000000", "--"],
            (_, _) => throw new InvalidOperationException("launch failed"),
            _ => true,
            error,
            value => exitCode = value
        );

        Assert.True(handled);
        Assert.Equal(1, exitCode);
        Assert.Contains("launch failed", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ParentWaitTreatsAReusedProcessIdAsAlreadyExited()
    {
        using var current = Process.GetCurrentProcess();

        bool exited = ApplicationRestartService.WaitForParentExit(
            current.Id,
            current.StartTime.ToUniversalTime().Ticks + TimeSpan.FromSeconds(2).Ticks,
            TimeSpan.Zero
        );

        Assert.True(exited);
    }

    [Fact]
    public void ParentWaitHonorsTheTimeoutForTheMatchingProcess()
    {
        using var current = Process.GetCurrentProcess();

        bool exited = ApplicationRestartService.WaitForParentExit(
            current.Id,
            current.StartTime.ToUniversalTime().Ticks,
            TimeSpan.Zero
        );

        Assert.False(exited);
    }

    [Fact]
    public void ParentWaitTreatsAMissingProcessAsAlreadyExited()
    {
        Assert.True(ApplicationRestartService.WaitForParentExit(int.MaxValue, 1, TimeSpan.Zero));
    }

    [Fact]
    public void LinuxAppImageRestartUsesTheStableImageInsteadOfItsTemporaryMount()
    {
        string temporaryMount = Path.Combine("tmp", ".mount_SrvSurvey", "SrvSurvey.Desktop");
        string appImage = Path.Combine("home", "cmdr", "Applications", "SrvSurvey.AppImage");

        string launcher = ApplicationRestartService.ResolveLauncherPath(
            temporaryMount,
            appImage,
            isLinux: true,
            path => path == appImage
        );

        Assert.Equal(Path.GetFullPath(appImage), launcher);
    }

    [Theory]
    [InlineData(false, "app/SrvSurvey.AppImage")]
    [InlineData(true, null)]
    [InlineData(true, "missing/SrvSurvey.AppImage")]
    public void RestartFallsBackToCurrentExecutableWithoutAUsableLinuxAppImage(bool isLinux, string? appImage)
    {
        string processPath = Path.Combine("app", "SrvSurvey.Desktop");

        string launcher = ApplicationRestartService.ResolveLauncherPath(processPath, appImage, isLinux, _ => false);

        Assert.Equal(Path.GetFullPath(processPath), launcher);
    }
}
