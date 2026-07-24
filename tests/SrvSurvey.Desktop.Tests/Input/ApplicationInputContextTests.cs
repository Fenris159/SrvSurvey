using SrvSurvey.Desktop.Input;

namespace SrvSurvey.Desktop.Tests.Input;

public sealed class ApplicationInputContextTests
{
    [Fact]
    public void SuspendsShortcutsWhileTextInputHasFocus()
    {
        var context = new ApplicationInputContext();

        context.SetActive(true);
        Assert.True(context.AreShortcutsActive);

        context.SetTextInputActive(true);
        Assert.False(context.AreShortcutsActive);

        context.SetTextInputActive(false);
        Assert.True(context.AreShortcutsActive);

        context.SetActive(false);
        Assert.False(context.AreShortcutsActive);
    }
}
