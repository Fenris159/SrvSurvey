using Avalonia;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform.Overlay;

namespace SrvSurvey.Desktop.ViewModels;

public sealed record OverlayPositionPreviewViewModel(
    OverlayLayoutDefinition Definition,
    string Subtitle,
    string Context,
    IReadOnlyList<OverlayPositionPreviewRowViewModel> Rows,
    IReadOnlyList<RouteBioTargetItemViewModel> RouteBioTargets,
    string Footer,
    string CompactText,
    bool IsCompact,
    bool ShowSubtitle,
    bool ShowFooter,
    double PreferredWidth,
    double EstimatedHeight)
{
    public string Title => Definition.DisplayName;

    public bool HasRows => Rows.Count > 0;

    public bool IsRouteBio => Definition.Name == "PlotRouteBio";

    public static OverlayPositionPreviewViewModel Create(
        OverlayLayoutDefinition definition)
    {
        return Create(definition, OverlayPreviewSimulationState.Default);
    }

    internal static OverlayPositionPreviewViewModel Create(
        OverlayLayoutDefinition definition,
        OverlayPreviewSimulationState simulation)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(simulation);
        var content = OverlayPreviewSimulationProjector.Project(
            definition,
            simulation);
        var isCompact = definition.PreviewSize.Height < 50;
        var rows = isCompact ? [] : content.Rows;
        var routeBioTargets = definition.Name == "PlotRouteBio"
            ? rows
                .Select(row => row.RouteBody)
                .OfType<RouteBioTargetItemViewModel>()
                .ToArray()
            : [];
        var preferredWidth = isCompact
            || definition.Name == "PlotRouteBio"
            ? definition.PreviewSize.Width
            : CalculatePreferredWidth(definition.DisplayName, content);
        var estimatedHeight = isCompact
            ? definition.PreviewSize.Height
            : 70
                + (definition.Name == "PlotRouteBio"
                    ? rows.Take(RouteBioTargetList.MaxVisibleItemCount)
                        .Sum(row => row.EstimatedHeight)
                    : rows.Sum(row => row.EstimatedHeight))
                + 22;

        return new OverlayPositionPreviewViewModel(
            definition,
            content.Subtitle,
            content.Context,
            rows,
            routeBioTargets,
            content.Footer,
            string.IsNullOrWhiteSpace(content.CompactText)
                ? content.Footer
                : content.CompactText,
            isCompact,
            !isCompact,
            !isCompact,
            preferredWidth,
            estimatedHeight);
    }

    public PixelSize GetEstimatedPixelSize(double scaling)
    {
        var safeScaling = double.IsFinite(scaling) && scaling > 0
            ? scaling
            : 1;
        return new PixelSize(
            Math.Max(1, (int)Math.Ceiling(PreferredWidth * safeScaling)),
            Math.Max(1, (int)Math.Ceiling(EstimatedHeight * safeScaling)));
    }

    private static double CalculatePreferredWidth(
        string title,
        OverlayPreviewSimulationContent content)
    {
        var maximumCharacters = new[]
            {
                title.Length,
                content.Subtitle.Length,
                content.Context.Length,
                content.Footer.Length,
            }
            .Concat(content.Rows.Select(row =>
                row.Label.Length
                + row.Value.Length
                + (row.HasGlyph ? 3 : 0)
                + (row.HasBodyIcon ? 4 : 0)
                + (row.ShowCompletionCheckBox ? 3 : 0)
                + (row.HasRewardBands ? row.RewardBands!.Count * 2 : 0)))
            .Max();
        return Math.Clamp(32 + maximumCharacters * 6.1, 190, 480);
    }
}

public sealed record OverlayPositionPreviewRowViewModel(
    string Label,
    string Value,
    double? Progress = null,
    string Glyph = "",
    OverlayPreviewGlyphTone GlyphTone = OverlayPreviewGlyphTone.Primary,
    IReadOnlyList<BiologySignalRewardBandViewModel>? RewardBands = null,
    bool ShowCompletionCheckBox = false,
    bool IsCompleted = false,
    string BodyIconAssetPath = "",
    string BodyIconAccessibleName = "",
    RouteBioTargetItemViewModel? RouteBody = null)
{
    public bool HasProgress => Progress is not null && !HasRewardBands;

    public bool HasGlyph => !string.IsNullOrWhiteSpace(Glyph);

    public bool HasRewardBands => RewardBands is { Count: > 0 };

    public bool HasBodyIcon => !string.IsNullOrWhiteSpace(BodyIconAssetPath);

    public bool IsRouteBody => RouteBody is not null;

    public bool IsPrimaryGlyph => GlyphTone == OverlayPreviewGlyphTone.Primary;

    public bool IsInformationGlyph =>
        GlyphTone == OverlayPreviewGlyphTone.Information;

    public bool IsGoldGlyph => GlyphTone == OverlayPreviewGlyphTone.Gold;

    public bool IsWarningGlyph => GlyphTone == OverlayPreviewGlyphTone.Warning;

    public bool IsDangerGlyph => GlyphTone == OverlayPreviewGlyphTone.Danger;

    public bool IsSuccessGlyph => GlyphTone == OverlayPreviewGlyphTone.Success;

    public double EstimatedHeight => IsRouteBody
        ? 30 + (RouteBody?.Species.Count ?? 0) * 14
        : ShowCompletionCheckBox
        ? 30
        : HasRewardBands
        ? 34
        : HasProgress
            ? 27
            : 20;
}

public enum OverlayPreviewGlyphTone
{
    Primary,
    Information,
    Gold,
    Warning,
    Danger,
    Success,
}
