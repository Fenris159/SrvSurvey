using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace SrvSurvey.Desktop.Controls;

/// <summary>Preview for the editor's Discord formatting: emphasis, code, quotes, lists, and timestamps.</summary>
public sealed partial class DiscordMessagePreview : TextBlock
{
    private static readonly string[] markers = ["`", "***", "**", "__", "~~", "*"];
    private const string EscapablePunctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
    public static readonly StyledProperty<string> MarkdownProperty = AvaloniaProperty.Register<
        DiscordMessagePreview,
        string
    >(nameof(Markdown), "");
    public string Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }
    public static readonly StyledProperty<DateTimeOffset> ReferenceTimeProperty = AvaloniaProperty.Register<
        DiscordMessagePreview,
        DateTimeOffset
    >(nameof(ReferenceTime), DateTimeOffset.UnixEpoch);
    public DateTimeOffset ReferenceTime
    {
        get => GetValue(ReferenceTimeProperty);
        set => SetValue(ReferenceTimeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty || change.Property == ReferenceTimeProperty)
        {
            InlineCollection inlines = Inlines ??= [];
            inlines.Clear();
            int offset = 0;
            AppendUntil(inlines, Markdown, ref offset, null, 0);
        }
    }

    private bool AppendUntil(
        InlineCollection target,
        string text,
        ref int offset,
        string? closing,
        int depth,
        int minimumClosingOffset = 0
    )
    {
        int plainStart = offset;
        while (offset < text.Length)
        {
            if (closing is not null && offset >= minimumClosingOffset && IsClosing(text, offset, closing))
            {
                AppendText(target, text[plainStart..offset]);
                offset += closing.Length;
                return true;
            }
            int start = offset;
            Inline? inline = ReadInline(text, ref offset, depth);
            if (inline is null)
            {
                offset++;
                continue;
            }
            AppendText(target, text[plainStart..start]);
            target.Add(inline);
            plainStart = offset;
        }
        AppendText(target, text[plainStart..]);
        return closing is null;
    }

    private Inline? ReadInline(string text, ref int offset, int depth)
    {
        if (text[offset] == '\\' && offset + 1 < text.Length && EscapablePunctuation.Contains(text[offset + 1]))
        {
            string literal = text[offset + 1].ToString();
            offset += 2;
            return new Run(literal);
        }
        Match special = SpecialFormatting().Match(text, offset);
        if (special.Success)
        {
            offset += special.Length;
            if (special.Groups["time"].Success)
            {
                return new Run(FormatTime(special)) { Background = new SolidColorBrush(Color.Parse("#4B4D55")) };
            }
            return new Run(special.Value.StartsWith('>') ? "┃ " : "• ");
        }
        if (depth > 8)
        {
            return null;
        }
        return ReadFormatting(text, ref offset, depth);
    }

    private Inline? ReadFormatting(string text, ref int offset, int depth)
    {
        foreach (string marker in markers)
        {
            if (!text.AsSpan(offset).StartsWith(marker, StringComparison.Ordinal))
            {
                continue;
            }
            Inline? inline = marker == "`" ? ReadCode(text, ref offset) : ReadEmphasis(text, ref offset, marker, depth);
            if (inline is not null)
            {
                return inline;
            }
        }
        return null;
    }

    private Span? ReadEmphasis(string text, ref int offset, string marker, int depth)
    {
        int start = offset;
        int runEnd = start;
        while (runEnd < text.Length && text[runEnd] == marker[0])
        {
            runEnd++;
        }
        if (text.IndexOf(marker, runEnd, StringComparison.Ordinal) < 0)
        {
            return null;
        }
        offset += marker.Length;
        Span span = CreateSpan(marker);
        if (AppendUntil(span.Inlines, text, ref offset, marker, depth + 1, runEnd) && span.Inlines.Count > 0)
        {
            return span;
        }
        offset = start;
        return null;
    }

    private static bool IsClosing(string text, int offset, string marker) =>
        text.AsSpan(offset).StartsWith(marker, StringComparison.Ordinal)
        && (
            marker != "*"
            || !text.AsSpan(offset).StartsWith("**", StringComparison.Ordinal)
            || text.AsSpan(offset).StartsWith("***", StringComparison.Ordinal)
        );

    private static void AppendText(InlineCollection target, string text)
    {
        if (text.Length > 0)
        {
            target.Add(new Run(text));
        }
    }

    private static Span? ReadCode(string text, ref int offset)
    {
        int end = text.IndexOf('`', offset + 1);
        if (end <= offset + 1)
        {
            return null;
        }
        Span span = CreateSpan("`");
        span.Inlines.Add(new Run(text[(offset + 1)..end]));
        offset = end + 1;
        return span;
    }

    private static Span CreateSpan(string marker)
    {
        var span = new Span();
        switch (marker)
        {
            case "***":
                span.FontWeight = FontWeight.Bold;
                span.FontStyle = FontStyle.Italic;
                break;
            case "**":
                span.FontWeight = FontWeight.Bold;
                break;
            case "*":
                span.FontStyle = FontStyle.Italic;
                break;
            case "__":
                span.TextDecorations = Avalonia.Media.TextDecorations.Underline;
                break;
            case "~~":
                span.TextDecorations = Avalonia.Media.TextDecorations.Strikethrough;
                break;
            case "`":
                span.FontFamily = new FontFamily("monospace");
                span.Background = new SolidColorBrush(Color.Parse("#1E1F22"));
                break;
        }
        return span;
    }

    private string FormatTime(Match match)
    {
        if (
            !long.TryParse(
                match.Groups["time"].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long seconds
            ) || seconds is < -62135596800 or > 253402300799
        )
        {
            return match.Value;
        }
        var value = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (match.Groups["style"].Value != "R")
        {
            return value.ToLocalTime().ToString("f", CultureInfo.CurrentCulture);
        }
        double minutes = (value - ReferenceTime).TotalMinutes;
        return minutes >= 0 ? $"in {Math.Ceiling(minutes):0} minutes" : $"{Math.Ceiling(-minutes):0} minutes ago";
    }

    [GeneratedRegex(
        @"\G(?:<t:(?<time>-?\d+):(?<style>[FR])>|(?<line>^(?:>|-) ))",
        RegexOptions.Multiline | RegexOptions.CultureInvariant
    )]
    private static partial Regex SpecialFormatting();
}
