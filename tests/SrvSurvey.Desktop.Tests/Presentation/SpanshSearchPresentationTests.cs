using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Routes;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class SpanshSearchPresentationTests
{
    /// <summary>The carrier-only behavior checkbox updates actual rows, saves its mode, and restores the source table.</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CarrierRestockBehaviorCheckboxReinterpretsRowsAndPersistsOnlyTheOption(bool carrier)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-CarrierFuel-UI-" + Guid.NewGuid().ToString("N"));
        FollowRouteKind kind = carrier ? FollowRouteKind.FleetCarrier : FollowRouteKind.Standard;
        double[] fuelCosts = [0, 400, 400, 400];
        var store = new FollowRouteStore(directory, kind);
        await store.SaveAsAsync(
            (await store.CreateNewAsync("F1")) with
            {
                IsActive = true,
                LastReachedIndex = 0,
                Hops = Enumerable
                    .Range(0, 4)
                    .Select(index => new FollowRouteHop(
                        "Stop " + index,
                        index + 1,
                        null,
                        null,
                        false,
                        false,
                        Carrier: carrier
                            ? new(400, 1200 - index * 400, 1000, 1500, fuelCosts[index], false, false, false, null)
                            : null
                    ))
                    .ToArray(),
            },
            "Fuel test"
        );
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(store),
            new RouteNameImporter(new Resolver()),
            new Client(),
            kind
        );
        var window = new RouteWindow(route);
        try
        {
            await route.UpdateContextAsync("F1", "Stop 0", 1, null);
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            CheckBox checkbox = window.FindControl<CheckBox>("CarrierRestockOnlyWhenLowCheckBox")!;
            Assert.Equal(carrier, checkbox.IsEffectivelyVisible);
            Assert.False(checkbox.IsChecked);
            if (!carrier)
            {
                route.RestockOnlyWhenLow = true;
                Assert.False(route.RestockOnlyWhenLow);
                return;
            }

            checkbox.IsChecked = true;
            Assert.True(route.RestockOnlyWhenLow);
            Assert.Equal($"{600:N0}", route.Hops[1].CarrierFuelRemaining);
            Assert.Equal("YES", route.Hops[2].CarrierRestock);
            Assert.Equal($"{800:N0}", route.Hops[2].CarrierRestockAmount);
            Assert.False(route.HasCarrierFuelPlanMessage);
            Assert.False(route.HasDefinitionChanges);
            Assert.True(route.CanSaveChanges);
            SaveFrame(window, "carrier-restock-only-when-low.png");

            await route.SaveAsync();
            Assert.False(route.IsDirty);
            FollowRouteDocument saved = (await store.LoadAsync("F1")).Route!;
            Assert.True(saved.RestockOnlyWhenLow);
            Assert.All(saved.Hops, hop => Assert.Equal(1000, hop.Carrier!.FuelRemainingTonnes));

            Assert.True(
                JournalEventEnvelope.TryParse(
                    """{"event":"CarrierJump","StarSystem":"Stop 1","SystemAddress":2}""",
                    out JournalEventEnvelope? arrival,
                    out string? error
                ),
                error
            );
            await route.ApplyJournalEventsAsync([arrival!]);
            Assert.Equal(2, route.ReachedCount);
            Assert.True(route.RestockOnlyWhenLow);
            Assert.Equal($"{600:N0}", route.Hops[1].CarrierFuelRemaining);

            checkbox.IsChecked = false;
            Assert.Equal($"{1000:N0}", route.Hops[1].CarrierFuelRemaining);
            Assert.Empty(route.Hops[2].CarrierRestock);
            await route.SaveAsync();
            Assert.False((await store.LoadAsync("F1")).Route!.RestockOnlyWhenLow);
        }
        finally
        {
            window.Close();
            await route.FlushWorkspaceAsync();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Checks that header opens dedicated search and successful search returns to its own itinerary.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeaderOpensDedicatedSearchAndSuccessfulSearchReturnsToItsOwnItinerary(bool carrier)
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-Spansh-UI-" + Guid.NewGuid().ToString("N"));
        var client = new Client();
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(
                new FollowRouteStore(directory, carrier ? FollowRouteKind.FleetCarrier : FollowRouteKind.Standard)
            ),
            new RouteNameImporter(new Resolver()),
            client,
            carrier ? FollowRouteKind.FleetCarrier : FollowRouteKind.Standard,
            plannerClient: client
        );
        var window = new RouteWindow(route);
        try
        {
            await route.UpdateContextAsync("F1", "Sol", 100, new(0, 0, 0));
            window.Show();
            using WriteableBitmap? routeFrame = window.CaptureRenderedFrame();
            Button launcher = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Spansh Search"));
            Assert.True(launcher.IsEffectivelyVisible);
            Assert.True(launcher.IsEnabled);
            launcher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SpanshSearchWindow dialog = Assert.Single(window.OwnedWindows.OfType<SpanshSearchWindow>());
            launcher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(window.OwnedWindows);
            using WriteableBitmap? frame = dialog.CaptureRenderedFrame();
            Assert.NotNull(frame);
            SpanshSearchViewModel search = route.SpanshSearch!;
            Assert.Equal(carrier ? 1 : 6, dialog.FindControl<ComboBox>("PlannerSelector")!.ItemCount);
            search.Fields.Single(field => field.Definition.Key == (carrier ? "destinations" : "to")).Value = "Achenar";
            if (!carrier)
            {
                search.Fields.Single(field => field.Definition.Key == "range").Number = 50;
                foreach (SpanshPlannerDefinition planner in search.Planners)
                {
                    search.SelectedPlanner = planner;
                    using WriteableBitmap? plannerFrame = dialog.CaptureRenderedFrame();
                    Assert.NotNull(plannerFrame);
                    Assert.Equal(planner.Fields.Count, dialog.FindControl<ItemsControl>("PlannerFields")!.ItemCount);
                }
                search.SelectedPlanner = search.Planners[0];
            }
            SaveFrame(window, carrier ? "carrier-workspace.png" : "route-workspace.png");
            SaveFrame(dialog, carrier ? "carrier-search.png" : "neutron-search.png");
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => closed.TrySetResult();
            dialog.FindControl<Button>("SearchButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(dialog.IsVisible);
            Assert.Empty(window.OwnedWindows);
            Assert.Equal(2, route.RouteCount);
            Assert.Equal(carrier ? SpanshRouteKind.FleetCarrier : SpanshRouteKind.Neutron, client.SubmittedKind);
            launcher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(window.OwnedWindows);
            window.Close();
            Assert.Empty(window.OwnedWindows);
        }
        finally
        {
            window.Close();
            route.SpanshSearch!.Dispose();
            await route.FlushWorkspaceAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>Reopening the actual search window refreshes retained fields and follows a newly selected ship.</summary>
    [AvaloniaTheory]
    [InlineData("73.456789", "73.456789")]
    [InlineData("8.25", null)]
    [InlineData("105.5", null)]
    public async Task ReopeningSearchRefreshesAllPlannerRangesFromTheCurrentShip(
        string reportedRange,
        string? expectedRange
    )
    {
        string directory = Path.Combine(Path.GetTempPath(), "SrvSurvey-Spansh-Range-" + Guid.NewGuid().ToString("N"));
        var ship = new CurrentShipExportViewModel();
        var client = new Client();
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(directory)),
            new RouteNameImporter(new Resolver()),
            client,
            plannerClient: client,
            currentShip: ship
        );
        var window = new RouteWindow(route);
        try
        {
            ship.Update(ShipSession("sidewinder", 1, reportedRange), null, false);
            await route.UpdateContextAsync("F1", "Sol", 100, new(0, 0, 0));
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Button launcher = window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Spansh Search"));
            launcher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SpanshSearchWindow dialog = Assert.Single(window.OwnedWindows.OfType<SpanshSearchWindow>());
            SpanshSearchViewModel search = route.SpanshSearch!;
            foreach (
                SpanshPlannerDefinition planner in search.Planners.Where(planner =>
                    planner.Fields.Any(field => field.Key == "range")
                )
            )
            {
                search.SelectedPlanner = planner;
                using WriteableBitmap? plannerFrame = dialog.CaptureRenderedFrame();
                NumericUpDown range = dialog
                    .GetVisualDescendants()
                    .OfType<NumericUpDown>()
                    .Single(control => control.DataContext is SpanshPlannerFieldViewModel { Definition.Key: "range" });
                SpanshPlannerFieldViewModel field = search.Fields.Single(field => field.Definition.Key == "range");
                Assert.Equal(expectedRange ?? string.Empty, field.Value);
                Assert.Equal(field.Number, range.Value);
                range.Value = 35m;
                Assert.Equal(35m, field.Number);
            }
            dialog.Close();
            launcher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog = Assert.Single(window.OwnedWindows.OfType<SpanshSearchWindow>());
            foreach (
                SpanshPlannerDefinition planner in search.Planners.Where(planner =>
                    planner.Fields.Any(field => field.Key == "range")
                )
            )
            {
                search.SelectedPlanner = planner;
                Assert.Equal(
                    expectedRange ?? string.Empty,
                    search.Fields.Single(field => field.Definition.Key == "range").Value
                );
            }
            dialog.Close();
            ship.Update(ShipSession("python", 2, "48.125"), null, false);
            launcher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            dialog = Assert.Single(window.OwnedWindows.OfType<SpanshSearchWindow>());
            using WriteableBitmap? reopenedFrame = dialog.CaptureRenderedFrame();
            Assert.Equal(48.125m, search.Fields.Single(field => field.Definition.Key == "range").Number);
            Assert.Equal(
                48.125m,
                dialog
                    .GetVisualDescendants()
                    .OfType<NumericUpDown>()
                    .Single(control => control.DataContext is SpanshPlannerFieldViewModel { Definition.Key: "range" })
                    .Value
            );
        }
        finally
        {
            window.Close();
            route.SpanshSearch!.Dispose();
            await route.FlushWorkspaceAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>Creates a selected ship with a journal-reported maximum range.</summary>
    private static JournalSessionState ShipSession(string ship, int id, string range)
    {
        var session = new JournalSessionState();
        foreach (
            string json in new[]
            {
                $$"""{"event":"LoadGame","FID":"F1","Ship":"{{ship}}","ShipID":{{id}}}""",
                $$"""{"event":"Loadout","Ship":"{{ship}}","ShipID":{{id}},"MaxJumpRange":{{range}},"Modules":[{"Slot":"FrameShiftDrive","Item":"Int_Hyperdrive_Size2_Class5"}]}""",
            }
        )
        {
            Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out string? error), error);
            session.Apply(entry!);
        }
        return session;
    }

    /// <summary>
    /// Checks that design window and close and cancel controls are usable.
    /// </summary>
    [AvaloniaFact]
    public void DesignWindowAndCloseAndCancelControlsAreUsable()
    {
        var window = new SpanshSearchWindow();
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.False(window.FindControl<Button>("SearchButton")!.IsEnabled);
            window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Cancel search"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => Equals(button.Content, "Close"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Optionally captures the planner layout for visual verification.
    /// </summary>
    private static void SaveFrame(Window window, string name)
    {
        string? output = Environment.GetEnvironmentVariable("SRVSURVEY_SPANSH_RENDER_OUTPUT");
        if (output is null)
        {
            return;
        }
        Directory.CreateDirectory(output);
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        using FileStream stream = File.Create(Path.Combine(output, name));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }

    /// <summary>
    /// Checks that destination plus uses the shared autocomplete and rows support editing and keyboard entry.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(SpanshRouteKind.Tourist)]
    [InlineData(SpanshRouteKind.Neutron)]
    [InlineData(SpanshRouteKind.FleetCarrier)]
    public void DestinationPlusUsesTheSharedAutocompleteAndRowsSupportEditingAndKeyboardEntry(SpanshRouteKind kind)
    {
        var client = new Client();
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(Path.Combine(Path.GetTempPath(), "SrvSurvey-Spansh-Picker"))),
            new RouteNameImporter(new Resolver()),
            client,
            kind == SpanshRouteKind.FleetCarrier ? FollowRouteKind.FleetCarrier : FollowRouteKind.Standard,
            plannerClient: client
        );
        var window = new SpanshSearchWindow(route.SpanshSearch!);
        try
        {
            var search = (SpanshSearchViewModel)window.DataContext!;
            search.SelectedPlanner = search.Planners.Single(planner => planner.Kind == kind);
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            SpanshSystemListViewModel list = search
                .Fields.Single(field => field.IsSystems && field.IsVisible)
                .SystemList!;
            SpanshSystemListEntry editor = window
                .GetVisualDescendants()
                .OfType<SpanshSystemListEntry>()
                .Single(control => ReferenceEquals(control.DataContext, list));
            SystemNameEntry input = editor.FindControl<SystemNameEntry>("DestinationEntry")!;
            Button add = editor.FindControl<Button>("AddDestinationButton")!;
            input.Text = "Col 285 Sector CC-K a38-2";
            Assert.Equal(input.Text, list.Draft);
            Assert.True(add.IsEnabled);
            Click(window, add);
            Assert.Equal("Col 285 Sector CC-K a38-2", Assert.Single(list.Rows).Name);
            Assert.Empty(input.Text);
            input.Text = "Vega";
            Click(window, add);
            using WriteableBitmap? populated = window.CaptureRenderedFrame();
            Assert.Equal(2, editor.FindControl<ItemsControl>("DestinationRows")!.ItemCount);
            SpanshDestinationViewModel first = list.Rows[0];
            Button edit = editor
                .GetVisualDescendants()
                .OfType<Button>()
                .Single(button => ReferenceEquals(button.DataContext, first) && Equals(button.Content, "Edit"));
            Click(window, edit);
            Assert.Equal(first.Name, input.Text);
            input.Text = "Sirius";
            Assert.True(input.FindControl<TextBox>("InputBox")!.Focus());
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal("Sirius", first.Name);
            Assert.Empty(input.Text);
            Assert.False(list.IsEditing);
            SaveFrame(window, kind + "-destinations.png");
        }
        finally
        {
            window.Close();
            route.SpanshSearch!.Dispose();
        }
    }

    /// <summary>
    /// Executes a real pointer click at the target control's rendered location.
    /// </summary>
    private static void Click(Window window, Control control)
    {
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Point point =
            control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The test control is not attached to its window.");
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    /// <summary>
    /// Checks that all planner fields fit at minimum and normal window sizes with website toggle defaults.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(560, 500, false)]
    [InlineData(720, 820, false)]
    [InlineData(560, 500, true)]
    [InlineData(720, 820, true)]
    public void AllPlannerFieldsFitAtMinimumAndNormalWindowSizesWithWebsiteToggleDefaults(
        int width,
        int height,
        bool carrier
    )
    {
        var client = new Client();
        var route = new RouteWorkspaceViewModel(
            new FollowRouteService(new FollowRouteStore(Path.Combine(Path.GetTempPath(), "SrvSurvey-Spansh-Geometry"))),
            new RouteNameImporter(new Resolver()),
            client,
            carrier ? FollowRouteKind.FleetCarrier : FollowRouteKind.Standard,
            plannerClient: client
        );
        var window = new SpanshSearchWindow(route.SpanshSearch!) { Width = width, Height = height };
        try
        {
            var search = (SpanshSearchViewModel)window.DataContext!;
            window.Show();
            foreach (SpanshPlannerDefinition planner in search.Planners)
            {
                search.SelectedPlanner = planner;
                using WriteableBitmap? frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                foreach (
                    SpanshPlannerFieldViewModel field in search.Fields.Where(field =>
                        field.IsRadioChoice && field.IsVisible
                    )
                )
                {
                    RadioButton second = window
                        .GetVisualDescendants()
                        .OfType<RadioButton>()
                        .Single(control => ReferenceEquals(control.DataContext, field.RadioChoices[1]));
                    Assert.True(second.IsEffectivelyVisible);
                    second.IsChecked = true;
                    Assert.Equal(field.Choices[1].Value, field.Value);
                    window
                        .GetVisualDescendants()
                        .OfType<RadioButton>()
                        .Single(control => ReferenceEquals(control.DataContext, field.RadioChoices[0]))
                        .IsChecked = true;
                    Assert.Equal(field.Choices[0].Value, field.Value);
                }
                foreach (
                    SystemNameEntry entry in window
                        .GetVisualDescendants()
                        .OfType<SystemNameEntry>()
                        .Where(entry => entry.IsEffectivelyVisible)
                )
                {
                    Assert.InRange(entry.Bounds.Width, 300, width - 48);
                }
                foreach (
                    NumericUpDown number in window
                        .GetVisualDescendants()
                        .OfType<NumericUpDown>()
                        .Where(number => number.IsEffectivelyVisible)
                )
                {
                    Assert.Equal(220, number.Bounds.Width);
                }
                foreach (
                    SpanshPlannerFieldViewModel field in search.Fields.Where(field =>
                        field.IsBoolean && field.IsVisible
                    )
                )
                {
                    CheckBox toggle = window
                        .GetVisualDescendants()
                        .OfType<CheckBox>()
                        .Single(control => ReferenceEquals(control.DataContext, field));
                    Assert.True(toggle.IsEffectivelyVisible);
                    Assert.Equal(field.IsChecked, toggle.IsChecked);
                    bool initial = field.IsChecked;
                    toggle.IsChecked = !initial;
                    Assert.Equal(!initial, field.IsChecked);
                    toggle.IsChecked = initial;
                }
                Assert.True(window.FindControl<Button>("SearchButton")!.IsEffectivelyVisible);
                if (carrier)
                {
                    SaveFrame(window, "carrier-minimum-" + width + ".png");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class Resolver : IStarSystemResolver
    {
        /// <summary>
        /// Returns deterministic system matches without contacting an external provider.
        /// </summary>
        public Task<IReadOnlyList<StarSystemReference>> SearchAsync(
            string query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<StarSystemReference>>([]);
    }

    private sealed class Client : ISpanshPlannerClient, ISpanshRouteClient
    {
        public SpanshRouteKind SubmittedKind { get; private set; }

        /// <summary>
        /// Returns a synthetic planner job while exposing the test's submission controls.
        /// </summary>
        public Task<SpanshRouteReference> SubmitAsync(
            SpanshPlannerRequest request,
            CancellationToken cancellationToken = default
        )
        {
            SubmittedKind = request.Planner.Kind;
            return Task.FromResult(new SpanshRouteReference(Guid.NewGuid(), SubmittedKind));
        }

        /// <summary>
        /// Returns deterministic itinerary fixtures for search and handoff tests.
        /// </summary>
        public Task<IReadOnlyList<FollowRouteHop>> GetRouteAsync(
            SpanshRouteReference route,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<IReadOnlyList<FollowRouteHop>>([
                new("Sol", 100, new(0, 0, 0), null, false, false),
                new("Achenar", 200, new(1, 2, 3), null, false, false),
            ]);
    }
}
