using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Settlements;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Configuration;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class HumanSiteViewModelOptions
{
    public HumanSiteSettingsStore? SettingsStore { get; init; }

    public HumanSiteKnowledgeStore? KnowledgeStore { get; init; }

    public HumanSiteMaterialStore? MaterialStore { get; init; }

    public HumanSiteTemplateCatalog? TemplateCatalog { get; init; }

    public ICanonnHumanSiteClient? CanonnClient { get; init; }

    public Func<bool>? UseExternalData { get; init; }

    public ICanonnHumanSitePublisher? CanonnPublisher { get; init; }

    public Func<bool>? PublishCanonnGeometry { get; init; }

    public Action<CanonnHumanSitePublicationResult>? ReportCanonnPublication
    {
        get;
        init;
    }

    public Version? ClientVersion { get; init; }
}

public sealed class SurfaceRadarMarkerOptions
{
    public required string Name { get; init; }

    public required SurfaceCoordinate Location { get; init; }

    public required double RadiusMeters { get; init; }

    public required SurfaceRadarMarkerKind Kind { get; init; }

    public required string StatusText { get; init; }

    public required SurfaceCoordinate Current { get; init; }

    public required EliteStatus Status { get; init; }

    public bool IsActive { get; init; } = true;
}

public sealed class BoxelSystemRowOptions
{
    public required string Name { get; init; }

    public required bool IsComplete { get; init; }

    public required bool IsKnown { get; init; }

    public required bool IsEmpty { get; init; }

    public required bool IsDeferred { get; init; }

    public required bool IsCurrent { get; init; }

    public required bool IsNextIncomplete { get; init; }

    public required string Distance { get; init; }

    public required string VisitedAt { get; init; }

    public required string SpanshUpdatedAt { get; init; }

    public required Func<Task> Complete { get; init; }

    public required Func<Task> Reopen { get; init; }

    public required Func<Task> Defer { get; init; }

    public required Func<Task> StartHere { get; init; }
}
