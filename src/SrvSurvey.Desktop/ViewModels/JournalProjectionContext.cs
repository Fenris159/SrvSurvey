using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>The monitor update and request flags every journal projection reads during one tick.</summary>
internal sealed record JournalProjectionContext(
    JournalMonitorUpdate Update,
    bool IsManualRefresh,
    CancellationToken CancellationToken
)
{
    /// <summary>Background polls without changes only service retained work; manual refreshes always project.</summary>
    public bool IsIdle => !Update.HasChanges && !IsManualRefresh;

    /// <summary>Startup replay of journal history, which must not run commands, notify, or publish.</summary>
    public bool IsBootstrapRead => Update.IsBootstrapRead;

    /// <summary>Live journal activity that may run commands, notify, and publish.</summary>
    public bool IsLive => !Update.IsBootstrapRead;

    public IReadOnlyList<JournalEventEnvelope> JournalEvents => Update.JournalEvents;
}
