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
    }
    public string Title => IsFiregroups ? "FIREGROUPS" : "MINING NOTIFICATIONS";
    public bool IsFiregroups { get; }
    public bool IsNotifications => !IsFiregroups;
    public double PanelWidth => IsFiregroups ? 220 : 360;
    public string GroupLabel => $"Group {(char)('A' + (firegroupsWorkspace?.ActiveGroupNumber ?? 0))}";
    public string PrimaryLabel => "Primary: " + (firegroupsWorkspace is null ? "Mining laser" : FormatModules(firegroupsWorkspace.ActiveGroup?.Primary));
    public string SecondaryLabel => "Secondary: " + (firegroupsWorkspace is null ? "Collector limpet" : FormatModules(firegroupsWorkspace.ActiveGroup?.Secondary));
    private static string FormatModules(IReadOnlyList<SrvSurvey.Core.Firegroups.FiregroupModule>? modules) => modules is { Count: > 0 } ? string.Join("\n", modules.Select(m => m.Display)) : "Not assigned";
    private void OnFiregroupsChanged(object? sender, PropertyChangedEventArgs e)
    {
        Changed(nameof(GroupLabel)); Changed(nameof(PrimaryLabel)); Changed(nameof(SecondaryLabel));
    }
    public IReadOnlyList<MiningNotice> Refined => Notices.Where(n => n.Kind == "Refined").ToArray();
    public IReadOnlyList<MiningNotice> Collected => Notices.Where(n => n.Kind == "Collected").ToArray();
    public IReadOnlyList<MiningNotice> Prospecting => Notices.Where(n => n.Kind is not ("Collected" or "Refined")).ToArray();
    public bool HasRefined => Refined.Count > 0;
    public bool HasCollected => Collected.Count > 0;
    public string Cargo => workspace?.CargoSummary ?? "Cargo: 72 / 128 t · Limpets: 34";
    private IReadOnlyList<MiningNotice> Notices => workspace?.VisibleNotices ?? PreviewNotices;
    private static readonly MiningNotice[] PreviewNotices = [new(default, "Prospected", "Platinum 34.8% · Painite 12.4%"), new(default, "Refined", "Platinum ×1"), new(default, "Collected", "Iron ×3")];
    public void Dispose() { if (workspace is not null) workspace.PropertyChanged -= OnChanged; if (firegroupsWorkspace is not null) firegroupsWorkspace.PropertyChanged -= OnFiregroupsChanged; }
    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MiningWorkspaceViewModel.VisibleNotices) or nameof(MiningWorkspaceViewModel.CargoSummary))) return;
        foreach (var name in new[] { nameof(GroupLabel), nameof(PrimaryLabel), nameof(SecondaryLabel), nameof(Refined), nameof(Collected), nameof(Prospecting), nameof(HasRefined), nameof(HasCollected), nameof(Cargo) }) Changed(name);
    }
}
