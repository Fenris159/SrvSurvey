using System.ComponentModel;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Read-only session overview shared by live, combined, and editor overlay presentations.</summary>
public sealed class MiningSessionOverlayViewModel : WorkspaceObservable, IDisposable
{
    private readonly MiningWorkspaceViewModel? workspace;
    private readonly MiningSession? preview;

    public MiningSessionOverlayViewModel(MiningWorkspaceViewModel? workspace)
    {
        this.workspace = workspace;
        if (workspace is null)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            preview = new MiningSession
            {
                Started = now.AddMinutes(-25),
                Ended = now,
                System = "Delkar",
                Ring = "Delkar 7 A Ring",
                Ship = "Python",
                AsteroidAdjustment = 18,
                ProspectorLimpets = 22,
                CollectorLimpets = 8,
            };
            preview.Collections.Add(new(now, "Platinum", 64, false));
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
    public string Cargo => workspace?.CargoSummary ?? "Cargo: 64 / 192 t";
    public string Notes => Session?.Notes ?? string.Empty;
    public bool HasNotes => Notes.Length > 0;

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is not (nameof(MiningWorkspaceViewModel.SessionSummary) or nameof(MiningWorkspaceViewModel.CargoSummary))
        )
        {
            return;
        }

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
                nameof(Cargo),
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
