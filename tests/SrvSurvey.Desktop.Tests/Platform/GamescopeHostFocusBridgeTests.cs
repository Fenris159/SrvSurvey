using System.Diagnostics;
using System.Globalization;
using Avalonia;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class GamescopeHostFocusBridgeTests
{
    [Theory]
    [InlineData("123\n1\n", true)]
    [InlineData("123\n0\n", false)]
    [InlineData("122\n1\n", false)]
    [InlineData("invalid\n1\n", false)]
    [InlineData("123\nunknown\n", false)]
    [InlineData("123\n", false)]
    [InlineData("123\n1\nextra\n", false)]
    public void StampedFocusRequiresValidSessionIdentityAndForeground(string contents, bool expected)
    {
        WithDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, GamescopeGameWindowFocus.MarkerPrefix + "42"), contents);
            Assert.Equal(expected, GamescopeGameWindowFocus.Read(directory, 42, 123, directory));
        });
    }

    [Theory]
    [InlineData("GAMESCOPE_HOST_FOCUS_FILE", "1", true)]
    [InlineData("ELITE_HOST_FOCUS_FILE", "1", true)]
    [InlineData("ELITE_HOST_FOCUS_FILE", "0", false)]
    [InlineData("ELITE_HOST_FOCUS_FILE", "invalid", false)]
    [InlineData("ELITE_HOST_FOCUS_FILE", "1\nextra", false)]
    public void AdvertisedFocusSupportsExistingLaunchers(string variable, string contents, bool expected)
    {
        WithDirectory(directory =>
        {
            string path = Path.Combine(directory, "host-focus");
            File.WriteAllText(path, contents);
            WriteEnvironment(directory, $"{variable}={path}\0");
            Assert.Equal(expected, GamescopeGameWindowFocus.Read(directory, 42, 123, directory));
        });
    }

    [Fact]
    public void MissingPublisherPreservesUnknownFocusForOlderBridges()
    {
        WithDirectory(directory =>
        {
            Assert.Null(GamescopeGameWindowFocus.Read(directory, 42, 123, directory));
            WriteEnvironment(directory, "DISPLAY=:7\0");
            Assert.Null(GamescopeGameWindowFocus.Read(directory, 42, 123, directory));
            WriteEnvironment(directory, "GAMESCOPE_HOST_FOCUS_FILE=\0");
            Assert.Null(GamescopeGameWindowFocus.Read(directory, 42, 123, directory));
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("directory")]
    [InlineData("relative")]
    public void DeclaredUnreadableOrRelativeFocusDoesNotClaimForeground(string source)
    {
        WithDirectory(directory =>
        {
            string path = source == "relative" ? "host-focus" : Path.Combine(directory, source);
            if (source == "directory")
            {
                Directory.CreateDirectory(path);
            }
            WriteEnvironment(directory, $"ELITE_HOST_FOCUS_FILE={path}\0");
            Assert.False(GamescopeGameWindowFocus.Read(directory, 42, 123, directory));
        });
    }

    [Fact]
    public void StampedPublisherAndGenericEnvironmentTakePrecedence()
    {
        WithDirectory(directory =>
        {
            string generic = Path.Combine(directory, "generic");
            string legacy = Path.Combine(directory, "legacy");
            File.WriteAllText(generic, "0");
            File.WriteAllText(legacy, "1");
            WriteEnvironment(directory, $"ELITE_HOST_FOCUS_FILE={legacy}\0GAMESCOPE_HOST_FOCUS_FILE={generic}\0");
            Assert.False(GamescopeGameWindowFocus.Read(directory, 42, 123, directory));

            File.WriteAllText(Path.Combine(directory, GamescopeGameWindowFocus.MarkerPrefix + "42"), "123\n1\n");
            Assert.True(GamescopeGameWindowFocus.Read(directory, 42, 123, directory));
        });
    }

    [Fact]
    public void ExistingLauncherSignalIsReadFromLiveProcessMetadata()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        WithDirectory(directory =>
        {
            string focus = Path.Combine(directory, "host-focus");
            File.WriteAllText(focus, "1");
            using Process process = Process.Start(
                new ProcessStartInfo("/bin/sleep")
                {
                    ArgumentList = { "30" },
                    Environment = { ["ELITE_HOST_FOCUS_FILE"] = focus },
                }
            )!;
            try
            {
                ulong start = X11OverlayInteractionMarker.TryReadProcessStartTime(process.Id)!.Value;
                File.WriteAllText(
                    Path.Combine(directory, GamescopeGameWindowBridge.MarkerPrefix + process.Id),
                    $"{start.ToString(CultureInfo.InvariantCulture)}\n:7\n100 80 1280 800\n"
                );
                var nested = new StubTracker(
                    new GameWindowSnapshot(42, 123, new PixelRect(0, 0, 1280, 800), true, true)
                );
                using var tracker = new GamescopeGameWindowTracker(
                    new StubTracker(GameWindowSnapshot.Unavailable),
                    () => GamescopeGameWindowBridge.TryRead(directory),
                    _ => nested
                );
                Assert.True(tracker.GetSnapshot().IsForeground);
                File.WriteAllText(focus, "0");
                Assert.False(tracker.GetSnapshot().IsForeground);
                File.WriteAllText(focus, "1");
                Assert.True(tracker.GetSnapshot().IsForeground);
                File.Delete(focus);
                Assert.False(tracker.GetSnapshot().IsForeground);
                process.Kill();
                process.WaitForExit();
                Assert.False(tracker.GetSnapshot().IsAvailable);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
        });
    }

    private static void WriteEnvironment(string directory, string contents)
    {
        string process = Directory.CreateDirectory(Path.Combine(directory, "42")).FullName;
        File.WriteAllText(Path.Combine(process, "environ"), contents);
    }

    private static void WithDirectory(Action<string> test)
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            test(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubTracker(GameWindowSnapshot snapshot) : IGameWindowTracker
    {
        public GameWindowSnapshot GetSnapshot() => snapshot;

        public void Dispose() { }
    }
}
