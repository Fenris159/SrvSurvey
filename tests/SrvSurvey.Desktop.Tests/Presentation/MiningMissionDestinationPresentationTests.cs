using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Behaviors;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MiningMissionDestinationPresentationTests
{
    private static readonly string[] ExpectedPresetNames =
    [
        "Default",
        "Monochrome Companion",
        "Nebula Cyan",
        "Toxic Green",
        "Crimson Wake",
        "Void Amethyst",
        "Cerulean Gold",
    ];

    [AvaloniaTheory]
    [InlineData("orange-dark")]
    [InlineData("blue-light")]
    public void WorkspaceFitsFourReadableMissionCardsPerRow(string theme)
    {
        using var fixture = new MiningMissionFixture();
        fixture.Workspace.Restore(
            MiningStore.Export(
                new MiningCommanderData
                {
                    Missions =
                    [
                        Mission(1, "Bertrandite", "Bismark Legacy", fixture.Now) with
                        {
                            Required = 704,
                            Delivered = 704,
                            OnBoard = 0,
                        },
                        Mission(2, "Bertrandite", "Bismark Legacy", fixture.Now) with
                        {
                            Required = 652,
                            Delivered = 0,
                            OnBoard = 180,
                        },
                        Mission(3, "Gallite", "Bismark Legacy", fixture.Now) with
                        {
                            Required = 772,
                            Delivered = 120,
                            OnBoard = 96,
                        },
                        Mission(4, "Indite", "Bismark Legacy", fixture.Now) with
                        {
                            Required = 844,
                            Delivered = 0,
                            OnBoard = 64,
                        },
                        Mission(5, "Gold", "Kepler Gateway", fixture.Now) with
                        {
                            DestinationSystem = "HIP 13644",
                            Required = 180,
                            Delivered = 60,
                            OnBoard = 40,
                        },
                        Mission(6, "Osmium", "Kepler Gateway", fixture.Now) with
                        {
                            DestinationSystem = "HIP 13644",
                            Required = 240,
                            Delivered = 0,
                            OnBoard = 30,
                        },
                        Mission(7, "Silver", "Krylov Enterprise", fixture.Now) with
                        {
                            DestinationSystem = "LFT 37",
                            Required = 300,
                            Delivered = 80,
                            OnBoard = 60,
                        },
                        Mission(8, "Platinum", "Krylov Enterprise", fixture.Now) with
                        {
                            DestinationSystem = "LFT 37",
                            Required = 120,
                            Delivered = 20,
                            OnBoard = 25,
                        },
                    ],
                }
            )
        );
        var themes = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(fixture.Directory, "theme.json"))
        );
        string original = themes.Current.Key;
        var view = new MiningMissionCardsView { DataContext = fixture.Workspace, Margin = new Thickness(16) };
        var window = new Window
        {
            Width = 1050,
            Height = 900,
            Content = new ScrollViewer { Content = view },
        };
        try
        {
            themes.Select(theme);
            window.Show();
            Settle(window);
            Border[] cards = view.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("mining-mission-card"))
                .ToArray();
            Assert.Equal(8, cards.Length);
            Assert.All(fixture.Workspace.MissionCards, mission => Assert.True(mission.IsTracked));
            Assert.Equal(3, fixture.Workspace.TrackedMissionDestinations.Count);
            Assert.Equal(
                8,
                fixture.Workspace.TrackedMissionDestinations.Sum(destination => destination.TrackedMissions)
            );
            Point[] positions = cards.Select(card => card.TranslatePoint(default, view)!.Value).ToArray();
            Assert.All(positions.Take(4), point => Assert.Equal(positions[0].Y, point.Y));
            Assert.True(positions[4].Y > positions[0].Y + 100);
            Assert.All(cards, card => Assert.InRange(card.Bounds.Width, 220, 260));
            Save(window, "mining-missions-workspace-eight-tracked-" + theme + ".png");
        }
        finally
        {
            window.Close();
            themes.Select(original);
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void LargeOverlayTotalsStayInsideTheLocationWidthAtMaximumValueSize(bool useTags)
    {
        using var fixture = new MiningMissionFixture();
        fixture.Workspace.Restore(
            MiningStore.Export(
                new MiningCommanderData
                {
                    Missions = Enumerable
                        .Range(1, 2)
                        .Select(id =>
                            Mission(id, "Bertrandite", "S", fixture.Now) with
                            {
                                DestinationSystem = "A",
                                Required = 10000,
                                Delivered = 5000,
                                OnBoard = 3000,
                            }
                        )
                        .ToList(),
                }
            )
        );
        fixture.Workspace.UseCommodityShortTags = useTags;
        using var model = new MiningMissionsOverlayViewModel(fixture.Workspace);
        var window = new MiningMissionsOverlayWindow(model);
        try
        {
            OverlayThemeResources.Apply(
                window,
                LegacyOverlayLayout.Empty,
                "PlotMiningMissions",
                new OverlayWindowRegistry()
            );
            window.Show();
            Settle(window);
            MiningMissionsOverlayPresentation presentation = Assert.Single(
                window.GetVisualDescendants().OfType<MiningMissionsOverlayPresentation>()
            );
            OverlayTypographyResources.Apply(presentation, new OverlayTypographyScale(0, 0, 100, 0, 0, 0));
            Settle(window);
            Border card = Assert.Single(DestinationCards(presentation));
            TextBlock delivery = Assert.Single(
                card.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "10,000/20,000"
            );
            Assert.Equal(28, delivery.FontSize);
            Assert.InRange(presentation.Bounds.Width, 240, 270);
            Assert.All(
                card.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible),
                text => AssertFitsHorizontally(text, card)
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SystemOnlyDestinationKeepsItsVisibleCopyAction()
    {
        using var fixture = new MiningMissionFixture();
        fixture.Workspace.Restore(
            MiningStore.Export(
                new MiningCommanderData
                {
                    Missions = [Mission(1, "Gold", "", fixture.Now) with { DestinationSystem = "Sol" }],
                }
            )
        );
        var view = new MiningMissionCardsView { DataContext = fixture.Workspace };
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Content = view,
        };
        try
        {
            window.Show();
            Settle(window);
            Border summary = Assert.Single(
                view.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("mining-destination-summary")
            );
            Button copy = Assert.Single(
                summary.GetVisualDescendants().OfType<Button>(),
                button => Equals(ClipboardCopyBehavior.GetText(button), "Sol")
            );
            Assert.True(Assert.IsType<TextBlock>(copy.Content).IsEffectivelyVisible);
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Sol", await window.Clipboard!.TryGetTextAsync());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SummaryReadoutsStayInsideTheirColumnWhenTheWorkspaceNarrows()
    {
        using var fixture = new MiningMissionFixture();
        fixture.Workspace.Restore(
            MiningStore.Export(
                new MiningCommanderData
                {
                    Missions =
                    [
                        Mission(1, "Bertrandite", "Bismark Legacy", fixture.Now) with
                        {
                            Required = 1988,
                            Delivered = 704,
                        },
                        Mission(2, "Hydrogen Peroxide", "Bismark Legacy", fixture.Now) with
                        {
                            Required = 2000,
                        },
                    ],
                }
            )
        );
        fixture.Workspace.UseCommodityShortTags = false;
        var view = new MiningMissionCardsView { DataContext = fixture.Workspace, Margin = new Thickness(16) };
        var window = new Window
        {
            Width = 500,
            Height = 1150,
            Content = new ScrollViewer { Content = view },
        };
        try
        {
            window.Show();
            foreach (int width in new[] { 500, 1050, 500 })
            {
                window.Width = width;
                Settle(window);
                Border summary = Assert.Single(
                    view.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("mining-destination-summary")
                );
                ItemsControl commodities = Assert.Single(summary.GetVisualDescendants().OfType<ItemsControl>());
                Assert.Contains(
                    commodities.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Delivered: 704/1,988"
                );
                Assert.All(
                    commodities.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible),
                    text => AssertFitsHorizontally(text, commodities)
                );
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OverlayScrollsAfterWholeDestinationCardsAndHonorsTheGameViewport()
    {
        using var fixture = new MiningMissionFixture();
        fixture.Workspace.Restore(
            MiningStore.Export(
                new MiningCommanderData
                {
                    Missions =
                    [
                        Mission(1, "Bertrandite", "Bismark Legacy", fixture.Now),
                        Mission(2, "Gallite", "Bismark Legacy", fixture.Now),
                        Mission(3, "Gold", "Second Port", fixture.Now),
                        Mission(4, "Platinum", "Third Port", fixture.Now),
                    ],
                }
            )
        );
        fixture.Workspace.MissionDestinationLimit = 1;
        using var model = new MiningMissionsOverlayViewModel(fixture.Workspace);
        var window = new MiningMissionsOverlayWindow(model);
        MiningMissionsOverlayPresentation presentation = Assert.IsType<MiningMissionsOverlayPresentation>(
            window.Content
        );
        ((IOverlayPanelViewportAware)presentation).SetAvailableHeight(1100);
        try
        {
            window.Show();
            Settle(window);
            ScrollViewer scroller = presentation.FindControl<ScrollViewer>("DestinationScroller")!;
            Assert.Equal(3, model.Destinations.Count);
            Assert.Equal(2, model.Destinations[0].Commodities.Count);
            Border[] cards = DestinationCards(presentation);
            Assert.InRange(
                cards[1].TranslatePoint(default, scroller)!.Value.Y,
                scroller.Viewport.Height - 1,
                scroller.Viewport.Height + 1
            );
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height + 100);
            fixture.Workspace.MissionDestinationLimit = 2;
            Settle(window);
            cards = DestinationCards(presentation);
            Assert.InRange(
                cards[2].TranslatePoint(default, scroller)!.Value.Y,
                scroller.Viewport.Height - 1,
                scroller.Viewport.Height + 1
            );
            fixture.Workspace.MissionDestinationLimit = 20;
            Settle(window);
            Assert.InRange(scroller.Extent.Height - scroller.Viewport.Height, -1, 1);
            ((IOverlayPanelViewportAware)presentation).SetAvailableHeight(240);
            Settle(window);
            Assert.InRange(presentation.Bounds.Height, 100, 240);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height + 100);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OverlayTagsMatchPowerplayAndSwitchToFullNamesWithoutLosingProgress()
    {
        using var fixture = new MiningMissionFixture();
        fixture.Workspace.Restore(
            MiningStore.Export(
                new MiningCommanderData { Missions = [Mission(1, "Bertrandite", "Bismark Legacy", fixture.Now)] }
            )
        );
        using var model = new MiningMissionsOverlayViewModel(fixture.Workspace);
        var window = new MiningMissionsOverlayWindow(model);
        try
        {
            OverlayThemeResources.Apply(
                window,
                LegacyOverlayLayout.Empty,
                "PlotMiningMissions",
                new OverlayWindowRegistry()
            );
            window.Show();
            Settle(window);
            Border tag = Assert.Single(
                window.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("commodity-code") && border.IsEffectivelyVisible
            );
            Assert.Equal(
                Color.Parse("#B98966"),
                Assert.IsType<ISolidColorBrush>(tag.Background, exactMatch: false).Color
            );
            TextBlock code = Assert.Single(tag.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal("BRT", code.Text);
            Assert.Equal(
                Color.Parse("#111111"),
                Assert.IsType<ISolidColorBrush>(code.Foreground, exactMatch: false).Color
            );
            var reference = new MiningReferenceCommodityRowViewModel("Bertrandite", 0);
            Assert.Equal("BRT", reference.Code);
            Assert.Equal("#B98966", reference.ColorHex);
            Assert.Equal("#111111", reference.ContrastForeground);
            Save(window, "mining-destinations-overlay-tags.png");
            MiningMissionsOverlayPresentation presentation = Assert.Single(
                window.GetVisualDescendants().OfType<MiningMissionsOverlayPresentation>()
            );
            window.Resources["LegacyTheme.TestBody"] = Brushes.Magenta;
            OverlayTypographyResources.Apply(
                presentation,
                new OverlayTypographyScale(0, 0, 0, 100, 0, 0)
                {
                    Brushes = new OverlayTypographyBrushes(Body: "TestBody"),
                }
            );
            Settle(window);
            Assert.Equal(12, code.FontSize);
            Assert.Equal(
                Color.Parse("#111111"),
                Assert.IsType<ISolidColorBrush>(code.Foreground, exactMatch: false).Color
            );
            TextBlock systemLabel = window
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Text == "Col 285 Sector VF-E c120-5");
            Assert.Equal(
                Colors.Magenta,
                Assert.IsType<ISolidColorBrush>(systemLabel.Foreground, exactMatch: false).Color
            );
            OverlayTypographyResources.Apply(presentation, new OverlayTypographyScale(0, 0, 0, 0, 0, 0, 100));
            Settle(window);
            Assert.Equal(24, code.FontSize);
            Assert.Equal(14, systemLabel.FontSize);
            Assert.Equal(
                Color.Parse("#111111"),
                Assert.IsType<ISolidColorBrush>(code.Foreground, exactMatch: false).Color
            );
            Assert.True(tag.Bounds.Width >= code.Bounds.Width);
            Assert.True(tag.Bounds.Height >= code.Bounds.Height);
            OverlayTypographyResources.Apply(presentation, OverlayTypographyScale.Default);
            fixture.Workspace.UseCommodityShortTags = false;
            Settle(window);
            Assert.DoesNotContain(
                window.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("commodity-code") && border.IsEffectivelyVisible
            );
            Assert.Contains(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Bertrandite" && text.IsEffectivelyVisible
            );
            Assert.Contains(
                window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "20/100" && text.IsEffectivelyVisible
            );
            Save(window, "mining-destinations-overlay-full-names.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NumericReadoutsFollowValueWhileFooterRowsKeepMatchingSizes()
    {
        using var fixture = new MiningMissionFixture();
        fixture.Feed("""{"event":"Loadout","Ship":"Python","CargoCapacity":512}""");
        fixture.Workspace.Restore(
            MiningStore.Export(
                new MiningCommanderData { Missions = [Mission(1, "Bertrandite", "Bismark Legacy", fixture.Now)] }
            )
        );
        using var model = new MiningMissionsOverlayViewModel(fixture.Workspace);
        var window = new MiningMissionsOverlayWindow(model);
        try
        {
            OverlayThemeResources.Apply(
                window,
                LegacyOverlayLayout.Empty,
                "PlotMiningMissions",
                new OverlayWindowRegistry()
            );
            window.Show();
            Settle(window);
            MiningMissionsOverlayPresentation presentation = Assert.Single(
                window.GetVisualDescendants().OfType<MiningMissionsOverlayPresentation>()
            );
            window.Resources["LegacyTheme.TestValue"] = Brushes.Magenta;
            OverlayTypographyResources.Apply(
                presentation,
                new OverlayTypographyScale(0, 0, 100, 0, -25, 50)
                {
                    Brushes = new OverlayTypographyBrushes(Value: "TestValue"),
                }
            );
            Settle(window);
            TextBlock[] texts = presentation.GetVisualDescendants().OfType<TextBlock>().ToArray();
            foreach (string readout in new[] { "1/1", "30", "20/100", "50", "6d 12h remaining", "1" })
            {
                TextBlock value = Assert.Single(texts, text => text.Text == readout && text.IsEffectivelyVisible);
                Assert.Equal(readout is "6d 12h remaining" or "1" ? 22 : 28, value.FontSize);
                Assert.Equal(
                    Colors.Magenta,
                    Assert.IsType<ISolidColorBrush>(value.Foreground, exactMatch: false).Color
                );
            }
            foreach (string label in new[] { "Earliest:", "Estimated Trips:" })
            {
                TextBlock caption = Assert.Single(texts, text => text.Text == label && text.IsEffectivelyVisible);
                Assert.Equal(16.5, caption.FontSize);
                Assert.NotEqual(
                    Colors.Magenta,
                    Assert.IsType<ISolidColorBrush>(caption.Foreground, exactMatch: false).Color
                );
            }
            Assert.Equal(10.5, Assert.Single(texts, text => text.Text == "On board:").FontSize);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SummaryAndOverlayCombineFiveSelectedMissionsAndUpdateTogether()
    {
        using var fixture = new MiningMissionFixture();
        fixture.Feed("""{"event":"Loadout","Ship":"Python","CargoCapacity":512}""");
        fixture.Workspace.Restore(
            MiningStore.Export(
                new MiningCommanderData
                {
                    Missions =
                    [
                        Mission(1, "Bertrandite", "Bismark Legacy", fixture.Now) with
                        {
                            Required = 704,
                            Delivered = 704,
                            OnBoard = 0,
                        },
                        Mission(2, "Bertrandite", "Bismark Legacy", fixture.Now) with
                        {
                            Required = 652,
                            Delivered = 0,
                            OnBoard = 100,
                        },
                        Mission(3, "Bertrandite", "Bismark Legacy", fixture.Now) with
                        {
                            Required = 632,
                            Delivered = 0,
                            OnBoard = 0,
                        },
                        Mission(4, "Gallite", "Bismark Legacy", fixture.Now) with
                        {
                            Required = 772,
                            Delivered = 10,
                            OnBoard = 32,
                        },
                        Mission(5, "Indite", "Bismark Legacy", fixture.Now) with
                        {
                            Required = 844,
                            Delivered = 0,
                            OnBoard = 20,
                        },
                        Mission(6, "Bertrandite", "Bismark Legacy", fixture.Now) with
                        {
                            IsTracked = false,
                        },
                        Mission(7, "Gallite", "Bismark Legacy", fixture.Now) with
                        {
                            IsTracked = false,
                        },
                    ],
                }
            )
        );
        using var model = new MiningMissionsOverlayViewModel(fixture.Workspace);
        var overlay = new MiningMissionsOverlayWindow(model);
        var view = new MiningMissionCardsView { DataContext = fixture.Workspace, Margin = new Thickness(16) };
        var workspace = new Window
        {
            Width = 1050,
            Height = 900,
            Content = new ScrollViewer { Content = view },
        };
        try
        {
            OverlayThemeResources.Apply(
                overlay,
                LegacyOverlayLayout.Empty,
                "PlotMiningMissions",
                new OverlayWindowRegistry()
            );
            overlay.Show();
            workspace.Show();
            Settle(overlay);
            Settle(workspace);
            Assert.Single(DestinationCards(overlay));
            Assert.Equal(3, model.Destinations[0].Commodities.Count);
            Assert.Contains(
                overlay.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "5/7" && text.IsEffectivelyVisible
            );
            Assert.Contains(
                overlay.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "704/1,988" && text.IsEffectivelyVisible
            );
            Assert.Contains(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "5/7 missions tracked" && text.IsEffectivelyVisible
            );
            Assert.Contains(
                overlay.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "6" && text.IsEffectivelyVisible
            );
            Save(overlay, "mining-missions-overlay-default-final-rc77-7.png");
            Save(workspace, "mining-destinations-workspace-aggregate.png");
            Border[] missionCards = view.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("mining-mission-card"))
                .ToArray();
            missionCards[4].GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = false;
            Settle(workspace);
            Settle(overlay);
            Assert.Contains(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "4/7 missions tracked" && text.IsEffectivelyVisible
            );
            Assert.Contains(
                overlay.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "4/7" && text.IsEffectivelyVisible
            );
            Assert.Equal(2, model.Destinations[0].Commodities.Count);
        }
        finally
        {
            overlay.Close();
            workspace.Close();
        }
    }

    [AvaloniaFact]
    public void OverlayPaletteStaysIndependentFromApplicationThemeChanges()
    {
        using var fixture = new MiningMissionFixture();
        fixture.Workspace.Restore(
            MiningStore.Export(
                new MiningCommanderData { Missions = [Mission(1, "Bertrandite", "Bismark Legacy", fixture.Now)] }
            )
        );
        var themes = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(fixture.Directory, "theme.json"))
        );
        string originalApp = themes.Current.Key;
        LegacyOverlayTheme originalOverlay = themes.CurrentOverlayTheme;
        var colors = new Dictionary<string, Color>(originalOverlay.Colors)
        {
            ["black"] = Color.Parse("#10161D"),
            ["orange"] = Color.Parse("#8BE0EC"),
            ["green"] = Color.Parse("#68D391"),
            ["red"] = Color.Parse("#F58D8D"),
        };
        themes.ApplyOverlayTheme(originalOverlay with { Colors = colors, IsCustom = true });
        using var model = new MiningMissionsOverlayViewModel(fixture.Workspace);
        var window = new MiningMissionsOverlayWindow(model);
        try
        {
            OverlayThemeResources.Apply(
                window,
                LegacyOverlayLayout.Empty,
                "PlotMiningMissions",
                new OverlayWindowRegistry()
            );
            window.Show();
            foreach (string appTheme in new[] { "blue-light", "orange-dark" })
            {
                themes.Select(appTheme);
                OverlayThemeResources.RefreshAll();
                Settle(window);
                MiningMissionsOverlayPresentation presentation = Assert.Single(
                    window.GetVisualDescendants().OfType<MiningMissionsOverlayPresentation>()
                );
                Border panel = presentation.FindControl<Border>("MissionPanel")!;
                Assert.Equal(
                    Color.Parse("#10161D"),
                    Assert.IsType<ISolidColorBrush>(panel.Background, exactMatch: false).Color
                );
                TextBlock station = presentation
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == "Bismark Legacy @");
                Assert.Equal(
                    Color.Parse("#8BE0EC"),
                    Assert.IsType<ISolidColorBrush>(station.Foreground, exactMatch: false).Color
                );
                ProgressBar[] bars = presentation.GetVisualDescendants().OfType<ProgressBar>().ToArray();
                Assert.Equal(
                    Color.Parse("#8BE0EC"),
                    Assert.IsType<ISolidColorBrush>(bars[0].Foreground, exactMatch: false).Color
                );
                Assert.Equal(
                    Color.Parse("#68D391"),
                    Assert.IsType<ISolidColorBrush>(bars[1].Foreground, exactMatch: false).Color
                );
                foreach ((string amount, string color) in new[] { ("20/100", "#68D391"), ("50", "#F58D8D") })
                {
                    TextBlock value = Assert.Single(
                        presentation.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Text == amount && text.IsEffectivelyVisible
                    );
                    Assert.Equal(
                        Color.Parse(color),
                        Assert.IsType<ISolidColorBrush>(value.Foreground, exactMatch: false).Color
                    );
                }
                Save(window, "mining-destinations-custom-overlay-" + appTheme + ".png");
            }
        }
        finally
        {
            window.Close();
            themes.ApplyOverlayTheme(originalOverlay);
            themes.Select(originalApp);
            OverlayThemeResources.RefreshAll();
        }
    }

    [AvaloniaFact]
    public void NamedThemePresetsApplyTheirPaletteToTheLiveMissionPanel()
    {
        Assert.Equal(ExpectedPresetNames, OverlayThemePresetCatalog.Presets.Select(preset => preset.Name));
        using var fixture = new MiningMissionFixture();
        fixture.Feed("""{"event":"Loadout","Ship":"Python","CargoCapacity":512}""");
        fixture.Workspace.Restore(
            MiningStore.Export(
                new MiningCommanderData { Missions = [Mission(1, "Bertrandite", "Bismark Legacy", fixture.Now)] }
            )
        );
        var themes = new RavenThemeService(
            Application.Current!,
            new ThemePreferenceStore(Path.Combine(fixture.Directory, "app-theme.json"))
        );
        string originalApp = themes.Current.Key;
        LegacyOverlayTheme originalOverlay = themes.CurrentOverlayTheme;
        var editor = new OverlayThemeSettingsViewModel(
            new LegacyOverlayThemeStore(Path.Combine(fixture.Directory, "active-overlay-theme.json")),
            new OverlayThemeStateStore(Path.Combine(fixture.Directory, "overlay-presets.json")),
            themes
        );
        using var model = new MiningMissionsOverlayViewModel(fixture.Workspace);
        var window = new MiningMissionsOverlayWindow(model);
        EventHandler refresh = (_, _) => OverlayThemeResources.RefreshAll();
        themes.OverlayThemeChanged += refresh;
        try
        {
            OverlayThemeResources.Apply(
                window,
                LegacyOverlayLayout.Empty,
                "PlotMiningMissions",
                new OverlayWindowRegistry()
            );
            window.Show();
            foreach (
                (
                    string preset,
                    string primary,
                    string header,
                    string valueColor,
                    string textColor,
                    string success,
                    string danger,
                    string background
                ) in new[]
                {
                    ("Default", "#FF6F00", "#CC0003", "#FFFF00", "#FFFFFF", "#00FF00", "#FF0000", "#000000"),
                    (
                        "Monochrome Companion",
                        "#C8CBD0",
                        "#D4BF8F",
                        "#D4BF8F",
                        "#D7D4CC",
                        "#91B69B",
                        "#CF8C86",
                        "#111315"
                    ),
                    ("Nebula Cyan", "#5EC8F2", "#FFE8A3", "#FFE8A3", "#D6EEF9", "#00FF00", "#FF0000", "#000000"),
                    ("Toxic Green", "#5CFF9E", "#FFF066", "#FFF066", "#D8FFE8", "#00FF00", "#FF0000", "#000000"),
                    ("Crimson Wake", "#FF6B6B", "#FFD966", "#FFD966", "#FFE4E4", "#00FF00", "#FF0000", "#000000"),
                    ("Void Amethyst", "#C9A0FF", "#7FFFD4", "#7FFFD4", "#E8E0F5", "#00FF00", "#FF0000", "#000000"),
                    ("Cerulean Gold", "#3D9EE8", "#FFCC33", "#FFCC33", "#C8E4FA", "#00FF00", "#FF0000", "#000000"),
                }
            )
            {
                editor.SelectedSavedState = preset;
                Assert.True(editor.ApplyCommand.CanExecute(null));
                editor.ApplyCommand.Execute(null);
                Settle(window);
                MiningMissionsOverlayPresentation presentation = Assert.Single(
                    window.GetVisualDescendants().OfType<MiningMissionsOverlayPresentation>()
                );
                Assert.Equal(
                    Color.Parse(background),
                    Assert
                        .IsType<ISolidColorBrush>(
                            presentation.FindControl<Border>("MissionPanel")!.Background,
                            exactMatch: false
                        )
                        .Color
                );
                TextBlock station = window
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == "Bismark Legacy @");
                TextBlock heading = window
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text => text.Text == "MINING MISSIONS");
                Assert.Equal(
                    Color.Parse(primary),
                    Assert.IsType<ISolidColorBrush>(station.Foreground, exactMatch: false).Color
                );
                Assert.Equal(
                    Color.Parse(header),
                    Assert.IsType<ISolidColorBrush>(heading.Foreground, exactMatch: false).Color
                );
                foreach (string label in new[] { "On board:", "Delivered:", "Need:", "t" })
                {
                    TextBlock detail = Assert.Single(
                        window.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Text == label && text.IsEffectivelyVisible
                    );
                    Assert.Equal(
                        Color.Parse(textColor),
                        Assert.IsType<ISolidColorBrush>(detail.Foreground, exactMatch: false).Color
                    );
                }
                foreach (string readout in new[] { "1/1", "30", "20/100", "50", "6d 12h remaining", "1" })
                {
                    TextBlock value = Assert.Single(
                        window.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Text == readout && text.IsEffectivelyVisible
                    );
                    string expectedColor = readout switch
                    {
                        "20/100" => success,
                        "50" => danger,
                        _ => valueColor,
                    };
                    Assert.Equal(
                        Color.Parse(expectedColor),
                        Assert.IsType<ISolidColorBrush>(value.Foreground, exactMatch: false).Color
                    );
                }
                Border tag = Assert.Single(
                    window.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("commodity-code") && border.IsEffectivelyVisible
                );
                Assert.Equal(
                    Color.Parse("#B98966"),
                    Assert.IsType<ISolidColorBrush>(tag.Background, exactMatch: false).Color
                );
                TextBlock code = Assert.Single(tag.GetVisualDescendants().OfType<TextBlock>());
                Assert.Equal("BRT", code.Text);
                Assert.Equal(
                    Color.Parse("#111111"),
                    Assert.IsType<ISolidColorBrush>(code.Foreground, exactMatch: false).Color
                );
                ProgressBar[] bars = presentation.GetVisualDescendants().OfType<ProgressBar>().ToArray();
                Assert.Equal(2, bars.Length);
                Assert.Equal(
                    Color.Parse(primary),
                    Assert.IsType<ISolidColorBrush>(bars[0].Foreground, exactMatch: false).Color
                );
                Assert.Equal(
                    Color.Parse(success),
                    Assert.IsType<ISolidColorBrush>(bars[1].Foreground, exactMatch: false).Color
                );
                Save(
                    window,
                    "mining-missions-overlay-all-presets-" + preset.Replace(' ', '-').ToLowerInvariant() + ".png"
                );
                fixture.Workspace.UseCommodityShortTags = false;
                Settle(window);
                TextBlock fullName = Assert.Single(
                    window.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Bertrandite" && text.IsEffectivelyVisible
                );
                Assert.Equal(
                    Color.Parse(textColor),
                    Assert.IsType<ISolidColorBrush>(fullName.Foreground, exactMatch: false).Color
                );
                fixture.Workspace.UseCommodityShortTags = true;
                Settle(window);
            }
        }
        finally
        {
            window.Close();
            themes.OverlayThemeChanged -= refresh;
            themes.ApplyOverlayTheme(originalOverlay);
            themes.Select(originalApp);
            OverlayThemeResources.RefreshAll();
        }
    }

    private static void AssertFitsHorizontally(TextBlock text, Control container)
    {
        Point position = text.TranslatePoint(default, container)!.Value;
        Assert.InRange(position.X, -1, container.Bounds.Width + 1);
        Assert.InRange(position.X + Math.Max(text.Bounds.Width, text.TextLayout.Width), 0, container.Bounds.Width + 1);
    }

    private static MiningMission Mission(int id, string commodity, string station, DateTimeOffset now) =>
        new()
        {
            Id = id,
            Commodity = commodity,
            Required = 100,
            Delivered = 20,
            OnBoard = 30,
            DestinationSystem = "Col 285 Sector VF-E c120-5",
            DestinationStation = station,
            Expiry = now.AddDays(6).AddHours(12).ToString("O"),
            Reward = 50000000,
        };

    private static Border[] DestinationCards(Control root) =>
        root.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("mining-destination-card"))
            .ToArray();

    private static void Settle(Window window)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        }
    }

    private static void Save(Window window, string name)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(Path.GetTempPath(), name), PngBitmapEncoderOptions.Default);
    }
}
