using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.Tests.ViewModels;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class ColonizationCarrierManifestPresentationTests
{
    [AvaloniaFact]
    public async Task ColonyFilterTogglesKnownNonColonyGoodsAndRefreshEnablesAtTheBoundary()
    {
        var client = new ColonizationCarrierManifestTests.ManifestClient
        {
            Linked =
            [
                ColonizationCarrierManifestTests.ManifestClient.First with
                {
                    Name = "ABCD",
                    Access = "All",
                    Cargo = new()
                    {
                        ["steel"] = 100,
                        ["platinum"] = 7,
                        ["metalloys"] = 3,
                        ["tritium"] = 25,
                    },
                },
            ],
        };
        var clock = new CarrierClock();
        using var model = new ColonizationCarrierManifestViewModel(
            client,
            "Cmdr",
            "key",
            () => true,
            _ => { },
            refreshCooldown: new ColonizationCarrierRefreshCooldown(timeProvider: clock)
        );
        var window = new ColonizationCarrierManifestWindow(model) { Width = 504 };
        window.Resources["RavenControlAccentBrush"] = Brushes.White;
        window.Resources["RavenAccentForegroundBrush"] = Brushes.Black;
        try
        {
            window.Show();
            await model.InitializeAsync();
            using WriteableBitmap? initial = window.CaptureRenderedFrame();
            Assert.NotNull(initial);
            ToggleButton filter = window.FindControl<ToggleButton>("HideNonColonyCommodities")!;
            Assert.False(filter.IsChecked);
            Click(window, filter);
            Assert.True(filter.IsChecked);
            using WriteableBitmap? filtered = window.CaptureRenderedFrame();
            ContentPresenter presenter = filter
                .GetVisualDescendants()
                .OfType<ContentPresenter>()
                .First(item => Equals(item.Content, filter.Content));
            Assert.Equal(Colors.Black, Assert.IsType<ISolidColorBrush>(presenter.Foreground, exactMatch: false).Color);
            Click(window, filter);
            Assert.False(model.HideNonColonyCommodities);
            Assert.Contains(
                model.Groups,
                group => group.Name == "Metals" && group.Rows.Any(row => row.Key == "platinum")
            );
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(
                Path.Combine(Path.GetTempPath(), "srvsurvey-carrier-manifest-filter-preview.png"),
                PngBitmapEncoderOptions.Default
            );
            Click(window, filter);
            Assert.Single(model.Groups.SelectMany(group => group.Rows));
            Button refresh = window.FindControl<Button>("RefreshCarrier")!;
            Assert.DoesNotContain("Squadron", Assert.IsType<string>(ToolTip.GetTip(refresh)), StringComparison.Ordinal);
            Assert.True(refresh.IsEnabled);
            Click(window, refresh);
            Assert.False(refresh.IsEnabled);
            clock.Now += TimeSpan.FromMinutes(5) - TimeSpan.FromMilliseconds(1);
            model.UpdateRefreshAvailability();
            Assert.False(refresh.IsEnabled);
            clock.Now += TimeSpan.FromMilliseconds(1);
            model.UpdateRefreshAvailability();
            Assert.True(refresh.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class CarrierClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [AvaloniaFact]
    public async Task AmountEntryValidatesPastingReplacementOverflowAndClearing()
    {
        using ColonizationCarrierManifestViewModel model = ColonizationCarrierManifestTests.Create(new());
        var window = new ColonizationCarrierManifestWindow(model);
        try
        {
            window.Show();
            await model.InitializeAsync();
            window.UpdateLayout();
            NonNegativeIntegerBox amount = window
                .GetVisualDescendants()
                .OfType<NonNegativeIntegerBox>()
                .Single(box => box.DataContext is CarrierManifestRowViewModel { Key: "steel" });
            Assert.True(amount.Focus());
            IClipboard clipboard = window.Clipboard!;
            amount.SelectionStart = 1;
            amount.SelectionEnd = 2;
            await clipboard.SetTextAsync("24");
            await amount.PasteNumericTextAsync();
            Assert.Equal("1240", amount.Text);
            Assert.Equal(3, amount.CaretIndex);
            amount.SelectAll();
            await clipboard.SetTextAsync("24a");
            amount.Paste();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("1240", amount.Text);
            Assert.Equal(0, amount.SelectionStart);
            Assert.Equal(4, amount.SelectionEnd);
            await clipboard.SetTextAsync("2147483647");
            await amount.PasteNumericTextAsync();
            Assert.Equal("2147483647", amount.Text);
            window.KeyTextInput("8");
            Assert.Equal("2147483647", amount.Text);
            amount.SelectAll();
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Assert.Equal(string.Empty, amount.Text);
            Assert.False(model.CanSave);
            window.KeyTextInput("0");
            Assert.Equal("0", amount.Text);
            Assert.True(model.CanSave);
            amount.IsReadOnly = true;
            await amount.PasteNumericTextAsync();
            Assert.Equal("0", amount.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TextToggleReturnsToTheTableAndCommodityPickerHasUnselectableCategoryHeaders()
    {
        using ColonizationCarrierManifestViewModel model = ColonizationCarrierManifestTests.Create(new());
        var window = new ColonizationCarrierManifestWindow(model);
        try
        {
            window.Show();
            await model.InitializeAsync();
            window.UpdateLayout();
            Button toggle = window.FindControl<Button>("ToggleManifestText")!;
            Click(window, toggle);
            Assert.True(model.TextMode);
            Click(window, toggle);
            Assert.False(model.TextMode);
            Click(window, toggle);
            window.FindControl<TextBox>("ManifestBulkText")!.Text = "Steel nope";
            Click(window, toggle);
            Assert.False(model.TextMode);
            Assert.Equal(100, ColonizationCarrierManifestTests.Row(model, "steel").Amount);
            ComboBox picker = window.FindControl<ComboBox>("AddManifestCommodity")!;
            picker.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(model.CommodityChoices[0].IsHeader);
            Assert.Equal("Chemicals", model.CommodityChoices[0].Name);
            ComboBoxItem header = Assert.IsType<ComboBoxItem>(picker.ContainerFromIndex(0));
            Assert.False(header.IsEnabled);
            ColonizationManifestCommodity chosen = model.CommodityChoices[1].Commodity!;
            picker.SelectedIndex = 1;
            Assert.Equal(0, ColonizationCarrierManifestTests.Row(model, chosen.Key).Amount);
            Assert.DoesNotContain(model.AvailableCommodities, item => item.Key == chosen.Key);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Click(Window window, Control control)
    {
        window.UpdateLayout();
        Point point = control
            .TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!
            .Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AmountEntryKeepsTheCaretAfterTypingAndInsertsAtTheClickedDigit(int typingMode)
    {
        using ColonizationCarrierManifestViewModel model = ColonizationCarrierManifestTests.Create(new());
        var window = new ColonizationCarrierManifestWindow(model);
        try
        {
            window.Show();
            await model.InitializeAsync();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            TextBox amount = window
                .GetVisualDescendants()
                .OfType<TextBox>()
                .Single(box => box.DataContext is CarrierManifestRowViewModel { Key: "steel" });
            Assert.True(amount.Focus());
            amount.SelectAll();
            var clientRequest = new TextInputMethodClientRequestedEventArgs
            {
                RoutedEvent = InputElement.TextInputMethodClientRequestedEvent,
            };
            amount.RaiseEvent(clientRequest);
            Assert.NotNull(clientRequest.Client);
            foreach (string digit in new[] { "5", "0", "0" })
            {
                if (typingMode == 2)
                {
                    clientRequest.Client.SetPreeditText(digit, 1);
                }
                window.KeyTextInput(digit);
                if (typingMode == 2)
                {
                    clientRequest.Client.SetPreeditText(string.Empty, 0);
                }
                Dispatcher.UIThread.RunJobs();
                if (typingMode == 1)
                {
                    continue;
                }
                using (WriteableBitmap? frame = window.CaptureRenderedFrame())
                {
                    Assert.NotNull(frame);
                }
                Assert.Equal(amount.Text!.Length, amount.CaretIndex);
                TextPresenter typedPresenter = amount.GetVisualDescendants().OfType<TextPresenter>().Single();
                Rect textEnd = typedPresenter.TextLayout.HitTestTextPosition(amount.Text.Length);
                Assert.Equal(
                    typedPresenter.TranslatePoint(textEnd.Position, amount)!.Value.X,
                    clientRequest.Client.CursorRectangle.X,
                    1
                );
            }
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Assert.Equal("500", amount.Text);
            TextPresenter presenter = amount.GetVisualDescendants().OfType<TextPresenter>().Single();
            Rect finalEnd = presenter.TextLayout.HitTestTextPosition(amount.Text!.Length);
            Assert.Equal(
                presenter.TranslatePoint(finalEnd.Position, amount)!.Value.X,
                clientRequest.Client.CursorRectangle.X,
                1
            );
            Rect digitBounds = presenter.TextLayout.HitTestTextPosition(1);
            Point click = presenter.TranslatePoint(new Point(digitBounds.X, digitBounds.Center.Y), window)!.Value;
            window.MouseDown(click, MouseButton.Left);
            window.MouseUp(click, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, amount.CaretIndex);
            window.KeyTextInput("2");
            Assert.Equal("5200", amount.Text);
            Assert.Equal(2, amount.CaretIndex);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("a")]
    [InlineData("-")]
    [InlineData(".")]
    [InlineData("5a")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    public async Task AmountEntryRejectsNonNumericInputWithoutMovingTheCaret(string input)
    {
        using ColonizationCarrierManifestViewModel model = ColonizationCarrierManifestTests.Create(new());
        var window = new ColonizationCarrierManifestWindow(model);
        try
        {
            window.Show();
            await model.InitializeAsync();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            TextBox amount = window
                .GetVisualDescendants()
                .OfType<TextBox>()
                .Single(box => box.DataContext is CarrierManifestRowViewModel { Key: "steel" });
            Assert.True(amount.Focus());
            amount.CaretIndex = 1;
            window.KeyTextInput(input);
            Assert.Equal("100", amount.Text);
            Assert.Equal(1, amount.CaretIndex);
            Assert.True(model.CanSave);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CompactPopupRendersGroupedRowsSwitchesCarriersAndSavesThroughItsButton()
    {
        var client = new ColonizationCarrierManifestTests.ManifestClient
        {
            Linked =
            [
                ColonizationCarrierManifestTests.ManifestClient.First with
                {
                    Name = "N4W-T0Z",
                    DisplayName = "D1RE ECLIPSE",
                    Owner = "Fenris Nihilus",
                    SystemName = "Col 285 Sector HI-N b9-0",
                    Access = "All",
                    Cargo = new()
                    {
                        ["evacuationshelter"] = 75,
                        ["ceramiccomposites"] = 375,
                        ["cmmcomposite"] = 333,
                        ["emergencypowercells"] = 63,
                        ["basicmedicines"] = 109,
                        ["combatstabilisers"] = 25,
                        ["microcontrollers"] = 55,
                        ["structuralregulators"] = 542,
                        ["battleweapons"] = 42,
                        ["reactivearmour"] = 42,
                    },
                },
                ColonizationCarrierManifestTests.ManifestClient.Second,
            ],
        };
        var local = new ColonizationLocalCarrierCargo();
        local.Observe(
            ColonizationCarrierManifestTests.LocalCarrier(0) with
            {
                Callsign = "N4W-T0Z",
                Cargo = client
                    .Linked[0]
                    .Cargo.Select(item => new FrontierInventorySnapshot("Commodity", item.Key, item.Value + 5, 0))
                    .ToArray(),
            },
            DateTimeOffset.UnixEpoch
        );
        using ColonizationCarrierManifestViewModel model = ColonizationCarrierManifestTests.Create(
            client,
            localCargo: local
        );
        var window = new ColonizationCarrierManifestWindow(model);
        try
        {
            window.Show();
            await model.InitializeAsync();
            using (WriteableBitmap? frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
                frame.Save(
                    Path.Combine(Path.GetTempPath(), "srvsurvey-carrier-manifest-preview.png"),
                    PngBitmapEncoderOptions.Default
                );
            }
            ComboBox selector = window.FindControl<ComboBox>("CarrierSelector")!;
            Assert.Equal(2, selector.ItemCount);
            Assert.Equal("N4W-T0Z", model.RawName);
            SystemNameEntry system = window.FindControl<SystemNameEntry>("CarrierSystem")!;
            Assert.Equal("Col 285 Sector HI-N b9-0", system.Text);
            Assert.True(window.FindControl<Button>("SaveCarrierManifest")!.IsEnabled);
            Assert.True(selector.Bounds.Width < 380);
            TextBlock journal = window
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .First(text => text.Classes.Contains("journal-quantity"));
            Assert.Equal("80", journal.Text);
            Assert.True(journal.Bounds.Width < 64);
            selector.SelectedIndex = 1;
            Assert.Equal("XYZ-987", model.RawName);
            Assert.Equal("Sol", system.Text);
            system.FindControl<TextBox>("InputBox")!.Text = " Alpha Centauri ";
            Assert.Equal(" Alpha Centauri ", model.SystemName);
            Assert.Empty(client.Writes);
            window.FindControl<Button>("SaveCarrierManifest")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(["location:Alpha Centauri", "cargo"], client.Writes);
            Assert.False(window.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WorkspaceOpenerReusesWindowInstallsSavedCargoAndClosesOnProfileChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-carrier-" + Guid.NewGuid().ToString("N"));
        using var http = new HttpClient(new RavenHandler());
        using var model = new ColonizationViewModel(
            new ColonizationSettingsStore(Path.Combine(directory, "ui.json")),
            new RavenColonialClient(http)
        );
        var owner = new Window();
        using var coordinator = new ColonizationProjectPreviewWindowCoordinator(model, owner);
        try
        {
            owner.Show();
            Assert.False(model.EditCarrierManifestCommand.CanExecute(null));
            model.IsEnabled = true;
            model.SetCommanderProfile("FID", true, "key");
            await model.SetCommanderAsync("Cmdr");
            Assert.False(model.FleetCarrierCargoSyncEnabled);
            await model.SeedLinkedCarrierCargoFromCapiAsync(
                Account("Cmdr", ColonizationCarrierManifestTests.LocalCarrier(300))
            );
            model.EditCarrierManifestCommand.Execute(null);
            ColonizationCarrierManifestWindow window = coordinator.CarrierManifestWindow!;
            Assert.NotNull(window);
            var editor = (ColonizationCarrierManifestViewModel)window.DataContext!;
            Assert.Equal("Other Cmdr", editor.Owner);
            Assert.Equal("300", ColonizationCarrierManifestTests.Row(editor, "steel").JournalAmount);
            model.UpdateStatus(new EliteStatus { Flags = StatusFlags.InMainShip });
            model.ApplyJournalEvents(
                [
                    Event(
                        "Docked",
                        "\"MarketID\":42,\"SystemAddress\":1,\"StarSystem\":\"Sol\",\"StationName\":\"ABC-123\",\"StationType\":\"FleetCarrier\""
                    ),
                    Event("MarketBuy", "\"MarketID\":42,\"Type\":\"steel\",\"Count\":25"),
                ],
                "Cmdr"
            );
            Assert.Equal("275", ColonizationCarrierManifestTests.Row(editor, "steel").JournalAmount);
            Assert.Equal("100", ColonizationCarrierManifestTests.Row(editor, "steel").AmountText);
            window.WindowState = WindowState.Minimized;
            model.EditCarrierManifestCommand.Execute(null);
            Assert.Same(window, coordinator.CarrierManifestWindow);
            Assert.Equal(WindowState.Normal, window.WindowState);
            ColonizationCarrierManifestTests.Row(editor, "steel").AmountText = "250";
            await editor.SaveAsync();
            Assert.Equal(250, Assert.Single(model.LinkedFleetCarriers).Cargo["steel"]);
            Assert.Null(coordinator.CarrierManifestWindow);
            model.EditCarrierManifestCommand.Execute(null);
            Assert.NotNull(coordinator.CarrierManifestWindow);
            var reopened = (ColonizationCarrierManifestViewModel)coordinator.CarrierManifestWindow.DataContext!;
            Assert.Equal("275", ColonizationCarrierManifestTests.Row(reopened, "steel").JournalAmount);
            model.SetCommanderProfile("FID", true, "new-key");
            Assert.Null(coordinator.CarrierManifestWindow);
            model.EditCarrierManifestCommand.Execute(null);
            var rekeyed = (ColonizationCarrierManifestViewModel)coordinator.CarrierManifestWindow!.DataContext!;
            Assert.Equal("275", ColonizationCarrierManifestTests.Row(rekeyed, "steel").JournalAmount);
            model.SetCommanderProfile("OtherFID", true, "key");
            Assert.Null(coordinator.CarrierManifestWindow);
            using ColonizationCarrierManifestViewModel isolated = model.CreateCarrierManifest();
            await isolated.InitializeAsync();
            Assert.Equal("N/A", ColonizationCarrierManifestTests.Row(isolated, "steel").JournalAmount);
            await model.SeedLinkedCarrierCargoFromCapiAsync(
                Account("Someone Else", ColonizationCarrierManifestTests.LocalCarrier(999))
            );
            Assert.Equal("N/A", ColonizationCarrierManifestTests.Row(isolated, "steel").JournalAmount);
            await model.SeedLinkedCarrierCargoFromCapiAsync(
                Account("Cmdr", ColonizationCarrierManifestTests.LocalCarrier(50))
            );
            model.ApplyJournalEvents(
                [Event("MarketBuy", "\"MarketID\":42,\"Type\":\"steel\",\"Count\":5")],
                "Someone Else"
            );
            Assert.Equal("50", ColonizationCarrierManifestTests.Row(isolated, "steel").JournalAmount);
            model.IsEnabled = false;
            Assert.Null(coordinator.CarrierManifestWindow);
            Assert.False(editor.CanSave);
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

    private static FrontierAccountSnapshot Account(string commander, FrontierCarrierSnapshot carrier) =>
        new(commander, 0, 0, false, true, "Sol", carrier.Callsign, null, [], [], [], carrier, DateTimeOffset.UnixEpoch);

    private static JournalEventEnvelope Event(string name, string fields)
    {
        string json = "{\"event\":\"" + name + "\"," + fields + "}";
        using var document = JsonDocument.Parse(json);
        return new(name, DateTimeOffset.UnixEpoch.AddMinutes(1), json, document.RootElement.Clone());
    }

    private sealed class RavenHandler : HttpMessageHandler
    {
        private const string Carrier =
            """{"marketId":42,"name":"ABC-123","displayName":"Team supply","owner":"Other Cmdr","systemName":"Sol","cargo":{"steel":100}}""";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri!.AbsolutePath;
            string payload = path switch
            {
                var value when value.EndsWith("/fc/all", StringComparison.Ordinal) => "[" + Carrier + "]",
                var value when value.EndsWith("/fc/42", StringComparison.Ordinal) => Carrier,
                var value when value.EndsWith("/fc/42/cargo", StringComparison.Ordinal) => "{\"steel\":250}",
                var value when value.EndsWith("/primary", StringComparison.Ordinal) => "null",
                _ => "[]",
            };
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                }
            );
        }
    }
}
