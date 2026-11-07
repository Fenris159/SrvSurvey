using Avalonia.Data.Converters;

namespace SrvSurvey.Desktop.Presentation;

/// <summary>Reserves space for the editor while keeping long review actions scrollable in short workspaces.</summary>
public static class WorkspaceLayoutExtentConverter
{
    private const double ReviewHeightShare = 0.5;
    private const double RecoveryHeightShare = 0.25;
    private const double MaximumRecoveryHeight = 260;

    public static FuncValueConverter<double, double> RecoveryHeight { get; } =
        new(height => double.IsFinite(height) ? Math.Clamp(height * RecoveryHeightShare, 0, MaximumRecoveryHeight) : 0);

    public static FuncValueConverter<double, double> HalfHeight { get; } =
        new(height => double.IsFinite(height) ? Math.Max(0, height * ReviewHeightShare) : 0);
}
