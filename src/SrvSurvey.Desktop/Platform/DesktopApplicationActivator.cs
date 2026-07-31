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
            foreach (var process in Process.GetProcessesByName(current.ProcessName))
            {
                using (process)
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
                            continue;
                        }

                        process.Refresh();
                        var handle = process.MainWindowHandle;
                        if (handle == nint.Zero)
                        {
                            continue;
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
                    }
                }
            }

            if (Environment.TickCount64 < deadline)
            {
                Thread.Sleep(100);
            }
        }
        while (Environment.TickCount64 < deadline);

        return false;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
}
