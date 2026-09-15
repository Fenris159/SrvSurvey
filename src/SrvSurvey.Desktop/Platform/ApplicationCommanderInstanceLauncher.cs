using System.Diagnostics;
using System.Reflection;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop.Platform;

public interface ICommanderInstanceLauncher
{
    Task LaunchAsync(string frontierId, string journalDirectory, CancellationToken cancellationToken = default);
}

public sealed class ApplicationCommanderInstanceLauncher : ICommanderInstanceLauncher
{
    public Task LaunchAsync(string frontierId, string journalDirectory, CancellationToken cancellationToken = default)
    {
        DesktopExternalEffectPolicy.ThrowIfDisabled();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        string processPath =
            Environment.ProcessPath
            ?? throw new InvalidOperationException("The current SrvSurvey executable path is unavailable.");
        string? entryAssemblyPath = Assembly.GetEntryAssembly()?.Location;
        ProcessStartInfo startInfo = CreateStartInfo(processPath, entryAssemblyPath, frontierId, journalDirectory);
        using Process process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("The additional SrvSurvey process did not start.");
        return Task.CompletedTask;
    }

    internal static ProcessStartInfo CreateStartInfo(
        string processPath,
        string? entryAssemblyPath,
        string frontierId,
        string journalDirectory
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(frontierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        var startInfo = new ProcessStartInfo { FileName = processPath, UseShellExecute = false };
        if (
            Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(entryAssemblyPath)
        )
        {
            startInfo.ArgumentList.Add(entryAssemblyPath);
        }

        startInfo.ArgumentList.Add(StartupOptions.MultiCommanderInstanceOption);
        startInfo.ArgumentList.Add("--frontier-id");
        startInfo.ArgumentList.Add(frontierId);
        startInfo.ArgumentList.Add("--journal-directory");
        startInfo.ArgumentList.Add(Path.GetFullPath(journalDirectory));
        return startInfo;
    }
}
