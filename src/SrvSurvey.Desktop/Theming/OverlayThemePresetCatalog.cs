using Avalonia.Media;

namespace SrvSurvey.Desktop.Theming;

public static class OverlayThemePresetCatalog
{
    public const string DefaultName = "Default";

    private static readonly string[] PresetIdentityKeys =
    [
        "orange",
        "orangeDark",
        "cyan",
        "cyanDark",
        "yellow",
        "white",
        "menuGold",
        "grey",
    ];

    private static readonly string[] ExpandedBiologyKeys =
    [
        "bio.confirmed",
        "bio.confirmedDim",
        "bio.potential",
        "bio.confirmedDimPotential",
        "bio.predictionPotential",
        "bio.goldFill",
        "bio.goldDarkFill",
        "bio.goldPotential",
        "bio.goldDarkPotential",
        "bio.galacticRegion",
        "bio.galacticRegionPotential",
        "bio.unknownGlyph",
        "bio.empty",
        "bio.confirmedEdge",
        "bio.confirmedDimEdge",
        "bio.predictionEdge",
        "bio.goldEdge",
        "bio.goldDarkEdge",
        "bio.galacticRegionEdge",
        "bio.unknownEdge",
        "bio.confirmedSegmentEdge",
        "bio.confirmedPotentialSegmentEdge",
        "bio.confirmedDimSegmentEdge",
        "bio.confirmedDimPotentialSegmentEdge",
        "bio.predictionSegmentEdge",
        "bio.predictionPotentialSegmentEdge",
        "bio.goldSegmentEdge",
        "bio.goldPotentialSegmentEdge",
        "bio.goldDarkSegmentEdge",
        "bio.goldDarkPotentialSegmentEdge",
        "bio.galacticRegionSegmentEdge",
        "bio.galacticRegionPotentialSegmentEdge",
    ];

    public static IReadOnlyList<OverlayThemePreset> Presets { get; } =
    [
        new(DefaultName, LegacyOverlayThemeStore.CreateDefault().Colors),
        CreateExpandedPreset(
            "Nebula Cyan",
            "#5EC8F2",
            "#B8E8FF",
            "#D6EEF9",
            "#FFE8A3"),
        CreateExpandedPreset(
            "Toxic Green",
            "#5CFF9E",
            "#A8FFCC",
            "#D8FFE8",
            "#FFF066"),
        CreateExpandedPreset(
            "Crimson Wake",
            "#FF6B6B",
            "#FFB8B8",
            "#FFE4E4",
            "#FFD966"),
        CreateExpandedPreset(
            "Void Amethyst",
            "#C9A0FF",
            "#E2CCFF",
            "#E8E0F5",
            "#7FFFD4"),
        CreateExpandedPreset(
            "Cerulean Gold",
            "#3D9EE8",
            "#F2F7FC",
            "#C8E4FA",
            "#FFCC33"),
    ];

    public static OverlayThemePreset Default => Presets[0];

    public static bool TryGet(string? name, out OverlayThemePreset preset)
    {
        var match = Presets.FirstOrDefault(candidate => string.Equals(
            candidate.Name,
            name,
            StringComparison.OrdinalIgnoreCase));
        preset = match ?? Default;
        return match is not null;
    }

    public static OverlayThemePreset? FindMatching(
        IReadOnlyDictionary<string, Color> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        return Presets.FirstOrDefault(preset => preset.Colors.All(entry =>
            colors.TryGetValue(entry.Key, out var candidate)
            && candidate == entry.Value));
    }

    internal static bool TryUpgradeLegacyBiologyPalette(
        Dictionary<string, Color> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        if (ExpandedBiologyKeys.Any(colors.ContainsKey))
        {
            return false;
        }

        var preset = Presets.FirstOrDefault(candidate => PresetIdentityKeys.All(key =>
            colors.TryGetValue(key, out var color)
            && candidate.Colors[key] == color));
        if (preset is null)
        {
            return false;
        }

        var legacyBiology = CreateLegacyBiologyPalette(preset);
        if (legacyBiology.Any(entry =>
                !colors.TryGetValue(entry.Key, out var color)
                || color != entry.Value))
        {
            return false;
        }

        foreach (var entry in preset.Colors.Where(entry =>
                     entry.Key.StartsWith("bio.", StringComparison.Ordinal)))
        {
            colors[entry.Key] = entry.Value;
        }

        return true;
    }

