using Avalonia.Media;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class OverlayThemeColorEditorViewModelTests
{
    [Fact]
    public void PickerColorUpdatesLegacyHexAndClearsValidation()
    {
        var changeCount = 0;
        var editor = CreateEditor(() => changeCount++);
        editor.HexValue = "not-a-colour";

        editor.Color = Color.FromArgb(128, 1, 2, 3);

        Assert.Equal("#01020380", editor.HexValue);
        Assert.Equal(Color.FromArgb(128, 1, 2, 3), editor.Color);
        Assert.False(editor.HasValidationError);
        Assert.True(editor.IsDirty);
        Assert.Equal(2, changeCount);
    }

    [Fact]
    public void HexEntryUpdatesPickerColorUsingTrailingAlpha()
    {
        var editor = CreateEditor(() => { });

        editor.HexValue = "#A1B2C340";

        Assert.Equal(Color.FromArgb(64, 161, 178, 195), editor.Color);
        Assert.False(editor.HasValidationError);
    }

    private static OverlayThemeColorEditorViewModel CreateEditor(Action changed)
    {
        return new OverlayThemeColorEditorViewModel(
            new OverlayThemeColorDefinition("Test", "test", "Test colour"),
            Color.FromRgb(10, 20, 30),
            changed);
    }
}
