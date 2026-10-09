using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class GuardianOverlayCoordinatorTests
{
    [AvaloniaFact]
    public async Task SuccessfulZoomPreparationRegistersAnInteractiveChild()
    {
        string root = CreateTemporaryDirectory();
        var existingWindows = OverlayWindowRegistry
            .Shared.Snapshot()
            .Select(registration => registration.Window)
            .ToHashSet();
        try
        {
            using GuardianViewModel guardian = await CreateLiveGuardianAsync(root);
            using HostedOverlayTestHarness overlays = CreateOverlays(zoomClickThrough: true, zoomInteractive: true);
            using var coordinator = new GuardianOverlayCoordinator(guardian, overlays.Session);

            RegisteredOverlayWindow[] guardianRegistrations = OverlayWindowRegistry
                .Shared.Snapshot()
                .Where(registration =>
                    !existingWindows.Contains(registration.Window) && registration.PlotterName == "PlotGuardians"
                )
                .ToArray();

            Assert.Contains(overlays.PreparedWindows, window => window is GuardianZoomOverlayWindow);
            Assert.Contains(overlays.InteractiveWindows, window => window is GuardianZoomOverlayWindow);
            Assert.Contains(guardianRegistrations, registration => registration.Window is GuardianOverlayWindow);
            Assert.Contains(guardianRegistrations, registration => registration.Window is GuardianZoomOverlayWindow);
            Assert.Single(guardianRegistrations, registration => registration.ParticipatesInPlacement);
            Assert.Contains(
                guardianRegistrations,
                registration => registration.Window is GuardianOverlayWindow && registration.ParticipatesInPlacement
            );
            Assert.Contains(
                guardianRegistrations,
                registration =>
                    registration.Window is GuardianZoomOverlayWindow && !registration.ParticipatesInPlacement
            );
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ZoomPreparationFailureIsLatchedWithoutSuppressingSite(bool zoomClickThrough, bool zoomInteractive)
    {
        string root = CreateTemporaryDirectory();
        try
        {
            using GuardianViewModel guardian = await CreateLiveGuardianAsync(root);
            using HostedOverlayTestHarness overlays = CreateOverlays(zoomClickThrough, zoomInteractive);
            using var coordinator = new GuardianOverlayCoordinator(guardian, overlays.Session);

            Assert.True(coordinator.IsLiveSiteVisible);
            Assert.Equal(1, overlays.PreparedWindows.Count(window => window is GuardianZoomOverlayWindow));

            coordinator.SetSuppressed(true);
            coordinator.SetSuppressed(false);

            Assert.True(coordinator.IsLiveSiteVisible);
            Assert.Equal(1, overlays.PreparedWindows.Count(window => window is GuardianZoomOverlayWindow));
            Assert.DoesNotContain(
                OverlayWindowRegistry.Shared.Snapshot(),
                registration => registration.Window is GuardianZoomOverlayWindow
            );
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static HostedOverlayTestHarness CreateOverlays(bool zoomClickThrough, bool zoomInteractive)
    {
        return new HostedOverlayTestHarness
        {
            Prepare = window =>
            {
                bool succeeded = window is not GuardianZoomOverlayWindow || zoomClickThrough;
                return new OverlayPreparationResult(
                    IsPrepared: succeeded,
                    IsClickThrough: succeeded,
                    Status: succeeded ? "Prepared" : "Unavailable"
                );
            },
            SetInteractive = (window, interactive) =>
            {
                bool succeeded = window is not GuardianZoomOverlayWindow || zoomInteractive;
                return new OverlayInteractionResult(
                    IsPrepared: succeeded,
                    IsInteractive: interactive && succeeded,
                    Status: succeeded ? "Prepared" : "Unavailable"
                );
            },
        };
    }

    private static async Task<GuardianViewModel> CreateLiveGuardianAsync(string root)
    {
        var guardian = new GuardianViewModel(root);
        await guardian.LoadProfileAsync("F123", isOdyssey: true);
        await guardian.ApplyJournalEventsAsync(
            [
                Parse("""{"event":"Location","StarSystem":"Synuefe XR-H d11-102","SystemAddress":3515254557027}"""),
                Parse(
                    """{"event":"ApproachSettlement","Name":"$Ancient:#index=1;","Name_Localised":"Ancient Ruins (1)","SystemAddress":3515254557027,"BodyID":13,"BodyName":"Synuefe XR-H d11-102 1 b","Latitude":-46.576923,"Longitude":133.985107}"""
                ),
            ],
            "Test Commander"
        );
        guardian.UpdateStatus(
            new EliteStatus
            {
                Flags = StatusFlags.HasLatLong | StatusFlags.InSrv,
                Latitude = -46.576923,
                Longitude = 133.985107,
                PlanetRadius = 1_000_000,
            }
        );
        Assert.True(guardian.ShouldShowLiveSiteOverlay);
        return guardian;
    }

    private static JournalEventEnvelope Parse(string json)
    {
        bool success = JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? journalEvent, out string? error);
        Assert.True(success, error);
        return Assert.IsType<JournalEventEnvelope>(journalEvent);
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "SrvSurvey.GuardianOverlayCoordinatorTests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
