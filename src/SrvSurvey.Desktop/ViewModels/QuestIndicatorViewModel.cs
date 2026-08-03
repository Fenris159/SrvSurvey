using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;
using SrvSurvey.Core.Quests;

namespace SrvSurvey.Desktop.ViewModels;

public sealed class QuestIndicatorViewModel : INotifyPropertyChanged
{
    private bool shouldShow;
    private string questTitle = string.Empty;
    private string unreadMessageText = string.Empty;
    private bool hasUnreadMessages;
    private IReadOnlyList<QuestObjectiveRowViewModel> objectives = [];
    private IReadOnlyList<QuestIndicatorLocationViewModel> locations = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool ShouldShow
    {
        get => shouldShow;
        private set => SetField(ref shouldShow, value);
    }

    public string QuestTitle
    {
        get => questTitle;
        private set => SetField(ref questTitle, value);
    }

    public string UnreadMessageText
    {
        get => unreadMessageText;
        private set => SetField(ref unreadMessageText, value);
    }

    public bool HasUnreadMessages
    {
        get => hasUnreadMessages;
        private set => SetField(ref hasUnreadMessages, value);
    }

    public IReadOnlyList<QuestObjectiveRowViewModel> Objectives
    {
        get => objectives;
        private set => SetField(ref objectives, value);
    }

    public IReadOnlyList<QuestIndicatorLocationViewModel> Locations
    {
        get => locations;
        private set => SetField(ref locations, value);
    }

    public void Update(
        IReadOnlyList<QuestRuntimeSnapshot> quests,
        EliteStatus? status,
        bool enabled,
        string? musicTrack = null)
    {
        ArgumentNullException.ThrowIfNull(quests);
        var firstQuest = quests.FirstOrDefault();
        var mode = OverlayGameModeResolver.Resolve(
            status,
            musicTrack: musicTrack);
        ShouldShow = enabled
            && firstQuest is not null
            && IsVisibleMode(mode);
        QuestTitle = firstQuest?.Title ?? string.Empty;
        var unread = quests.Sum(quest => quest.UnreadMessageCount);
        HasUnreadMessages = unread > 0;
        UnreadMessageText = unread > 0
            ? $"{unread:N0} unread message{(unread == 1 ? string.Empty : "s")}"
            : string.Empty;
        Objectives = firstQuest?.Objectives
            .Where(pair => pair.Value.StartsWith(
                "visible",
                StringComparison.Ordinal))
            .Select(pair => QuestObjectiveRowViewModel.Create(
                pair.Key,
                firstQuest.ObjectiveLabels.GetValueOrDefault(pair.Key)
                    ?? pair.Key,
                pair.Value))
            .ToArray()
            ?? [];
        Locations = quests.SelectMany(quest => quest.BodyLocations)
            .Select(pair => CreateLocation(pair.Key, pair.Value, status))
            .Where(location => location is not null)
            .Select(location => location!)
            .ToArray();
    }

    private static bool IsVisibleMode(OverlayGameMode mode)
    {
        return mode is OverlayGameMode.Flying
            or OverlayGameMode.SuperCruising
            or OverlayGameMode.GlideMode
            or OverlayGameMode.InSrv
            or OverlayGameMode.OnFoot
            or OverlayGameMode.OnFootInStation
            or OverlayGameMode.InTaxi
            or OverlayGameMode.CommsPanel
            or OverlayGameMode.InFighter
            or OverlayGameMode.Docked
            or OverlayGameMode.Landed
            or OverlayGameMode.FsdJumping
            or OverlayGameMode.StationServices
            or OverlayGameMode.ExternalPanel
            or OverlayGameMode.InternalPanel;
    }

    private static QuestIndicatorLocationViewModel? CreateLocation(
        string name,
        string encoded,
        EliteStatus? status)
    {
        var parts = encoded.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !double.TryParse(
                parts[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var latitude)
            || !double.TryParse(
                parts[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var longitude)
            || !double.TryParse(
                parts[2],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var targetRadius)
            || !double.IsFinite(targetRadius)
            || targetRadius < 0)
        {
            return null;
        }

        try
        {
            var target = new SurfaceCoordinate(latitude, longitude);
            if (status?.HasLatitudeLongitude != true
                || status.PlanetRadius <= 0)
            {
                return new QuestIndicatorLocationViewModel(
                    name,
                    "Position unavailable",
                    string.Empty,
                    false);
            }

            var origin = new SurfaceCoordinate(
                status.Latitude,
                status.Longitude);
            var distance = SurfaceNavigation.GetDistance(
                origin,
                target,
                decimal.ToDouble(status.PlanetRadius));
            var bearing = SurfaceNavigation.GetBearing(origin, target);
            var relative = SurfaceNavigation.NormalizeDegrees(
                bearing - status.NormalizedHeading);
            return new QuestIndicatorLocationViewModel(
                name,
                FormatDistance(distance),
                $"{relative:N0}° relative",
                distance < targetRadius);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string FormatDistance(double meters)
    {
        return meters >= 10_000
            ? $"{meters / 1_000:N1} km"
            : meters >= 1_000
                ? $"{meters / 1_000:N2} km"
                : $"{meters:N0} m";
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}

public sealed record QuestIndicatorLocationViewModel(
    string Label,
    string Distance,
    string Bearing,
    bool IsWithinTarget)
{
    public string StateGlyph => IsWithinTarget ? "✓" : "◇";
}
