using SrvSurvey.ReplayController;

namespace SrvSurvey.ReplayController.Tests;

public sealed class DiagnosticInstanceLauncherTests
{
    [Fact]
    public async Task LaunchPassesDiagnosticArgumentsAndObservesProcessExit()
    {
        var executablePath = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "where.exe")
            : "/usr/bin/env";
        var launcher = new ProcessDiagnosticInstanceLauncher();

        await using var instance = await launcher.LaunchAsync(
            executablePath,
            Path.Combine(Path.GetTempPath(), "replay-session.json"),
            CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var exitCode = await instance.WaitForExitAsync(timeout.Token);

        Assert.NotEqual(0, exitCode);
        Assert.False(instance.IsRunning);
        await instance.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LaunchHonorsPreCanceledOperation()
    {
        var launcher = new ProcessDiagnosticInstanceLauncher();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            launcher.LaunchAsync(
                "unused",
                "unused",
                cancellation.Token));
    }
}
