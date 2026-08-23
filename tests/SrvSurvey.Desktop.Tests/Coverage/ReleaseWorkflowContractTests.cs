namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class ReleaseWorkflowContractTests
{
    [Fact]
    public void DispatchedReleasesUseTheDesktopProjectVersion()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "build-srvsurvey-xp.yml"));

        Assert.DoesNotContain("      version:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("      rc_number:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("REQUESTED_VERSION", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("RC_NUMBER", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "$packageVersion = $projectVersionText",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Development releases require the project Version to end in -rc.N.",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Stable releases require the project Version without an -rc.N suffix.",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasePackagesIncludeTheReplayController()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "build-srvsurvey-xp.yml"));

        Assert.Contains(
            "dotnet publish src/SrvSurvey.ReplayController/SrvSurvey.ReplayController.csproj",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "SrvSurvey.ReplayController.exe",
            workflow,
            StringComparison.Ordinal);
        var normalizedWorkflow = string.Join(
            ' ',
            workflow.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(
            "Get-ChildItem -LiteralPath $controllerOutput -File ` "
                + "-Filter 'SrvSurvey.ReplayController*' | "
                + "Copy-Item -Destination \"artifacts/${{ matrix.rid }}\" -Force",
            normalizedWorkflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "test -x squashfs-root/usr/lib/srvsurvey/SrvSurvey.ReplayController",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxAppImageExposesReplayControllerDispatch()
    {
        var appRun = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "packaging",
            "linux",
            "AppRun"));

        Assert.Contains(
            "--replay-controller",
            appRun,
            StringComparison.Ordinal);
        Assert.Contains(
            "SrvSurvey.ReplayController",
            appRun,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SrvSurvey.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find the repository root from the test directory.");
    }
}
