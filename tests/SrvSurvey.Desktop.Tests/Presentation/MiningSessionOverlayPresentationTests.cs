using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MiningSessionOverlayPresentationTests
{
    /// <summary>The mining settings bind persisted sort choices and expose right-side arrows that step by one.</summary>
    [AvaloniaTheory]
    [InlineData(660)]
    [InlineData(1000)]
    public void MiningSettingsExposeSessionSortingAndResultCountSpinner(int width)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"SrvSurvey-mining-settings-{Guid.NewGuid():N}");
        using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
            null,
            builder =>
                builder.WithAppDataPaths(
                    new(
                        Path.Combine(directory, "config"),
                        Path.Combine(directory, "data"),
                        Path.Combine(directory, "cache"),
                        []
                    )
                )
        );
        var context = new JournalSessionState();
        JournalEventEnvelope load = MiningMissionFixture.Event(
            """{"event":"LoadGame","FID":"F1","Commander":"Test","Ship":"Python"}"""
        );
        context.Apply(load);
        var status = new EliteStatus { Flags = StatusFlags.InMainShip };
        var view = new Views.OverlaySettingsView(OverlaySettingsCategory.Mining) { DataContext = main };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1000,
        };
        try
        {
            window.Show();
            using WriteableBitmap? initialFrame = window.CaptureRenderedFrame();
            ComboBox sort = view.FindControl<ComboBox>("MiningSessionYieldSortInput")!;
            NumericUpDown limit = view.FindControl<NumericUpDown>("MiningSessionYieldLimitInput")!;
            Assert.False(sort.IsEffectivelyEnabled);
            Assert.False(limit.IsEffectivelyEnabled);
            Assert.False(main.MiningWorkspace.SaveSettingsCommand.CanExecute(null));
            Assert.Contains(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == "Waiting for commander journal data."
            );
            main.MiningWorkspace.Apply(new(null, [load], status, null, null, null, [], true), context, null, status);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.True(sort.IsEffectivelyEnabled);
            Assert.True(limit.IsEffectivelyEnabled);
            Assert.True(main.MiningWorkspace.SaveSettingsCommand.CanExecute(null));
            Assert.True(sort.IsEffectivelyVisible);
            Assert.True(limit.IsEffectivelyVisible);
            Assert.Equal(0, sort.SelectedIndex);
            Assert.Equal(5, limit.Value);
            Assert.Equal(1, limit.Increment);
            Assert.True(limit.ShowButtonSpinner);
            Assert.Equal("Right", limit.ButtonSpinnerLocation.ToString());
            sort.SelectedIndex = 1;
            Button increase = Assert.Single(
                limit.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "PART_IncreaseButton"
            );
            Button decrease = Assert.Single(
                limit.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "PART_DecreaseButton"
            );
            increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(6, limit.Value);
            Assert.Equal(6, main.MiningWorkspace.SessionYieldLimit);
            decrease.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(5, main.MiningWorkspace.SessionYieldLimit);
            main.MiningWorkspace.SessionYieldLimit = 7;
            Assert.Equal(7, limit.Value);
            MiningPreferences recovered = new MiningStore(main.AppDataPaths.DataDirectory).Load("F1").Settings;
            Assert.Equal(MiningSessionYieldSort.Finds, recovered.SessionYieldSort);
            Assert.Equal(7, recovered.SessionYieldLimit);
            string? output = Environment.GetEnvironmentVariable("SRVSURVEY_MINING_SETTINGS_RENDER_OUTPUT");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                frame!.Save(
                    Path.Combine(output, $"mining-session-overlay-settings-default-{width}.png"),
                    PngBitmapEncoderOptions.Default
                );
                using WriteableBitmap? updated = window.CaptureRenderedFrame();
                updated!.Save(
                    Path.Combine(output, $"mining-session-overlay-settings-{width}.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
        }
        finally
        {
            window.Close();
            main.Dispose();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Renders the journal's material content under recent prospects and omits unknown content.</summary>
    [AvaloniaTheory]
    [InlineData("$AsteroidMaterialContent_High;", "Material content: High")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NotificationsRenderRecentProspectsMaterialContent(string? content, string expected)
    {
        using var fixture = new MiningMissionFixture();
        fixture.Workspace.StartCommand.Execute(null);
        using var overlay = new MiningActivityOverlayViewModel(fixture.Workspace, false);
        string contentField = content is null ? "" : $",\"Content\":\"{content}\"";
        fixture.Feed(
            $$"""{"timestamp":"{{fixture.Now.AddSeconds(1):O}}","event":"ProspectedAsteroid","Materials":[{"Name":"Platinum","Proportion":35}]{{contentField}}}"""
        );
        var presentation = new MiningActivityOverlayPresentation { DataContext = overlay };
        var window = new Window { Content = presentation, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            TextBlock[] visible = presentation
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible)
                .ToArray();
            Assert.Contains(visible, text => text.Text == "RECENT PROSPECTS");
            if (expected.Length > 0)
            {
                Assert.Single(visible, text => text.Text == expected);
            }
            else
            {
                Assert.DoesNotContain(
                    visible,
                    text => text.Text?.StartsWith("Material content:", StringComparison.Ordinal) == true
                );
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SessionPreviewUsesTheLivePresentationAndMiningCategory()
    {
        OverlayLayoutDefinition definition = OverlayLayoutCatalog.GetRequired("PlotMiningSession");
        Assert.Equal(OverlayLayoutCategory.Mining, definition.Category);
        using var previewModel = new MiningSessionOverlayViewModel(null);
        Assert.True(previewModel.HasSession);
        Assert.Equal("Active", previewModel.State);
        var preview = new OverlayPositionPreviewWindow(definition);
        try
        {
            preview.Show();
            using WriteableBitmap? frame = preview.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.IsType<SrvSurvey.Desktop.MiningSessionOverlayPresentation>(preview.RuntimePresentation);
            Assert.Contains(preview.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "SESSION");
            Assert.Contains(
                preview.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text != null && text.Text.Contains("64 t", StringComparison.Ordinal)
            );
            TextBlock[] textBlocks = preview.GetVisualDescendants().OfType<TextBlock>().ToArray();
            Assert.Contains(textBlocks, text => text.Text == "Prospecting yields");
            Assert.Contains(textBlocks, text => text.Text == "Platinum");
            Assert.Contains(textBlocks, text => text.Text == "Osmium");
            Assert.Contains(textBlocks, text => text.Text == "Quality hits");
            Assert.Contains(textBlocks, text => text.Text == $"{43.6:0.0}%");
            Assert.DoesNotContain(textBlocks, text => text.Text?.Contains("Cargo", StringComparison.Ordinal) == true);
            Assert.True(previewModel.HasProspectingYields);
            Assert.Equal(2, previewModel.ProspectingYields.Count);
            Assert.Equal(2, previewModel.ProspectingYields[0].QualityHits);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-mining-session-preview.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            preview.Close();
        }
        Assert.All(
            OverlayLayoutCatalog.Supported.Where(panel =>
                panel.SettingsCategories.Contains(OverlaySettingsCategory.MineMap)
            ),
            panel => Assert.Equal(OverlayLayoutCategory.SurfaceMining, panel.Category)
        );
        Assert.Equal(
            "Mining (surface)",
            OverlayLayoutCatalog
                .Categories.Single(category => category.Category == OverlayLayoutCategory.SurfaceMining)
                .DisplayName
        );
    }
}
