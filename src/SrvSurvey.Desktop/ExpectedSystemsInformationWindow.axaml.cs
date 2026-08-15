using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using SrvSurvey.Desktop.Localization;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SrvSurvey.Desktop;

public sealed partial class ExpectedSystemsInformationWindow : Window
{
    private const string RavenAccentBrush = nameof(RavenAccentBrush);
    private const string ExampleTemplate =
        "For example the end system of the {0} boxel is: {1} so you would enter 7640 and choose APPLY";
    private static readonly string[] ExampleSystemNames =
        ["Phimbee AA-A d0", "Phimbee AA-A d7640"];
    private static readonly Regex ExamplePlaceholderPattern = new(
        @"\{([01])\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public ExpectedSystemsInformationWindow()
    {
        InitializeComponent();
        BuildExampleSentence();
    }

    private void BuildExampleSentence()
    {
        var template = LocalizationCatalog.Translate(ExampleTemplate);
        var inlines = new InlineCollection();
        var position = 0;
        foreach (Match match in ExamplePlaceholderPattern.Matches(template))
        {
            if (match.Index > position)
            {
                inlines.Add(template[position..match.Index]);
            }

            var systemIndex = int.Parse(
                match.Groups[1].Value,
                CultureInfo.InvariantCulture);
            inlines.Add(CreateSystemNameInline(ExampleSystemNames[systemIndex]));
            position = match.Index + match.Length;
        }

        if (position < template.Length)
        {
            inlines.Add(template[position..]);
        }

        ExpectedSystemsExample.Inlines = inlines;
    }

    private StackPanel CreateSystemNameInline(string systemName)
    {
        var openingQuote = CreateUnlocalizedTextBlock("\"");
        var name = CreateUnlocalizedTextBlock(systemName);
        name.FontWeight = FontWeight.SemiBold;
        if (this.TryFindResource(RavenAccentBrush, out var accentResource)
            && accentResource is IBrush accentBrush)
        {
            name.Foreground = accentBrush;
        }

        var closingQuote = CreateUnlocalizedTextBlock("\"");
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Children =
            {
                openingQuote,
                name,
                closingQuote,
            },
        };
    }

    private static TextBlock CreateUnlocalizedTextBlock(string text)
    {
        var textBlock = new TextBlock { Text = text };
        LocalizationBehavior.SetEnabled(textBlock, false);
        return textBlock;
    }

    private void Close_Click(object? sender, RoutedEventArgs eventArgs)
    {
        Close();
    }
}
