using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SrvSurvey.Desktop.Platform;

internal static partial class DesktopApplicationActivator
{
    private const int RestoreWindow = 9;
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(2);

    public static bool TryActivateExistingInstance()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var current = Process.GetCurrentProcess();
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        var deadline = Environment.TickCount64
            + (long)ActivationTimeout.TotalMilliseconds;
        do
        {
            if (TryActivateMatchingProcess(current, executablePath))
            {
                return true;
            }

            if (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(100);
            }
        }
        while (Environment.TickCount64 < deadline);

        return false;
    }

    private static bool TryActivateMatchingProcess(
        Process current,
        string executablePath)
    {
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (TryActivateProcessWindow(current, process, executablePath))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryActivateProcessWindow(
        Process current,
        Process process,
        string executablePath)
    {
        try
        {
            if (process.Id == current.Id
                || process.SessionId != current.SessionId
                || !string.Equals(
                    process.MainModule?.FileName,
                    executablePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            process.Refresh();
            var handle = process.MainWindowHandle;
            if (handle == nint.Zero)
            {
                return false;
            }

            _ = ShowWindow(handle, RestoreWindow);
            _ = SetForegroundWindow(handle);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or NotSupportedException
                or Win32Exception)
        {
            // The original instance can exit while the callback
            // process is inspecting or activating its window.
            return false;
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
}
