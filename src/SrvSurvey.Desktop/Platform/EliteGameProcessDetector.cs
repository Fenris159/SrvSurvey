using System.ComponentModel;
using System.Diagnostics;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Platform;

public interface IEliteGameProcessDetector
{
    bool IsRunning();
}

public sealed class EliteGameProcessDetector : IEliteGameProcessDetector
{
    public bool IsRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(
                EliteGameWindowIdentity.WindowsProcessName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or Win32Exception)
        {
            return false;
        }
    }
}

public static class VisitedStarsCacheTargetLocator
{
    public static string? ResolveCurrent(string frontierId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return ResolveWindows(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            frontierId);
    }

    public static string? ResolveWindows(
        string localApplicationData,
        string frontierId)
    {
        if (string.IsNullOrWhiteSpace(localApplicationData)
            || frontierId.Length < 2
            || frontierId[0] is not ('F' or 'f')
            || !frontierId[1..].All(char.IsAsciiDigit))
        {
            return null;
        }

        return Path.Combine(
            Path.GetFullPath(localApplicationData),
            "Frontier Developments",
            "Elite Dangerous",
            frontierId[1..],
            VisitedStarsCacheService.CacheFileName);
    }
}
