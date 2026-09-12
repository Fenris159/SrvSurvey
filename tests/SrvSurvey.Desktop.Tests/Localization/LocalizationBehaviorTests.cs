using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using SrvSurvey.Desktop.Localization;

namespace SrvSurvey.Desktop.Tests.Localization;

[Collection(LocalizationTestCollection.Name)]
public sealed class LocalizationBehaviorTests : IDisposable
{
    [Fact]
    public void EnabledBehaviorTranslatesLiteralControlText()
    {
        LocalizationCatalog.Initialize("de");
        var textBlock = new TextBlock { Text = "Bodies" };

        LocalizationBehavior.SetEnabled(textBlock, true);

        Assert.Equal("Himmelskörper", textBlock.Text);
    }

    [Fact]
    public void EnabledBehaviorTranslatesBindingUpdatesWithoutReplacingBinding()
    {
        LocalizationCatalog.Initialize("de");
        var textBlock = new TextBlock();
        textBlock.Bind(
            TextBlock.TextProperty,
            new Binding(nameof(BoundText.Value)) { Source = new BoundText("Bodies") }
        );

        LocalizationBehavior.SetEnabled(textBlock, true);

        Assert.NotNull(BindingOperations.GetBindingExpressionBase(textBlock, TextBlock.TextProperty));

        textBlock.SetCurrentValue(TextBlock.TextProperty, "Close");

        Assert.Equal("Schließen", textBlock.Text);
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(textBlock, TextBlock.TextProperty));
    }

    [Fact]
    public void EnabledBehaviorTranslatesLegacyAcceleratorVariant()
    {
        LocalizationCatalog.Initialize("de");
        var button = new Button { Content = "Close" };

        LocalizationBehavior.SetEnabled(button, true);

        Assert.Equal("Schließen", button.Content);
    }

    [Fact]
    public void EnabledBehaviorTranslatesContextMenuHeaders()
    {
        LocalizationCatalog.Initialize("de");
        var menuItem = new MenuItem { Header = "New bookmark" };

        LocalizationBehavior.SetEnabled(menuItem, true);

        Assert.Equal("Neues Lesezeichen", menuItem.Header);
    }

    [Fact]
    public void EnabledBehaviorTranslatesTooltipsAndAccessibilityNames()
    {
        LocalizationCatalog.Initialize("de");
        var button = new Button();
        ToolTip.SetTip(button, "Open category overlay settings");
        AutomationProperties.SetName(button, "Open category overlay settings");

        LocalizationBehavior.SetEnabled(button, true);

        Assert.Equal("Öffnen Sie die Kategorie-Overlay-Einstellungen", ToolTip.GetTip(button));
        Assert.Equal("Öffnen Sie die Kategorie-Overlay-Einstellungen", AutomationProperties.GetName(button));
    }

    [AvaloniaFact]
    public void EnabledBehaviorPreservesKdeOverlayWindowTitle()
    {
        const string title = "SrvSurvey Galaxy Map search guidance overlay";
        LocalizationCatalog.Initialize("qps-ploc");
        var window = new Window { Title = title };

        LocalizationBehavior.SetEnabled(window, true);

        Assert.Equal(title, window.Title);
    }

    public void Dispose()
    {
        LocalizationCatalog.Initialize("en");
    }

    private sealed record BoundText(string Value);
}
