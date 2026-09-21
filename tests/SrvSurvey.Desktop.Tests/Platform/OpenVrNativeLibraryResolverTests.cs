using System.Runtime.InteropServices;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class OpenVrNativeLibraryResolverTests
{
    [Fact]
    public void LinuxCandidatesIncludeTheBundledRuntimeClient()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string? runtimeIdentifier = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "linux-x64",
            Architecture.Arm64 => "linux-arm64",
            _ => null,
        };
        if (runtimeIdentifier is null)
        {
            return;
        }

        string expectedSuffix = Path.Combine("runtimes", runtimeIdentifier, "native", "libopenvr_api.so");

        Assert.Contains(
            OpenVrNativeLibraryResolver.GetLinuxCandidates(),
            candidate => candidate.EndsWith(expectedSuffix, StringComparison.Ordinal)
        );
    }
}
