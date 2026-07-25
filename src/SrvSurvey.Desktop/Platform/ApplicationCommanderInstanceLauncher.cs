using System.Diagnostics;
using System.Reflection;

namespace SrvSurvey.Desktop.Platform;

public interface ICommanderInstanceLauncher
{
    Task LaunchAsync(
        string frontierId,
        string journalDirectory,
        CancellationToken cancellationToken = default);
}

public sealed class ApplicationCommanderInstanceLauncher
    : ICommanderInstanceLauncher
{
    public Task LaunchAsync(
        string frontierId,
        string journalDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "The current SrvSurvey executable path is unavailable.");
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = false,
        };
        var entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        if (Path.GetFileNameWithoutExtension(processPath).Equals(
                "dotnet",
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(entryAssemblyPath))
        {
            startInfo.ArgumentList.Add(entryAssemblyPath);
        }

        startInfo.ArgumentList.Add("--frontier-id");
        startInfo.ArgumentList.Add(frontierId);
        startInfo.ArgumentList.Add("--journal-directory");
        startInfo.ArgumentList.Add(Path.GetFullPath(journalDirectory));
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "The additional SrvSurvey process did not start.");
        return Task.CompletedTask;
    }
}
