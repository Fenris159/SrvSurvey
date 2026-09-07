using System.ComponentModel;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class MiningActivityOverlayViewModel : WorkspaceObservable, IDisposable
{
    private readonly MiningWorkspaceViewModel? workspace;
    public MiningActivityOverlayViewModel(MiningWorkspaceViewModel? workspace, bool firegroups)
    {
        this.workspace = workspace;
        IsFiregroups = firegroups;
        if (workspace is not null) workspace.PropertyChanged += OnChanged;
    }
    public string Title => IsFiregroups ? "FIREGROUPS" : "MINING NOTIFICATIONS";
    public bool IsFiregroups { get; }
    public bool IsNotifications => !IsFiregroups;
    public double PanelWidth => IsFiregroups ? 220 : 360;
    public string GroupLabel => $"Group {(char)('A' + (workspace?.ActiveFiregroupNumber ?? 0))}";
    public string PrimaryLabel => "Primary: " + (workspace is null ? "Mining laser" : workspace.ActiveFiregroupDetails?.Primary ?? "Not configured");
    public string SecondaryLabel => "Secondary: " + (workspace is null ? "Collector limpet" : workspace.ActiveFiregroupDetails?.Secondary ?? "Not configured");
    public string Firegroup => workspace?.ActiveFiregroup ?? "Group A · Primary: Mining laser · Secondary: Collector limpet";
    public IReadOnlyList<MiningNotice> Refined => Notices.Where(n => n.Kind == "Refined").ToArray();
    public IReadOnlyList<MiningNotice> Collected => Notices.Where(n => n.Kind == "Collected").ToArray();
    public IReadOnlyList<MiningNotice> Prospecting => Notices.Where(n => n.Kind is not ("Collected" or "Refined")).ToArray();
    public bool HasRefined => Refined.Count > 0;
    public bool HasCollected => Collected.Count > 0;
    public string Cargo => workspace?.CargoSummary ?? "Cargo: 72 / 128 t · Limpets: 34";
    private IReadOnlyList<MiningNotice> Notices => workspace?.VisibleNotices ?? PreviewNotices;
    private static readonly MiningNotice[] PreviewNotices = [new(default, "Prospected", "Platinum 34.8% · Painite 12.4%"), new(default, "Refined", "Platinum ×1"), new(default, "Collected", "Iron ×3")];
    public void Dispose() { if (workspace is not null) workspace.PropertyChanged -= OnChanged; }
    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MiningWorkspaceViewModel.VisibleNotices) or nameof(MiningWorkspaceViewModel.ActiveFiregroup) or nameof(MiningWorkspaceViewModel.CargoSummary))) return;
        foreach (var name in new[] { nameof(Firegroup), nameof(GroupLabel), nameof(PrimaryLabel), nameof(SecondaryLabel), nameof(Refined), nameof(Collected), nameof(Prospecting), nameof(HasRefined), nameof(HasCollected), nameof(Cargo) }) Changed(name);
    }
}
