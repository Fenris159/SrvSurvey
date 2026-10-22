using System.Net;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationNexusCreationPresentationTests
{
    [AvaloniaTheory]
    [InlineData(760)]
    [InlineData(620)]
    public void EditorReusesAutocompleteAndListCommandsAndFitsItsMinimumSize(int width)
    {
        var api = new ColonizationNexusEditorViewModelTests.Writer();
        using var model = new ColonizationNexusEditorViewModel(
            api,
            "Fenris",
            "key",
            () => true,
            _ => Task.CompletedTask,
            [
                new()
                {
                    MarketId = 42,
                    Name = "ABC-123",
                    DisplayName = "Test Carrier",
                },
            ]
        );
        var window = new ColonizationNexusEditorWindow(model) { Width = width, Height = 850 };
        try
        {
            window.Show();
            ToggleSwitch visibility = window.FindControl<ToggleSwitch>("PrivateNexus")!;
            Assert.True(visibility.IsChecked);
            visibility.IsChecked = false;
            Assert.False(model.IsPrivate);
            visibility.IsChecked = true;
            Assert.True(model.IsPrivate);
            window.FindControl<TextBox>("NewNexusName")!.Text = "New Nexus";
            window.FindControl<TextBox>("NewNexusNotes")!.Text = "Test route";
            SpanshSystemListEntry systems = window.FindControl<SpanshSystemListEntry>("NexusSystems")!;
            SystemNameEntry autocomplete = systems.FindControl<SystemNameEntry>("DestinationEntry")!;
            Assert.Equal("Add Systems", autocomplete.PlaceholderText);
            autocomplete.Text = "Sol";
            systems.FindControl<Button>("AddDestinationButton")!.Command!.Execute(null);
            Assert.Equal("Sol", Assert.Single(model.Systems.Rows).Name);
            window.FindControl<TextBox>("NewNexusCommander")!.Text = "Other";
            window.FindControl<Button>("AddNexusCommander")!.Command!.Execute(null);
            Assert.Equal("Other", Assert.Single(model.Commanders.Rows).Name);
            window.FindControl<Button>("AddNexusCarrier")!.Command!.Execute(null);
            Assert.Equal(42, Assert.Single(model.Carriers).MarketId);
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-nexus-create-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
            ScrollViewer scroll = window.FindControl<ScrollViewer>("NexusEditorScroll")!;
            scroll.ScrollToEnd();
            using WriteableBitmap? bottom = window.CaptureRenderedFrame();
            Assert.NotNull(bottom);
            bottom.Save(
                Path.Combine(Path.GetTempPath(), $"srvsurvey-nexus-create-lists-{width}.png"),
                PngBitmapEncoderOptions.Default
            );
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            Assert.True(window.FindControl<Button>("SubmitNexus")!.IsEnabled);
            Button removeCarrier = window
                .FindControl<ItemsControl>("NexusCarrierRows")!
                .GetVisualDescendants()
                .OfType<Button>()
                .Single();
            Assert.NotNull(removeCarrier.Command);
            removeCarrier.Command.Execute(removeCarrier.CommandParameter);
            Assert.Empty(model.Carriers);
            window.FindControl<Button>("CancelNexusCreation")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsVisible);
            Assert.Equal(0, api.Creates);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task NativeCreateButtonOpensOneWindowPublishesAndRefreshesTheNewSelection()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-nexus-create-" + Guid.NewGuid().ToString("N"));
        var api = new CreationHandler();
        using var http = new HttpClient(api);
        using var model = new ColonizationViewModel(
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json")),
            new RavenColonialClient(http)
        );
        var view = new ColonizationNexusView { DataContext = model.Nexus };
        var owner = new Window
        {
            Content = view,
            Width = 1000,
            Height = 800,
        };
        using var coordinator = new ColonizationProjectPreviewWindowCoordinator(model, owner);
        try
        {
            owner.Show();
            Button create = view.FindControl<Button>("CreateNexus")!;
            Assert.False(create.Command!.CanExecute(null));
            model.IsEnabled = true;
            model.SetCommanderProfile("F1", true, "fenris-personal-test-key");
            await model.SetCommanderAsync("Fenris");
            Assert.True(create.Command.CanExecute(null));
            create.Command.Execute(null);
            ColonizationNexusEditorWindow editor = Assert.IsType<ColonizationNexusEditorWindow>(
                coordinator.NexusEditorWindow
            );
            editor.WindowState = WindowState.Minimized;
            create.Command.Execute(null);
            Assert.Same(editor, coordinator.NexusEditorWindow);
            Assert.Equal(WindowState.Normal, editor.WindowState);
            editor.FindControl<TextBox>("NewNexusName")!.Text = "New Nexus";
            Assert.IsType<ColonizationNexusEditorViewModel>(editor.DataContext).Systems.Load("Sol");
            editor.FindControl<Button>("SubmitNexus")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(coordinator.NexusEditorWindow);
            Assert.Equal(1, api.Creates);
            Assert.Equal(5, api.Updates);
            Assert.Equal(1, api.IndexReads);
            Assert.Equal("new", model.Nexus.SelectedNexus!.Id);
            Assert.Equal("New Nexus", model.Nexus.Detail!.Nexus.Name);
            model.Nexus.EditNexusCommand.Execute(null);
            Assert.True(
                Assert.IsType<ColonizationNexusEditorViewModel>(coordinator.NexusEditorWindow!.DataContext).IsEditing
            );
            create.Command.Execute(null);
            Assert.False(
                Assert.IsType<ColonizationNexusEditorViewModel>(coordinator.NexusEditorWindow.DataContext).IsEditing
            );
            model.SetCommanderProfile("F1", true, "new-personal-key");
            Assert.Null(coordinator.NexusEditorWindow);
            create.Command.Execute(null);
            Assert.NotNull(coordinator.NexusEditorWindow);
            await model.SetCommanderAsync(null);
            Assert.Null(coordinator.NexusEditorWindow);
            model.Nexus.CreateNexusCommand.Execute(null);
            Assert.Null(coordinator.NexusEditorWindow);
        }
        finally
        {
            owner.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private sealed class CreationHandler : HttpMessageHandler
    {
        public int Creates { get; private set; }
        public int Updates { get; private set; }
        public int IndexReads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            string response;
            if (path.StartsWith("/api/Chain/", StringComparison.Ordinal))
            {
                Assert.Equal("fenris-personal-test-key", Assert.Single(request.Headers.GetValues("rcc-key")));
                if (request.Method == HttpMethod.Put)
                {
                    Creates++;
                }
                if (request.Method == HttpMethod.Post)
                {
                    Updates++;
                }
                response = JsonSerializer.Serialize(ColonizationNexusEditorViewModelTests.Writer.Snapshot());
            }
            else if (path == "/api/Cmdr/nexus")
            {
                IndexReads++;
                response = """[{"id":"new","name":"New Nexus","owner":"Fenris"}]""";
            }
            else
            {
                response = path.EndsWith("/primary", StringComparison.Ordinal) ? "null" : "[]";
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) }
            );
        }
    }
}
