using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationRestartServiceTests
{
    [Fact]
    public void FrameworkDependentLaunchPreservesAssemblyAndArguments()
    {
        var startInfo = ApplicationRestartService.CreateStartInfo(
            Path.Combine("runtime", "dotnet.exe"),
            Path.Combine("app", "SrvSurvey.Desktop.dll"),
            ["--journal-directory", "C:\\Elite Journals", "--frontier-id", "F123"]);

        Assert.Equal(Path.Combine("runtime", "dotnet.exe"), startInfo.FileName);
        Assert.Equal(
            [
                Path.Combine("app", "SrvSurvey.Desktop.dll"),
                "--journal-directory",
                "C:\\Elite Journals",
                "--frontier-id",
                "F123",
            ],
            startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void SelfContainedLaunchDoesNotAddManagedAssembly()
    {
        var startInfo = ApplicationRestartService.CreateStartInfo(
            Path.Combine("app", "SrvSurvey.Desktop.exe"),
            Path.Combine("app", "SrvSurvey.Desktop.dll"),
            ["--frontier-id", "F123"]);

        Assert.Equal(
            ["--frontier-id", "F123"],
            startInfo.ArgumentList);
    }
}
