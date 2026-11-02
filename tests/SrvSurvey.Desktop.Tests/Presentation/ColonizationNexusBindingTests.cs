using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationNexusBindingTests
{
    /// <summary>Clears and replaces the view context without binding warnings or commands targeting the previous Nexus.</summary>
    [AvaloniaFact]
    public async Task ClearingAndReplacingContextKeepsBindingsQuietAndReconnectsActions()
    {
        var originalApi = new ColonizationNexusEditorViewModelTests.Writer { Existing = Snapshot(true) };
        var replacementApi = new ColonizationNexusEditorViewModelTests.Writer { Existing = Snapshot(false) };
        using var original = new ColonizationNexusViewModel(
            originalApi,
            ColonizationBuildCatalog.LoadEmbedded(),
            _ => { }
        );
        using var replacement = new ColonizationNexusViewModel(
            replacementApi,
            ColonizationBuildCatalog.LoadEmbedded(),
            _ => { }
        );
        original.SetContext(true, "Test", "fixture-only");
        replacement.SetContext(true, "Test", "fixture-only");
        await original.RefreshAsync();
        await replacement.RefreshAsync();
        ILogSink? previous = Logger.Sink;
        var log = new BindingLogSink();
        Logger.Sink = log;
        Window? window = null;
        try
        {
            var view = new ColonizationNexusView();
            window = new Window
            {
                Content = view,
                Width = 1000,
                Height = 850,
            };
            window.Show();
            Render(window);
            Assert.Empty(log.Messages);

            view.DataContext = original;
            Render(window);
            Assert.Equal(2, Buttons(view, "nexus-privacy").Length);
            Assert.All(
                Buttons(view, "nexus-privacy"),
                button => Assert.Equal("Public Nexus. Click to make the entire Nexus private.", ToolTip.GetTip(button))
            );

            view.DataContext = null;
            Render(window);
            Assert.Empty(Buttons(view, "nexus-privacy"));
            Assert.Empty(log.Messages);

            view.DataContext = replacement;
            Render(window);
            Assert.Equal(2, Buttons(view, "nexus-privacy").Length);
            Button privacy = Buttons(view, "nexus-privacy")[0];
            Assert.Equal("Private Nexus. Click to make the entire Nexus public.", ToolTip.GetTip(privacy));
            Assert.True(privacy.IsEnabled);
            Button remove = Buttons(view, "nexus-remove-system")[0];
            Assert.True(remove.IsEnabled);
            remove.Command!.Execute(remove.CommandParameter);
            Render(window);
            Assert.True(view.FindControl<Border>("RemoveNexusSystemPrompt")!.IsVisible);
            Assert.False(original.IsSystemRemovalVisible);
            view.FindControl<Button>("CancelNexusSystemRemoval")!.Command!.Execute(null);
            privacy.Command!.Execute(null);
            await replacement.ActiveTask;
            Render(window);
            Assert.True(replacement.IsPublic);
            Assert.Equal(1, replacementApi.PrivacyChanges);
            Assert.Equal(0, originalApi.PrivacyChanges);
            Assert.All(
                Buttons(view, "nexus-privacy"),
                button => Assert.Equal("Public Nexus. Click to make the entire Nexus private.", ToolTip.GetTip(button))
            );

            view.DataContext = null;
            Render(window);
            Assert.Empty(log.Messages);
        }
        finally
        {
            window?.Close();
            Logger.Sink = previous;
        }
    }

    private static ColonizationNexus Snapshot(bool isPublic) =>
        new()
        {
            Id = isPublic ? "original" : "replacement",
            Name = "Binding fixture",
            Owner = "Test",
            Open = isPublic,
            Commanders = ["Test"],
            Systems = [new() { Name = "Alpha", SystemAddress = 1 }, new() { Name = "Beta", SystemAddress = 2 }],
        };

    private static Button[] Buttons(Control view, string style) =>
        view.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains(style)).ToArray();

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
    }

    /// <summary>Observes binding diagnostics without changing which failures Avalonia reports.</summary>
    private sealed class BindingLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];

        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area == "Binding";

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Log(level, area, source, messageTemplate, []);

        public void Log(
            LogEventLevel level,
            string area,
            object? source,
            string messageTemplate,
            params object?[] propertyValues
        )
        {
            if (IsEnabled(level, area))
            {
                Messages.Add(messageTemplate + " " + string.Join(" | ", propertyValues));
            }
        }
    }
}
