using SrvSurvey.Core.Exobiology;
using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

/// <summary>Results earlier operations hand to later operations of one journal projection.</summary>
internal sealed class JournalProjectionTick(JournalProjectionContext context)
{
    public bool AllowLiveEffects { get; set; } = context.IsLive;

    public bool IsCancellationDrain { get; set; }

    public CancellationToken WorkCancellationToken { get; set; } = context.CancellationToken;

    public string? PreviousFrontierId { get; set; }

    public string? PreviousCommanderName { get; set; }

    public bool AllowSharedCargo { get; set; }

    public bool CargoChanged { get; set; }

    public bool CodexDiscoveryChanged { get; set; }

    /// <summary>Bootstrap replay over an already persisted profile must not reapply its journal mutations.</summary>
    public bool LoadedExistingProfile { get; set; }

    public bool SkipPersistedBootstrapEvents { get; set; }

    public ExplorationSnapshot ExplorationBefore { get; set; } = null!;

    public int ExobiologyVersionBefore { get; set; }

    public BoxelSearchNotificationState BoxelBefore { get; set; } = null!;

    public IReadOnlyDictionary<
        JournalEventEnvelope,
        ScreenshotGuardianContext
    > GuardianScreenshotContexts { get; set; } = null!;

    public bool RequestShutdown { get; set; }

    public HashSet<string> ScansLostToDeath { get; } = new(StringComparer.Ordinal);

    public ExobiologySnapshot ExobiologyAfter { get; set; } = null!;

    public bool ExobiologyChanged { get; set; }
}
