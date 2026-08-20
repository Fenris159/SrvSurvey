using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

public sealed class OverlayWindowRegistry
{
    private readonly ConditionalWeakTable<Window, Registration> registrations =
        new();
    private readonly List<WeakReference<Window>> windows = [];
    private readonly HashSet<string> userHiddenPlotters =
        new(StringComparer.Ordinal);
    private bool galaxyMapContextActive;
    private bool editorSuppressed;
    private bool manualSuppressed;
    private bool suitSuppressed;
    private bool sessionSuppressed;
    private OverlayPriorityFact priorityFacts;
    private bool isReconciling;
    private bool reconcileAgain;
    private bool changePending;

    public static OverlayWindowRegistry Shared { get; } = new();

    public event EventHandler? Changed;

    public bool IsGalaxyMapContextActive => galaxyMapContextActive;

    public void Register(Window window, string plotterName)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        var definition = OverlayLayoutCatalog.GetRequired(plotterName);
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

        EventHandler opened = (_, _) => OnWindowOpened(window);
        EventHandler closed = (_, _) => Unregister(window);
        var facts = CreateFacts(definition, requested: false);
        registrations.Add(
            window,
            new Registration(
                definition.Id,
                plotterName,
                opened,
                closed,
                facts,
                OverlayVisibilityPolicy.Evaluate(facts)));
        windows.Add(new WeakReference<Window>(window));
        window.Opened += opened;
        window.Closed += closed;
        ReconcileAndNotify();
    }

    public void SetGalaxyMapContextActive(bool active)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (galaxyMapContextActive == active)
        {
            return;
        }

        galaxyMapContextActive = active;
        ReconcileAndNotify();
    }

    public bool IsUserVisible(string plotterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        _ = OverlayLayoutCatalog.GetRequired(plotterName);
        return !userHiddenPlotters.Contains(plotterName);
    }

    public void SetUserVisibility(string plotterName, bool visible)
    {
        VerifyAccessWhenWindowsAreRegistered();
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        _ = OverlayLayoutCatalog.GetRequired(plotterName);
        var changed = visible
            ? userHiddenPlotters.Remove(plotterName)
            : userHiddenPlotters.Add(plotterName);
        if (!changed)
        {
            return;
        }

        ReconcileAndNotify();
    }

    internal void SetEditorSuppressed(bool suppressed)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (editorSuppressed == suppressed)
        {
            return;
        }

        editorSuppressed = suppressed;
        ReconcileAndNotify();
    }

    internal void SetGlobalSuppression(
        bool manualSuppressed,
        bool suitSuppressed,
        bool sessionSuppressed)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (this.manualSuppressed == manualSuppressed
            && this.suitSuppressed == suitSuppressed
            && this.sessionSuppressed == sessionSuppressed)
        {
            return;
        }

        this.manualSuppressed = manualSuppressed;
        this.suitSuppressed = suitSuppressed;
        this.sessionSuppressed = sessionSuppressed;
        ReconcileAndNotify();
    }

    internal void SetPriorityFacts(OverlayPriorityFact facts)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (priorityFacts == facts)
        {
            return;
        }

        priorityFacts = facts;
        ReconcileAndNotify();
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
                registration.Presented));
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
        var definition = OverlayLayoutCatalog.GetRequired(plotterName);
        return OverlayVisibilityPolicy.Evaluate(
            CreateFacts(definition, requested: true)).ShouldPresent;
    }

    internal bool ShouldPresent(Window window) => GetDecision(window).ShouldPresent;

    internal bool ShouldHost(string plotterName)
    {
        var definition = OverlayLayoutCatalog.GetRequired(plotterName);
        return OverlayVisibilityPolicy.Evaluate(
            CreateFacts(definition, requested: true)).ShouldHost;
    }

    internal OverlayVisibilityDecision GetDecision(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return registrations.TryGetValue(window, out var registration)
            ? registration.Decision
            : throw new InvalidOperationException(
                $"{window.GetType().Name} is not registered as an overlay.");
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
        bool galaxyMapActive,
        bool userVisible = true)
    {
        return requestedVisibility
            && userVisible
            && ShouldPresentInContext(plotterName, galaxyMapActive);
    }

    internal void SetPresentationVisual(
        Window window,
        Visual? presentationVisual)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(window);
        if (!registrations.TryGetValue(window, out var registration))
        {
            return;
        }

        registration.PresentationVisual = presentationVisual;
        registration.Facts = registration.Facts with
        {
            Requested = presentationVisual is not null,
        };
        ReconcileAndNotify();
    }

    internal void SetPresentationVisible(Window window, bool visible)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(window);
        if (!registrations.TryGetValue(window, out var registration)
            || registration.PresentationVisual is null)
        {
            return;
        }

        if (registration.Facts.Requested == visible)
        {
            return;
        }

        registration.Facts = registration.Facts with { Requested = visible };
        ReconcileAndNotify();
    }

    private void OnWindowOpened(Window window)
    {
        if (!registrations.TryGetValue(window, out var registration))
        {
            return;
        }

        registration.Facts = registration.Facts with { Requested = true };
        ReconcileAndNotify();
    }

    private OverlayVisibilityFacts CreateFacts(
        OverlayLayoutDefinition definition,
        bool requested)
    {
        return new OverlayVisibilityFacts(
            Requested: requested,
            HostEligible: true,
            UserEnabled: IsUserVisible(definition.Name),
            GalaxyMapAllowed: !galaxyMapContextActive
                || definition.ShowInGalaxyMap,
            EditorSuppressed: editorSuppressed,
            ManualSuppressed: manualSuppressed,
            SuitSuppressed: suitSuppressed,
            SessionSuppressed: sessionSuppressed,
            PriorityObscured: OverlayPriorityRules.IsObscured(
                definition.Id,
                IsAnyPresented,
                priorityFacts));
    }

    private bool IsAnyPresented(OverlayId id)
    {
        return GetRegisteredWindows().Any(window =>
            registrations.TryGetValue(window, out var registration)
            && registration.Id == id
            && registration.Presented);
    }

    private void ReconcileAndNotify()
    {
        changePending = true;
        if (isReconciling)
        {
            reconcileAgain = true;
            return;
        }

        isReconciling = true;
        try
        {
            do
            {
                reconcileAgain = false;
                ReconcileCore();
            }
            while (reconcileAgain);
        }
        finally
        {
            isReconciling = false;
        }

        if (changePending)
        {
            changePending = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReconcileCore()
    {
        var entries = new List<(Window Window, Registration Registration)>();
        foreach (var window in GetRegisteredWindows())
        {
            if (registrations.TryGetValue(window, out var registration))
            {
                entries.Add((window, registration));
            }
        }
        for (var pass = 0; pass <= entries.Count; pass++)
        {
            var previousPresented = entries.Select(entry =>
                entry.Registration.Presented).ToArray();
            foreach (var entry in entries)
            {
                var definition = OverlayLayoutCatalog.GetRequired(
                    entry.Registration.Id);
                entry.Registration.Facts = CreateFacts(
                    definition,
                    entry.Registration.Facts.Requested);
                entry.Registration.Decision = OverlayVisibilityPolicy.Evaluate(
                    entry.Registration.Facts);
            }

            foreach (var entry in entries.Where(entry =>
                         !entry.Registration.Decision.ShouldPresent))
            {
                ApplyVisibility(
                    entry.Window,
                    entry.Registration,
                    visible: false);
            }

            foreach (var entry in entries.Where(entry =>
                         entry.Registration.Decision.ShouldPresent))
            {
                ApplyVisibility(
                    entry.Window,
                    entry.Registration,
                    visible: true);
            }

            if (entries.Select(entry => entry.Registration.Presented)
                .SequenceEqual(previousPresented))
            {
                break;
            }
        }
    }

    private static void ApplyVisibility(
        Window window,
        Registration registration,
        bool visible)
    {
        if (registration.PresentationVisual is not null)
        {
            registration.PresentationVisible = visible;
            registration.Presented = visible;
            return;
        }

        if (window.IsVisible == visible)
        {
            registration.Presented = window.IsVisible;
            return;
        }

        try
        {
            if (visible)
            {
                window.Show();
            }
            else
            {
                window.Hide();
            }
        }
        catch (InvalidOperationException)
        {
            // The owning coordinator closed the panel during reconciliation.
        }

        registration.Presented = window.IsVisible;
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

    private void VerifyAccessWhenWindowsAreRegistered()
    {
        if (windows.Count > 0)
        {
            Dispatcher.UIThread.VerifyAccess();
        }
    }

    private void Unregister(Window window)
    {
        Dispatcher.UIThread.VerifyAccess();
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

        ReconcileAndNotify();
    }

    private sealed class Registration(
        OverlayId id,
        string plotterName,
        EventHandler openedHandler,
        EventHandler closedHandler,
        OverlayVisibilityFacts facts,
        OverlayVisibilityDecision decision)
    {
        public OverlayId Id { get; } = id;

        public string PlotterName { get; } = plotterName;

        public EventHandler OpenedHandler { get; } = openedHandler;

        public EventHandler ClosedHandler { get; } = closedHandler;

        public Visual? PresentationVisual { get; set; }

        public bool PresentationVisible { get; set; }

        public bool Presented { get; set; }

        public OverlayVisibilityFacts Facts { get; set; } = facts;

        public OverlayVisibilityDecision Decision { get; set; } = decision;
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
    public static PixelSize PrepareForPlacement(
        Window window,
        LegacyOverlayLayout layout,
        string plotterName,
        double targetScaling)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentException.ThrowIfNullOrWhiteSpace(plotterName);
        var definition = OverlayLayoutCatalog.GetRequired(plotterName);
        var scaling = NormalizeScaling(targetScaling);
        var scaleIndex = layout.GetScaleIndex(plotterName);
        OverlayThemeResources.ApplyScale(window, scaleIndex, scaling);
        var fallbackScale = scaling * OverlayScaleCatalog.GetRelativeScale(
            scaleIndex,
            scaling);
        var fallback = ScalePixelSize(definition.PreviewSize, fallbackScale);
        return GetPixelSize(window, fallback, scaling);
    }

    public static PixelSize GetPixelSize(RegisteredOverlayWindow registered)
    {
        ArgumentNullException.ThrowIfNull(registered);
        var scaling = NormalizeScaling(registered.Window.RenderScaling);
        var previewSize = OverlayLayoutCatalog
            .GetRequired(registered.PlotterName)
            .PreviewSize;
        var fallback = ScalePixelSize(previewSize, scaling);
        return GetPixelSize(
            registered.Window,
            fallback,
            scaling,
            registered.PresentationVisual);
    }

    private static PixelSize ScalePixelSize(PixelSize size, double scaling) =>
        new(
            Math.Max(1, (int)Math.Ceiling(size.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(size.Height * scaling)));

    private static PixelSize GetPixelSize(
        Window window,
        PixelSize fallback,
        double scaling,
        Visual? presentation = null)
    {
        var logicalWidth = presentation is not null
            && presentation.Bounds.Width > 0
            ? presentation.Bounds.Width
            : window.Bounds.Width;
        if (!(logicalWidth > 0))
        {
            logicalWidth = window.Width;
        }

        var logicalHeight = presentation is not null
            && presentation.Bounds.Height > 0
            ? presentation.Bounds.Height
            : window.Bounds.Height;
        if (!(logicalHeight > 0))
        {
            logicalHeight = window.Height;
        }

        var width = double.IsFinite(logicalWidth) && logicalWidth > 0
            ? (int)Math.Ceiling(logicalWidth * scaling)
            : fallback.Width;
        var height = double.IsFinite(logicalHeight) && logicalHeight > 0
            ? (int)Math.Ceiling(logicalHeight * scaling)
            : fallback.Height;
        return new PixelSize(Math.Max(width, 1), Math.Max(height, 1));
    }

    private static double NormalizeScaling(double scaling) =>
        double.IsFinite(scaling) && scaling > 0 ? scaling : 1d;
}
