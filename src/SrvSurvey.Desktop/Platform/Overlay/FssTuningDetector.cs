using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.Platform.Overlay;

public enum FssTuningDetectionState
{
    None,
    Skipped,
    Waiting,
    White,
    Yellow,
}

public readonly record struct FssRgbPixel(byte Red, byte Green, byte Blue);

public readonly record struct FssPixelRegion(
    int X,
    int Y,
    int Width,
    int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

public interface IFssPixelSource
{
    int Width { get; }

    int Height { get; }

    FssRgbPixel GetPixel(int x, int y);
}

public sealed record FssTuningAnalysis(
    FssTuningDetectionState State,
    FssPixelRegion? WatchArea,
    int WhitePixelCount,
    int YellowPixelCount,
    string? Failure)
{
    public bool FoundWatchArea => WatchArea is not null;
}

public static class FssTuningDetector
{
    public static FssTuningAnalysis Analyze(
        IFssPixelSource source,
        FssTuningDetectorSettings settings,
        FssTuningDetectionState previousState)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);

        if (!TryFindWatchArea(source, settings, out var area, out var failure))
        {
            return new FssTuningAnalysis(
                previousState,
                null,
                0,
                0,
                failure);
        }

        var white = 0;
        var yellow = 0;
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                var pixel = source.GetPixel(x, y);
                if (Matches(pixel, settings.WhiteText))
                {
                    white++;
                }

                if (Matches(pixel, settings.YellowText))
                {
                    yellow++;
                }
            }
        }

        var state = previousState;
        if (yellow > white * 0.25f)
        {
            state = FssTuningDetectionState.Yellow;
        }
        else if (white > 25)
        {
            state = FssTuningDetectionState.White;
        }

        return new FssTuningAnalysis(
            state,
            area,
            white,
            yellow,
            null);
    }

    private static bool TryFindWatchArea(
        IFssPixelSource source,
        FssTuningDetectorSettings settings,
        out FssPixelRegion area,
        out string? failure)
    {
        area = default;
        failure = null;
        if (source.Width < 4 || source.Height < 4)
        {
            failure = "The captured FSS quadrant is too small.";
            return false;
        }

        var center = source.Width / 2;
        var x = center;
        var y = source.Height - 1;
        var yellowY = 0;
        while (y > 0)
        {
            if (Matches(source.GetPixel(x, y), settings.YellowBar))
            {
                yellowY = y;
                break;
            }

            y--;
        }

        if (yellowY == 0)
        {
            failure = "The FSS tuning bar was not found.";
            return false;
        }

        var horizontalYellow = settings.YellowBar with
        {
            Tolerance = settings.YellowHorizontalTolerance,
        };
        var left = 0;
        while (x > 0)
        {
            if (!Matches(source.GetPixel(x, y), horizontalYellow))
            {
                left = x;
                break;
            }

            x--;
        }

        if (left == 0)
        {
            failure = "The left edge of the FSS tuning bar was not found.";
            return false;
        }

        x = center;
        var right = 0;
        while (x < source.Width)
        {
            if (!Matches(source.GetPixel(x, y), horizontalYellow))
            {
                right = x;
                break;
            }

            x++;
        }

        if (right == 0)
        {
            failure = "The right edge of the FSS tuning bar was not found.";
            return false;
        }

        var width = (right - left) / 2;
        var watchX = left + width;
        var blackX = right - 30;
        if (width <= 0 || blackX < 0 || blackX >= source.Width)
        {
            failure = "The detected FSS tuning bar has invalid dimensions.";
            return false;
        }

        var blackY = 0;
        while (y < source.Height)
        {
            if (Matches(source.GetPixel(blackX, y), settings.BlackArea))
            {
                blackY = y;
                break;
            }

            y++;
        }

        if (blackY == 0)
        {
            failure = "The dark area below the FSS tuning bar was not found.";
            return false;
        }

        var heightDelta = (blackY - yellowY) / 3;
        var watchY = blackY + heightDelta;
        var height = (source.Height - watchY - 1) / 2;
        if (height <= 0
            || watchX < 0
            || watchY < 0
            || watchX + width > source.Width
            || watchY + height > source.Height)
        {
            failure = "The detected FSS text area has invalid dimensions.";
            return false;
        }

        area = new FssPixelRegion(watchX, watchY, width, height);
        return true;
    }

    private static bool Matches(FssRgbPixel actual, FssPixelColor expected)
    {
        return actual.Red > expected.Red - expected.Tolerance
            && actual.Red < expected.Red + expected.Tolerance
            && actual.Green > expected.Green - expected.Tolerance
            && actual.Green < expected.Green + expected.Tolerance
            && actual.Blue > expected.Blue - expected.Tolerance
            && actual.Blue < expected.Blue + expected.Tolerance;
    }
}
