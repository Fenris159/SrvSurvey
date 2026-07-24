using SharpHook.Data;
using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class KeyboardChordFormatterTests
{
    [Fact]
    public void FormatsLegacyModifierOrderAndSpecialKeys()
    {
        Assert.Equal(
            "ALT CTRL SHIFT Backspace",
            KeyboardChordFormatter.Format(
                KeyCode.VcBackspace,
                EventMask.LeftCtrl
                    | EventMask.RightAlt
                    | EventMask.LeftShift));
        Assert.Equal(
            "CTRL +",
            KeyboardChordFormatter.Format(
                KeyCode.VcEquals,
                EventMask.LeftCtrl));
        Assert.Equal("D1", KeyboardChordFormatter.GetKeyName(KeyCode.Vc1));
    }

    [Theory]
    [InlineData(KeyCode.VcLeftAlt)]
    [InlineData(KeyCode.VcRightControl)]
    [InlineData(KeyCode.VcLeftShift)]
    [InlineData(KeyCode.VcUndefined)]
    public void IgnoresStandaloneModifiers(KeyCode keyCode)
    {
        Assert.Null(KeyboardChordFormatter.Format(keyCode, EventMask.None));
    }
}
