using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed partial class ColonizationShoppingPresentationTests
{
    /// <summary>Checks only the requested market identities, independently of display names and roster ordering.</summary>
    private static void SetCarrierChecks(CarrierMultiSelect picker, long marketId)
    {
        ColonizationCarrierSelectionViewModel selection = Assert.IsType<ColonizationCarrierSelectionViewModel>(
            picker.DataContext
        );
        if (marketId == 0)
        {
            selection.SelectAllCommand.Execute(null);
            return;
        }
        selection.ClearCommand.Execute(null);
        selection.Items.Single(item => item.MarketId == marketId).IsSelected = true;
    }

    /// <summary>Pointer and keyboard changes keep the popup open and preserve the three-carrier subset when reopened.</summary>
    [AvaloniaFact]
    public void CarrierCheckboxPickerKeepsThreeOfSevenCheckedAcrossClicksKeyboardAndReopening()
    {
        var selection = new ColonizationCarrierSelectionViewModel();
        selection.UpdateCarriers(
            Enumerable
                .Range(1, 7)
                .Select(index => new ColonizationFleetCarrier
                {
                    MarketId = 3_700_000_000L + index,
                    Name = $"ABC-00{index}",
                    DisplayName = $"Carrier {index}",
                })
                .ToArray()
        );
        var picker = new CarrierMultiSelect { DataContext = selection, Width = 300 };
        var window = new Window
        {
            Width = 620,
            Height = 500,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Children =
                {
                    picker,
                    new TextBox { Margin = new Thickness(0, 20, 0, 0), PlaceholderText = "Another field" },
                },
            },
        };
        try
        {
            window.Show();
            Button button = picker.FindControl<Button>("CarrierPickerButton")!;
            ClickCarrierControl(button);
            Flyout flyout = Assert.IsType<Flyout>(button.Flyout);
            Assert.True(flyout.IsOpen);
            Control content = Assert.IsType<Control>(flyout.Content, exactMatch: false);
            CheckBox[] boxes = content.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.Equal(7, boxes.Length);
            foreach (
                CheckBox box in boxes.Where(box =>
                    Assert.IsType<ColonizationCarrierSelectionItem>(box.DataContext).MarketId
                        is 3_700_000_002
                            or 3_700_000_004
                            or 3_700_000_005
                            or 3_700_000_006
                )
            )
            {
                ClickCarrierControl(box);
                Assert.False(box.IsChecked);
                Assert.True(flyout.IsOpen);
            }
            CheckBox first = boxes.Single(box =>
                Assert.IsType<ColonizationCarrierSelectionItem>(box.DataContext).MarketId == 3_700_000_001
            );
            first.Focus();
            TopLevel popup = TopLevel.GetTopLevel(first)!;
            popup.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            popup.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.Equal(2, selection.SelectedCount);
            Assert.True(flyout.IsOpen);
            popup.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            popup.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.Equal(new long[] { 3_700_000_001, 3_700_000_003, 3_700_000_007 }, selection.CaptureSelectedIds());
            Assert.Equal("3 carriers selected", picker.FindControl<TextBlock>("CarrierSelectionNote")!.Text);
            Assert.Contains(
                content.GetVisualDescendants().OfType<TextBlock>(),
                label => label.Text == "3 carriers selected"
            );
            using WriteableBitmap? openFrame = popup.CaptureRenderedFrame();
            Assert.NotNull(openFrame);
            openFrame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-carrier-checkboxes-open.png"),
                PngBitmapEncoderOptions.Default
            );
            popup.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(flyout.IsOpen);
            using WriteableBitmap? closedFrame = window.CaptureRenderedFrame();
            Assert.NotNull(closedFrame);
            closedFrame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-carrier-checkboxes-selected.png"),
                PngBitmapEncoderOptions.Default
            );
            ClickCarrierControl(button);
            Assert.True(flyout.IsOpen);
            Assert.Equal(3, content.GetVisualDescendants().OfType<CheckBox>().Count(box => box.IsChecked == true));
            flyout.Hide();
        }
        finally
        {
            window.Close();
        }
    }

    private static void ClickCarrierControl(Control control)
    {
        TopLevel top = TopLevel.GetTopLevel(control)!;
        using WriteableBitmap? frame = top.CaptureRenderedFrame();
        Point point = control
            .TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!
            .Value;
        top.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        top.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }
}
