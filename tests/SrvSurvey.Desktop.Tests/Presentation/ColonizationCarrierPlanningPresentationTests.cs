using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    /// <summary>Keeps each heading aligned with its values and separates Show from Shop as the window resizes.</summary>
    [AvaloniaTheory]
    [InlineData(820)]
    [InlineData(1180)]
    [InlineData(1440)]
    public async Task ProjectHeadersAlignWithTheirColumns(int width)
    {
        using var http = new HttpClient(
            new RavenHandler
            {
                ActiveProjects =
                [
                    new()
                    {
                        BuildId = "one",
                        BuildName = "Chomsky Enterprise",
                        BuildType = "ares",
                        SystemName = "Synuefai CX-V c18-6",
                        SystemAddress = 1732784263842,
                        MaximumRequired = 9_922,
                        RemainingRequired = 8_726,
                        Commodities = new() { ["steel"] = 8_726 },
                    },
                ],
                LinkedCarriers =
                [
                    new()
                    {
                        MarketId = 42,
                        Name = "N4W-T0Z",
                        Cargo = new() { ["steel"] = 465 },
                    },
                ],
            }
        );
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        var view = new Views.ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 800,
        };
        try
        {
            window.Show();
            StackPanel table = view.FindControl<StackPanel>("ProjectTable")!;
            table.BringIntoView();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Grid headers = view.FindControl<Grid>("ProjectTableHeaders")!;
            Grid row = Assert.Single(
                table.GetVisualDescendants().OfType<Grid>(),
                grid => grid.Classes.Contains("colonization-project-columns")
            );
            Assert.Equal(headers.Bounds.Width, row.Bounds.Width, precision: 1);
            foreach (TextBlock header in headers.Children.OfType<TextBlock>())
            {
                int column = Grid.GetColumn(header);
                Control cell = Assert.Single(row.Children, child => Grid.GetColumn(child) == column);
                double headerX = header.TranslatePoint(default, table)!.Value.X;
                double cellX = cell.TranslatePoint(default, table)!.Value.X;
                if (column is 0 or 6)
                {
                    Assert.InRange(Math.Abs(headerX + header.Bounds.Width / 2 - cellX - cell.Bounds.Width / 2), 0, 1);
                }
                else
                {
                    TextBlock value = cell as TextBlock ?? cell.GetVisualDescendants().OfType<TextBlock>().First();
                    Assert.Equal(headerX, value.TranslatePoint(default, table)!.Value.X, precision: 1);
                }
                Assert.True(header.Bounds.Width >= header.DesiredSize.Width, "Column headings must remain readable.");
                Assert.True(cellX + cell.Bounds.Width <= table.Bounds.Width + 1, "Columns must fit inside the table.");
            }
            Assert.Equal("5.33%", Assert.Single(main.Colonization.Projects).CarrierCoverageText);
            using var preview = new RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(table.Bounds.Width), (int)Math.Ceiling(table.Bounds.Height)),
                new Vector(96, 96)
            );
            preview.Render(table);
            preview.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-project-columns-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Updates the rendered Loaded percentage from the latest remaining needs and the selected carrier.</summary>
    [AvaloniaTheory]
    [InlineData(0, "45.00%", "90.00%")]
    [InlineData(42, "5.00%", "10.00%")]
    [InlineData(43, "40.00%", "80.00%")]
    public async Task LoadedUsesSelectedCarrierAndRefreshesWhenRemainingNeedsChange(
        long selectedMarketId,
        string before,
        string after
    )
    {
        var project = new ColonizationProject
        {
            BuildId = "one",
            BuildName = "Port",
            MaximumRequired = 1_000,
            RemainingRequired = 1_000,
            Commodities = new() { ["steel"] = 1_000 },
        };
        var handler = new RavenHandler
        {
            ActiveProjects = [project],
            LinkedCarriers =
            [
                new()
                {
                    MarketId = 42,
                    Name = "AAA-111",
                    Cargo = new() { ["steel"] = 50 },
                },
                new()
                {
                    MarketId = 43,
                    Name = "BBB-222",
                    Cargo = new() { ["steel"] = 400 },
                },
            ],
        };
        using var http = new HttpClient(handler);
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        var view = new Views.ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1180,
            Height = 800,
        };
        try
        {
            window.Show();
            ComboBox selector = view.FindControl<ComboBox>("PlanningCarrierSelector")!;
            selector.SelectedItem = main.Colonization.PlanningCarrierOptions.Single(choice =>
                choice.MarketId == selectedMarketId
            );
            Assert.Equal(before, Assert.Single(main.Colonization.Projects).CarrierCoverageText);
            handler.ActiveProjects =
            [
                project with
                {
                    RemainingRequired = 500,
                    Commodities = new() { ["steel"] = 500 },
                },
            ];
            await main.Colonization.RefreshAsync();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.Equal(selectedMarketId, main.Colonization.SelectedPlanningCarrier.MarketId);
            Assert.Equal(selectedMarketId, Assert.IsType<ColonizationCarrierChoice>(selector.SelectedItem).MarketId);
            Assert.Equal(500, Assert.Single(main.Colonization.Projects).Project.RemainingRequired);
            Assert.Equal(after, Assert.Single(main.Colonization.Projects).CarrierCoverageText);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == after);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Restores Use Carrier with the selector attached before workspace loading and keeps it across refreshes.</summary>
    [AvaloniaFact]
    public async Task PlanningCarrierSelectionSurvivesStartupWithSelectorAttached()
    {
        using var http = new HttpClient(
            new RavenHandler
            {
                LinkedCarriers =
                [
                    new()
                    {
                        MarketId = 43,
                        Name = "BBB-222",
                        Cargo = new() { ["steel"] = 25 },
                    },
                ],
            }
        );
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        var store = new ColonizationSettingsStore(main.AppDataPaths.UiSettingsPath);
        store.SavePlanningCarrier("Example Cmdr", 43);
        main.Colonization.IsEnabled = true;
        var view = new Views.ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1180,
            Height = 800,
        };
        try
        {
            window.Show();
            main.Colonization.SetCommanderProfile("F123", true, "test-key");
            await main.Colonization.SetCommanderAsync("Example Cmdr");
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            ComboBox selector = view.FindControl<ComboBox>("PlanningCarrierSelector")!;
            Assert.Equal(43, main.Colonization.SelectedPlanningCarrier.MarketId);
            Assert.Equal(43, Assert.IsType<ColonizationCarrierChoice>(selector.SelectedItem).MarketId);
            Assert.Equal(43, store.LoadPlanningCarrier("Example Cmdr"));
            await main.Colonization.RefreshAsync();
            Assert.Equal(43, main.Colonization.SelectedPlanningCarrier.MarketId);
            Assert.Equal(43, store.LoadPlanningCarrier("Example Cmdr"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PlanningCarrierSelectorUpdatesLoadedColumnAndManifestDefault()
    {
        using var http = new HttpClient(
            new RavenHandler
            {
                LinkedCarriers =
                [
                    new()
                    {
                        MarketId = 43,
                        Name = "BBB-222",
                        DisplayName = "Team Carrier",
                        Owner = "Teammate",
                        Cargo = new() { ["steel"] = 25 },
                    },
                ],
            }
        );
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder => builder.WithExternalNetworkClient(http)
        );
        main.Colonization.IsEnabled = true;
        main.Colonization.SetCommanderProfile("F123", true, "test-key");
        await main.Colonization.SetCommanderAsync("Example Cmdr");
        var view = new Views.ColonizationView { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = 1180,
            Height = 800,
        };
        try
        {
            window.Show();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            ComboBox selector = view.FindControl<ComboBox>("PlanningCarrierSelector")!;
            Assert.Equal("ALL", Assert.IsType<ColonizationCarrierChoice>(selector.SelectedItem).Label);
            Assert.Equal(2, selector.ItemCount);
            selector.SelectedItem = main.Colonization.PlanningCarrierOptions[1];
            Assert.Equal(43, main.Colonization.SelectedPlanningCarrier.MarketId);
            Assert.Equal("22.73%", main.Colonization.Projects[0].CarrierCoverageText);
            TextBlock label = Assert.Single(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "LOADED"
            );
            Assert.Equal(2, Grid.GetColumn(label));
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "22.73%");
            using ColonizationCarrierManifestViewModel editor = main.Colonization.CreateCarrierManifest();
            await editor.InitializeAsync();
            Assert.Equal(43, editor.SelectedCarrier!.MarketId);
            selector.BringIntoView();
            using WriteableBitmap? preview = window.CaptureRenderedFrame();
            Assert.NotNull(preview);
            preview.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-carrier-planning-preview.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }
}
