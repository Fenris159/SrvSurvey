using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PipeWire.NET;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

public sealed class PipeWirePortalMarshallingTests
{
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
            }
        }
    }
}
