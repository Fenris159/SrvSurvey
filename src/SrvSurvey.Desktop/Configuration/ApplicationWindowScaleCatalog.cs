namespace SrvSurvey.Desktop.Configuration;

public static class ApplicationWindowScaleCatalog
{
    public const int DefaultPercent = 100;

    public static IReadOnlyList<ApplicationWindowScaleOption> All { get; } =
    [
        new(80),
        new(90),
        new(DefaultPercent),
        new(110),
        new(125),
        new(150),
    ];

    public static int Normalize(int percent)
    {
        return All.Any(option => option.Percent == percent)
            ? percent
            : DefaultPercent;
    }
}

public sealed record ApplicationWindowScaleOption(int Percent)
{
    public string DisplayName => $"{Percent}%";

    public override string ToString() => DisplayName;
}
