using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media;

namespace SrvSurvey.Desktop.Theming;

public sealed class LegacyOverlayThemeStore
{
    private static readonly IReadOnlyDictionary<string, Color> DefaultColors =
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["orange"] = Color.FromArgb(255, 255, 111, 0),
            ["orangeDark"] = Color.FromArgb(255, 95, 48, 3),
            ["cyan"] = Color.FromArgb(255, 84, 223, 237),
            ["cyanDark"] = Color.FromArgb(255, 0, 139, 139),
            ["red"] = Color.FromArgb(255, 255, 0, 0),
            ["redDark"] = Color.FromArgb(255, 139, 0, 0),
            ["yellow"] = Color.FromArgb(255, 255, 255, 0),
            ["green"] = Color.FromArgb(255, 0, 255, 0),
            ["greenDark"] = Color.FromArgb(255, 0, 139, 0),
            ["white"] = Color.FromArgb(255, 255, 255, 255),
            ["black"] = Color.FromArgb(255, 0, 0, 0),
            ["menuGold"] = Color.FromArgb(235, 235, 145, 0),
            ["grey"] = Color.FromArgb(255, 100, 100, 100),
            ["bio.gold"] = Color.FromArgb(255, 255, 215, 0),
            ["bio.goldDark"] = Color.FromArgb(255, 120, 95, 0),
            ["bio.unknown"] = Color.FromArgb(255, 105, 105, 105),
            ["bio.hatch"] = Color.FromArgb(242, 64, 64, 64),
            ["bio.white"] = Color.FromArgb(255, 255, 255, 255),
            ["bio.prediction"] = Color.FromArgb(255, 47, 79, 79),
            ["colonise.surplus"] = Color.FromArgb(255, 0, 255, 0),
            ["colonise.surplusDark"] = Color.FromArgb(255, 0, 139, 0),
            ["colonise.deficit"] = Color.FromArgb(255, 255, 0, 0),
            ["colonise.deficitDark"] = Color.FromArgb(255, 139, 0, 0),
            ["colonise.highlight"] = Color.FromArgb(255, 255, 255, 0),
            ["colonise.item"] = Color.FromArgb(255, 255, 111, 0),
            ["colonise.itemDark"] = Color.FromArgb(255, 95, 48, 3),
            ["fcz.checkpoint"] = Color.FromArgb(255, 255, 255, 0),
            ["fcz.checkpointLocal"] = Color.FromArgb(255, 0, 255, 0),
            ["fcz.powerPost"] = Color.FromArgb(255, 218, 165, 32),
        };

    private readonly string path;

    public LegacyOverlayThemeStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    public LegacyOverlayTheme Load()
    {
        if (!File.Exists(path))
        {
            return CreateDefault();
        }

        try
        {
            var root = JsonNode.Parse(
                    File.ReadAllText(path),
                    documentOptions: new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                    })
                as JsonObject
                ?? throw new InvalidDataException(
                    "The legacy overlay theme is not a JSON object.");
            var colors = new Dictionary<string, Color>(StringComparer.Ordinal);
            ParseObject(root, string.Empty, colors);
            foreach (var fallback in DefaultColors)
            {
                colors.TryAdd(fallback.Key, fallback.Value);
            }

            return new LegacyOverlayTheme(colors, true, null);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException
                or InvalidDataException
                or FormatException
                or OverflowException)
        {
            var fallback = CreateDefault();
            return fallback with
            {
                Error = $"Could not read legacy overlay theme '{path}': "
                    + exception.Message,
            };
        }
    }

    public static LegacyOverlayTheme CreateDefault()
    {
        return new LegacyOverlayTheme(
            new Dictionary<string, Color>(DefaultColors, StringComparer.Ordinal),
            false,
            null);
    }

    private static void ParseObject(
        JsonObject source,
        string prefix,
        Dictionary<string, Color> colors)
    {
        foreach (var entry in source)
        {
            var name = prefix + entry.Key;
            if (entry.Value is JsonObject child)
            {
                ParseObject(child, name + ".", colors);
                continue;
            }

            colors[name] = ParseColor(name, entry.Value, colors);
        }
    }

    private static Color ParseColor(
        string name,
        JsonNode? value,
        IReadOnlyDictionary<string, Color> parsedColors)
    {
        if (value is null)
        {
            if (DefaultColors.TryGetValue(name, out var fallback))
            {
                return fallback;
            }

            throw new InvalidDataException(
                $"Default colour not found for '{name}'.");
        }

        if (value is JsonArray components)
        {
            return ParseComponents(name, components);
        }

        if (value is JsonValue textValue
            && textValue.TryGetValue<string>(out var text))
        {
            if (text.StartsWith('#'))
            {
                return ParseHtmlColor(name, text);
            }

            if (parsedColors.TryGetValue(text, out var referenced))
            {
                return referenced;
            }

            throw new InvalidDataException(
                $"Prior colour '{text}' referenced by '{name}' was not found.");
        }

        throw new InvalidDataException(
            $"Colour '{name}' must be an RGB/ARGB array, HTML colour, prior name, or null.");
    }

    private static Color ParseComponents(string name, JsonArray components)
    {
        if (components.Count is not 3 and not 4)
        {
            throw new InvalidDataException(
                $"Colour '{name}' must contain three RGB or four ARGB values.");
        }

        Span<byte> values = stackalloc byte[4];
        var offset = components.Count == 3 ? 1 : 0;
        if (offset == 1)
        {
            values[0] = 255;
        }

        for (var index = 0; index < components.Count; index++)
        {
            if (components[index] is not JsonValue component
                || !component.TryGetValue<int>(out var number)
                || number is < 0 or > 255)
            {
                throw new InvalidDataException(
                    $"Colour '{name}' components must be integers from 0 to 255.");
            }

            values[index + offset] = (byte)number;
        }

        return Color.FromArgb(values[0], values[1], values[2], values[3]);
    }

    private static Color ParseHtmlColor(string name, string text)
    {
        var hex = text.AsSpan(1);
        if (hex.Length is not 6 and not 8)
        {
            throw new InvalidDataException(
                $"HTML colour '{name}' must use #RRGGBB or #RRGGBBAA.");
        }

        var red = ParseHexByte(hex[..2]);
        var green = ParseHexByte(hex.Slice(2, 2));
        var blue = ParseHexByte(hex.Slice(4, 2));
        var alpha = hex.Length == 8 ? ParseHexByte(hex.Slice(6, 2)) : (byte)255;
        return Color.FromArgb(alpha, red, green, blue);
    }

    private static byte ParseHexByte(ReadOnlySpan<char> value)
    {
        return byte.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}

public sealed record LegacyOverlayTheme(
    IReadOnlyDictionary<string, Color> Colors,
    bool IsCustom,
    string? Error)
{
    public Color GetColor(string name)
    {
        return Colors.TryGetValue(name, out var color)
            ? color
            : throw new KeyNotFoundException(
                $"The legacy overlay theme does not define '{name}'.");
    }
}
