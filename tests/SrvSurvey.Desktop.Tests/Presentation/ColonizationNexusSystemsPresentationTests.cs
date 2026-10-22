using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationNexusSystemsPresentationTests
{
    [AvaloniaTheory]
    [InlineData(820)]
    [InlineData(1180)]
    public async Task CarrierAssignmentsStackSystemsWithTheirOwnClickableBuildTags(int width)
    {
        ColonizationNexus source = NexusFixture.Snapshot();
        source = source with
        {
            Systems =
            [
                source.Systems[0] with
                {
                    Builds =
                    [
                        source.Systems[0].Builds[0],
                        new()
                        {
                            BuildId = "old",
                            BuildName = "Finished port",
                            IsComplete = true,
                        },
                    ],
                },
                source.Systems[1] with
                {
                    FleetCarrierIds = [42],
                    Builds =
                    [
                        new()
                        {
                            BuildId = "second",
                            BuildName = "Second port",
                            BuildType = "outpost",
                        },
                    ],
                },
                source.Systems[2],
            ],
        };
        var api = new ColonizationNexusEditorViewModelTests.Writer { Existing = source };
        ColonizationProject? opened = null;
        using var model = new ColonizationNexusViewModel(
            api,
            ColonizationBuildCatalog.LoadEmbedded(),
            project => opened = project
        );
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        var view = new ColonizationNexusView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1050,
        };
        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            ItemsControl assignments = view.GetVisualDescendants()
                .OfType<ItemsControl>()
                .Single(control => control.Name == "NexusCarrierAssignments");
            Assert.Equal(2, assignments.ItemCount);
            IReadOnlyList<ColonizationNexusSystemLink> links = model.Detail!.Carriers[0].SystemLinks;
            Assert.Equal("Test Port", Assert.Single(links[0].Builds).Project.BuildName);
            Assert.Equal("Second port", Assert.Single(links[1].Builds).Project.BuildName);
            Button[] tags = assignments
                .GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("nexus-assigned-build"))
                .ToArray();
            Assert.Equal(2, tags.Length);
            Assert.DoesNotContain(
                assignments.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Finished port"
            );
            Control first = assignments.ContainerFromIndex(0)!;
            Control second = assignments.ContainerFromIndex(1)!;
            Assert.True(
                second.TranslatePoint(default, assignments)!.Value.Y
                    > first.TranslatePoint(default, assignments)!.Value.Y
            );
            tags[1].Command!.Execute(null);
            Assert.Equal("second", opened!.BuildId);
            assignments.BringIntoView();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-nexus-assigned-builds-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TrashOpensConfirmationCancelKeepsSystemAndLastSystemIsDisabled()
    {
        ColonizationNexus source = NexusFixture.Snapshot();
        var api = new ColonizationNexusEditorViewModelTests.Writer
        {
            Existing = source with { Systems = source.Systems.Take(2).ToArray() },
        };
        using var model = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        var view = new ColonizationNexusView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = 1000,
            Height = 850,
        };
        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            Button trash = view.GetVisualDescendants()
                .OfType<Button>()
                .First(button => button.Classes.Contains("nexus-remove-system"));
            Assert.True(trash.IsEnabled);
            trash.Command!.Execute(trash.CommandParameter);
            Assert.NotNull(window.CaptureRenderedFrame());
            Border prompt = view.FindControl<Border>("RemoveNexusSystemPrompt")!;
            Assert.True(prompt.IsVisible);
            Assert.False(view.FindControl<ScrollViewer>("NexusPageScroll")!.IsEnabled);
            Assert.True(view.FindControl<Button>("CancelNexusSystemRemoval")!.IsKeyboardFocusWithin);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(prompt.IsVisible);
            Assert.True(view.FindControl<ScrollViewer>("NexusPageScroll")!.IsEnabled);
            trash.Command.Execute(trash.CommandParameter);
            view.FindControl<Button>("CancelNexusSystemRemoval")!.Command!.Execute(null);
            Assert.False(prompt.IsVisible);
            Assert.Equal(0, api.SystemChanges);
            trash.Command.Execute(trash.CommandParameter);
            view.FindControl<Button>("ConfirmNexusSystemRemoval")!.Command!.Execute(null);
            await model.ActiveTask;
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.Single(model.Detail!.Systems);
            Assert.Equal(1, api.SystemChanges);
            Assert.False(prompt.IsVisible);
            trash = Assert.Single(
                view.GetVisualDescendants().OfType<Button>(),
                button => button.Classes.Contains("nexus-remove-system")
            );
            Assert.False(trash.IsEnabled);
            Assert.Contains("Delete Nexus", Assert.IsType<string>(ToolTip.GetTip(trash)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(680, 0)]
    [InlineData(1180, 0)]
    [InlineData(680, 1)]
    [InlineData(1180, 1)]
    [InlineData(680, 2)]
    public async Task CargoScopeDropdownOpensByClickWithoutScrollingTheNexusPage(int width, int selection)
    {
        using var model = new ColonizationNexusViewModel(
            new ColonizationNexusViewModelTests.NexusReader(),
            ColonizationBuildCatalog.LoadEmbedded(),
            _ => { }
        );
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        var view = new ColonizationNexusView { DataContext = model };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 650,
        };
        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            ComboBox scope = view.GetVisualDescendants()
                .OfType<ComboBox>()
                .Single(combo => combo.Name == "NexusScopePicker");
            Assert.Contains(
                view.GetVisualDescendants().OfType<TextBlock>(),
                text =>
                    text.Text
                    == "Ready cargo uses all Nexus carriers. Each system is estimated independently; All systems counts shared stock once."
            );
            scope.SelectedIndex = selection;
            scope.BringIntoView();
            Assert.NotNull(window.CaptureRenderedFrame());
            ScrollViewer scroll = view.FindControl<ScrollViewer>("NexusPageScroll")!;
            Vector before = scroll.Offset;
            Point click = scope
                .TranslatePoint(new Point(scope.Bounds.Width - 15, scope.Bounds.Height / 2), window)!
                .Value;
            window.MouseDown(click, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(click, MouseButton.Left, RawInputModifiers.None);
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.NotNull(window.CaptureRenderedFrame());
            Assert.True(
                scope.IsDropDownOpen,
                $"Dropdown closed after click; page offset={scroll.Offset} (was {before})."
            );
            Assert.Equal(before, scroll.Offset);
            Assert.True(Assert.Single(scope.GetTemplateDescendants().OfType<Popup>()).IsOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task NexusPickerUsesSubtleSelectedAndHoveredRowColors()
    {
        using var model = new ColonizationNexusViewModel(
            new ColonizationNexusViewModelTests.NexusReader(),
            ColonizationBuildCatalog.LoadEmbedded(),
            _ => { }
        );
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        var view = new ColonizationNexusView { DataContext = model };
        view.Resources["RavenSecondaryFillBrush"] = new SolidColorBrush(Color.Parse("#252525"));
        view.Resources["RavenTextBrush"] = Brushes.White;
        var window = new Window
        {
            Content = view,
            Width = 1000,
            Height = 650,
        };
        try
        {
            window.Show();
            Assert.NotNull(window.CaptureRenderedFrame());
            ComboBox picker = view.FindControl<ComboBox>("NexusPicker")!;
            Popup popup = Assert.Single(picker.GetTemplateDescendants().OfType<Popup>());
            popup.ShouldUseOverlayLayer = true;
            picker.IsDropDownOpen = true;
            Assert.NotNull(window.CaptureRenderedFrame());
            ComboBoxItem selected = Assert.Single(
                popup.Child!.GetVisualDescendants().OfType<ComboBoxItem>(),
                item => item.IsSelected
            );
            ContentPresenter presenter = selected
                .GetTemplateDescendants()
                .OfType<ContentPresenter>()
                .Single(control => control.Name == "PART_ContentPresenter");
            Assert.Equal(Color.Parse("#252525"), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
            Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Foreground).Color);
            Point point = selected
                .TranslatePoint(new Point(selected.Bounds.Width / 2, selected.Bounds.Height / 2), window)!
                .Value;
            window.MouseMove(point);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(selected.IsPointerOver);
            Assert.Equal(Color.Parse("#252525"), Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-nexus-picker-soft-highlight.png"),
                PngBitmapEncoderOptions.Default
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(680, 58, 1)]
    [InlineData(680, 58, 2)]
    [InlineData(1180, 58, 2)]
    [InlineData(680, 0, 2)]
    public async Task ReopeningCargoNeedsDropdownWithWindowChromeDoesNotMoveThePage(
        int width,
        int chromeOffset,
        double scale
    )
    {
        ColonizationNexus source = NexusFixture.Snapshot();
        var api = new ColonizationNexusViewModelTests.NexusReader
        {
            Read = (_, _) => Task.FromResult<ColonizationNexus?>(source with { Systems = [source.Systems[0]] }),
        };
        using var model = new ColonizationNexusViewModel(api, ColonizationBuildCatalog.LoadEmbedded(), _ => { });
        model.SetContext(true, "Test", "key");
        await model.RefreshAsync();
        var view = new ColonizationNexusView { DataContext = model };
        var workspace = new ColonizationView();
        TabControl tabs = workspace.FindControl<TabControl>("ColonizationTabs")!;
        ((Border)workspace.FindControl<ScrollViewer>("UnconfirmedDeliveryRecoveryScroll")!.Parent!).IsVisible = false;
        ((TabItem)tabs.Items[3]!).Content = view;
        tabs.SelectedIndex = 3;
        var window = new Window
        {
            Content = new Border { Child = workspace, Padding = new Thickness(0, 0, 0, chromeOffset) },
            Width = width,
            Height = 450,
            RenderTransform = new TranslateTransform(0, chromeOffset),
        };
        try
        {
            window.Show();
            window.SetRenderScaling(scale);
            Assert.NotNull(window.CaptureRenderedFrame());
            ComboBox scope = view.GetVisualDescendants()
                .OfType<ComboBox>()
                .Single(combo => combo.Name == "NexusScopePicker");
            Popup popup = Assert.Single(scope.GetTemplateDescendants().OfType<Popup>());
            popup.ShouldUseOverlayLayer = true;
            scope.BringIntoView();
            Assert.NotNull(window.CaptureRenderedFrame());
            ScrollViewer scroll = view.FindControl<ScrollViewer>("NexusPageScroll")!;
            Vector before = scroll.Offset;
            Visual root = Assert.IsAssignableFrom<Visual>(window.GetPresentationSource()?.RootVisual);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Point click = scope
                    .TranslatePoint(new Point(scope.Bounds.Width - 15, scope.Bounds.Height / 2), root)!
                    .Value;
                window.MouseMove(click);
                Assert.True(scope.IsPointerOver, $"Click {click} missed Cargo needs on opening {attempt + 1}.");
                window.MouseDown(click, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                window.MouseUp(click, MouseButton.Left, RawInputModifiers.None);
                Assert.NotNull(window.CaptureRenderedFrame());
                Assert.NotNull(window.CaptureRenderedFrame());
                Assert.True(scope.IsDropDownOpen);
                Assert.True(
                    scroll.Offset == before,
                    $"Opening {attempt + 1} moved page from {before} to {scroll.Offset}, chrome={chromeOffset}, scale={scale}."
                );
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.NotNull(window.CaptureRenderedFrame());
                Assert.False(scope.IsDropDownOpen);
                Assert.Equal(before, scroll.Offset);
            }
        }
        finally
        {
            window.Close();
        }
    }
}
