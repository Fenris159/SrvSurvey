using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Behaviors;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.Platform;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MiningMissionsPresentationTests
{
    [AvaloniaTheory]
    [InlineData(1050, "monochrome-dark", 2)]
    [InlineData(1050, "orange-dark", 2)]
    [InlineData(500, "blue-light", 1)]
    public async Task CardsFollowTheThemeWrapAndSupportTrackingCopyAndFind(int width, string theme, int columns)
    {
        using var fixture = new MiningMissionFixture();
        var themes = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(fixture.Directory, "theme.json"))
        );
        string original = themes.Current.Key;
        var view = new MiningMissionCardsView { DataContext = fixture.Workspace, Margin = new Thickness(16) };
        var window = new Window
        {
            Width = width,
            Height = 900,
            Content = new ScrollViewer { Content = view },
        };
        try
        {
            themes.Select(theme);
            fixture.Workspace.Restore(
                MiningStore.Export(
                    new MiningCommanderData
                    {
                        Missions =
                        [
                            new()
                            {
                                Id = 1,
                                Commodity = "Bromellite",
                                Required = 236,
                                OnBoard = 73,
                                DestinationSystem = "Ngolona",
                                DestinationStation = "Huss Port",
                                Reward = 32077200,
                                Expiry = fixture.Now.AddDays(6).AddHours(20).ToString("O"),
                            },
                            new()
                            {
                                Id = 2,
                                Commodity = "Praseodymium",
                                Required = 57,
                                Delivered = 12,
                                OnBoard = 18,
                                DestinationSystem = "Ngolona",
                                DestinationStation = "Huss Port",
                                Reward = 648094,
                                Expiry = fixture.Now.AddDays(6).AddHours(23).ToString("O"),
                            },
                            new()
                            {
                                Id = 3,
                                Commodity = "Osmium",
                                Required = 36,
                                Delivered = 36,
                                DestinationSystem = "Sol",
                                DestinationStation = "Galileo",
                                Reward = 2486386,
                                Expiry = fixture.Now.AddDays(2).ToString("O"),
                            },
                        ],
                    }
                )
            );
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Border[] cards = view.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("mining-mission-card"))
                .ToArray();
            Assert.Equal(3, cards.Length);
            Point first = cards[0].TranslatePoint(default, view)!.Value;
            Point second = cards[1].TranslatePoint(default, view)!.Value;
            if (columns == 2)
            {
                Assert.Equal(first.Y, second.Y);
                Assert.True(second.X > first.X + 300);
            }
            else
            {
                Assert.True(second.Y > first.Y + 100);
                Assert.Equal(first.X, second.X);
            }
            Assert.All(cards, card => Assert.InRange(card.Bounds.Width, 250, width - 20));
            TextBlock completion = cards[2].GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "✓");
            Assert.True(completion.IsVisible);
            Assert.Equal(Color.Parse(themes.Current.SuccessColor), ((ISolidColorBrush)completion.Foreground!).Color);
            Assert.All(view.GetVisualDescendants().OfType<ProgressBar>(), bar => Assert.Equal(4, bar.Bounds.Height));
            using WriteableBitmap? settled = window.CaptureRenderedFrame();
            Assert.InRange(cards[1].GetVisualDescendants().OfType<ProgressBar>().Last().Value, 21.05, 21.06);
            settled!.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-mining-missions-{theme}.png"),
                PngBitmapEncoderOptions.Default
            );
            CheckBox tracking = cards[0].GetVisualDescendants().OfType<CheckBox>().Single();
            Assert.True(tracking.IsChecked);
            tracking.IsChecked = false;
            Assert.False(fixture.Workspace.MissionCards[0].IsTracked);
            Assert.Equal(2, fixture.Workspace.TrackedMissionCards.Count);
            using WriteableBitmap? toggled = window.CaptureRenderedFrame();
            Button copy = view.GetVisualDescendants()
                .OfType<Button>()
                .First(button => Equals(ClipboardCopyBehavior.GetText(button), "Ngolona"));
            Assert.Equal("Huss Port @ Ngolona", Assert.IsType<TextBlock>(copy.Content).Text);
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Ngolona", await window.Clipboard!.TryGetTextAsync());
            Button find = view.GetVisualDescendants()
                .OfType<Button>()
                .First(button => Equals(button.Content, "Find hotspots"));
            Assert.NotNull(find.Command);
            find.Command.Execute(find.CommandParameter);
            Assert.Equal(4, fixture.Workspace.SelectedTab);
        }
        finally
        {
            window.Close();
            themes.Select(original);
        }
    }

    [AvaloniaFact]
    public void SharedPreviewAndLiveOverlaySizeToTrackedMissionsAndReleaseTheirHosts()
    {
        using var fixture = new MiningMissionFixture();
        OverlayLayoutDefinition definition = OverlayLayoutCatalog.GetRequired("PlotMiningMissions");
        Assert.Equal(OverlayLayoutCategory.Mining, definition.Category);
        using var model = new MiningMissionsOverlayViewModel(fixture.Workspace);
        using var previewModel = new MiningMissionsOverlayViewModel(null);
        Assert.Equal(2, previewModel.Missions.Count);
        var preview = new OverlayPositionPreviewWindow(definition);
        var live = new MiningMissionsOverlayWindow(model);
        try
        {
            preview.Show();
            using WriteableBitmap? previewFrame = preview.CaptureRenderedFrame();
            Assert.NotNull(previewFrame);
            Assert.IsType<MiningMissionsOverlayPresentation>(preview.RuntimePresentation);
            Assert.Contains(preview.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Bromellite");
            previewFrame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-mining-missions-overlay-preview.png"),
                PngBitmapEncoderOptions.Default
            );
            fixture.Accept(1);
            fixture.Accept(2);
            OverlayThemeResources.Apply(live, LegacyOverlayLayout.Empty, definition.Name, new OverlayWindowRegistry());
            live.Show();
            using WriteableBitmap? two = live.CaptureRenderedFrame();
            MiningMissionsOverlayPresentation presentation = Assert.Single(
                live.GetVisualDescendants().OfType<MiningMissionsOverlayPresentation>()
            );
            Assert.True(presentation.AutoHeight);
            double expanded = presentation.Bounds.Height;
            fixture.Workspace.MissionCards[0].IsTracked = false;
            using WriteableBitmap? one = live.CaptureRenderedFrame();
            Assert.Single(model.Missions);
            Assert.True(presentation.Bounds.Height < expanded);
            fixture.Feed("""{"event":"CargoDepot","MissionID":2,"UpdateType":"Deliver","ItemsDelivered":10}""");
            using WriteableBitmap? complete = live.CaptureRenderedFrame();
            Assert.Contains(
                presentation.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "✓" && text.IsEffectivelyVisible
            );
            using var hosts = new HostedOverlayTestHarness();
            var firegroups = new FiregroupsWorkspaceViewModel(fixture.Directory);
            var coordinator = new MiningActivityOverlayCoordinator(fixture.Workspace, firegroups, hosts.Session);
            Assert.Equal(5, hosts.PlatformsCreated);
            Assert.Contains(hosts.PreparedWindows, window => window is MiningMissionsOverlayWindow && window.IsVisible);
            coordinator.SetSuppressed(true);
            Assert.DoesNotContain(
                hosts.PreparedWindows,
                window => window is MiningMissionsOverlayWindow && window.IsVisible
            );
            coordinator.SetSuppressed(false);
            Assert.Contains(hosts.PreparedWindows, window => window is MiningMissionsOverlayWindow && window.IsVisible);
            coordinator.Dispose();
            Assert.Equal(5, hosts.PlatformsDisposed);
            Assert.Equal(hosts.TrackersCreated, hosts.TrackersDisposed);
            Assert.Equal(0, hosts.RunningTimers);
        }
        finally
        {
            live.Close();
            preview.Close();
        }
    }
}
