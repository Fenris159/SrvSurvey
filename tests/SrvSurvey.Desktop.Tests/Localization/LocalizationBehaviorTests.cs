using Avalonia.Controls;
using Avalonia.Data;
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
    public void EnabledBehaviorDoesNotReplaceBindings()
    {
        LocalizationCatalog.Initialize("de");
        var textBlock = new TextBlock();
        textBlock.Bind(
            TextBlock.TextProperty,
            new Binding(nameof(BoundText.Value))
            {
                Source = new BoundText("Bodies"),
            });

        LocalizationBehavior.SetEnabled(textBlock, true);

        Assert.NotNull(BindingOperations.GetBindingExpressionBase(
            textBlock,
            TextBlock.TextProperty));
    }

    public void Dispose()
    {
        LocalizationCatalog.Initialize("en");
    }

    private sealed record BoundText(string Value);
}
