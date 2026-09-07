using SrvSurvey.Core.Firegroups;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class FiregroupsWorkspaceViewModelTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly JournalSessionState journal = new();

    [Fact]
    public void EquippedFilterIncludesDuplicateHardpointsUtilityAndLimpetsButNotShieldCellBanksOrCoreModules()
    {
        var vm = Create();
        var options = vm.Primary[0].Options;
        Assert.Equal(4, options.Count);
        Assert.Equal(2, options.Count(m => m.Name.StartsWith("Pulse Laser")));
        Assert.Contains(options, m => m.Slot == "TinyHardpoint1");
        Assert.Contains(options, m => m.Name.Contains("Limpet Controller"));
        Assert.DoesNotContain(options, m => m.Symbol.Contains("shieldcellbank") || m.Slot == "PowerPlant");
        Assert.Equal(4, options.Select(m => m.Display).Distinct().Count());
    }

    [Fact]
    public void MultiModuleGroupsSaveReloadAndFollowStatusLetters()
    {
        var vm = Create();
        vm.Primary[0].SelectedModule = vm.Primary[0].Options[0];
        vm.AddPrimaryCommand.Execute(null);
        vm.Primary[1].SelectedModule = vm.Primary[1].Options[1];
        vm.Secondary[0].SelectedModule = vm.Secondary[0].Options[3];
        vm.AddGroupCommand.Execute(null);
        Assert.Equal("B", vm.GroupLetter);
        vm.Primary[0].SelectedModule = vm.Primary[0].Options[2];
        vm.ConfigurationName = "Mining setup";
        vm.SaveCommand.Execute(null);
        Assert.StartsWith("Saved", vm.Status);
        Assert.Equal(2, Assert.Single(vm.SavedProfiles).Profile.Groups.Count);
        var reloaded = new FiregroupsWorkspaceViewModel(directory);
        Feed(reloaded, [], 1);
        Assert.Equal("Mining setup", reloaded.ActiveProfile!.Name);
        Assert.Equal("Survey Python", reloaded.ActiveProfile.Ship.Name);
        Assert.Equal(2, reloaded.ActiveProfile.Groups.Single(g => g.Number == 0).Primary.Count);
        using var overlay = new MiningActivityOverlayViewModel(null, true, reloaded);
        Assert.Equal("Group B", overlay.GroupLabel);
        Assert.Contains("Heat Sink", overlay.PrimaryLabel);
        Feed(reloaded, [], 0);
        Assert.Equal("Group A", overlay.GroupLabel);
        Assert.Contains("MediumHardpoint1", overlay.PrimaryLabel);
        Assert.Contains("MediumHardpoint2", overlay.PrimaryLabel);
        Assert.Contains("Collector Limpet", overlay.SecondaryLabel);
    }

    [Fact]
    public void NamedConfigurationsSwitchByShipIdAndPreserveUnsavedDraftAcrossSwitches()
    {
        var vm = Create(); SaveOne(vm, "First ship");
        vm.ConfigurationName = "Unfinished edit";
        Feed(vm, [Loadout(2, "Other Python")]);
        Assert.Null(vm.ActiveProfile);
        Assert.Equal("", vm.ConfigurationName);
        SaveOne(vm, "Second ship");
        Feed(vm, [Loadout(1, "Renamed Python")]);
        Assert.Equal("First ship", vm.ActiveProfile!.Name);
        Assert.Equal("Unfinished edit", vm.ConfigurationName);
        Assert.Contains("Renamed Python", vm.ShipSummary);
        Assert.Equal(2, vm.SavedProfiles.Count);
        Feed(vm, [], flags: StatusFlags.InSrv);
        Assert.False(vm.ShouldShow);
        Feed(vm, [], flags: StatusFlags.InMainShip);
        Assert.True(vm.ShouldShow);
    }

    [Fact]
    public void AddRemoveRowsAndLettersKeepGroupsAndSavedNamesEditable()
    {
        var vm = Create();
        vm.PreviousGroupCommand.Execute(null); Assert.Equal("H", vm.GroupLetter);
        vm.NextGroupCommand.Execute(null); Assert.Equal("A", vm.GroupLetter);
        vm.AddPrimaryCommand.Execute(null); Assert.Equal(2, vm.Primary.Count);
        vm.Primary[1].RemoveCommand!.Execute(null); Assert.Single(vm.Primary);
        SaveOne(vm, "Original");
        var saved = Assert.Single(vm.SavedProfiles);
        vm.NewCommand.Execute(null);
        saved.EditCommand.Execute(null);
        Assert.Equal("Original", vm.ConfigurationName);
        Assert.NotNull(vm.Primary[0].SelectedModule);
        vm.ConfigurationName = "Renamed"; vm.SaveCommand.Execute(null);
        Assert.Equal(saved.Profile.Id, Assert.Single(vm.SavedProfiles).Profile.Id);
        vm.RemoveCommand.Execute(null);
        Assert.Empty(vm.SavedProfiles); Assert.Null(vm.ActiveProfile);
        var reloaded = new FiregroupsWorkspaceViewModel(directory); Feed(reloaded, []); Assert.Empty(reloaded.SavedProfiles);
    }

    [Fact]
    public void RemovingAllModuleRowsRemovesThatGroupFromPreviewAndSavedConfiguration()
    {
        var vm = Create(); SaveOne(vm, "Two groups");
        vm.GroupLetter = "B";
        vm.Primary[0].SelectedModule = vm.Primary[0].Options[1];
        vm.SaveCommand.Execute(null);
        vm.GroupLetter = "A";
        vm.Primary[0].RemoveCommand!.Execute(null);
        Assert.DoesNotContain(vm.GroupPreview, group => group.Label == "Group A");
        vm.SaveCommand.Execute(null);
        Assert.Equal(1, Assert.Single(vm.ActiveProfile!.Groups).Number);
        vm.GroupLetter = "B";
        vm.Primary[0].RemoveCommand!.Execute(null);
        vm.SaveCommand.Execute(null);
        Assert.Contains("at least one", vm.Status);
        Assert.Single(vm.ActiveProfile.Groups);
    }

    [Fact]
    public void LoadoutRefreshRetainsMissingAssignmentsAndNoOpUpdatesPreserveEditorRows()
    {
        var vm = Create(); SaveOne(vm, "Reference");
        var row = vm.Primary[0];
        Feed(vm, []); Assert.Same(row, vm.Primary[0]);
        Feed(vm, [Loadout(1, "Survey Python", empty: true)]);
        Assert.Same(row, vm.Primary[0]);
        Assert.NotNull(row.SelectedModule);
        Assert.Contains("Not equipped", row.Warning);
        Assert.Equal(row.SelectedModule, Assert.Single(row.Options));
    }

    [Fact]
    public void MissingNameDuplicateAssignmentsAndSaveFailureAreVisibleWithoutChangingSavedConfig()
    {
        var vm = Create();
        vm.Primary[0].SelectedModule = vm.Primary[0].Options[0];
        vm.SaveCommand.Execute(null); Assert.Contains("name", vm.Status);
        vm.ConfigurationName = "Reference";
        vm.AddPrimaryCommand.Execute(null); vm.Primary[1].SelectedModule = vm.Primary[0].SelectedModule;
        vm.SaveCommand.Execute(null); Assert.Contains("only once", vm.Status); Assert.Empty(vm.SavedProfiles);
        vm.Primary[1].RemoveCommand!.Execute(null); vm.SaveCommand.Execute(null);
        var saved = Assert.Single(vm.SavedProfiles).Profile;
        var file = Directory.GetFiles(Path.Combine(directory, "firegroups"), "*.json").Single();
        File.Delete(file); Directory.CreateDirectory(file);
        vm.ConfigurationName = "Must not be committed";
        vm.SaveCommand.Execute(null);
        Assert.Contains("could not be saved", vm.Status);
        Assert.Same(saved, Assert.Single(vm.SavedProfiles).Profile);
        Assert.Equal("Reference", vm.ActiveProfile!.Name);
    }

    [Fact]
    public void CommanderSwitchDoesNotCopyPriorCommandersLoadoutIntoTheNewProfile()
    {
        var vm = Create(); SaveOne(vm, "Commander one");
        Feed(vm, [Loadout(2, "Previous commander ship"), Event("""{"event":"LoadGame","FID":"F2","Commander":"Other","Ship":"python","ShipID":2}""")]);
        Assert.False(vm.CanEdit);
        Assert.Empty(vm.SavedProfiles);
        Assert.Empty(new FiregroupStore(directory).Load("F2").Ships);
        Feed(vm, [Loadout(2, "Commander two ship")]);
        Assert.True(vm.CanEdit);
        Assert.Contains("Commander two ship", vm.ShipSummary);
        Assert.Single(new FiregroupStore(directory).Load("F1").Profiles);
    }

    [Fact]
    public void ExistingFlatFiregroupsMigrateOnceAndRemainInLegacyBackup()
    {
        var legacy = new MiningStore(directory);
        var document = new MiningCommanderData(); document.Settings.Firegroups.Add(new(0, "Old laser", "Old limpet"));
        legacy.Save("F1", document);
        var vm = Create();
        Assert.Equal("Imported firegroups", Assert.Single(vm.SavedProfiles).Name);
        Assert.Equal("Old laser", vm.Primary[0].SelectedModule!.Name);
        Assert.Contains("Not equipped", vm.Primary[0].Warning);
        var reloaded = new FiregroupsWorkspaceViewModel(directory); Feed(reloaded, []);
        Assert.Single(reloaded.SavedProfiles); Assert.Single(legacy.Load("F1").Settings.Firegroups);
    }

    private FiregroupsWorkspaceViewModel Create()
    {
        var vm = new FiregroupsWorkspaceViewModel(directory);
        Feed(vm, [Event("""{"event":"LoadGame","Commander":"Test","FID":"F1","Ship":"python","ShipID":1}"""), Loadout(1, "Survey Python")]);
        return vm;
    }
    private static void SaveOne(FiregroupsWorkspaceViewModel vm, string name)
    { vm.Primary[0].SelectedModule = vm.Primary[0].Options.First(); vm.ConfigurationName = name; vm.SaveCommand.Execute(null); Assert.StartsWith("Saved", vm.Status); }
    private void Feed(FiregroupsWorkspaceViewModel vm, IReadOnlyList<JournalEventEnvelope> events, int group = 0, StatusFlags flags = StatusFlags.InMainShip)
    {
        foreach (var entry in events) journal.Apply(entry);
        var status = new EliteStatus { Flags = flags, FireGroup = group };
        vm.Apply(new JournalMonitorUpdate(null, events, status, null, null, null, [], false), journal, status);
    }
    internal static JournalEventEnvelope Loadout(int id, string name, bool empty = false) => Event($$"""
        {"event":"Loadout","Ship":"python","ShipID":{{id}},"ShipName":"{{name}}","Modules":{{(empty ? "[]" : Modules)}}}
        """);
    private const string Modules = """
        [{"Slot":"MediumHardpoint1","Item":"Hpt_PulseLaser_Fixed_Medium"},
         {"Slot":"MediumHardpoint2","Item":"Hpt_PulseLaser_Fixed_Medium"},
         {"Slot":"TinyHardpoint1","Item":"Hpt_HeatSinkLauncher_Turret_Tiny"},
         {"Slot":"Slot01_Size3","Item":"Int_DroneControl_Collection_Size3_Class1"},
         {"Slot":"Slot02_Size3","Item":"Int_ShieldCellBank_Size3_Class1"},
         {"Slot":"PowerPlant","Item":"Int_PowerPlant_Size4_Class5"}]
        """;
    internal static JournalEventEnvelope Event(string json)
    { Assert.True(JournalEventEnvelope.TryParse(json, out var entry, out _)); return entry!; }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
