using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OverlayWindowRegistry
{
    private readonly ConditionalWeakTable<Window, Registration> registrations =
        new();
    private readonly List<WeakReference<Window>> windows = [];

    public static OverlayWindowRegistry Shared { get; } = new();

    public event EventHandler? Changed;

    public void Register(Window window, string plotterName)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        if (registrations.TryGetValue(window, out _))
        {
            return;
        }

        EventHandler closed = (_, _) => Unregister(window);
        registrations.Add(window, new Registration(plotterName, closed));
        windows.Add(new WeakReference<Window>(window));
        window.Closed += closed;
        Changed?.Invoke(this, EventArgs.Empty);
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
        registration.PresentationVisible = presentationVisual is not null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void SetPresentationVisible(Window window, bool visible)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!registrations.TryGetValue(window, out var registration)
            || registration.PresentationVisual is null
            || registration.PresentationVisible == visible)
        {
            return;
        }

        registration.PresentationVisible = visible;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Unregister(Window window)
    {
        if (!registrations.TryGetValue(window, out var registration))
        {
            return;
        }

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
        EventHandler closedHandler)
    {
        public string PlotterName { get; } = plotterName;

        public EventHandler ClosedHandler { get; } = closedHandler;

        public Visual? PresentationVisual { get; set; }

        public bool PresentationVisible { get; set; }
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
