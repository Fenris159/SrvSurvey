using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using SrvSurvey.Desktop.Runtime;

namespace SrvSurvey.Desktop.Platform;

internal sealed record ApplicationRestartRequest(
    int ParentProcessId,
    long ParentProcessStartTimeUtcTicks,
    IReadOnlyList<string> ApplicationArguments
);

public sealed class ApplicationRestartService
{
    internal const string RestartAfterProcessArgument = "--restart-after-process";
    private const string ArgumentSeparator = "--";
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromSeconds(30);
    private readonly string processPath;
    private readonly string entryAssemblyPath;
    private readonly IReadOnlyList<string> arguments;
    private readonly Func<(int ProcessId, long StartTimeUtcTicks)> currentProcessIdentity;
    private readonly Func<ProcessStartInfo, bool> processStarter;

    public ApplicationRestartService()
        : this(ResolveLauncherPath(), ResolveEntryAssemblyPath(), Program.StartupArguments) { }

    internal ApplicationRestartService(string processPath, string entryAssemblyPath, IReadOnlyList<string> arguments)
        : this(processPath, entryAssemblyPath, arguments, GetCurrentProcessIdentity, StartProcess) { }

    internal ApplicationRestartService(
        string processPath,
        string entryAssemblyPath,
        IReadOnlyList<string> arguments,
        Func<(int ProcessId, long StartTimeUtcTicks)> currentProcessIdentity,
        Func<ProcessStartInfo, bool> processStarter
    )
    {
        this.processPath = Path.GetFullPath(processPath);
        this.entryAssemblyPath = Path.GetFullPath(entryAssemblyPath);
        this.arguments = arguments?.ToArray() ?? throw new ArgumentNullException(nameof(arguments));
        this.currentProcessIdentity =
            currentProcessIdentity ?? throw new ArgumentNullException(nameof(currentProcessIdentity));
        this.processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
    }

    public void StartRestartHelper()
    {
        DesktopExternalEffectPolicy.ThrowIfDisabled();
        (int processId, long startTimeUtcTicks) = currentProcessIdentity();
        ProcessStartInfo startInfo = CreateRestartHelperStartInfo(
            processPath,
            entryAssemblyPath,
            arguments,
            processId,
            startTimeUtcTicks
        );
        if (!processStarter(startInfo))
        {
            throw new InvalidOperationException("The SrvSurvey restart helper did not start.");
        }
    }

    internal static bool TryRunRestartHelper(IReadOnlyList<string> arguments)
    {
        return TryRunRestartHelper(
            arguments,
            WaitForParentExit,
            StartCurrentApplication,
            Console.Error,
            exitCode => Environment.ExitCode = exitCode
        );
    }

    internal static bool TryRunRestartHelper(
        IReadOnlyList<string> arguments,
        Func<int, long, bool> waitForParentExit,
        Func<IReadOnlyList<string>, bool> startReplacement,
        TextWriter errorWriter,
        Action<int> setExitCode
    )
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(waitForParentExit);
        ArgumentNullException.ThrowIfNull(startReplacement);
        ArgumentNullException.ThrowIfNull(errorWriter);
        ArgumentNullException.ThrowIfNull(setExitCode);
        if (arguments.Count == 0 || !string.Equals(arguments[0], RestartAfterProcessArgument, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryParseRestartRequest(arguments, out ApplicationRestartRequest? request))
        {
            errorWriter.WriteLine("SrvSurvey restart helper arguments were invalid.");
            setExitCode(2);
            return true;
        }

        try
        {
            setExitCode(RunRestartHelper(request, waitForParentExit, startReplacement));
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidOperationException
                        or Win32Exception
                        or NotSupportedException
            )
        {
            errorWriter.WriteLine("SrvSurvey restart helper failed: " + exception.Message);
            setExitCode(1);
        }

        return true;
    }

    internal static int RunRestartHelper(
        ApplicationRestartRequest request,
        Func<int, long, bool> waitForParentExit,
        Func<IReadOnlyList<string>, bool> startReplacement
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(waitForParentExit);
        ArgumentNullException.ThrowIfNull(startReplacement);
        if (!waitForParentExit(request.ParentProcessId, request.ParentProcessStartTimeUtcTicks))
        {
            return 1;
        }

        return startReplacement(request.ApplicationArguments) ? 0 : 1;
    }

