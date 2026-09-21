using System.Diagnostics;
using System.Text.Json;
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
        foreach (
            string? file in new[]
            {
                "New-CrossPlatformPackageManifest.ps1",
                "New-CrossPlatformReleaseIndex.ps1",
                "Resolve-CrossPlatformReleaseContract.ps1",
            }
        )
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
        Assert.Contains("./scripts/Resolve-CrossPlatformReleaseContract.ps1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("$releaseTag = \"xp-v$packageVersion\"", workflow, StringComparison.Ordinal);
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

        Assert.Contains("RELEASE_REPOSITORY: \"Fenris159/SrvSurvey\"", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "gh-releases-zsync|$release_owner|$release_name|latest-pre",
            workflow,
            StringComparison.Ordinal
        );
        Assert.Contains("gh-releases-zsync|$release_owner|$release_name|latest|", workflow, StringComparison.Ordinal);
        Assert.Contains("GH_REPO: ${{ env.RELEASE_REPOSITORY }}", workflow, StringComparison.Ordinal);
        Assert.Contains("--appimage-updateinformation", workflow, StringComparison.Ordinal);
        Assert.Contains("--updateinformation \"$update_information\"", appImageScript, StringComparison.Ordinal);
        Assert.Contains("$output_path.zsync", appImageScript, StringComparison.Ordinal);
        Assert.Contains("runtimeIdentifier = 'linux-x64-appimage'", indexScript, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2.1.3.0-rc.51", "xp-v2.1.3.0-rc.51", 1)]
    [InlineData("2.1.3.0-rc.52", "xp2-v2.1.3.0-rc.52", 2)]
    [InlineData("2.1.4.0", "xp2-v2.1.4.0", 2)]
    public void ReleaseContractUsesPermanentBridgeAndCurrentNamespace(
        string version,
        string expectedTag,
        int expectedSchema
    )
    {
        string root = FindRepositoryRoot();
        string output = RunPowerShell(root, "scripts/Resolve-CrossPlatformReleaseContract.ps1", "-Version", version);
        using var contract = JsonDocument.Parse(output);

        Assert.Equal(expectedTag, contract.RootElement.GetProperty("releaseTag").GetString());
        Assert.Equal(expectedSchema, contract.RootElement.GetProperty("indexSchemaVersion").GetInt32());
    }

    [Theory]
    [InlineData("2.1.3.0-rc.51", 1, 2)]
    [InlineData("2.1.3.0-rc.52", 2, 3)]
    public void GeneratedReleaseIndexMatchesReleaseCompatibilityContract(
        string version,
        int expectedSchema,
        int expectedPackageCount
    )
    {
        string root = FindRepositoryRoot();
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-release-contract-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            foreach (
                string fileName in new[]
                {
                    $"SrvSurvey-XP-{version}-win-x64.zip",
                    $"SrvSurvey-XP-{version}-linux-x64.tar.gz",
                    $"SrvSurvey-XP-{version}-x86_64.AppImage",
                }
            )
            {
                File.WriteAllText(Path.Combine(temporaryDirectory, fileName), fileName);
            }

            string indexPath = Path.Combine(temporaryDirectory, "release-index.json");
            RunPowerShell(
                root,
                "scripts/New-CrossPlatformReleaseIndex.ps1",
                "-PackageDirectory",
                temporaryDirectory,
                "-Version",
                version,
                "-OutputPath",
                indexPath
            );

            using var index = JsonDocument.Parse(File.ReadAllText(indexPath));
            JsonElement packages = index.RootElement.GetProperty("packages");
            Assert.Equal(expectedSchema, index.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(expectedPackageCount, packages.GetArrayLength());
            Assert.Equal(
                expectedSchema == 2,
                packages
                    .EnumerateArray()
                    .Any(package => package.GetProperty("runtimeIdentifier").GetString() == "linux-x64-appimage")
            );
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private static string RunPowerShell(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process =
            Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start pwsh.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"PowerShell exited with code {process.ExitCode}: {error}\n{output}");
        return output.Trim();
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
