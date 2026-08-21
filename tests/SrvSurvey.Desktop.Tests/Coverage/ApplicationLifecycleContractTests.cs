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

    [Fact]
    public void LinuxTerminationSignalUsesOrderlyDesktopShutdown()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "App.axaml.cs"));

        Assert.Contains("PosixSignal.SIGTERM", source, StringComparison.Ordinal);
        Assert.Contains(
            "Dispatcher.UIThread.Post(() => desktop.Shutdown());",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "linuxTerminationRegistration?.Dispose();",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BoxelClipboardWriterMatchesTheDesktopLifetime()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "App.axaml.cs"));
        var windowCreated = source.IndexOf(
            "mainWindow = new MainWindow(viewModel);",
            StringComparison.Ordinal);
        var writerRegistered = source.IndexOf(
            "viewModel.BoxelClipboard.SetWriter(WriteClipboardAsync);",
            StringComparison.Ordinal);
        var writerCleared = source.IndexOf(
            "viewModel.BoxelClipboard.SetWriter(null);",
            StringComparison.Ordinal);
        var servicesDisposed = source.IndexOf(
            "await DisposeDesktopServicesAsync(viewModel);",
            StringComparison.Ordinal);

        Assert.True(windowCreated >= 0);
        Assert.True(writerRegistered >= 0);
        Assert.True(writerCleared >= 0);
        Assert.True(servicesDisposed >= 0);
        Assert.True(writerRegistered > windowCreated);
        Assert.True(writerCleared > writerRegistered);
        Assert.True(servicesDisposed > writerCleared);
        Assert.Equal(
            writerRegistered,
            source.LastIndexOf(
                "viewModel.BoxelClipboard.SetWriter(WriteClipboardAsync);",
                StringComparison.Ordinal));
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
