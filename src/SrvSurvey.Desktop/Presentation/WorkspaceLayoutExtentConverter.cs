using Avalonia.Data.Converters;

namespace SrvSurvey.Desktop.Presentation;

/// <summary>Keeps workspace controls scrollable while reserving room for their primary content.</summary>
public static class WorkspaceLayoutExtentConverter
{
    private const double ReviewHeightShare = 0.5;
    private const double RecoveryHeightShare = 0.25;
    private const double MaximumRecoveryHeight = 260;
    private const double HistoryReservedHeight = 340;

    public static FuncValueConverter<double, double> RecoveryHeight { get; } =
        new(height => double.IsFinite(height) ? Math.Clamp(height * RecoveryHeightShare, 0, MaximumRecoveryHeight) : 0);

    public static FuncValueConverter<double, double> HalfHeight { get; } =
        new(height => double.IsFinite(height) ? Math.Max(0, height * ReviewHeightShare) : 0);

    public static FuncValueConverter<double, double> HistoryControlsHeight { get; } =
        new(height => double.IsFinite(height) ? Math.Max(0, height - HistoryReservedHeight) : 0);
}
