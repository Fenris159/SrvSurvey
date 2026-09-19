using System.Text.RegularExpressions;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed partial class ReleaseWorkflowContractTests
{
    [GeneratedRegex("-notmatch '([^']+)'")]
    private static partial Regex WorkflowVersionPattern();

    [GeneratedRegex(@"ValidatePattern\('([^']+)'\)")]
    private static partial Regex PackageVersionPattern();

    [GeneratedRegex("-match '([^']+)'")]
    private static partial Regex WorkflowPrereleasePattern();

    [Theory]
    [InlineData("2.1.3.0", true)]
    [InlineData("2.1.3.0-rc.44", true)]
    [InlineData("2.1.3.0-rc.44.5", true)]
    [InlineData("2.1.3.0-rc.44.0", true)]
    [InlineData("2.1.3.0-rc.44.05", false)]
    [InlineData("2.1.3.0-rc.044.5", false)]
    [InlineData("2.1.3.0-rc.44.", false)]
    [InlineData("2.1.3.0-rc.44.5.1", false)]
    public void WorkflowAndPackageValidatorsAgreeOnCandidateRevisions(string version, bool expected)
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "build-srvsurvey-xp.yml"));
        Match workflowPattern = WorkflowVersionPattern().Match(workflow);
        Assert.True(workflowPattern.Success);
        Assert.Equal(expected, System.Text.RegularExpressions.Regex.IsMatch(version, workflowPattern.Groups[1].Value));
        foreach (string? file in new[] { "New-CrossPlatformPackageManifest.ps1", "New-CrossPlatformReleaseIndex.ps1" })
        {
            string script = File.ReadAllText(Path.Combine(root, "scripts", file));
            Match pattern = PackageVersionPattern().Match(script);
            Assert.True(pattern.Success);
            Assert.Equal(expected, System.Text.RegularExpressions.Regex.IsMatch(version, pattern.Groups[1].Value));
        }

        if (expected)
        {
            Match prereleasePattern = WorkflowPrereleasePattern().Match(workflow);
            Assert.True(prereleasePattern.Success);
            Assert.Equal(
                version.Contains("-rc.", StringComparison.Ordinal),
                System.Text.RegularExpressions.Regex.IsMatch(version, prereleasePattern.Groups[1].Value)
            );
        }
    }

    [Fact]
    public void DispatchedReleasesUseTheDesktopProjectVersion()
    {
        string workflow = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ".github", "workflows", "build-srvsurvey-xp.yml")
        );

        Assert.DoesNotContain("      version:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("      rc_number:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("REQUESTED_VERSION", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("RC_NUMBER", workflow, StringComparison.Ordinal);
        Assert.Contains("$packageVersion = $projectVersionText", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "Development releases require the project Version to end in -rc.N or -rc.N.N.",
            workflow,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Stable releases require the project Version without a release-candidate suffix.",
            workflow,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ReleasePackagesIncludeTheReplayController()
    {
        string workflow = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), ".github", "workflows", "build-srvsurvey-xp.yml")
        );

        Assert.Contains(
            "dotnet publish src/SrvSurvey.ReplayController/SrvSurvey.ReplayController.csproj",
            workflow,
            StringComparison.Ordinal
        );
        Assert.Contains("SrvSurvey.ReplayController.exe", workflow, StringComparison.Ordinal);
        string normalizedWorkflow = string.Join(
            ' ',
            workflow.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );
        Assert.Contains(
            "Get-ChildItem -LiteralPath $controllerOutput -File ` "
                + "-Filter 'SrvSurvey.ReplayController*' | "
                + "Copy-Item -Destination \"artifacts/${{ matrix.rid }}\" -Force",
            normalizedWorkflow,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "test -x squashfs-root/usr/lib/srvsurvey/SrvSurvey.ReplayController",
            workflow,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void LinuxAppImageExposesReplayControllerDispatch()
    {
        string appRun = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "packaging", "linux", "AppRun"));

        Assert.Contains("--replay-controller", appRun, StringComparison.Ordinal);
        Assert.Contains("SrvSurvey.ReplayController", appRun, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxAppImageSmokeRunsAreStoppedAsOwnedProcessGroups()
    {
        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "Test-LinuxAppImageRuntime.sh"));

        Assert.Contains("setsid --wait xvfb-run", script, StringComparison.Ordinal);
        Assert.Contains("kill -TERM -- \"-$process_group_id\"", script, StringComparison.Ordinal);
        Assert.Contains("kill -KILL -- \"-$process_group_id\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxAppImagePublishesEmbeddedUpdateInformationAndZsyncAsset()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "build-srvsurvey-xp.yml"));
        string appImageScript = File.ReadAllText(Path.Combine(root, "scripts", "New-LinuxAppImage.sh"));
        string indexScript = File.ReadAllText(Path.Combine(root, "scripts", "New-CrossPlatformReleaseIndex.ps1"));

        Assert.Contains("gh-releases-zsync|Fenris159|SrvSurvey|latest-pre", workflow, StringComparison.Ordinal);
        Assert.Contains("--appimage-updateinformation", workflow, StringComparison.Ordinal);
        Assert.Contains("--updateinformation \"$update_information\"", appImageScript, StringComparison.Ordinal);
        Assert.Contains("$output_path.zsync", appImageScript, StringComparison.Ordinal);
        Assert.Contains("runtimeIdentifier = 'linux-x64-appimage'", indexScript, StringComparison.Ordinal);
        Assert.Contains("schemaVersion = 2", indexScript, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not find the repository root from the test directory.");
    }
}
