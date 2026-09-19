using Avalonia;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

public enum FirstFootfallInferenceOutcome
{
    Detected,
    NotDetected,
    Disabled,
    Unavailable,
    GameNotForeground,
}

public sealed record FirstFootfallInferenceResult(
    FirstFootfallInferenceOutcome Outcome,
    double MaximumMatchRatio,
    int SampleCount,
    string? Detail
)
{
    public bool Detected => Outcome == FirstFootfallInferenceOutcome.Detected;
}

public interface IFirstFootfallInferenceService : IDisposable
{
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    Task<FirstFootfallInferenceResult> DetectAsync(
        FirstFootfallInferencePreferences preferences,
        CancellationToken cancellationToken = default
    );
}

public static class FirstFootfallColorDetector
{
    public static double GetMatchRatio(IFssPixelSource source, FirstFootfallInferencePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(preferences);
        int pixelCount = checked(source.Width * source.Height);
        if (pixelCount == 0)
        {
            return 0;
        }

        int matches = 0;
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                FssRgbPixel pixel = source.GetPixel(x, y);
                if (Matches(pixel, preferences))
                {
                    matches++;
                }
            }
        }

        return (double)matches / pixelCount;
    }

    private static bool Matches(FssRgbPixel actual, FirstFootfallInferencePreferences expected)
    {
        return actual.Red > expected.Red - expected.Tolerance
            && actual.Red < expected.Red + expected.Tolerance
            && actual.Green > expected.Green - expected.Tolerance
            && actual.Green < expected.Green + expected.Tolerance
            && actual.Blue > expected.Blue - expected.Tolerance
            && actual.Blue < expected.Blue + expected.Tolerance;
    }
}

public sealed class FirstFootfallInferenceService : IFirstFootfallInferenceService
{
    private readonly IGameWindowTracker windowTracker;
    private readonly IGameScreenCapture screenCapture;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private bool disposed;

    public FirstFootfallInferenceService(
        IGameWindowTracker windowTracker,
        IGameScreenCapture screenCapture,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        this.windowTracker = windowTracker ?? throw new ArgumentNullException(nameof(windowTracker));
        this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
        this.delay = delay ?? Task.Delay;
    }

    public bool IsAvailable => screenCapture.IsAvailable;

    public string? UnavailableReason => screenCapture.UnavailableReason;

    public static IFirstFootfallInferenceService CreateCurrent(
        Func<CancellationToken, Task<bool>>? confirmWaylandScreenShare = null,
        Action<string>? log = null
    )
    {
        return new FirstFootfallInferenceService(
            GameWindowTracker.CreateCurrent(),
            GameScreenCapture.CreateCurrent(
                enableWaylandPortalFallback: true,
                confirmWaylandScreenShare: confirmWaylandScreenShare,
                log: log,
                capturePurpose: "first-footfall inference",
                waylandFeature: WaylandCaptureFeatures.FirstFootfall
            )
        );
    }

    public async Task<FirstFootfallInferenceResult> DetectAsync(
        FirstFootfallInferencePreferences preferences,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.Enabled)
        {
            return new FirstFootfallInferenceResult(
                FirstFootfallInferenceOutcome.Disabled,
                0,
                0,
                "First-footfall screen inference is disabled."
            );
        }

        if (!screenCapture.IsAvailable)
        {
            return new FirstFootfallInferenceResult(
                FirstFootfallInferenceOutcome.Unavailable,
                0,
                0,
                screenCapture.UnavailableReason
            );
        }

        var sampleInterval = TimeSpan.FromSeconds(1d / preferences.SamplesPerSecond);
        int maximumSamples = checked(preferences.DurationSeconds * preferences.SamplesPerSecond);
        double maximumRatio = 0d;
        for (int sample = 0; sample < maximumSamples; sample++)
        {
            await delay(sampleInterval, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            GameWindowSnapshot window = windowTracker.GetSnapshot();
            if (!window.IsAvailable || !window.IsVisible || !window.IsForeground)
            {
                return new FirstFootfallInferenceResult(
                    FirstFootfallInferenceOutcome.GameNotForeground,
                    maximumRatio,
                    sample,
                    "Elite must remain visible and foreground while first-footfall "
                        + "notification detection is active."
                );
            }

            PixelRect watchBounds = GetLegacyWatchBounds(window.ClientBounds);
            CapturedPixelBuffer capture = await Task.Run(
                () => screenCapture.Capture(watchBounds, window.ClientBounds),
                cancellationToken
            );
            double ratio = FirstFootfallColorDetector.GetMatchRatio(capture, preferences);
            maximumRatio = Math.Max(maximumRatio, ratio);
            if (ratio > preferences.Threshold)
            {
                return new FirstFootfallInferenceResult(
                    FirstFootfallInferenceOutcome.Detected,
                    maximumRatio,
                    sample + 1,
                    null
                );
            }
        }

        return new FirstFootfallInferenceResult(
            FirstFootfallInferenceOutcome.NotDetected,
            maximumRatio,
            maximumSamples,
            null
        );
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        screenCapture.Dispose();
        windowTracker.Dispose();
    }

    internal static PixelRect GetLegacyWatchBounds(PixelRect clientBounds)
    {
        int halfWidth = clientBounds.Width / 8;
        int height = clientBounds.Height / 7;
        return new PixelRect(
            clientBounds.X + (clientBounds.Width / 2) - halfWidth,
            clientBounds.Y + (int)(clientBounds.Height * 0.17),
            halfWidth * 2,
            height
        );
    }
}

public sealed class UnavailableFirstFootfallInferenceService : IFirstFootfallInferenceService
{
    public UnavailableFirstFootfallInferenceService(string? reason = null)
    {
        UnavailableReason = string.IsNullOrWhiteSpace(reason)
            ? "First-footfall screen inference was not configured."
            : reason;
    }

    public bool IsAvailable => false;

    public string UnavailableReason { get; }

    public Task<FirstFootfallInferenceResult> DetectAsync(
        FirstFootfallInferencePreferences preferences,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            new FirstFootfallInferenceResult(FirstFootfallInferenceOutcome.Unavailable, 0, 0, UnavailableReason)
        );
    }

    public void Dispose() { }
}
