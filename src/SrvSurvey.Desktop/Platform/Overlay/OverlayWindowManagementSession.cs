using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Applies one startup window-management preference to live panels and position-editor windows.</summary>
internal sealed class OverlayWindowManagementSession : IDisposable
{
    private readonly OverlayWindowRegistry registry;
    private readonly IOverlayWindowManagement native;
    private readonly Action<string>? log;
    private readonly HashSet<Window> windows = [];
    private bool disposed;

    /// <summary>Observes window preparation before mapping; the caller owns the choice for this session.</summary>
    internal OverlayWindowManagementSession(
        OverlayWindowRegistry registry,
        IOverlayWindowManagement native,
        Action<string>? log = null
    )
    {
        this.registry = registry;
        this.native = native;
        this.log = log;
        registry.WindowPreparing += PrepareWindow;
    }

    /// <summary>Attempts bypass once before showing an overlay or editor window, retaining management on failure.</summary>
    private void PrepareWindow(Window window)
    {
        if (windows.Contains(window))
        {
            return;
        }

        if (!native.TryBypassWindowManagement(window))
        {
            log?.Invoke(
                $"Window management bypass could not be applied to {window.GetType().Name}; using normal management."
            );
            return;
        }

        windows.Add(window);
        window.Opened += RaiseWindow;
        window.PositionChanged += RaiseMovedWindow;
        window.Closed += ReleaseWindow;
    }

    /// <summary>Raises shown windows and honors editor activation while passive panels retain game focus.</summary>
    private void RaiseWindow(object? sender, EventArgs args)
    {
        if (sender is Window window)
        {
            native.RaiseUnmanagedWindow(window, activate: window.ShowActivated);
        }
    }

    /// <summary>Raises moved panels and editor windows without changing coordinates, focus, or monitor-lock policy.</summary>
    private void RaiseMovedWindow(object? sender, PixelPointEventArgs args)
    {
        if (sender is Window { IsVisible: true } window)
        {
            native.RaiseUnmanagedWindow(window);
        }
    }

    /// <summary>Releases native-lifecycle observers when a panel or editor window closes.</summary>
    private void ReleaseWindow(object? sender, EventArgs args)
    {
        if (sender is Window window)
        {
            DetachWindow(window);
        }
    }

    /// <summary>Removes observers without mapping, closing, or moving the panel.</summary>
    private void DetachWindow(Window window)
    {
        windows.Remove(window);
        window.Opened -= RaiseWindow;
        window.PositionChanged -= RaiseMovedWindow;
        window.Closed -= ReleaseWindow;
    }

    /// <summary>Stops observing overlay and editor windows and disposes the owned native connection.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        registry.WindowPreparing -= PrepareWindow;
        foreach (Window window in windows.ToArray())
        {
            DetachWindow(window);
        }
        native.Dispose();
    }
}

/// <summary>Native operations for unmanaged overlay windows; unsupported backends do not implement this contract.</summary>
internal interface IOverlayWindowManagement : IDisposable
{
    /// <summary>Sets override-redirect only while the native window is still unmapped.</summary>
    bool TryBypassWindowManagement(Window window);

    /// <summary>Raises an unmanaged window, optionally honoring editor activation, without remapping a hidden panel.</summary>
    void RaiseUnmanagedWindow(Window window, bool activate = false);
}
