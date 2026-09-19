using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using PipeWire.NET;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class PipeWirePortalMarshallingTests
{
    private const int FileDescriptorCloseOnExec = 1;
    private const int GetFileDescriptorFlags = 1;

    [Fact]
    public async Task PortalRemoteStartDoesNotThrowMarshalDirectiveException()
    {
        if (OperatingSystem.IsLinux())
        {
            await RunLinuxAsync();
            return;
        }

        Assert.Skip("PipeWire portal descriptor duplication only runs on Linux.");
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void PortalRemoteDuplicateIsClosedOnExec()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Portal file descriptors only run on Linux.");
        }

        nint path = Marshal.StringToCoTaskMemUTF8("/dev/null");
        try
        {
            int sourceDescriptor = OpenFile(path, 0);
            Assert.True(sourceDescriptor >= 0, "Could not open a source descriptor for the portal duplication test.");
            using var sourceHandle = new SafeFileHandle(sourceDescriptor, ownsHandle: true);
            int duplicate = PipeWireContext.DuplicateFileDescriptor(sourceDescriptor);
            Assert.True(duplicate >= 0, "Could not duplicate the portal descriptor.");
            using var duplicateHandle = new SafeFileHandle(duplicate, ownsHandle: true);

            int flags = GetDescriptorFlags(duplicate, GetFileDescriptorFlags);
            Assert.True(flags >= 0, "Could not read the duplicated portal descriptor flags.");

            Assert.True(
                (flags & FileDescriptorCloseOnExec) != 0,
                "The permission-scoped portal socket must not leak into child processes."
            );
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    [SupportedOSPlatform("linux")]
    private static async Task RunLinuxAsync()
    {
        PipeWireContext? context;
        try
        {
            context = new PipeWireContext("SrvSurvey.Test.ScreenCapture");
        }
        catch (Exception exception) when (exception is DllNotFoundException or InvalidOperationException)
        {
            Assert.Skip("libpipewire is not available in this environment: " + exception.Message);
            return;
        }

        await using (context)
        {
            using FileStream stream = File.OpenRead("/dev/null");
            try
            {
                await context.StartAsync(stream.SafeFileHandle);
            }
            catch (MarshalDirectiveException exception)
            {
                Assert.Fail(
                    "Wayland portal PipeWire startup used a SetLastError P/Invoke that DisableRuntimeMarshalling rejects: "
                        + exception.Message
                );
            }
            catch (InvalidOperationException)
            {
                // /dev/null is not a portal PipeWire remote. Reaching connect proves the
                // descriptor-duplication P/Invoke is compatible with runtime-marshalling-disabled.
                Assert.False(context.IsLoopStarted);
            }
        }
    }

    [DllImport("libc", EntryPoint = "fcntl")]
    private static extern int GetDescriptorFlags(int fileDescriptor, int command);

    [DllImport("libc", EntryPoint = "open")]
    private static extern int OpenFile(nint path, int flags);
}
