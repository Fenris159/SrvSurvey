using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayEditorControlsHeightInputTests
{
    [AvaloniaFact]
    public void NumericHeightCanBeEnteredAndFocusCanMoveAway()
    {
        var input = new NumericUpDown
        {
            Minimum = -100,
            Maximum = 100,
            Increment = 0.1m,
            FormatString = "0.0",
            ShowButtonSpinner = false,
            Value = 0,
        };
        var other = new Button { Content = "Other control" };
        var panel = new StackPanel();
        panel.Children.Add(input);
        panel.Children.Add(other);
        var window = new Window { Content = panel };
        try
        {
            window.Show();
            TextBox entry = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(entry.Focus());
            entry.Text = "12.3";
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(12.3m, input.Value);

            entry.Text = "-4.5";
            Assert.True(other.Focus());
            Assert.True(other.IsFocused);
            Assert.Equal(-4.5m, input.Value);
        }
        finally
        {
            window.Close();
        }
    }
}
