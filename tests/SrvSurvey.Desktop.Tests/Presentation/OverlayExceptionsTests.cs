using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SrvSurvey.Core.Journal;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform.Overlay;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Presentation;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class OverlayExceptionsTests
{
    private static readonly string[] ExpectedGroups = ["Small", "Medium", "Large", "Vessel / Vehicle"];

    [Theory]
    [InlineData("testbuggy")]
    [InlineData("combat_multicrew_srv_01")]
    [InlineData("mev_rhino")]
    [InlineData("lander01")]
    public void BoardedVehicleOverridesMothershipAndOnFootOverridesParkedVehicle(string vehicle)
    {
        var journal = Journal("python");
        Apply(journal, $$"""{"event":"LaunchSRV","SRVType":"{{vehicle}}","ID":1}""");
        var srv = new EliteStatus { Flags = StatusFlags.InSrv };
        journal.ReconcileVehicleStatus(srv);
        Assert.Equal(vehicle, OverlayVehicleCatalog.Resolve(journal, srv));
        Assert.Equal("on-foot", OverlayVehicleCatalog.Resolve(journal, srv with { Flags2 = StatusFlags2.OnFoot }));
        Assert.Equal(
            "fighters",
            OverlayVehicleCatalog.Resolve(journal, new EliteStatus { Flags = StatusFlags.InFighter })
        );
        Assert.Equal(
            "python",
            OverlayVehicleCatalog.Resolve(journal, new EliteStatus { Flags = StatusFlags.InMainShip })
        );
        Assert.Equal("unknown", OverlayVehicleCatalog.Resolve(journal, null));
    }

    [Theory]
    [InlineData(StatusFlags2.InTaxi)]
    [InlineData(StatusFlags2.InMulticrew)]
    [InlineData(StatusFlags2.TelepresenceMulticrew)]
    [InlineData(StatusFlags2.PhysicalMulticrew)]
    public void PassengerContextsDoNotInheritTheOwnedShipFilter(StatusFlags2 passenger)
    {
        var journal = Journal("python");
        var status = new EliteStatus { Flags = StatusFlags.InMainShip, Flags2 = passenger };
        Assert.Equal("unknown", OverlayVehicleCatalog.Resolve(journal, status));
        Assert.Equal("fighters", OverlayVehicleCatalog.Resolve(journal, status with { Flags = StatusFlags.InFighter }));
    }

    [Theory]
    [InlineData("MediumTransport01", "Medium")]
    [InlineData("SmallCombat01_NX", "Small")]
    [InlineData("Explorer_NX", "Large")]
    [InlineData("PantherMkII", "Large")]
    public void RecentShipSymbolsResolveToTheirSizeGroups(string symbol, string group)
    {
        var id = OverlayVehicleCatalog.Resolve(Journal(symbol), new EliteStatus { Flags = StatusFlags.InMainShip });
        Assert.Equal(symbol.ToLowerInvariant(), id);
        Assert.Equal(group, OverlayVehicleCatalog.All.Single(v => v.Id == id).Group);
        Assert.Equal(OverlayVehicleCatalog.All.Count, OverlayVehicleCatalog.All.Select(v => v.Id).Distinct().Count());
    }

    [Fact]
    public void DedicatedFiregroupsExceptionsInheritOnceAndThenRemainIndependent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new OverlayVehicleSettingsStore(path);
            store.Save(OverlaySettingsCategory.Global, ["python"]);
            _ = new OverlayExceptionsViewModel(store, new OverlayWindowRegistry());
            store.Save(OverlaySettingsCategory.Global, ["anaconda"]);
            var model = new OverlayExceptionsViewModel(store, new OverlayWindowRegistry());
            Assert.True(model.ForCategory(OverlaySettingsCategory.Firegroups).Allows("python"));
            Assert.False(model.ForCategory(OverlaySettingsCategory.Firegroups).Allows("anaconda"));
            model.ForCategory(OverlaySettingsCategory.Firegroups).UncheckAllCommand.Execute(null);
            Assert.Empty(store.Load(OverlaySettingsCategory.Firegroups)!);
            Assert.Contains("anaconda", store.Load(OverlaySettingsCategory.Global)!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DefaultFiregroupsExceptionsDoNotInheritLaterGlobalChangesAfterRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new OverlayVehicleSettingsStore(path);
            _ = new OverlayExceptionsViewModel(store, new OverlayWindowRegistry());
            store.Save(OverlaySettingsCategory.Global, []);
            var restarted = new OverlayExceptionsViewModel(store, new OverlayWindowRegistry());
            Assert.All(
                restarted.ForCategory(OverlaySettingsCategory.Firegroups).Entries,
                entry => Assert.True(entry.IsAllowed)
            );
            Assert.Null(store.Load(OverlaySettingsCategory.Firegroups));
            Assert.Empty(store.Load(OverlaySettingsCategory.Global)!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void CategoryExceptionsPersistAndHideLivePresentationsWithoutLosingIntentOrUserToggle()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var window = new Window();
        try
        {
            File.WriteAllText(path, """{"Unrelated":{"Keep":true}}""");
            var registry = new OverlayWindowRegistry();
            var settings = new OverlayVehicleSettingsStore(path);
            var model = new OverlayExceptionsViewModel(settings, registry);
            var exploration = model.ForCategory(OverlaySettingsCategory.Exploration);
            Assert.All(model.Categories.SelectMany(c => c.Entries), e => Assert.True(e.IsAllowed));
            model.UpdateBoardedVehicle(Journal("python"), new EliteStatus { Flags = StatusFlags.InMainShip });
            registry.Register(window, "PlotBodyInfo");
            registry.SetPresentationVisual(window, new Border());
            Assert.True(registry.ShouldPresent(window));
            exploration.Entries.Single(e => e.Definition.Id == "python").IsAllowed = false;
            Assert.False(registry.ShouldPresent(window));
            Assert.True(registry.IsUserVisible("PlotBodyInfo"));
            Assert.True(registry.ShouldPresent("PlotJumpInfo"));
            Assert.True(registry.GetDecision(window).Reasons.HasFlag(OverlayVisibilityReasons.VehicleExcluded));
            Assert.DoesNotContain("python", settings.Load(OverlaySettingsCategory.Exploration)!);
            Assert.Contains("Unrelated", File.ReadAllText(path));
            model.UpdateBoardedVehicle(Journal("anaconda"), new EliteStatus { Flags = StatusFlags.InMainShip });
            Assert.True(registry.ShouldPresent(window));
            exploration.UncheckAllCommand.Execute(null);
            Assert.False(registry.ShouldPresent(window));
            Assert.Empty(settings.Load(OverlaySettingsCategory.Exploration)!);
            registry.SetUserVisibility("PlotBodyInfo", false);
            exploration.CheckAllCommand.Execute(null);
            Assert.False(registry.ShouldPresent(window));
            registry.SetUserVisibility("PlotBodyInfo", true);
            Assert.True(registry.ShouldPresent(window));
            Assert.Equal(OverlayVehicleCatalog.All.Count, settings.Load(OverlaySettingsCategory.Exploration)!.Count);
            Assert.All(
                new OverlayExceptionsViewModel(settings, new OverlayWindowRegistry())
                    .ForCategory(OverlaySettingsCategory.Exploration)
                    .Entries,
                e => Assert.True(e.IsAllowed)
            );
        }
        finally
        {
            window.Close();
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void ExceptionsDialogAndCompactFiregroupsRenderWithVerticalContent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var model = new OverlayExceptionsViewModel(new OverlayVehicleSettingsStore(path), new OverlayWindowRegistry());
        var dialog = new OverlayExceptionsWindow { DataContext = model.ForCategory(OverlaySettingsCategory.Mining) };
        var firegroup = new MiningActivityOverlayViewModel(null, true);
        var presentation = new MiningActivityOverlayPresentation { DataContext = firegroup };
        var window = new Window { Content = presentation, SizeToContent = SizeToContent.WidthAndHeight };
        try
        {
            dialog.Show();
            using var dialogFrame = dialog.CaptureRenderedFrame();
            Assert.Equal(ExpectedGroups, model.ForCategory(OverlaySettingsCategory.Mining).Groups.Select(g => g.Name));
            Assert.Equal(OverlayVehicleCatalog.All.Count, dialog.GetVisualDescendants().OfType<CheckBox>().Count());
            window.Show();
            using var frame = window.CaptureRenderedFrame();
            var labels = presentation
                .GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible)
                .ToArray();
            Assert.DoesNotContain(labels, t => t.Text == "FIREGROUPS");
            var group = Assert.Single(labels, t => t.Text == "Group A");
            var primary = Assert.Single(labels, t => t.Text == "Primary: Mining laser");
            var secondary = Assert.Single(labels, t => t.Text == "Secondary: Collector limpet");
            Assert.True(group.Bounds.Y < primary.Bounds.Y && primary.Bounds.Y < secondary.Bounds.Y);
            Assert.Equal(220, presentation.Bounds.Width);
            var definition = OverlayLayoutCatalog.GetRequired("PlotMiningFiregroups");
            Assert.Equal("Firegroups", definition.DisplayName);
            Assert.Equal(OverlayLayoutCategory.StatusAndUtilities, definition.Category);
            Assert.Equal(LegacyHorizontalAnchor.Right, definition.DefaultPlacement.Horizontal);
            Assert.Equal(LegacyVerticalAnchor.Bottom, definition.DefaultPlacement.Vertical);
            var output = Environment.GetEnvironmentVariable("SRVSURVEY_EXCEPTIONS_RENDER_OUTPUT");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                using var dialogStream = File.Create(Path.Combine(output, "exceptions.png"));
                dialogFrame!.Save(dialogStream, PngBitmapEncoderOptions.Default);
                using var fireStream = File.Create(Path.Combine(output, "firegroups.png"));
                frame!.Save(fireStream, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            dialog.Close();
            window.Close();
            firegroup.Dispose();
            File.Delete(path);
        }
    }

    private static JournalSessionState Journal(string ship)
    {
        var journal = new JournalSessionState();
        Apply(journal, $$"""{"event":"LoadGame","Commander":"Test","FID":"F1","Ship":"{{ship}}"}""");
        return journal;
    }

    private static void Apply(JournalSessionState journal, string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out var entry, out _));
        journal.Apply(entry!);
    }
}
