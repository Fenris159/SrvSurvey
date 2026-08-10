using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OverlayWindowRegistry
{
    private readonly ConditionalWeakTable<Window, Registration> registrations =
        new();
    private readonly List<WeakReference<Window>> windows = [];
    private bool galaxyMapContextActive;

    public static OverlayWindowRegistry Shared { get; } = new();

    public event EventHandler? Changed;

    public bool IsGalaxyMapContextActive => galaxyMapContextActive;

    public void Register(Window window, string plotterName)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        _ = OverlayLayoutCatalog.GetRequired(plotterName);
        if (registrations.TryGetValue(window, out var existing))
        {
            if (!string.Equals(
                    existing.PlotterName,
                    plotterName,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{window.GetType().Name} is already registered as "
                        + $"'{existing.PlotterName}', not '{plotterName}'.");
            }

            return;
        }

        EventHandler opened = (_, _) => SuppressOpenedWindowForGalaxyMap(window);
        EventHandler closed = (_, _) => Unregister(window);
        registrations.Add(
            window,
            new Registration(plotterName, opened, closed));
        windows.Add(new WeakReference<Window>(window));
        window.Opened += opened;
        window.Closed += closed;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetGalaxyMapContextActive(bool active)
    {
        if (galaxyMapContextActive == active)
        {
            return;
        }

        galaxyMapContextActive = active;
        foreach (var window in GetRegisteredWindows())
        {
            if (registrations.TryGetValue(window, out var registration))
            {
                ApplyGalaxyMapContext(window, registration, active);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyGalaxyMapContext(
        Window window,
        Registration registration,
        bool active)
    {
        if (registration.PresentationVisual is not null)
        {
            registration.PresentationVisible = ResolvePresentationVisibility(
                registration.PlotterName,
                registration.RequestedPresentationVisible,
                galaxyMapContextActive);
            return;
        }

        if (active)
        {
            SuppressSeparateWindowForGalaxyMap(window, registration);
            return;
        }

        RestoreSeparateWindowAfterGalaxyMap(window, registration);
    }

    private void SuppressSeparateWindowForGalaxyMap(
        Window window,
        Registration registration)
    {
        if (ShouldPresent(registration.PlotterName) || !window.IsVisible)
        {
            return;
        }

        registration.RestoreAfterGalaxyMap = true;
        window.Hide();
    }

    private static void RestoreSeparateWindowAfterGalaxyMap(
        Window window,
        Registration registration)
    {
        if (!registration.RestoreAfterGalaxyMap)
        {
            return;
        }

        registration.RestoreAfterGalaxyMap = false;
        try
        {
            window.Show();
        }
        catch (InvalidOperationException)
        {
            // The owning coordinator closed the panel while the map was open.
        }
    }

    public IReadOnlyList<RegisteredOverlayWindow> Snapshot()
    {
        var result = new List<RegisteredOverlayWindow>(windows.Count);
        for (var index = windows.Count - 1; index >= 0; index--)
        {
            if (!windows[index].TryGetTarget(out var window)
                || !registrations.TryGetValue(window, out var registration))
            {
                windows.RemoveAt(index);
                continue;
            }

            result.Add(new RegisteredOverlayWindow(
                window,
                registration.PlotterName,
                registration.PresentationVisual,
                registration.PresentationVisual is null
                    ? window.IsVisible
                    : registration.PresentationVisible));
        }

        result.Reverse();
        return result;
    }

    internal bool TryGetPlotterName(Window window, out string plotterName)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (registrations.TryGetValue(window, out var registration))
        {
            plotterName = registration.PlotterName;
            return true;
        }

        plotterName = string.Empty;
        return false;
    }

    internal bool ShouldPresent(string plotterName)
    {
        return ShouldPresentInContext(plotterName, galaxyMapContextActive);
    }

    internal static bool ShouldPresentInContext(
        string plotterName,
        bool galaxyMapActive)
    {
        return !galaxyMapActive
            || OverlayLayoutCatalog.GetRequired(plotterName).ShowInGalaxyMap;
    }

    internal static bool ResolvePresentationVisibility(
        string plotterName,
        bool requestedVisibility,
        bool galaxyMapActive)
    {
        return requestedVisibility
            && ShouldPresentInContext(plotterName, galaxyMapActive);
    }

    internal void SetPresentationVisual(
        Window window,
        Visual? presentationVisual)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!registrations.TryGetValue(window, out var registration))
        {
            return;
        }

        registration.PresentationVisual = presentationVisual;
        registration.RequestedPresentationVisible = presentationVisual is not null;
        registration.PresentationVisible = ResolvePresentationVisibility(
            registration.PlotterName,
            presentationVisual is not null,
            galaxyMapContextActive);
        if (presentationVisual is not null)
        {
            registration.RestoreAfterGalaxyMap = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void SetPresentationVisible(Window window, bool visible)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!registrations.TryGetValue(window, out var registration)
            || registration.PresentationVisual is null)
        {
            return;
        }

        registration.RequestedPresentationVisible = visible;
        var effectiveVisibility = ResolvePresentationVisibility(
            registration.PlotterName,
            visible,
            galaxyMapContextActive);
        if (registration.PresentationVisible == effectiveVisibility)
        {
            return;
        }

        registration.PresentationVisible = effectiveVisibility;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SuppressOpenedWindowForGalaxyMap(Window window)
    {
        if (!galaxyMapContextActive
            || !registrations.TryGetValue(window, out var registration)
            || ShouldPresent(registration.PlotterName)
            || registration.PresentationVisual is not null)
        {
            return;
        }

        registration.RestoreAfterGalaxyMap = true;
        window.Hide();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<Window> GetRegisteredWindows()
    {
        var result = new List<Window>(windows.Count);
        for (var index = windows.Count - 1; index >= 0; index--)
        {
            if (!windows[index].TryGetTarget(out var window)
                || !registrations.TryGetValue(window, out _))
            {
                windows.RemoveAt(index);
                continue;
            }

            result.Add(window);
        }

        return result;
    }

    private void Unregister(Window window)
    {
        if (!registrations.TryGetValue(window, out var registration))
        {
            return;
        }

        window.Opened -= registration.OpenedHandler;
        window.Closed -= registration.ClosedHandler;
        registrations.Remove(window);
        for (var index = windows.Count - 1; index >= 0; index--)
        {
            if (!windows[index].TryGetTarget(out var candidate)
                || ReferenceEquals(candidate, window))
            {
                windows.RemoveAt(index);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Registration(
        string plotterName,
        EventHandler openedHandler,
        EventHandler closedHandler)
    {
        public string PlotterName { get; } = plotterName;

        public EventHandler OpenedHandler { get; } = openedHandler;

        public EventHandler ClosedHandler { get; } = closedHandler;

        public Visual? PresentationVisual { get; set; }

        public bool PresentationVisible { get; set; }

        public bool RequestedPresentationVisible { get; set; }

        public bool RestoreAfterGalaxyMap { get; set; }
    }
}

public sealed record RegisteredOverlayWindow(
    Window Window,
    string PlotterName,
    Visual? PresentationVisual = null,
    bool IsVisible = false)
{
    public Visual RenderSource => PresentationVisual ?? Window;
}

internal static class OverlayWindowMetrics
{
    public static PixelSize GetPixelSize(RegisteredOverlayWindow registered)
    {
        ArgumentNullException.ThrowIfNull(registered);
        var fallback = OverlayLayoutCatalog
            .GetRequired(registered.PlotterName)
            .PreviewSize;
        var scaling = Math.Max(0.1, registered.Window.RenderScaling);
        var presentation = registered.PresentationVisual;
        var logicalWidth = presentation is not null
            && presentation.Bounds.Width > 0
            ? presentation.Bounds.Width
            : registered.Window.Bounds.Width;
        if (!(logicalWidth > 0))
        {
            logicalWidth = registered.Window.Width;
        }

        var logicalHeight = presentation is not null
            && presentation.Bounds.Height > 0
            ? presentation.Bounds.Height
            : registered.Window.Bounds.Height;
        if (!(logicalHeight > 0))
        {
            logicalHeight = registered.Window.Height;
        }

        var width = double.IsFinite(logicalWidth) && logicalWidth > 0
            ? (int)Math.Ceiling(logicalWidth * scaling)
            : fallback.Width;
        var height = double.IsFinite(logicalHeight) && logicalHeight > 0
            ? (int)Math.Ceiling(logicalHeight * scaling)
            : fallback.Height;
        return new PixelSize(Math.Max(width, 1), Math.Max(height, 1));
    }
}