    internal static bool AddMissingExpandedBiologyColors(
        Dictionary<string, Color> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var defaults = LegacyOverlayThemeStore.CreateDefault().Colors;
        var preset = Presets.FirstOrDefault(candidate => PresetIdentityKeys.All(key =>
            colors.TryGetValue(key, out var color)
            && candidate.Colors[key] == color));
        var changed = false;
        foreach (var fallback in defaults.Where(entry =>
                     entry.Key.StartsWith("bio.", StringComparison.Ordinal)))
        {
            if (colors.ContainsKey(fallback.Key))
            {
                continue;
            }

            colors[fallback.Key] = preset is not null
                ? preset.Colors[fallback.Key]
                : DeriveMissingBiologyColor(fallback.Key, colors, fallback.Value);
            changed = true;
        }

        return changed;
    }

    private static OverlayThemePreset CreateExpandedPreset(
        string name,
        string headerPrimary,
        string headerSecondary,
        string commodity,
        string values)
    {
        var colors = LegacyOverlayThemeStore.CreateDefault().Colors.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
        var primary = Color.Parse(headerPrimary);
        var secondary = Color.Parse(headerSecondary);
        var text = Color.Parse(commodity);
        var value = Color.Parse(values);
        var palette = new ExpandedPalette(
            primary,
            Scale(primary, 0.42),
            secondary,
            Scale(secondary, 0.45),
            text,
            value,
            Scale(text, 0.56),
            Scale(primary, 0.10));

        ApplyGeneral(colors, palette);
        ApplyBiology(colors, palette);
        ApplyColonisation(colors, palette);
        ApplySettlements(colors, palette);
        ApplyGuardian(colors, palette);
        return new OverlayThemePreset(name, colors);
    }

