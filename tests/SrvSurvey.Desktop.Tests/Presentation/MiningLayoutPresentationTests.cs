using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.Theming;
using SrvSurvey.Desktop.ViewModels;
using SrvSurvey.Desktop.Views;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MiningLayoutPresentationTests
{
    [AvaloniaTheory]
    [InlineData(1180, true)]
    [InlineData(660, false)]
    public void SessionTablesUseAvailableWidthAndSupportingToolsReflow(int width, bool paired)
    {
        using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        LoadCommander(model);
        Assert.True(model.MiningWorkspace.Restore(MiningStore.Export(SampleData())), model.MiningWorkspace.Status);
        var view = new MiningView { DataContext = model };
        Window window = CreateWindow(view, width);
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            ScrollViewer scroller = view.FindControl<ScrollViewer>("MaterialsHorizontalScroller")!;
            Grid table = Assert.IsType<Grid>(scroller.Content);
            Assert.Equal(Math.Max(640, scroller.Viewport.Width), table.Bounds.Width, 1);
            Grid header = view.FindControl<Grid>("MaterialsHeader")!;
            Assert.InRange(header.ColumnDefinitions[0].ActualWidth, 140, 400);
            Assert.InRange(header.ColumnDefinitions[2].ActualWidth, 90, 250);
            Assert.Equal(paired, scroller.Extent.Width <= scroller.Viewport.Width);
            AssertPairedOrStacked(
                view.FindControl<Border>("SessionCargo")!,
                view.FindControl<Border>("SessionAdjustments")!,
                paired
            );
            SaveFrame(frame, $"session-{width}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SessionReadoutsFollowPauseResumeAndEndControls()
    {
        using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        LoadCommander(model);
        Assert.True(model.MiningWorkspace.Restore(MiningStore.Export(SampleData())), model.MiningWorkspace.Status);
        var view = new MiningView { DataContext = model };
        Window window = CreateWindow(view, 1180);
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal("Paused", view.FindControl<TextBlock>("SessionStateLabel")!.Text);
            Assert.Equal("01:00:00", view.FindControl<TextBlock>("SessionElapsed")!.Text);
            Assert.Equal("30 t", view.FindControl<TextBlock>("SessionRefinedTons")!.Text);
            Assert.Equal("1", view.FindControl<TextBlock>("SessionAsteroids")!.Text);
            model.MiningWorkspace.AddAsteroidCommand.Execute(null);
            using WriteableBitmap? corrected = window.CaptureRenderedFrame();
            Assert.Equal("2", view.FindControl<TextBlock>("SessionAsteroids")!.Text);
            model.MiningWorkspace.PauseCommand.Execute(null);
            using WriteableBitmap? resumed = window.CaptureRenderedFrame();
            Assert.Equal("Active", view.FindControl<TextBlock>("SessionStateLabel")!.Text);
            model.MiningWorkspace.Settings.AutoSwitchTabs = false;
            model.MiningWorkspace.StopCommand.Execute(null);
            using WriteableBitmap? stopped = window.CaptureRenderedFrame();
            Assert.Equal("No active session", view.FindControl<TextBlock>("SessionStateLabel")!.Text);
            Assert.Equal("00:00:00", view.FindControl<TextBlock>("SessionElapsed")!.Text);
            Assert.Equal("0 t", view.FindControl<TextBlock>("SessionRefinedTons")!.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CargoAndEngineeringTablesShowCapitalizedNamesAndKeepRawIdentifiers()
    {
        using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        LoadCommander(model);
        Assert.True(model.MiningWorkspace.Restore(MiningStore.Export(SampleData())), model.MiningWorkspace.Status);
        var view = new MiningView { DataContext = model };
        Window window = CreateWindow(view, 1180);
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            ListBox cargo = view.FindControl<ListBox>("CargoRows")!;
            Assert.Collection(
                cargo
                    .GetVisualDescendants()
                    .OfType<Grid>()
                    .Where(grid => grid.Classes.Contains("table-row"))
                    .Select(grid => Assert.IsType<TextBlock>(grid.Children[0]).Text)
                    .Order(),
                name => Assert.Equal("Limpets", name),
                name => Assert.Equal("Platinum", name)
            );
            Assert.Collection(
                model.MiningWorkspace.Cargo.Select(item => item.Name).Order(),
                name => Assert.Equal("drones", name),
                name => Assert.Equal("platinum", name)
            );
            Expander engineering = Assert.Single(
                view.GetVisualDescendants().OfType<Expander>(),
                expander => Equals(expander.Header, "Engineering materials collected")
            );
            engineering.IsExpanded = true;
            using WriteableBitmap? expanded = window.CaptureRenderedFrame();
            ListBox materials = view.FindControl<ListBox>("EngineeringMaterialRows")!;
            Assert.Contains(materials.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Iron");
            Assert.Equal("iron", Assert.Single(model.MiningWorkspace.EngineeringMaterials).Name);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ReportsKeepTheSelectedSessionsNotesAndScreenshotsTogether()
    {
        using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        LoadCommander(model);
        Assert.True(model.MiningWorkspace.Restore(MiningStore.Export(SampleData())), model.MiningWorkspace.Status);
        model.MiningWorkspace.SelectedTab = 1;
        var view = new MiningView { DataContext = model };
        Window window = CreateWindow(view, 1180);
        try
        {
            window.Show();
            using WriteableBitmap? initial = window.CaptureRenderedFrame();
            TextBox notes = view.FindControl<TextBox>("ReportNotesEditor")!;
            Assert.False(notes.IsEffectivelyEnabled);
            Assert.False(notes.IsEffectivelyVisible);
            ListBox history = view.FindControl<ListBox>("HistoryRows")!;
            history.SelectedIndex = 0;
            using WriteableBitmap? selected = window.CaptureRenderedFrame();
            Assert.NotNull(selected);
            Assert.True(notes.IsEffectivelyEnabled);
            Assert.True(notes.IsEffectivelyVisible);
            Assert.Equal("Platinum run near the High RES.", notes.Text);
            Assert.Equal(1, view.FindControl<ItemsControl>("ReportScreenshotRows")!.ItemCount);
            AssertPairedOrStacked(
                view.FindControl<StackPanel>("ReportNotes")!,
                view.FindControl<StackPanel>("ReportAttachments")!,
                true
            );
            notes.Text = "Return with more limpets.";
            Button save = Assert.Single(
                view.GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "Save notes")
            );
            save.Command!.Execute(save.CommandParameter);
            Assert.Equal("Return with more limpets.", Assert.Single(model.MiningWorkspace.History).Notes);
            SaveFrame(selected, "reports-selected");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LongReportLibrariesKeepTheirHeadersAlignedAndDetailsReachable()
    {
        using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        LoadCommander(model);
        MiningCommanderData data = SampleData();
        MiningSession session = data.History[0];
        data.History.Clear();
        data.History.AddRange(
            Enumerable
                .Range(1, 20)
                .Select(index =>
                    session with
                    {
                        Id = Guid.Parse($"00000000-0000-0000-0000-{index:D12}"),
                        Started = session.Started.AddDays(-index),
                        Ended = session.Ended!.Value.AddDays(-index),
                    }
                )
                .ToList()
        );
        Assert.True(model.MiningWorkspace.Restore(MiningStore.Export(data)), model.MiningWorkspace.Status);
        model.MiningWorkspace.SelectedTab = 1;
        var view = new MiningView { DataContext = model };
        Window window = CreateWindow(view, 1180);
        try
        {
            window.Show();
            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            ListBox history = view.FindControl<ListBox>("HistoryRows")!;
            Assert.Equal(20, history.ItemCount);
            Assert.InRange(history.Bounds.Height, 200, 280);
            Grid header = view.FindControl<Grid>("HistoryHeader")!;
            Grid row = history.GetVisualDescendants().OfType<Grid>().First(grid => grid.Classes.Contains("table-row"));
            Assert.Equal(header.Bounds.Width, row.Bounds.Width, 1);
            history.SelectedIndex = 19;
            history.ScrollIntoView(history.SelectedItem!);
            using WriteableBitmap? lastSession = window.CaptureRenderedFrame();
            Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000020"), model.MiningWorkspace.SelectedSession!.Id);
            Assert.Equal("Platinum run near the High RES.", view.FindControl<TextBox>("ReportNotesEditor")!.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1180, true)]
    [InlineData(660, false)]
    public void SettingsReflowWithoutLosingThresholdOrAudioEditors(int width, bool paired)
    {
        using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        model.MiningWorkspace.SelectedTab = 7;
        model.MiningWorkspace.Settings.Thresholds["platinum"] = 30;
        var view = new MiningView { DataContext = model };
        Window window = CreateWindow(view, width);
        try
        {
            window.Show();
            using WriteableBitmap? sessionSettings = window.CaptureRenderedFrame();
            Assert.NotNull(sessionSettings);
            AssertPairedOrStacked(
                view.FindControl<Border>("MiningAutomationSettings")!,
                view.FindControl<Border>("MiningSessionShortcuts")!,
                paired
            );
            SaveFrame(sessionSettings, $"settings-session-{width}");
            view.FindControl<TabControl>("MiningSettingsTabs")!.SelectedIndex = 1;
            using WriteableBitmap? announcements = window.CaptureRenderedFrame();
            Assert.NotNull(announcements);
            AssertPairedOrStacked(
                view.FindControl<Border>("AnnouncementThresholds")!,
                view.FindControl<Border>("AnnouncementEvents")!,
                paired
            );
            Assert.Equal(1, view.FindControl<ListBox>("ThresholdRows")!.ItemCount);
            Assert.Equal(3, view.FindControl<ComboBox>("ChimeSelector")!.ItemCount);
            NumericUpDown slots = view.FindControl<NumericUpDown>("PersistentProspectSlotsInput")!;
            slots.Value = 5;
            Assert.Equal(5, model.MiningWorkspace.Settings.PersistentProspectSlots);
            CheckBox hide = view.FindControl<CheckBox>("HideBelowThresholdInput")!;
            hide.IsChecked = true;
            Assert.True(model.MiningWorkspace.HideProspectsBelowThreshold);
            Assert.InRange(view.FindControl<ComboBox>("LocalVoiceSelector")!.Bounds.Width, 250, width);
            SaveFrame(announcements, $"settings-announcements-{width}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1180, 2)]
    [InlineData(660, 4)]
    public void ResDescriptionWrapsWithoutWideningTheReferencePage(int width, int lines)
    {
        using MainWindowViewModel model = MainWindowViewModelTestBuilder.Create(null, _ => { });
        model.MiningWorkspace.SelectedTab = 6;
        var view = new MiningView { DataContext = model };
        Window window = CreateWindow(view, width);
        try
        {
            window.Show();
            using WriteableBitmap? collapsed = window.CaptureRenderedFrame();
            Expander res = view.FindControl<Expander>("ResourceExtractionSites")!;
            ScrollViewer page = Assert.Single(res.GetVisualAncestors().OfType<ScrollViewer>());
            double originalWidth = page.Bounds.Width;
            res.IsExpanded = true;
            res.BringIntoView();
            using WriteableBitmap? expanded = window.CaptureRenderedFrame();
            Assert.NotNull(expanded);
            TextBlock description = view.FindControl<TextBlock>("ResourceExtractionDescription")!;
            Assert.Equal(lines, description.TextLayout.TextLines.Count);
            Assert.Equal(originalWidth, page.Bounds.Width, 1);
            Assert.InRange(page.Extent.Width, 0, page.Viewport.Width + 1);
            Assert.EndsWith("do not themselves prove an overlap.", description.Text);
            SaveFrame(expanded, $"reference-res-{width}");
        }
        finally
        {
            window.Close();
        }
    }

    private static Window CreateWindow(MiningView view, int width)
    {
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = 1100,
        };
        RavenThemeDefinition theme = RavenThemeCatalog.Get("monochrome-dark");
        window.RequestedThemeVariant = ThemeVariant.Dark;
        foreach (
            KeyValuePair<string, string> entry in new Dictionary<string, string>
            {
                ["RavenWindowBrush"] = theme.WindowColor,
                ["RavenSurfaceBrush"] = theme.SurfaceColor,
                ["RavenRaisedSurfaceBrush"] = theme.RaisedSurfaceColor,
                ["RavenTextBrush"] = theme.TextColor,
                ["RavenMutedTextBrush"] = theme.MutedTextColor,
                ["RavenAccentBrush"] = theme.AccentColor,
                ["RavenBorderBrush"] = theme.BorderColor,
                ["RavenControlAccentBrush"] = theme.ControlAccentColor,
                ["RavenSecondaryFillBrush"] = theme.SecondaryFillColor,
                ["RavenAccentForegroundBrush"] = theme.AccentForegroundColor,
            }
        )
        {
            window.Resources[entry.Key] = new SolidColorBrush(Color.Parse(entry.Value));
        }
        return window;
    }

    private static void AssertPairedOrStacked(Control first, Control second, bool paired)
    {
        Point position = second.TranslatePoint(default, first)!.Value;
        if (paired)
        {
            Assert.Equal(0, position.Y, 1);
            Assert.InRange(position.X, first.Bounds.Width + 20, first.Bounds.Width + 28);
        }
        else
        {
            Assert.Equal(0, position.X, 1);
            Assert.True(position.Y >= first.Bounds.Height + 20);
        }
    }

    private static void LoadCommander(MainWindowViewModel model)
    {
        var journal = new JournalSessionState();
        Assert.True(
            JournalEventEnvelope.TryParse(
                """{"event":"LoadGame","FID":"MiningLayout","Commander":"Layout","Ship":"python"}""",
                out JournalEventEnvelope? load,
                out _
            )
        );
        journal.Apply(load!);
        var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
        model.MiningWorkspace.Apply(
            new JournalMonitorUpdate(null, [load!], ship, null, null, null, [], true),
            journal,
            new CargoSnapshot(
                new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero),
                "Cargo",
                "Ship",
                42,
                [new CargoItem("platinum", "Platinum", 30, 0), new CargoItem("drones", "Limpets", 12, 0)]
            ),
            ship
        );
    }

    private static MiningCommanderData SampleData()
    {
        var start = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        var completed = new MiningSession
        {
            Started = start,
            Ended = start.AddHours(1),
            System = "Delkar",
            Ring = "Delkar 7 A Ring",
            Ship = "Python",
            Notes = "Platinum run near the High RES.",
            Screenshots = ["mining-run.png"],
            Prospects =
            [
                new MiningProspect(
                    start.AddMinutes(5),
                    [new MiningMaterial("Platinum", 42), new MiningMaterial("Osmium", 11)],
                    "",
                    "High"
                ),
            ],
            Collections =
            [
                new MiningCollectionEntry(start.AddMinutes(30), "platinum", 30, false),
                new MiningCollectionEntry(start.AddMinutes(30), "iron", 3, true),
            ],
        };
        return new MiningCommanderData
        {
            Current = completed with { Ended = null, PausedAt = start.AddHours(1) },
            History = [completed],
        };
    }

    private static void SaveFrame(WriteableBitmap frame, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("SRVSURVEY_MINING_LAYOUT_OUTPUT");
        if (directory is null)
        {
            return;
        }
        Directory.CreateDirectory(directory);
        using FileStream file = File.Create(Path.Combine(directory, $"mining-{name}.png"));
        frame.Save(file, PngBitmapEncoderOptions.Default);
    }
}
