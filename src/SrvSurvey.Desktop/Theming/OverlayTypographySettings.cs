using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Theming;

public sealed record OverlayTypographySettings(
    double Header,
    double Title,
    double Value,
    double Body,
    double Detail,
    double Caption)
{
    public const double MinimumFontSize = 7;
    public const double MaximumFontSize = 32;
    public const double FontSizeIncrement = 0.5;

    public static OverlayTypographySettings Default { get; } = new(
        Header: 10,
        Title: 15,
        Value: 12,
        Body: 11,
        Detail: 10,
        Caption: 9);

    internal static OverlayTypographySettings Parse(
        JsonObject? values,
        string context)
    {
        if (values is null)
        {
            return Default;
        }

        return new OverlayTypographySettings(
            Read(values, "header", Default.Header, context),
            Read(values, "title", Default.Title, context),
            Read(values, "value", Default.Value, context),
            Read(values, "body", Default.Body, context),
            Read(values, "detail", Default.Detail, context),
            Read(values, "caption", Default.Caption, context));
    }

    internal JsonObject ToJson() => new()
    {
        ["header"] = Header,
        ["title"] = Title,
        ["value"] = Value,
        ["body"] = Body,
        ["detail"] = Detail,
        ["caption"] = Caption,
    };

    internal static double Normalize(double value) => Math.Clamp(
        Math.Round(
            value / FontSizeIncrement,
            MidpointRounding.AwayFromZero)
        * FontSizeIncrement,
        MinimumFontSize,
        MaximumFontSize);

    private static double Read(
        JsonObject values,
        string key,
        double fallback,
        string context)
    {
        if (values[key] is null)
        {
            return fallback;
        }

        if (values[key] is not JsonValue value
            || !value.TryGetValue<double>(out var fontSize)
            || !double.IsFinite(fontSize)
            || fontSize is < MinimumFontSize or > MaximumFontSize)
        {
            throw new InvalidDataException(
                $"{context} typography '{key}' must be a number from "
                + $"{MinimumFontSize:0.#} to {MaximumFontSize:0.#}.");
        }

        return Normalize(fontSize);
    }
}
