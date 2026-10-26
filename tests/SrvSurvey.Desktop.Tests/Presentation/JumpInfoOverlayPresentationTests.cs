using System.Net;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class JumpInfoOverlayPresentationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectNeutronTargetKeepsItsPillVisibleWhenDetailsAreHidden(bool minimal)
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), $"jump-neutron-{Guid.NewGuid():N}.json");
        using var http = new HttpClient(new DiscoveryHandler(Task.CompletedTask));
        using var jump = new JumpInfoViewModel(new SystemSummaryClient(http), new JumpInfoSettingsStore(settingsPath));
        jump.Minimal = minimal;
        jump.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                null,
                null,
                [Parse("""{"event":"FSDTarget","Name":"Beta","SystemAddress":3,"StarClass":"N"}""")],
                new EliteStatus { Flags = StatusFlags.InMainShip },
                null
            )
        );
        using var model = new JumpInfoOverlayViewModel(jump, OverlayPlatformCapabilities.DetectCurrent());
        var window = new JumpInfoOverlayWindow(model);
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            TextBlock pill = Assert.Single(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "NEUTRON"
            );
            Assert.True(pill.IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "STAR CLASS N");
            Assert.Empty(jump.RouteLegs);
        }
        finally
        {
            window.Close();
            File.Delete(settingsPath);
        }
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _));
        return entry!;
    }

    /// <summary>Renders real provider discovery data arriving after the passive overlay is already visible.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoveryCreditAppearsAfterTheLiveLookupCompletes(bool combined)
    {
        var responseReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        string settingsPath = Path.Combine(Path.GetTempPath(), $"jump-presentation-{Guid.NewGuid():N}.json");
        using var http = new HttpClient(new DiscoveryHandler(responseReady.Task));
        using var jump = new JumpInfoViewModel(
            new SystemSummaryClient(http, new Uri("https://edsm.test/"), new Uri("https://spansh.test/api/")),
            new JumpInfoSettingsStore(settingsPath)
        );
        using var model = new JumpInfoOverlayViewModel(jump, OverlayPlatformCapabilities.DetectCurrent());
        jump.ApplyUpdate(
            new JumpInfoApplyUpdateRequest(
                "Sol",
                1,
                new GalacticCoordinate(0, 0, 0),
                null,
                [],
                new EliteStatus
                {
                    Flags = StatusFlags.InMainShip,
                    Flags2 = StatusFlags2.FsdChargingJump,
                    Destination = new StatusDestination
                    {
                        Name = "Beta",
                        System = 3,
                        Body = 0,
                    },
                },
                null
            )
        );
        var window = new JumpInfoOverlayWindow(model);
        var host = new CombinedOverlayWindow { Width = 800, Height = 600 };
        Window surface = window;
        try
        {
            OverlayThemeResources.Apply(window);
            if (combined)
            {
                Control content = Assert.IsType<Control>(window.Content, exactMatch: false);
                window.Content = null;
                host.Add(new ContentControl { Content = content, DataContext = model });
                OverlayThemeResources.Apply(host);
                surface = host;
            }
            surface.Show();
            jump.BeginOverlayPresentation();
            using WriteableBitmap? initial = surface.CaptureRenderedFrame();
            Assert.True(jump.IsLoading);
            Assert.False(jump.HasDiscoveryText);

            responseReady.SetResult(true);
            await jump.PendingSummaryLoad;
            using WriteableBitmap? loaded = surface.CaptureRenderedFrame();

            Assert.False(jump.IsLoading);
            Assert.Contains("Discovered by System Pathfinder on ", jump.DiscoveryText);
            Assert.Contains("updated ", jump.DiscoveryText);
            Assert.DoesNotContain("Body Surveyor", jump.DiscoveryText);
            TextBlock discovery = Assert.Single(
                surface.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == jump.DiscoveryText
            );
            Assert.True(discovery.IsEffectivelyVisible);
            Assert.True(discovery.Bounds.Width > 0);
            Assert.True(discovery.Bounds.Height > 0);
            Assert.True(jump.HasDiscoveryOrRouteGuidance);
            Assert.True(jump.IsScoopableStarClass);
            Assert.False(jump.HasRouteGuidanceBadges);

            jump.Minimal = true;
            using WriteableBitmap? minimal = surface.CaptureRenderedFrame();
            Assert.False(discovery.IsEffectivelyVisible);
            jump.Minimal = false;
            using WriteableBitmap? restored = surface.CaptureRenderedFrame();
            Assert.True(discovery.IsEffectivelyVisible);
        }
        finally
        {
            host.Close();
            window.Close();
            File.Delete(settingsPath);
        }
    }

    /// <summary>Verifies editor badges are rendered from the same flags and rows as live jump guidance.</summary>
    [AvaloniaTheory]
    [InlineData(0, true, false)]
    [InlineData(1, false, true)]
    [InlineData(2, false, false)]
    [InlineData(3, false, false)]
    public void PreviewRendersOnlyTheGuidanceAvailableForItsTarget(int state, bool refuel, bool neutron)
    {
        using var model = (JumpInfoOverlayViewModel)OverlayEditorPreviewCatalog.Create("PlotJumpInfo", state);
        using JumpInfoViewModel jump = model.JumpInfo;
        var window = new JumpInfoOverlayWindow(model);
        try
        {
            OverlayThemeResources.Apply(window);
            window.Show();
            using WriteableBitmap frame = Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame());
            TextBlock[] texts = window.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.True(Assert.Single(texts, text => text.Text == jump.DiscoveryText).IsEffectivelyVisible);
            Assert.Equal(refuel, Assert.Single(texts, text => text.Text == "REFUEL").IsEffectivelyVisible);
            Assert.Equal(neutron, Assert.Single(texts, text => text.Text == "NEUTRON").IsEffectivelyVisible);
            Assert.DoesNotContain(texts, text => text.Text is "Next hop:" or "Neutron:" or "Scoopable:");
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_OVERLAY_RENDER_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, $"next-jump-preview-{state}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class DiscoveryHandler(Task responseReady) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            await responseReady.WaitAsync(cancellationToken);
            string json = request.RequestUri!.AbsolutePath switch
            {
                "/api-system-v1/bodies" => """
                    {"id64":3,"bodyCount":1,"bodies":[{
                      "isMainStar":true,"spectralClass":"K3","updateTime":"2026-10-23 04:05:06",
                      "discovery":{"commander":"Body Surveyor","date":"2025-01-02 03:04:05"}
                    }]}
                    """,
                "/api-system-v1/traffic" => """
                    {"id64":3,"discovery":{"commander":"System Pathfinder","date":"2024-01-02 03:04:05"},
                     "traffic":{"day":3,"week":20,"total":100}}
                    """,
                _ => """{"system":{"bodyCount":1,"coords":{"x":40,"y":0,"z":0}}}""",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }
}