    private static IReadOnlyDictionary<string, Color> CreateLegacyBiologyPalette(
        OverlayThemePreset preset)
    {
        if (string.Equals(preset.Name, DefaultName, StringComparison.Ordinal))
        {
            return new Dictionary<string, Color>(StringComparer.Ordinal)
            {
                ["bio.gold"] = Color.Parse("#FFD700"),
                ["bio.goldDark"] = Color.Parse("#785F00"),
                ["bio.unknown"] = Color.Parse("#696969"),
                ["bio.hatch"] = Color.FromArgb(242, 64, 64, 64),
                ["bio.white"] = Color.Parse("#FFFFFF"),
                ["bio.prediction"] = Color.Parse("#2F4F4F"),
            };
        }

        var surface = Scale(preset.Colors["orange"], 0.10);
        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["bio.gold"] = preset.Colors["yellow"],
            ["bio.goldDark"] = Scale(preset.Colors["yellow"], 0.42),
            ["bio.unknown"] = preset.Colors["grey"],
            ["bio.hatch"] = WithAlpha(surface, 242),
            ["bio.white"] = preset.Colors["white"],
            ["bio.prediction"] = Scale(preset.Colors["cyan"], 0.32),
        };
    }

    private static void ApplyGeneral(
        Dictionary<string, Color> colors,
        ExpandedPalette palette)
    {
        colors["orange"] = palette.Primary;
        colors["orangeDark"] = palette.PrimaryDark;
        colors["cyan"] = palette.Secondary;
        colors["cyanDark"] = palette.SecondaryDark;
        colors["yellow"] = palette.Value;
        colors["white"] = palette.Text;
        colors["menuGold"] = WithAlpha(palette.Value, 235);
        colors["grey"] = palette.Muted;
    }

    private static void ApplyBiology(
        Dictionary<string, Color> colors,
        ExpandedPalette palette)
    {
        var prediction = Blend(palette.Primary, palette.Secondary, 0.65);
        var predictionDark = Scale(prediction, 0.45);
        var goldFill = Scale(palette.Value, 0.68);
        var goldDarkFill = Scale(goldFill, 0.34);
        colors["bio.confirmed"] = palette.Primary;
        colors["bio.confirmedDim"] = palette.PrimaryDark;
        colors["bio.potential"] = WithAlpha(palette.PrimaryDark, 140);
        colors["bio.confirmedDimPotential"] = WithAlpha(
            Scale(palette.PrimaryDark, 0.33),
            140);
        colors["bio.prediction"] = prediction;
        colors["bio.predictionPotential"] = WithAlpha(predictionDark, 180);
        colors["bio.gold"] = palette.Value;
        colors["bio.goldDark"] = Scale(palette.Value, 0.42);
        colors["bio.goldFill"] = goldFill;
        colors["bio.goldDarkFill"] = goldDarkFill;
        colors["bio.goldPotential"] = WithAlpha(goldFill, 144);
        colors["bio.goldDarkPotential"] = WithAlpha(goldFill, 140);
        colors["bio.galacticRegion"] = palette.Text;
        colors["bio.galacticRegionPotential"] = WithAlpha(
            Scale(palette.Text, 0.74),
            140);
        colors["bio.unknown"] = palette.Muted;
        colors["bio.unknownGlyph"] = palette.Muted;
        colors["bio.hatch"] = WithAlpha(palette.Muted, 242);
        colors["bio.empty"] = colors["black"];
        colors["bio.white"] = palette.Text;
        colors["bio.confirmedEdge"] = WithAlpha(palette.Primary, 96);
        colors["bio.confirmedDimEdge"] = WithAlpha(palette.PrimaryDark, 96);
        colors["bio.predictionEdge"] = WithAlpha(predictionDark, 96);
        colors["bio.goldEdge"] = WithAlpha(palette.Value, 96);
        colors["bio.goldDarkEdge"] = WithAlpha(goldFill, 96);
        colors["bio.galacticRegionEdge"] = WithAlpha(palette.Text, 96);
        colors["bio.unknownEdge"] = WithAlpha(predictionDark, 96);
        colors["bio.confirmedSegmentEdge"] = palette.PrimaryDark;
        colors["bio.confirmedPotentialSegmentEdge"] = WithAlpha(
            palette.Primary,
            124);
        colors["bio.confirmedDimSegmentEdge"] = Scale(palette.PrimaryDark, 0.33);
        colors["bio.confirmedDimPotentialSegmentEdge"] = WithAlpha(
            Scale(palette.Primary, 0.33),
            124);
        colors["bio.predictionSegmentEdge"] = predictionDark;
        colors["bio.predictionPotentialSegmentEdge"] = predictionDark;
        colors["bio.goldSegmentEdge"] = palette.Value;
        colors["bio.goldPotentialSegmentEdge"] = WithAlpha(
            Blend(goldFill, palette.Value, 0.4),
            144);
        colors["bio.goldDarkSegmentEdge"] = goldFill;
        colors["bio.goldDarkPotentialSegmentEdge"] = WithAlpha(
            goldDarkFill,
            124);
        colors["bio.galacticRegionSegmentEdge"] = Scale(palette.Text, 0.5);
        colors["bio.galacticRegionPotentialSegmentEdge"] = WithAlpha(
            palette.Text,
            144);
    }

    private static Color DeriveMissingBiologyColor(
        string key,
        IReadOnlyDictionary<string, Color> colors,
        Color fallback)
    {
        Color Get(string name, Color value) => colors.TryGetValue(name, out var color)
            ? color
            : value;

        return key switch
        {
            "bio.goldFill" => Scale(Get("bio.gold", fallback), 0.68),
            "bio.goldDarkFill" => Scale(Get("bio.goldDark", fallback), 0.34),
            "bio.confirmedDimPotential" => WithAlpha(
                Scale(
                    Get("bio.confirmedDim", Get("orangeDark", fallback)),
                    0.33),
                140),
            "bio.goldPotential" => WithAlpha(
                Get("bio.goldFill", fallback),
                144),
            "bio.goldDarkPotential" => WithAlpha(
                Get("bio.goldFill", fallback),
                140),
            "bio.predictionPotential" => WithAlpha(
                Scale(
                    Get("bio.prediction", Get("cyan", fallback)),
                    0.45),
                180),
            "bio.galacticRegion" =>
                Get("bio.white", Get("white", fallback)),
            "bio.galacticRegionPotential" => WithAlpha(
                Scale(
                    Get(
                        "bio.galacticRegion",
                        Get("bio.white", Get("white", fallback))),
                    0.74),
                140),
            "bio.unknownGlyph" =>
                Get("bio.unknown", Get("grey", fallback)),
            "bio.empty" => Get("black", fallback),
            "bio.confirmedEdge" => WithAlpha(
                Get("bio.confirmed", Get("orange", fallback)),
                96),
            "bio.confirmedDimEdge" => WithAlpha(
                Get("bio.confirmedDim", Get("orangeDark", fallback)),
                96),
            "bio.predictionEdge" or "bio.unknownEdge" => WithAlpha(
                Get("cyanDark", fallback),
                96),
            "bio.goldEdge" => WithAlpha(Get("bio.gold", fallback), 96),
            "bio.goldDarkEdge" => WithAlpha(
                Get("bio.goldFill", Get("bio.goldDark", fallback)),
                96),
            "bio.galacticRegionEdge" => WithAlpha(
                Get("bio.white", Get("white", fallback)),
                96),
            "bio.confirmedSegmentEdge" =>
                Get("orangeDark", Get("bio.confirmedDim", fallback)),
            "bio.confirmedPotentialSegmentEdge" => WithAlpha(
                Get("bio.confirmed", Get("orange", fallback)),
                124),
            "bio.confirmedDimSegmentEdge" => Scale(
                Get("bio.confirmedDim", Get("orangeDark", fallback)),
                0.33),
            "bio.confirmedDimPotentialSegmentEdge" => WithAlpha(
                Scale(Get("bio.confirmed", Get("orange", fallback)), 0.33),
                124),
            "bio.predictionSegmentEdge" or
                "bio.predictionPotentialSegmentEdge" =>
                Get("cyanDark", fallback),
            "bio.goldSegmentEdge" => Get("bio.gold", fallback),
            "bio.goldPotentialSegmentEdge" => WithAlpha(
                Blend(
                    Get("bio.goldFill", fallback),
                    Get("bio.gold", fallback),
                    0.4),
                144),
            "bio.goldDarkSegmentEdge" => Get("bio.goldFill", fallback),
            "bio.goldDarkPotentialSegmentEdge" => WithAlpha(
                Get("bio.goldDarkFill", fallback),
                124),
            "bio.galacticRegionSegmentEdge" => Scale(
                Get("bio.white", Get("white", fallback)),
                0.5),
            "bio.galacticRegionPotentialSegmentEdge" => WithAlpha(
                Get("bio.white", Get("white", fallback)),
                144),
            _ => fallback,
        };
    }

    private static void ApplyColonisation(
        Dictionary<string, Color> colors,
        ExpandedPalette palette)
    {
        colors["colonise.highlight"] = palette.Value;
        colors["colonise.item"] = palette.Primary;
        colors["colonise.itemDark"] = palette.PrimaryDark;
        colors["colonise.rowHighlight"] = WithAlpha(palette.Primary, 72);
    }

    private static void ApplySettlements(
        Dictionary<string, Color> colors,
        ExpandedPalette palette)
    {
        colors["fcz.checkpoint"] = palette.Value;
        colors["fcz.powerPost"] = palette.Primary;
    }

    private static void ApplyGuardian(
        Dictionary<string, Color> colors,
        ExpandedPalette palette)
    {
        colors["guardian.surface"] = palette.Surface;
        colors["guardian.header"] = palette.Value;
        colors["guardian.primary"] = palette.Primary;
        colors["guardian.primaryDark"] = palette.PrimaryDark;
        colors["guardian.secondary"] = palette.Secondary;
        colors["guardian.secondaryDark"] = palette.SecondaryDark;
        colors["guardian.text"] = palette.Text;
        colors["guardian.muted"] = palette.Muted;
        colors["guardian.warning"] = palette.Value;
    }

    private static Color Scale(Color color, double factor)
    {
        return Color.FromArgb(
            color.A,
            (byte)Math.Round(color.R * factor),
            (byte)Math.Round(color.G * factor),
            (byte)Math.Round(color.B * factor));
    }

    private static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static Color Blend(Color first, Color second, double secondWeight)
    {
        var firstWeight = 1 - secondWeight;
        return Color.FromArgb(
            255,
            (byte)Math.Round(first.R * firstWeight + second.R * secondWeight),
            (byte)Math.Round(first.G * firstWeight + second.G * secondWeight),
            (byte)Math.Round(first.B * firstWeight + second.B * secondWeight));
    }

    private sealed record ExpandedPalette(
        Color Primary,
        Color PrimaryDark,
        Color Secondary,
        Color SecondaryDark,
        Color Text,
        Color Value,
        Color Muted,
        Color Surface);
}

public sealed record OverlayThemePreset(
    string Name,
    IReadOnlyDictionary<string, Color> Colors);
