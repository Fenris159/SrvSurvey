using Avalonia;
using Avalonia.Controls;

namespace SrvSurvey.Desktop.Platform.Overlay;

internal interface IOverlayDragPointerProbe : IDisposable
{
    OverlayDragPointerSample? Read();
}

internal sealed record OverlayDragPointerSample(PixelPoint Position, bool LeftButtonPressed);

internal sealed class OverlayDragDiagnostics : IDisposable
{
    private readonly Action<string>? log;
    private readonly IOverlayDragPointerProbe? probe;
    private long maximumDifferenceX;
    private long maximumDifferenceY;
    private int samples;
    private bool completed;

    internal OverlayDragDiagnostics(Window window, PixelPoint pointerPosition, OverlayDragOptions options)
    {
        log =
            options.Log
            ?? (Program.ApplicationLog is { } applicationLog ? message => _ = applicationLog.Append(message) : null);
        probe =
            options.PointerProbe
            ?? (log is null ? null : X11Native.TryCreatePointerProbe(window.TryGetPlatformHandle()?.HandleDescriptor));
        log?.Invoke(
            $"Overlay drag: start; window={window.Title}; position={window.Position}; pointer={pointerPosition}; scale={window.RenderScaling}; lock={options.MonitorBounds?.ToString() ?? "off"}."
        );
        Observe(pointerPosition);
    }

    internal void Observe(PixelPoint reportedPosition)
    {
        if (completed || probe?.Read() is not { } native)
        {
            return;
        }

        samples++;
        maximumDifferenceX = Math.Max(maximumDifferenceX, Math.Abs((long)native.Position.X - reportedPosition.X));
        maximumDifferenceY = Math.Max(maximumDifferenceY, Math.Abs((long)native.Position.Y - reportedPosition.Y));
        if (samples == 1)
        {
            log?.Invoke(
                $"Overlay drag: pointer sample; reported={reportedPosition}; native={native.Position}; nativeLeftButton={native.LeftButtonPressed}. Native samples can lead queued pointer events."
            );
        }
    }

    internal void Complete(string reason, PixelPoint position)
    {
        if (completed)
        {
            return;
        }

        completed = true;
        log?.Invoke(
            $"Overlay drag: stop; reason={reason}; position={position}; nativeSamples={samples}; maxPointerDifferencePx={maximumDifferenceX},{maximumDifferenceY}."
        );
        probe?.Dispose();
    }

    public void Dispose() => Complete("disposed", default);
}
