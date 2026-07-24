using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class InputChordTests
{
    [Theory]
    [InlineData("ctrl alt s", "ALT CTRL S")]
    [InlineData("SHIFT ctrl Backspace", "CTRL SHIFT Backspace")]
    [InlineData("b10 b2", "B10 B2")]
    [InlineData("povur b1", "B1 PovUR")]
    [InlineData("rt lt", "LT RT")]
    public void NormalizesLegacyKeyboardAndControllerChords(
        string value,
        string expected)
    {
        Assert.True(InputChord.TryNormalize(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CTRL")]
    [InlineData("CTRL A B")]
    [InlineData("B129")]
    [InlineData("B1 B1")]
    public void RejectsDisabledOrInvalidChords(string? value)
    {
        Assert.False(InputChord.TryNormalize(value, out _));
    }
}
