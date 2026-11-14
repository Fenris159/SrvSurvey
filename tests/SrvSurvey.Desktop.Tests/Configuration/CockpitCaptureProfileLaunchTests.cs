using System.Text;
using SrvSurvey.Core.Network;
using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Configuration;

public sealed class CockpitCaptureProfileLaunchTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-camera-launch-{Guid.NewGuid():N}");

    [Fact]
    public async Task DisabledCockpitModeDoesNotCheckOrCreateACache()
    {
        var handler = new ProfileDownload();
        var updater = new CockpitCaptureProfileUpdater(directory, handler.ReadAsync);
        var model = new CockpitOverlayViewModel(
            new CockpitOverlaySettingsStore(Path.Combine(directory, "settings.json")),
            updater
        );
        await model.RefreshCaptureProfileAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, handler.Requests);
        Assert.False(Directory.Exists(directory));
        Assert.Equal(
            "Camera profiles are checked on launch while cockpit mode is enabled.",
            model.CaptureProfileStatus
        );
    }

    [Fact]
    public async Task LaunchRefreshReportsTheGameRestartSeparatelyFromApplicationRestart()
    {
        var download = new ProfileDownload();
        var store = new CockpitOverlaySettingsStore(Path.Combine(directory, "settings.json"));
        store.Save(
            new CockpitOverlayPreferences(
                true,
                new Dictionary<string, SrvSurvey.Desktop.Platform.Overlay.CockpitPanelPose>()
            )
        );
        var model = new CockpitOverlayViewModel(store, new CockpitCaptureProfileUpdater(directory, download.ReadAsync));
        await model.RefreshCaptureProfileAsync(TestContext.Current.CancellationToken);
        int version = CockpitCaptureProfile.Embedded.ProfileVersion + 1;
        Assert.Equal(
            $"Downloaded camera profile v{version}. Elite checks it on its next launch.",
            model.CaptureProfileStatus
        );
        Assert.False(model.RestartRequired);
        Assert.Equal("Waiting for the live cockpit camera bridge.", model.StatusMessage);
    }

    private sealed class ProfileDownload
    {
        public int Requests { get; private set; }

        public Task<byte[]> ReadAsync(Uri source, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(WellKnownUris.PublishedCockpitCaptureProfile, source);
            Requests++;
            int version = CockpitCaptureProfile.Embedded.ProfileVersion + 1;
            return Task.FromResult(
                Encoding.UTF8.GetBytes(
                    $"[Capture]\nProfileVersion={version}\nProtocolVersion=1\nValidated=1\nVertexShaderHash=0123456789abcdef\nRigidLayoutVersion=1\nDrawKind=3\n"
                )
            );
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