    internal static bool TryParseRestartRequest(
        IReadOnlyList<string> arguments,
        [NotNullWhen(true)] out ApplicationRestartRequest? request
    )
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (
            arguments.Count < 4
            || !string.Equals(arguments[0], RestartAfterProcessArgument, StringComparison.Ordinal)
            || !int.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parentProcessId)
            || parentProcessId <= 0
            || !long.TryParse(
                arguments[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long parentProcessStartTimeUtcTicks
            )
            || parentProcessStartTimeUtcTicks <= 0
            || !string.Equals(arguments[3], ArgumentSeparator, StringComparison.Ordinal)
        )
        {
            request = null;
            return false;
        }

        request = new ApplicationRestartRequest(
            parentProcessId,
            parentProcessStartTimeUtcTicks,
            arguments.Skip(4).ToArray()
        );
        return true;
    }

    internal static ProcessStartInfo CreateRestartHelperStartInfo(
        string processPath,
        string entryAssemblyPath,
        IReadOnlyList<string> arguments,
        int parentProcessId,
        long parentProcessStartTimeUtcTicks
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parentProcessId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parentProcessStartTimeUtcTicks);
        ProcessStartInfo startInfo = CreateStartInfo(processPath, entryAssemblyPath, []);
        startInfo.ArgumentList.Add(RestartAfterProcessArgument);
        startInfo.ArgumentList.Add(parentProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(parentProcessStartTimeUtcTicks.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(ArgumentSeparator);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    internal static ProcessStartInfo CreateStartInfo(
        string processPath,
        string entryAssemblyPath,
        IReadOnlyList<string> arguments
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryAssemblyPath);
        ArgumentNullException.ThrowIfNull(arguments);
        var startInfo = new ProcessStartInfo
        {
            FileName = processPath,
            WorkingDirectory = ResolveWorkingDirectory(processPath, entryAssemblyPath),
            UseShellExecute = false,
        };
        if (IsDotnetHost(processPath))
        {
            startInfo.ArgumentList.Add(entryAssemblyPath);
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static bool WaitForParentExit(int parentProcessId, long parentProcessStartTimeUtcTicks)
    {
        return WaitForParentExit(parentProcessId, parentProcessStartTimeUtcTicks, ParentExitTimeout);
    }

    internal static bool WaitForParentExit(int parentProcessId, long parentProcessStartTimeUtcTicks, TimeSpan timeout)
    {
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            long actualStartTimeUtcTicks = parent.StartTime.ToUniversalTime().Ticks;
            if (Math.Abs(actualStartTimeUtcTicks - parentProcessStartTimeUtcTicks) > TimeSpan.FromSeconds(1).Ticks)
            {
                return true;
            }

            return parent.WaitForExit((int)timeout.TotalMilliseconds);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return true;
        }
    }

    private static bool StartCurrentApplication(IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = CreateStartInfo(ResolveLauncherPath(), ResolveEntryAssemblyPath(), arguments);
        using var process = Process.Start(startInfo);
        return process is not null;
    }

    private static (int ProcessId, long StartTimeUtcTicks) GetCurrentProcessIdentity()
    {
        using var current = Process.GetCurrentProcess();
        return (current.Id, current.StartTime.ToUniversalTime().Ticks);
    }

    private static bool StartProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        return process is not null;
    }

    private static string ResolveLauncherPath()
    {
        string processPath =
            Environment.ProcessPath
            ?? throw new InvalidOperationException("The current application executable could not be resolved.");
        return ResolveLauncherPath(
            processPath,
            Environment.GetEnvironmentVariable("APPIMAGE"),
            OperatingSystem.IsLinux(),
            File.Exists
        );
    }

    internal static string ResolveLauncherPath(
        string processPath,
        string? appImagePath,
        bool isLinux,
        Func<string, bool> fileExists
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        ArgumentNullException.ThrowIfNull(fileExists);
        if (isLinux && !string.IsNullOrWhiteSpace(appImagePath) && fileExists(appImagePath))
        {
            return Path.GetFullPath(appImagePath);
        }

        return Path.GetFullPath(processPath);
    }

    private static string ResolveEntryAssemblyPath()
    {
        return Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("The current application assembly could not be resolved.");
    }

    private static string ResolveWorkingDirectory(string processPath, string entryAssemblyPath)
    {
        string path = IsDotnetHost(processPath) ? entryAssemblyPath : processPath;
        return Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidOperationException("The SrvSurvey restart working directory could not be resolved.");
    }

    private static bool IsDotnetHost(string path)
    {
        return string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase);
    }
}
