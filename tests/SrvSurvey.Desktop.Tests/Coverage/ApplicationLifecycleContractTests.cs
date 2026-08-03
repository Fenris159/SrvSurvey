namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class ApplicationLifecycleContractTests
{
    [Fact]
    public void ClosingMainWindowShutsDownAndDisposesOverlayRuntime()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "App.axaml.cs"));

        Assert.Contains(
            "desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;",
            source,
            StringComparison.Ordinal);
        Assert.Contains("desktop.Exit +=", source, StringComparison.Ordinal);
        Assert.Contains(
            "systemSurveyOverlayCoordinator?.Dispose();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "guardianOverlayCoordinator?.Dispose();",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "overlayPresentationSession?.Dispose();",
            source,
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
