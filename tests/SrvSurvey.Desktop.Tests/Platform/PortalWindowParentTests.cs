using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class PortalWindowParentTests
{
    /// <summary>X11 handles use hexadecimal XIDs, including on a Wayland desktop running XWayland.</summary>
    [Theory]
    [InlineData(0x1a2b, "XID", "x11:1a2b")]
    [InlineData(0, "XID", "")]
    [InlineData(0x1a2b, "HWND", "")]
    [InlineData(0x1a2b, "wl_surface", "")]
    public void FormatsOnlySupportedNativeHandles(int value, string descriptor, string expected)
    {
        Assert.Equal(expected, PortalWindowParent.FormatHandle(new PlatformHandle((nint)value, descriptor)));
        Assert.Empty(PortalWindowParent.FormatHandle(null));
    }

    /// <summary>An active child dialog is preferred, with a visible main window as the fallback.</summary>
    [AvaloniaFact]
    public void ChoosesTheActiveWindowAndIgnoresHiddenOrClosedMainWindows()
    {
        var main = new Window();
        var child = new Window();
        try
        {
            Assert.Null(PortalWindowParent.FindOwner([], main));
            main.Show();
            Assert.Same(main, PortalWindowParent.FindOwner([], main));
            child.Show(main);
            child.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.True(child.IsActive);
            Assert.Same(child, PortalWindowParent.FindOwner([child], main));
            child.Hide();
            Assert.Same(main, PortalWindowParent.FindOwner([child], main));
            main.Hide();
            Assert.Null(PortalWindowParent.FindOwner([main, child], main));
        }
        finally
        {
            child.Close();
            main.Close();
        }
        Assert.Null(PortalWindowParent.FindOwner([main, child], main));
        Assert.Null(PortalWindowParent.FindOwner([], null));
    }

    /// <summary>A worker requests the identifier while only the small native-handle read runs on the dispatcher.</summary>
    [AvaloniaFact]
    public async Task ReadsHandlesOnTheDispatcherFromWorkersAndUiCallers()
    {
        int reads = 0;
        IPlatformHandle GetHandle()
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            reads++;
            return new PlatformHandle((nint)0x1234, "XID");
        }

        Assert.Equal("x11:1234", await PortalWindowParent.ResolveAsync(GetHandle, CancellationToken.None));
        Assert.Equal(
            "x11:1234",
            await Task.Run(() => PortalWindowParent.ResolveAsync(GetHandle, CancellationToken.None))
        );
        Assert.Equal(2, reads);
    }

    /// <summary>Shutdown cancellation never reads a native window after its lifetime has ended.</summary>
    [AvaloniaFact]
    public async Task CancellationPreventsTheWindowRead()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PortalWindowParent.ResolveAsync(
                () => throw new InvalidOperationException("Must not read"),
                cancellation.Token
            )
        );
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PortalWindowParent.GetAsync(cancellation.Token));
    }

    /// <summary>Non-desktop contexts have no suitable owner and do not need a dispatcher round trip.</summary>
    [AvaloniaFact]
    public async Task MissingDesktopLifetimeUsesThePortalFallback()
    {
        Assert.Null(Application.Current!.ApplicationLifetime);
        Assert.Empty(await PortalWindowParent.GetAsync(CancellationToken.None));
        Assert.Empty(await Task.Run(() => PortalWindowParent.GetAsync(CancellationToken.None)));
    }
}
