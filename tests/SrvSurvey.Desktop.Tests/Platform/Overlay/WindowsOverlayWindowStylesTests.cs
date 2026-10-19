using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.Tests.Platform.Overlay;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class WindowsOverlayWindowStylesTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OnlyCaptureWindowsOmitTheToolWindowFlagThatObsExcludes(bool capture, bool interactive)
    {
        Window window = capture ? new StreamOverlayWindow() : new Window();
        const long toolWindow = 0x00000080;
        const long transparent = 0x00000020;
        const long layered = 0x00080000;
        const long noActivate = 0x08000000;
        const long unrelatedStyle = 0x00000200;
        nint updated = WindowsOverlayWindowStyles.Apply(
            window,
            (nint)(toolWindow | transparent | noActivate | unrelatedStyle),
            interactive
        );

        Assert.Equal(!capture, ((long)updated & toolWindow) != 0);
        Assert.Equal(!interactive, ((long)updated & transparent) != 0);
        Assert.Equal(!interactive, ((long)updated & noActivate) != 0);
        Assert.NotEqual(0, (long)updated & layered);
        Assert.NotEqual(0, (long)updated & unrelatedStyle);
    }
}
