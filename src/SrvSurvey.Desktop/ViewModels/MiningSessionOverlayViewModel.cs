using System.ComponentModel;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Read-only session overview shared by live, combined, and editor overlay presentations.</summary>
public sealed class MiningSessionOverlayViewModel : WorkspaceObservable, IDisposable
{
    private readonly MiningWorkspaceViewModel? workspace;
    private readonly MiningSession? preview;
    private IReadOnlyList<MiningMaterialSummary>? prospectingYields;

    public MiningSessionOverlayViewModel(MiningWorkspaceViewModel? workspace)
    {
        this.workspace = workspace;
        if (workspace is null)
        {
            const string platinum = "Platinum";
            DateTimeOffset now = DateTimeOffset.UtcNow;
            preview = new MiningSession
            {
                Started = now.AddMinutes(-25),
                Ended = now,
                System = "Delkar",
                Ring = "Delkar 7 A Ring",
                Ship = "Python",
                AsteroidAdjustment = 15,
                ProspectorLimpets = 22,
                CollectorLimpets = 8,
            };
            preview.Collections.Add(new(now, platinum, 64, false));
            preview.Thresholds[platinum] = 30;
            preview.Thresholds["Osmium"] = 20;
            preview.Prospects.Add(new(now, [new(platinum, 34.8), new("Osmium", 18.2)], "", "High"));
            preview.Prospects.Add(new(now, [new(platinum, 52.4)], "", "High"));
            preview.Prospects.Add(new(now, [new("Osmium", 25.6)], "", "High"));
        }
        else
        {
            workspace.PropertyChanged += OnChanged;
        }
    }

    private MiningSession? Session => workspace is null ? preview : workspace.Current;
    public bool HasSession => Session is not null;
    public string State =>
        Session switch
        {
            null => "No active session",
            { PausedAt: null } => "Active",
            _ => "Paused",
        };
    public string Location =>
        Session is { } session
            ? string.Join(" · ", new[] { session.System, session.Ring, session.Ship }.Where(value => value.Length > 0))
            : string.Empty;
    public string Duration =>
        Session?.ActiveDuration.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
        ?? "00:00:00";
    public string Yield =>
        Session is { } session ? $"{session.RefinedTons:N0} t · {session.TonsPerHour:0.0} t/h" : "0 t";
    public string Activity =>
        Session is { } session ? $"{session.Asteroids:N0} asteroids · {session.CoreHits:N0} cores" : string.Empty;
    public string Limpets =>
        Session is { } session
            ? $"{session.ProspectorLimpets:N0} prospectors · {session.CollectorLimpets:N0} collectors"
            : string.Empty;
    public IReadOnlyList<MiningMaterialSummary> ProspectingYields => prospectingYields ??= ReadProspectingYields();

    /// <summary>Sorts complete session totals before selecting the configured number of overlay rows.</summary>
    private MiningMaterialSummary[] ReadProspectingYields()
    {
        if (Session is not { } session)
        {
            return [];
        }

        IEnumerable<MiningMaterialSummary> yields = session.Summarize(
            workspace?.Settings.Thresholds ?? session.Thresholds
        );
        if (workspace?.Settings.SessionYieldSort == MiningSessionYieldSort.Finds)
        {
            yields = yields.OrderByDescending(row => row.Finds).ThenByDescending(row => row.Average);
        }

        return yields.Take(Math.Clamp(workspace?.Settings.SessionYieldLimit ?? 5, 1, 100)).ToArray();
    }

    public bool HasProspectingYields => ProspectingYields.Count > 0;
    public string Notes => Session?.Notes ?? string.Empty;
    public bool HasNotes => Notes.Length > 0;

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MiningWorkspaceViewModel.SessionSummary))
        {
            return;
        }

        prospectingYields = null;
        foreach (
            string name in new[]
            {
                nameof(HasSession),
                nameof(State),
                nameof(Location),
                nameof(Duration),
                nameof(Yield),
                nameof(Activity),
                nameof(Limpets),
                nameof(ProspectingYields),
                nameof(HasProspectingYields),
                nameof(Notes),
                nameof(HasNotes),
            }
        )
        {
            Changed(name);
        }
    }

    public void Dispose()
    {
        workspace?.PropertyChanged -= OnChanged;
    }
}
