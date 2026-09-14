using System.Diagnostics;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationRestartServiceTests
{
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
