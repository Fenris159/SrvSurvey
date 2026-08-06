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
        if (!TryFindYellowBarY(source, settings, center, out var yellowY))
        {
            failure = "The FSS tuning bar was not found.";
            return false;
        }

        var horizontalYellow = settings.YellowBar with
        {
            Tolerance = settings.YellowHorizontalTolerance,
        };
        if (!TryFindBarLeft(source, horizontalYellow, center, yellowY, out var left))
        {
            failure = "The left edge of the FSS tuning bar was not found.";
            return false;
        }

        if (!TryFindBarRight(source, horizontalYellow, center, yellowY, out var right))
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

        if (!TryFindBlackAreaY(source, settings, blackX, yellowY, out var blackY))
        {
            failure = "The dark area below the FSS tuning bar was not found.";
            return false;
        }

        return TryBuildWatchArea(
            source,
            yellowY,
            blackY,
            watchX,
            width,
            out area,
            out failure);
    }

    private static bool TryFindYellowBarY(
        IFssPixelSource source,
        FssTuningDetectorSettings settings,
        int x,
        out int yellowY)
    {
        yellowY = 0;
        var y = source.Height - 1;
        while (y > 0)
        {
            if (Matches(source.GetPixel(x, y), settings.YellowBar))
            {
                yellowY = y;
                return true;
            }

            y--;
        }

        return false;
    }

    private static bool TryFindBarLeft(
        IFssPixelSource source,
        FssPixelColor horizontalYellow,
        int center,
        int y,
        out int left)
    {
        left = 0;
        var x = center;
        while (x > 0)
        {
            if (!Matches(source.GetPixel(x, y), horizontalYellow))
            {
                left = x;
                return true;
            }

            x--;
        }

        return false;
    }

    private static bool TryFindBarRight(
        IFssPixelSource source,
        FssPixelColor horizontalYellow,
        int center,
        int y,
        out int right)
    {
        right = 0;
        var x = center;
        while (x < source.Width)
        {
            if (!Matches(source.GetPixel(x, y), horizontalYellow))
            {
                right = x;
                return true;
            }

            x++;
        }

        return false;
    }

    private static bool TryFindBlackAreaY(
        IFssPixelSource source,
        FssTuningDetectorSettings settings,
        int blackX,
        int startY,
        out int blackY)
    {
        blackY = 0;
        var y = startY;
        while (y < source.Height)
        {
            if (Matches(source.GetPixel(blackX, y), settings.BlackArea))
            {
                blackY = y;
                return true;
            }

            y++;
        }

        return false;
    }

    private static bool TryBuildWatchArea(
        IFssPixelSource source,
        int yellowY,
        int blackY,
        int watchX,
        int width,
        out FssPixelRegion area,
        out string? failure)
    {
        area = default;
        failure = null;
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
