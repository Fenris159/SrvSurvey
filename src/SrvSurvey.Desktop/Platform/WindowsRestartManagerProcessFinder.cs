using System.Runtime.InteropServices;

namespace SrvSurvey.Desktop.Platform;

internal static partial class WindowsRestartManagerProcessFinder
{
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;
    private const int SessionKeyLength = 32;

    public static IReadOnlySet<int> FindLockingProcessIds(string executablePath, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!OperatingSystem.IsWindows())
        {
            return new HashSet<int>();
        }

        char[] sessionKey = new char[SessionKeyLength + 1];
        int startResult = RmStartSession(out uint session, 0, sessionKey);
        if (startResult != ErrorSuccess)
        {
            log?.Invoke($"Windows Restart Manager session failed with error {startResult}.");
            return new HashSet<int>();
        }

        try
        {
            int registerResult = RmRegisterResources(
                session,
                1,
                [Path.GetFullPath(executablePath)],
                0,
                IntPtr.Zero,
                0,
                null
            );
            if (registerResult != ErrorSuccess)
            {
                log?.Invoke($"Windows Restart Manager registration failed with error {registerResult}.");
                return new HashSet<int>();
            }

            return ReadProcessIds(session, log);
        }
        finally
        {
            _ = RmEndSession(session);
        }
    }

    private static HashSet<int> ReadProcessIds(uint session, Action<string>? log)
    {
        uint count = 0;
        uint rebootReasons = 0;
        int result = RmGetList(session, out uint needed, ref count, null, ref rebootReasons);
        if (result == ErrorSuccess && needed == 0)
        {
            return [];
        }

        if (result != ErrorMoreData)
        {
            LogDiscoveryFailure(log, result);
            return [];
        }

        return ReadAllocatedProcessList(session, needed, rebootReasons, log);
    }

    private static HashSet<int> ReadAllocatedProcessList(
        uint session,
        uint needed,
        uint rebootReasons,
        Action<string>? log
    )
    {
        int result = ErrorMoreData;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint count = needed;
            var processes = new RestartManagerProcessInfo[count];
            result = RmGetList(session, out needed, ref count, processes, ref rebootReasons);
            if (result == ErrorSuccess)
            {
                return processes
                    .Take(checked((int)count))
                    .Select(process => process.Process.ProcessId)
                    .Where(processId => processId > 0)
                    .ToHashSet();
            }

            if (result != ErrorMoreData)
            {
                break;
            }
        }

        LogDiscoveryFailure(log, result);
        return [];
    }

    private static void LogDiscoveryFailure(Action<string>? log, int error)
    {
        log?.Invoke($"Windows Restart Manager discovery failed with error {error}.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RestartManagerUniqueProcess
    {
        public int ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RestartManagerProcessInfo
    {
        public RestartManagerUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string ApplicationName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceShortName;

        public uint ApplicationType;
        public uint ApplicationStatus;
        public uint TerminalServicesSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

#pragma warning disable SYSLIB1054 // Restart Manager arrays require runtime marshalling.
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, [Out] char[] sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        string[] fileNames,
        uint applicationCount,
        IntPtr applications,
        uint serviceCount,
        string[]? serviceNames
    );

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint processInfoNeeded,
        ref uint processInfoCount,
        [In, Out] RestartManagerProcessInfo[]? affectedApplications,
        ref uint rebootReasons
    );
#pragma warning restore SYSLIB1054

    [LibraryImport("rstrtmgr.dll")]
    private static partial int RmEndSession(uint sessionHandle);
}
