using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Behaviors;
using SrvSurvey.Desktop.Controls;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class SystemNameEntryTests
{
    [Fact]
    public void ConstructorRejectsInvalidDependenciesBeforeInitialization()
    {
        Assert.Throws<ArgumentNullException>(() => new SystemNameEntry(null!, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SystemNameEntry(new StubClient([]), TimeSpan.FromMilliseconds(-1))
        );
    }

    [AvaloniaFact]
    public async Task SelectionReplacesId64WithResolvedNameAndRetainsAddress()
    {
        var client = new StubClient([new SystemNameSuggestion("Sol", 10477373803, "EDSM")]);
        var control = new SystemNameEntry(client, TimeSpan.Zero);
        var selections = new List<(string? Name, long? Address)>();
        control.SystemSelected += (_, _) => selections.Add((control.Text, control.SelectedSystemAddress));
        var window = new Window
        {
            Content = control,
            Width = 400,
            Height = 200,
        };
        try
        {
            window.Show();
            TextBox input = control.FindControl<TextBox>("InputBox")!;
            Assert.True(input.Focus());
            input.Text = "10477373803";
            await WaitUntilAsync(() => control.HasSuggestions);

            Assert.Equal("1 suggestion from EDSM.", control.Status);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

            Assert.Equal("Sol", control.Text);
            Assert.Equal(10477373803, control.SelectedSystemAddress);
            Assert.False(control.HasSuggestions);
            Assert.Equal("Selected Sol · id64 10477373803.", control.Status);
            Assert.Equal([("Sol", (long?)10477373803)], selections);
        }
        finally
        {
            window.Close();
        }
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
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (!condition() && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "Timed out waiting for system suggestions.");
    }

    private sealed class StubClient(IReadOnlyList<SystemNameSuggestion> suggestions) : ISystemNameSuggestionClient
    {
        public Task<IReadOnlyList<SystemNameSuggestion>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(suggestions);
        }
    }
}
