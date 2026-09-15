using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationCommanderInstanceLauncherTests
{
    [Fact]
    public void LaunchedCommanderInstanceCarriesTheConcurrentInstanceAuthorization()
    {
        string journalDirectory = Path.GetFullPath(Path.Combine("profiles", "F123"));

        System.Diagnostics.ProcessStartInfo startInfo = ApplicationCommanderInstanceLauncher.CreateStartInfo(
            Path.GetFullPath("SrvSurvey.Desktop.exe"),
            null,
            "F123",
            journalDirectory
        );

        Assert.Equal(
            [
                StartupOptions.MultiCommanderInstanceOption,
                "--frontier-id",
                "F123",
                "--journal-directory",
                journalDirectory,
            ],
            startInfo.ArgumentList
        );
    }
}
