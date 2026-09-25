using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayEditorControlsHeightInputTests
{
    [AvaloniaFact]
    public void EditorHeightEntryIsAsCompactAsNearbyShortcutEntry()
    {
        var settings = new OverlaySettingsView();
        var window = new Window
        {
            Content = settings,
            Width = 1200,
            Height = 900,
        };
        try
        {
            window.Show();
            OverlayEditorHeightEntry entry = Assert.IsType<OverlayEditorHeightEntry>(
                settings.FindControl<OverlayEditorHeightEntry>("EditorControlsHeightInput")
            );
            ShortcutCaptureBox shortcut = settings.GetVisualDescendants().OfType<ShortcutCaptureBox>().First();

            Assert.True(
                entry.Bounds.Height <= shortcut.Bounds.Height + 4,
                $"Editor entry height {entry.Bounds.Height} exceeds shortcut height {shortcut.Bounds.Height}."
            );
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UnsubmittedHeightIsDiscardedWhenFocusMovesAway()
    {
        var input = new OverlayEditorHeightEntry { Value = 0 };
        var other = new Button { Content = "Other control" };
        var panel = new StackPanel();
        panel.Children.Add(input);
        panel.Children.Add(other);
        var window = new Window { Content = panel };
        try
        {
            window.Show();
            Assert.True(input.Focus());
            input.Text = "12.3";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(12.3, input.Value);

            input.Text = "-4.5";
            Assert.True(other.Focus());
            Assert.True(other.IsFocused);
            Assert.Equal(12.3, input.Value);
            Assert.Equal("12.3", input.Text);

            Assert.True(input.Focus());
            input.Text = "not a number";
            Assert.True(other.Focus());
            Assert.Equal("12.3", input.Text);

            Assert.True(input.Focus());
            input.Text = "NaN";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(12.3, input.Value);
            Assert.Equal("12.3", input.Text);

            input.Text = "99";
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(12.3, input.Value);
            Assert.Equal("12.3", input.Text);
        }
        finally
        {
            window.Close();
        }
    }
}
