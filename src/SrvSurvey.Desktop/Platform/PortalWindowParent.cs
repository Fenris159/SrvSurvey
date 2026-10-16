using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SrvSurvey.Desktop.Platform;

/// <summary>Associates desktop portal dialogs with the application window that initiated them.</summary>
internal static class PortalWindowParent
{
    /// <summary>Reads the current owner on the dispatcher without moving portal operations onto the UI thread.</summary>
    public static Task<string> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return Task.FromResult(string.Empty);
        }

        return ResolveAsync(
            () => FindOwner(desktop.Windows, desktop.MainWindow)?.TryGetPlatformHandle(),
            cancellationToken
        );
    }

    /// <summary>Prefers the active application window and otherwise uses the visible main window.</summary>
    internal static Window? FindOwner(IReadOnlyList<Window> windows, Window? mainWindow) =>
        windows.FirstOrDefault(window => window.IsVisible && window.IsActive)
        ?? (mainWindow?.IsVisible == true ? mainWindow : null);

    /// <summary>Obtains a native handle only on the dispatcher, including when requested by a capture worker.</summary>
    internal static async Task<string> ResolveAsync(
        Func<IPlatformHandle?> getHandle,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess())
        {
            return FormatHandle(getHandle());
        }

        return await Dispatcher
            .UIThread.InvokeAsync(() => FormatHandle(getHandle()), DispatcherPriority.Normal, cancellationToken)
            .GetTask()
            .ConfigureAwait(false);
    }

    /// <summary>Formats the X11/XWayland handles used by this desktop backend; other handles are not exported IDs.</summary>
    internal static string FormatHandle(IPlatformHandle? handle) =>
        handle is { HandleDescriptor: "XID" } && handle.Handle != nint.Zero
            ? "x11:" + handle.Handle.ToString("x", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
}
