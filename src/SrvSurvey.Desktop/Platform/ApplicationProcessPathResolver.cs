using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SrvSurvey.Desktop.Platform;

internal static unsafe partial class ApplicationProcessPathResolver
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int MaximumWindowsPathCapacity = 32_768;

    public static bool TryResolve(
        Process process,
        out string? executablePath,
        out string method,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(process);
        error = null;
        try
        {
            var path = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path))
            {
                executablePath = Canonicalize(path);
                method = "Process.MainModule";
                error = null;
                return true;
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception
                or InvalidOperationException
                or NotSupportedException)
        {
            error = exception.Message;
        }

        if (OperatingSystem.IsWindows()
            && TryResolveWindows(process.Id, out executablePath, out error))
        {
            method = "QueryFullProcessImageNameW";
            return true;
        }

        if (OperatingSystem.IsLinux()
            && TryResolveLinux(process.Id, out executablePath, out error))
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
        var fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsLinux())
        {
            try
            {
                fullPath = new FileInfo(fullPath)
                    .ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? fullPath;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException)
            {
                // The normalized absolute path remains a safe fallback.
            }
        }
        else if (OperatingSystem.IsWindows()
            && TryGetFinalWindowsPath(fullPath, out var finalPath))
        {
            fullPath = finalPath;
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
    }

    private static bool TryResolveWindows(
        int processId,
        out string? executablePath,
        out string? error)
    {
        using var handle = OpenProcess(
            ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);
        if (handle.IsInvalid)
        {
            executablePath = null;
            error = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
            return false;
        }

        var capacity = MaximumWindowsPathCapacity;
        var buffer = new char[capacity];
        fixed (char* bufferPointer = buffer)
        {
            if (!QueryFullProcessImageNameW(
                handle,
                0,
                bufferPointer,
                ref capacity))
            {
                executablePath = null;
                error = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
                return false;
            }
        }

        executablePath = Canonicalize(new string(buffer, 0, capacity));
        error = null;
        return true;
    }

    private static bool TryResolveLinux(
        int processId,
        out string? executablePath,
        out string? error)
    {
        try
        {
            var link = new FileInfo($"/proc/{processId}/exe")
                .ResolveLinkTarget(returnFinalTarget: true);
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
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            executablePath = null;
            error = exception.Message;
            return false;
        }
    }

    private static bool TryGetFinalWindowsPath(
        string path,
        out string finalPath)
    {
        try
        {
            using var handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var buffer = new char[MaximumWindowsPathCapacity];
            uint length;
            fixed (char* bufferPointer = buffer)
            {
                length = GetFinalPathNameByHandleW(
                    handle,
                    bufferPointer,
                    (uint)buffer.Length,
                    0);
            }
            if (length == 0 || length >= buffer.Length)
            {
                finalPath = path;
                return false;
            }

            finalPath = RemoveWindowsDevicePrefix(new string(buffer, 0, (int)length));
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            finalPath = path;
            return false;
        }
    }

    private static string RemoveWindowsDevicePrefix(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        return path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)
            ? path[devicePrefix.Length..]
            : path;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "QueryFullProcessImageNameW",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(
        SafeProcessHandle process,
        uint flags,
        char* executableName,
        ref int size);

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "GetFinalPathNameByHandleW",
        SetLastError = true)]
    private static partial uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        char* path,
        uint capacity,
        uint flags);
}
