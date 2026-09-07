using System.ComponentModel;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MiningActivityOverlayViewModel : WorkspaceObservable, IDisposable
{
    private readonly MiningWorkspaceViewModel? workspace;
    private readonly FiregroupsWorkspaceViewModel? firegroupsWorkspace;
    public MiningActivityOverlayViewModel(MiningWorkspaceViewModel? workspace, bool firegroups, FiregroupsWorkspaceViewModel? firegroupsWorkspace = null)
    {
        this.workspace = workspace;
        this.firegroupsWorkspace = firegroupsWorkspace;
        if (firegroupsWorkspace is not null) firegroupsWorkspace.PropertyChanged += OnFiregroupsChanged;
        IsFiregroups = firegroups;
        if (workspace is not null) workspace.PropertyChanged += OnChanged;
        RefreshNotices();
    }
    public string Title => IsFiregroups ? "FIREGROUPS" : "MINING NOTIFICATIONS";
    public bool IsFiregroups { get; }
    public bool IsNotifications => !IsFiregroups;
    public double PanelWidth => IsFiregroups ? 220 : 360;
    public string GroupLabel => $"Group {(char)('A' + (firegroupsWorkspace?.ActiveGroupNumber ?? 0))}";
    public string PrimaryLabel => "Primary: " + (firegroupsWorkspace is null ? "Mining laser" : FormatModules(firegroupsWorkspace.ActiveGroup?.Primary));
    public string SecondaryLabel => "Secondary: " + (firegroupsWorkspace is null ? "Collector limpet" : FormatModules(firegroupsWorkspace.ActiveGroup?.Secondary));
    private static string FormatModules(IReadOnlyList<SrvSurvey.Core.Firegroups.FiregroupModule>? modules)
    {
        if (modules is not { Count: > 0 }) return "Not assigned";
        return string.Join("\n", modules.Select(OverlayName)
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Count() > 1 ? $"{group.Key} ×{group.Count()}" : group.Key));
    }

    private static string OverlayName(SrvSurvey.Core.Firegroups.FiregroupModule module)
    {
        var name = module.Name.Trim();
        if (module.Symbol.Length == 0 || !name.EndsWith(')')) return name;
        var technicalDetails = name.LastIndexOf(" (", StringComparison.Ordinal);
        return technicalDetails > 0 ? name[..technicalDetails] : name;
    }
    private void OnFiregroupsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Changed(nameof(GroupLabel)); Changed(nameof(PrimaryLabel)); Changed(nameof(SecondaryLabel));
    }
    public IReadOnlyList<MiningNotice> Refined { get; private set; } = [];
    public IReadOnlyList<MiningNotice> Collected { get; private set; } = [];
    public string ProspectReport => workspace?.CurrentProspectText ?? "Platinum 34.8% · Painite 12.4% · Remaining 73%";
    public bool HasProspectReport => ProspectReport.Length > 0;
    private void RefreshNotices()
    {
        Refined = Notices.Where(n => n.Kind == "Refined").ToArray();
        Collected = Notices.Where(n => n.Kind == "Collected").ToArray();
    }
    public bool HasRefined => Refined.Count > 0;
    public bool HasCollected => Collected.Count > 0;
    public string Cargo => workspace?.CargoSummary ?? "Cargo: 72 / 128 t · Limpets: 34";
    private IReadOnlyList<MiningNotice> Notices => workspace?.VisibleNotices ?? PreviewNotices;
    private static readonly MiningNotice[] PreviewNotices = [new(default, "Refined", "Platinum ×1"), new(default, "Collected", "Iron ×3")];
    public void Dispose()
    {
        if (workspace is not null) { workspace.PropertyChanged -= OnChanged; }
        if (firegroupsWorkspace is not null) { firegroupsWorkspace.PropertyChanged -= OnFiregroupsChanged; }
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MiningWorkspaceViewModel.VisibleNotices) or nameof(MiningWorkspaceViewModel.CargoSummary) or nameof(MiningWorkspaceViewModel.CurrentProspectText) or nameof(MiningWorkspaceViewModel.HasCurrentProspect))) return;
        RefreshNotices();
        foreach (var name in new[] { nameof(GroupLabel), nameof(PrimaryLabel), nameof(SecondaryLabel), nameof(Refined), nameof(Collected), nameof(ProspectReport), nameof(HasProspectReport), nameof(HasRefined), nameof(HasCollected), nameof(Cargo) }) Changed(name);
    }
}
