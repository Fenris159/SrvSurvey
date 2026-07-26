using System.Runtime.CompilerServices;
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
                registration.PlotterName));
        }

        result.Reverse();
        return result;
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

    private sealed record Registration(
        string PlotterName,
        EventHandler ClosedHandler);
}

public sealed record RegisteredOverlayWindow(
    Window Window,
    string PlotterName);
