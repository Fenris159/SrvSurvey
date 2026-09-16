using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SrvSurvey.Desktop.Platform;

internal static partial class ApplicationProcessPathResolver
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int MaximumWindowsPathCapacity = 32_768;

    public static bool TryResolve(Process process, out string? executablePath, out string method, out string? error)
    {
        ArgumentNullException.ThrowIfNull(process);
        error = null;
        try
        {
            string? path = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path))
            {
                executablePath = Canonicalize(path);
                method = "Process.MainModule";
                error = null;
                return true;
            }
        }
        catch (Exception exception)
            when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            error = exception.Message;
        }

        if (OperatingSystem.IsWindows() && TryResolveWindows(process.Id, out executablePath, out error))
        {
            method = "QueryFullProcessImageNameW";
            return true;
        }

        if (OperatingSystem.IsLinux() && TryResolveLinux(process.Id, out executablePath, out error))
        {
            method = "/proc/pid/exe";
            return true;
        }

        executablePath = null;
        method = "unavailable";
        error ??= "The executable path was not available.";
        return false;
    }

    public static string Canonicalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsLinux())
        {
            try
            {
                fullPath = new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fullPath;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // The normalized absolute path remains a safe fallback.
            }
        }
        else if (OperatingSystem.IsWindows() && TryGetFinalWindowsPath(fullPath, out string? finalPath))
        {
            fullPath = finalPath;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
    }

    internal static bool TryResolveWindows(int processId, out string? executablePath, out string? error)
    {
        using SafeProcessHandle handle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);
        if (handle.IsInvalid)
        {
            executablePath = null;
            error = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
            return false;
        }

        char[] buffer = ArrayPool<char>.Shared.Rent(MaximumWindowsPathCapacity);
        try
        {
            int capacity = buffer.Length;
            if (!QueryFullProcessImageNameW(handle, 0, buffer, ref capacity))
            {
                executablePath = null;
                error = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
                return false;
            }

            executablePath = Canonicalize(new string(buffer, 0, capacity));
            error = null;
            return true;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    internal static bool TryResolveLinux(int processId, out string? executablePath, out string? error)
    {
        try
        {
            FileSystemInfo? link = new FileInfo($"/proc/{processId}/exe").ResolveLinkTarget(returnFinalTarget: true);
            if (link is null)
            {
                executablePath = null;
                error = "The /proc executable link was unavailable.";
                return false;
            }

            executablePath = Canonicalize(link.FullName);
            error = null;
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            executablePath = null;
            error = exception.Message;
            return false;
        }
    }

    internal static bool TryReadLinuxEnvironmentValue(int processId, string name, out string? value, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            byte[] data = File.ReadAllBytes($"/proc/{processId}/environ");
            foreach (string entry in Encoding.UTF8.GetString(data).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = entry.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                if (!entry.AsSpan(0, separator).SequenceEqual(name))
                {
                    continue;
                }

                value = entry[(separator + 1)..];
                error = null;
                return !string.IsNullOrWhiteSpace(value);
            }

            value = null;
            error = $"The {name} environment variable was not present.";
            return false;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            value = null;
            error = exception.Message;
            return false;
        }
    }

    internal static bool TryReadLinuxCommandLine(int processId, out IReadOnlyList<string> arguments, out string? error)
    {
        try
        {
            byte[] data = File.ReadAllBytes($"/proc/{processId}/cmdline");
            if (data.Length == 0)
            {
                arguments = [];
                error = "The /proc command line was empty.";
                return false;
            }

            arguments = Encoding.UTF8.GetString(data).Split('\0', StringSplitOptions.RemoveEmptyEntries);
            error = null;
            return arguments.Count > 0;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            arguments = [];
            error = exception.Message;
            return false;
        }
    }

    internal static bool TryGetFinalWindowsPath(string path, out string finalPath)
    {
        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            char[] buffer = ArrayPool<char>.Shared.Rent(MaximumWindowsPathCapacity);
            try
            {
                uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
                if (length == 0 || length >= buffer.Length)
                {
                    finalPath = path;
                    return false;
                }

                finalPath = RemoveWindowsDevicePrefix(new string(buffer, 0, (int)length));
                return true;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            finalPath = path;
            return false;
        }
    }

    internal static string RemoveWindowsDevicePrefix(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        return path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase) ? path[devicePrefix.Length..] : path;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId
    );

#pragma warning disable SYSLIB1054 // Output arrays require runtime marshalling.
    [DllImport(
        "kernel32.dll",
        EntryPoint = "QueryFullProcessImageNameW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(
        SafeProcessHandle process,
        uint flags,
        [Out] char[] executableName,
        ref int size
    );

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = CharSet.Unicode,
        SetLastError = true
    )]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        [Out] char[] path,
        uint capacity,
        uint flags
    );
#pragma warning restore SYSLIB1054
}
