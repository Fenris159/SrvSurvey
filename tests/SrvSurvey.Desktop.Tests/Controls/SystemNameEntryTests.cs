using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Behaviors;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class SystemNameEntryTests
{
    [AvaloniaFact]
    public async Task SelectionReplacesId64WithResolvedNameAndRetainsAddress()
    {
        var client = new StubClient(
        [
            new SystemNameSuggestion("Sol", 10477373803, "EDSM"),
        ]);
        var control = new SystemNameEntry(client, TimeSpan.Zero)
        {
            Text = "10477373803",
        };
        await WaitUntilAsync(() => control.HasSuggestions);

        Assert.Equal("1 suggestion from EDSM.", control.Status);
        Assert.True(control.SelectCurrentSuggestion());

        Assert.Equal("Sol", control.Text);
        Assert.Equal(10477373803, control.SelectedSystemAddress);
        Assert.False(control.HasSuggestions);
        Assert.Equal(
            "Selected Sol · id64 10477373803.",
            control.Status);
    }

    [AvaloniaFact]
    public void CopyBehaviorMarksPopulatedLinkWithAClipboardHint()
    {
        var button = new Button();

        ClipboardCopyBehavior.SetText(button, "Sol");

        Assert.Equal("Sol", ClipboardCopyBehavior.GetText(button));
        Assert.Equal("Click to copy", ToolTip.GetTip(button));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for system suggestions.");
    }

    private sealed class StubClient(
        IReadOnlyList<SystemNameSuggestion> suggestions)
        : ISystemNameSuggestionClient
    {
        public Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(suggestions);
        }
    }
}
