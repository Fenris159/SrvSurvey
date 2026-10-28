using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.Tests.Infrastructure;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class VrPlatformPickerTests
{
    [AvaloniaFact]
    public void ClosedPickerSkipsDisabledRowsBeforePopupContainersExistAndStillNavigatesAfterClosing()
    {
        VrPlatformPicker picker = LinuxPicker();
        var window = new Window
        {
            Width = 420,
            Height = 120,
            Content = picker,
        };
        try
        {
            window.Show();
            picker.Focus();
            Assert.Equal(8, picker.ItemCount);
            Assert.False(picker.IsDropDownOpen);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal(VrPlatformProfileCatalog.MetaAlvrProfileId, picker.SelectedProfile?.Id);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal(VrPlatformProfileCatalog.CustomOpenVrProfileId, picker.SelectedProfile?.Id);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.Equal(VrPlatformProfileCatalog.MetaAlvrProfileId, picker.SelectedProfile?.Id);
            picker.IsDropDownOpen = true;
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.All(
                picker.Items.OfType<ComboBoxItem>(),
                item => Assert.Equal(Assert.IsType<VrPlatformProfile>(item.Content).IsAvailable, item.IsEnabled)
            );
            picker.IsDropDownOpen = false;
            picker.Focus();
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            Assert.Equal(VrPlatformProfileCatalog.SteamVrProfileId, picker.SelectedProfile?.Id);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TypeAheadAndMouseWheelSelectAvailableRoutesAndCannotActivateUnsupportedRows()
    {
        VrPlatformPicker picker = LinuxPicker();
        var window = new Window
        {
            Width = 420,
            Height = 120,
            Content = picker,
        };
        try
        {
            window.Show();
            picker.Focus();
            picker.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "Meta" });
            Assert.Equal(VrPlatformProfileCatalog.MetaAlvrProfileId, picker.SelectedProfile?.Id);
            picker.RaiseEvent(
                new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = " Quest — VDXR" }
            );
            Assert.Equal(VrPlatformProfileCatalog.MetaAlvrProfileId, picker.SelectedProfile?.Id);
            picker.RaiseEvent(
                new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = string.Empty }
            );
            picker.RaiseEvent(
                new TextInputEventArgs
                {
                    RoutedEvent = InputElement.TextInputEvent,
                    Text = "Meta",
                    Handled = true,
                }
            );
            picker.SelectedItem = picker
                .Items.OfType<ComboBoxItem>()
                .Single(item => item.Content is VrPlatformProfile { Id: VrPlatformProfileCatalog.MetaVdxrProfileId });
            Assert.Equal(VrPlatformProfileCatalog.MetaAlvrProfileId, picker.SelectedProfile?.Id);
            Assert.Equal(
                VrPlatformProfileCatalog.MetaAlvrProfileId,
                Assert.IsType<VrPlatformProfile>(Assert.IsType<ComboBoxItem>(picker.SelectedItem).Content).Id
            );
            picker.MaxDropDownHeight = 700;
            picker.IsDropDownOpen = true;
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            ComboBoxItem disabled = picker
                .Items.OfType<ComboBoxItem>()
                .Single(item => item.Content is VrPlatformProfile { Id: VrPlatformProfileCatalog.MetaVdxrProfileId });
            TopLevel popup = TopLevel.GetTopLevel(disabled)!;
            Point disabledCenter = disabled
                .TranslatePoint(new Point(disabled.Bounds.Width / 2, disabled.Bounds.Height / 2), popup)!
                .Value;
            popup.MouseDown(disabledCenter, MouseButton.Left);
            popup.MouseUp(disabledCenter, MouseButton.Left);
            Assert.Equal(VrPlatformProfileCatalog.MetaAlvrProfileId, picker.SelectedProfile?.Id);
            picker.IsDropDownOpen = false;
            picker.Focus();
            Point center = picker
                .TranslatePoint(new Point(picker.Bounds.Width / 2, picker.Bounds.Height / 2), window)!
                .Value;
            window.MouseWheel(center, new Vector(0, -1));
            Assert.Equal(VrPlatformProfileCatalog.CustomOpenVrProfileId, picker.SelectedProfile?.Id);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedDisabledSelectionRemainsReadableAndCanMoveToAnAvailableRoute(bool open)
    {
        VrPlatformPicker picker = LinuxPicker();
        picker.SelectedProfile = picker.Profiles.Single(profile =>
            profile.Id == VrPlatformProfileCatalog.MetaVdxrProfileId
        );
        var window = new Window
        {
            Width = 420,
            Height = 120,
            Content = picker,
        };
        try
        {
            window.Show();
            Assert.False(Assert.IsType<ComboBoxItem>(picker.SelectedItem).IsEnabled);
            Assert.Equal(picker.SelectedProfile, picker.SelectionBoxItem);
            picker.Focus();
            picker.IsDropDownOpen = open;
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            if (open)
            {
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            Assert.Equal(VrPlatformProfileCatalog.MetaAlvrProfileId, picker.SelectedProfile?.Id);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Assert.Equal(VrPlatformProfileCatalog.CustomOpenVrProfileId, picker.SelectedProfile?.Id);
            picker.SelectedProfile = null;
            Assert.Null(picker.SelectedItem);
            picker.Profiles = [];
            Assert.Empty(picker.Items);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ActualSettingsPickerBindsAvailabilityAndShowsDisabledReasons()
    {
        string directory = Path.Combine(Path.GetTempPath(), "srvsurvey-vr-picker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using MainWindowViewModel viewModel = MainWindowViewModelTestBuilder.Create(directory);
            var settings = new OverlaySettingsView { DataContext = viewModel };
            var window = new Window
            {
                Width = 1180,
                Height = 900,
                Content = new ScrollViewer { Content = settings },
            };
            try
            {
                window.Show();
                settings.FindControl<TabControl>("GlobalOverlaySections")!.SelectedIndex = 4;
                VrPlatformPicker picker = Assert.IsType<VrPlatformPicker>(
                    settings.FindControl<VrPlatformPicker>("VrRoutePicker")
                );
                picker.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                picker.MaxDropDownHeight = 700;
                picker.IsDropDownOpen = true;
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Same(viewModel.VrOverlay.SelectedPlatformProfile, picker.SelectedProfile);
                Assert.Equal(8, picker.ItemCount);
                foreach (ComboBoxItem item in picker.Items.OfType<ComboBoxItem>())
                {
                    VrPlatformProfile profile = Assert.IsType<VrPlatformProfile>(item.Content);
                    Assert.Equal(profile.IsAvailable, item.IsEnabled);
                    if (!profile.IsAvailable)
                    {
                        Assert.Equal(0.45, item.Opacity);
                        Assert.Contains(
                            item.GetVisualDescendants().OfType<TextBlock>(),
                            text => text.Text == profile.UnavailableReason
                        );
                    }
                }
                string? renderDirectory = Environment.GetEnvironmentVariable("SRVSURVEY_VR_PICKER_RENDER_DIRECTORY");
                if (!string.IsNullOrWhiteSpace(renderDirectory))
                {
                    ComboBoxItem first = picker.Items.OfType<ComboBoxItem>().First();
                    TopLevel popup = TopLevel.GetTopLevel(first)!;
                    using WriteableBitmap? popupFrame = popup.CaptureRenderedFrame();
                    Assert.NotNull(popupFrame);
                    Directory.CreateDirectory(renderDirectory);
                    popupFrame.Save(
                        Path.Combine(renderDirectory, "vr-platform-picker.png"),
                        PngBitmapEncoderOptions.Default
                    );
                }
                ComboBoxItem alvr = picker
                    .Items.OfType<ComboBoxItem>()
                    .Single(item =>
                        item.Content is VrPlatformProfile { Id: VrPlatformProfileCatalog.MetaAlvrProfileId }
                    );
                picker.SelectedItem = alvr;
                Assert.Equal(
                    VrPlatformProfileCatalog.MetaAlvrProfileId,
                    viewModel.VrOverlay.SelectedPlatformProfile.Id
                );
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static VrPlatformPicker LinuxPicker()
    {
        IReadOnlyList<VrPlatformProfile> profiles = VrPlatformProfileCatalog.ForPlatform(
            OSPlatform.Linux,
            Architecture.X64
        );
        return new VrPlatformPicker
        {
            Profiles = profiles,
            ItemTemplate = new FuncDataTemplate<VrPlatformProfile?>(
                (profile, _) => new TextBlock { Text = profile?.DisplayName }
            ),
            SelectedProfile = profiles[0],
        };
    }
}
