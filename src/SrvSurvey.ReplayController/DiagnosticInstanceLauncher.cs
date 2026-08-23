using System.Diagnostics;

namespace SrvSurvey.ReplayController;

public interface IDiagnosticInstanceLauncher
{
    Task<IDiagnosticInstance> LaunchAsync(
        string executablePath,
        string manifestPath,
        CancellationToken cancellationToken);
}

public interface IDiagnosticInstance : IAsyncDisposable
{
    bool IsRunning { get; }

    Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

internal sealed class ProcessDiagnosticInstanceLauncher : IDiagnosticInstanceLauncher
{
    public Task<IDiagnosticInstance> LaunchAsync(
        string executablePath,
        string manifestPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullExecutablePath = Path.GetFullPath(executablePath);
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = fullExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(fullExecutablePath)
                ?? AppContext.BaseDirectory,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--diagnostic-replay");
        startInfo.ArgumentList.Add(fullManifestPath);
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "The diagnostic SrvSurvey process could not be started.");
        return Task.FromResult<IDiagnosticInstance>(
            new ProcessDiagnosticInstance(process));
    }

    private sealed class ProcessDiagnosticInstance(Process process)
        : IDiagnosticInstance
    {
        public bool IsRunning
        {
            get
            {
                try
                {
                    return !process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (!IsRunning)
            {
                return;
            }

            _ = process.CloseMainWindow();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                return;
            }
            catch (OperationCanceledException) when (
                timeout.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                // The controller owns this diagnostic child and may terminate it.
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (IsRunning)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
        }

        public async Task<int> WaitForExitAsync(
            CancellationToken cancellationToken)
        {
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await StopAsync(CancellationToken.None);
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
