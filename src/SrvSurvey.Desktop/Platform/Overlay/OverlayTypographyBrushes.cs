using System.Text.Json.Nodes;

namespace SrvSurvey.Desktop.Platform.Overlay;

/// <summary>Optional theme-swatch references for each text role; null retains the presentation's own brush.</summary>
public sealed record OverlayTypographyBrushes(
    string? Header = null,
    string? Title = null,
    string? Value = null,
    string? Body = null,
    string? Detail = null,
    string? Caption = null
)
{
    public static OverlayTypographyBrushes Default { get; } = new();

    public string? Get(OverlayTypographyRole role) =>
        role switch
        {
            OverlayTypographyRole.Header => Header,
            OverlayTypographyRole.Title => Title,
            OverlayTypographyRole.Value => Value,
            OverlayTypographyRole.Body => Body,
            OverlayTypographyRole.Detail => Detail,
            OverlayTypographyRole.Caption => Caption,
            _ => null,
        };

    public OverlayTypographyBrushes With(OverlayTypographyRole role, string? key)
    {
        if (key is not null && (string.IsNullOrWhiteSpace(key) || key.Length > 100))
        {
            throw new ArgumentException("Choose a valid overlay theme brush.", nameof(key));
        }
        return role switch
        {
            OverlayTypographyRole.Header => this with { Header = key },
            OverlayTypographyRole.Title => this with { Title = key },
            OverlayTypographyRole.Value => this with { Value = key },
            OverlayTypographyRole.Body => this with { Body = key },
            OverlayTypographyRole.Detail => this with { Detail = key },
            OverlayTypographyRole.Caption => this with { Caption = key },
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
    }

    internal JsonObject ToJson()
    {
        var result = new JsonObject();
        foreach (
            OverlayTypographyRole role in Enum.GetValues<OverlayTypographyRole>()
                .Where(role => role != OverlayTypographyRole.Icons)
        )
        {
            if (Get(role) is { } brush)
            {
                result[role.ToString().ToLowerInvariant()] = brush;
            }
        }
        return result;
    }

    internal static OverlayTypographyBrushes Parse(JsonNode? node)
    {
        if (node is null)
        {
            return Default;
        }

        if (node is not JsonObject values)
        {
            throw new InvalidDataException("Overlay text brushes must be an object.");
        }

        OverlayTypographyBrushes result = Default;
        foreach (
            OverlayTypographyRole role in Enum.GetValues<OverlayTypographyRole>()
                .Where(role => role != OverlayTypographyRole.Icons)
        )
        {
            if (values[role.ToString().ToLowerInvariant()] is not { } value)
            {
                continue;
            }

            if (value is not JsonValue scalar || !scalar.TryGetValue<string>(out string? key))
            {
                throw new InvalidDataException("Overlay text brushes must contain theme-swatch names.");
            }

            try
            {
                result = result.With(role, key);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    "Overlay text brushes must contain valid theme-swatch names.",
                    exception
                );
            }
        }
        return result;
    }
}
