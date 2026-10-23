using System.Net;
using System.Text.RegularExpressions;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Exploration;

public sealed record ExplorationPoiDescriptionBlock(string Text, Uri? ImageUri = null, string Caption = "");

public sealed class ExplorationPoiDescription
{
    /// <summary>Captures source-ordered description blocks and the plain text summary.</summary>
    private ExplorationPoiDescription(IReadOnlyList<ExplorationPoiDescriptionBlock> blocks)
    {
        Blocks = blocks;
        Text = string.Join("\n\n", blocks.Where(block => block.ImageUri is null).Select(block => block.Text));
    }

    public string Text { get; }
    public IReadOnlyList<ExplorationPoiDescriptionBlock> Blocks { get; }

    /// <summary>Separates readable description text from supported remote image references.</summary>
    public static ExplorationPoiDescription Parse(string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        markup = HiddenHtml().Replace(markup, string.Empty);
        var blocks = new List<ExplorationPoiDescriptionBlock>();
        int position = 0;
        foreach (Match image in Images().Matches(markup))
        {
            AddText(blocks, markup[position..image.Index]);
            string url = image.Groups["url"].Value.Trim('<', '>');
            string caption = image.Groups["caption"].Value;
            if (image.Value.StartsWith('<'))
            {
                url = Attribute(image.Value, "src");
                caption = Attribute(image.Value, "alt");
            }
            if (TryImageUri(WebUtility.HtmlDecode(url), out Uri? uri))
            {
                blocks.Add(new ExplorationPoiDescriptionBlock(string.Empty, uri, CleanText(caption)));
            }
            position = image.Index + image.Length;
        }
        AddText(blocks, markup[position..]);
        return new ExplorationPoiDescription(blocks);
    }

    /// <summary>Adds a nonempty cleaned text block to the description sequence.</summary>
    private static void AddText(List<ExplorationPoiDescriptionBlock> blocks, string markup)
    {
        string text = CleanText(markup);
        if (text.Length > 0)
        {
            blocks.Add(new ExplorationPoiDescriptionBlock(text));
        }
    }

    /// <summary>Removes hidden content and markup while preserving readable paragraphs and decoded entities.</summary>
    private static string CleanText(string markup)
    {
        string text = HiddenHtml().Replace(markup, string.Empty);
        text = UnsupportedImages().Replace(text, string.Empty);
        text = HtmlBreaks().Replace(text, "\n\n");
        text = HtmlTags().Replace(text, string.Empty);
        text = Links().Replace(text, "${label}");
        text = text.Replace("*", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);
        return WebUtility.HtmlDecode(text).Trim();
    }

    /// <summary>Reads a supported image attribute from the source HTML fragment.</summary>
    private static string Attribute(string tag, string name) =>
        Attributes()
            .Matches(tag)
            .Cast<Match>()
            .FirstOrDefault(match =>
                string.Equals(match.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase)
            )
            ?.Groups["value"]
            .Value
        ?? string.Empty;

    /// <summary>Accepts only HTTP image addresses, including provider-relative references.</summary>
    private static bool TryImageUri(string url, out Uri? uri)
    {
        if (url.StartsWith('/'))
        {
            url = new Uri(new Uri(WellKnownUris.EdastroPoiPagePrefix), url).AbsoluteUri;
        }
        return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme is "http" or "https";
    }

    /// <summary>Matches HTML or Markdown image references for source-ordered extraction.</summary>
    private static readonly Regex ImagesPattern = new(
        "!\\[(?<caption>[^\\]\\r\\n]*)\\]\\(\\s*(?<url><[^>\\r\\n]+>|(?:[^()\\s]|\\([^()\\r\\n]*\\))+)(?:[ \\t]+[\"'][^\"'\\r\\n]*[\"'])?\\s*\\)|<img\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>Returns the cached linear-time pattern for images.</summary>
    private static Regex Images() => ImagesPattern;

    /// <summary>Matches HTML attribute names and quoted or unquoted values.</summary>
    private static readonly Regex AttributesPattern = new(
        "\\b(?<name>src|alt)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>Returns the cached linear-time pattern for attributes.</summary>
    private static Regex Attributes() => AttributesPattern;

    /// <summary>Matches script and style sections excluded from description previews.</summary>
    private static readonly Regex HiddenHtmlPattern = new(
        "<(?:script|style)\\b[^>]*>.*?</(?:script|style)\\s*>",
        RegexOptions.IgnoreCase
            | RegexOptions.Singleline
            | RegexOptions.CultureInvariant
            | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>Returns the cached linear-time pattern for hidden html.</summary>
    private static Regex HiddenHtml() => HiddenHtmlPattern;

    /// <summary>Matches remaining image markup that should not appear as preview text.</summary>
    private static readonly Regex UnsupportedImagesPattern = new(
        "!\\[[^\\]]*\\](?:\\([^)]*\\)|\\[[^\\]]*\\])",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>Returns the cached linear-time pattern for unsupported images.</summary>
    private static Regex UnsupportedImages() => UnsupportedImagesPattern;

    /// <summary>Matches HTML line breaks and paragraph endings for readable text conversion.</summary>
    private static readonly Regex HtmlBreaksPattern = new(
        "</(?:p|div|h[1-6]|li)\\s*>|<br\\s*/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>Returns the cached linear-time pattern for html breaks.</summary>
    private static Regex HtmlBreaks() => HtmlBreaksPattern;

    /// <summary>Matches remaining HTML tags removed from plain description text.</summary>
    private static readonly Regex HtmlTagsPattern = new(
        "<[^>]*>",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>Returns the cached linear-time pattern for html tags.</summary>
    private static Regex HtmlTags() => HtmlTagsPattern;

    /// <summary>Matches Markdown links whose visible label is retained in plain text.</summary>
    private static readonly Regex LinksPattern = new(
        "\\[(?<label>[^\\]]*)\\]\\([^)]*\\)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>Returns the cached linear-time pattern for links.</summary>
    private static Regex Links() => LinksPattern;
}
