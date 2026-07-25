using System.Globalization;
using System.Text.Json;

namespace SrvSurvey.Core.Quests;

public static class QuestProgressMapper
{
    public static RavenCommanderQuest FromLegacy(LegacyQuestProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return new RavenCommanderQuest
        {
            Publisher = progress.Reference.Publisher,
            Id = progress.Reference.Id,
            Version = progress.Reference.Version,
            Quest = progress.PortableDefinition
                ?? (progress.Definition is null
                    ? null
                    : FromLegacy(progress.Definition)),
            Objectives = progress.Objectives.ToDictionary(
                pair => pair.Key,
                pair => FormatObjective(pair.Value),
                StringComparer.Ordinal),
            StartTime = progress.StartTime,
            EndTime = progress.EndTime,
            Paused = progress.Paused,
            Tags = progress.Tags.ToHashSet(StringComparer.Ordinal),
            BodyLocations = progress.BodyLocations.ToDictionary(
                pair => pair.Key,
                pair => FormatBodyLocation(pair.Value),
                StringComparer.Ordinal),
            Chapters = progress.Chapters.Select(chapter =>
                new RavenQuestChapterState
                {
                    Id = chapter.Id,
                    StartTime = chapter.StartTime,
                    EndTime = chapter.EndTime,
                    Variables = CloneJsonMap(chapter.Variables),
                }).ToList(),
            Messages = progress.Messages
                .Select(message => MapMessage(message, progress.Definition))
                .ToList(),
            Variables = CloneJsonMap(progress.Variables),
            KeptJournalEvents = CloneJsonMap(progress.KeptJournalEvents),
            Routes = progress.Routes.Select(route => new RavenQuestRoute
            {
                Id = route.Id,
                Width = route.Width,
                Waypoints = route.Waypoints
                    .Select(waypoint => waypoint.ToArray())
                    .ToList(),
            }).ToList(),
        };
    }

    public static RavenQuestDefinition FromLegacy(
        LegacyQuestDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new RavenQuestDefinition
        {
            Publisher = definition.Publisher,
            Id = definition.Id,
            Version = definition.Version,
            Title = definition.Title,
            Subtitle = definition.Subtitle,
            Description = definition.Description,
            Tags = definition.Tags.ToHashSet(StringComparer.Ordinal),
            Duration = definition.Duration switch
            {
                LegacyQuestDuration.Short => RavenQuestDuration.Short,
                LegacyQuestDuration.Medium => RavenQuestDuration.Medium,
                LegacyQuestDuration.Long => RavenQuestDuration.Long,
                LegacyQuestDuration.Extended => RavenQuestDuration.Extended,
                _ => RavenQuestDuration.Unknown,
            },
            OnlySquadrons = definition.OnlySquadrons.ToHashSet(
                StringComparer.Ordinal),
            OnlyCommanders = definition.OnlyCommanders.ToHashSet(
                StringComparer.Ordinal),
            Hidden = definition.Hidden,
            FirstChapter = definition.FirstChapter,
            Objectives = definition.Objectives.ToDictionary(
                StringComparer.Ordinal),
            Strings = definition.Strings.ToDictionary(StringComparer.Ordinal),
            Messages = definition.Messages.Select(message =>
                new RavenQuestMessageDefinition
                {
                    Id = message.Id,
                    From = message.From,
                    Subject = message.Subject,
                    Body = message.Body,
                    Actions = message.Actions.Count == 0
                        ? null
                        : message.Actions.ToDictionary(StringComparer.Ordinal),
                    Tags = message.Tags.Count == 0
                        ? null
                        : message.Tags.ToHashSet(StringComparer.Ordinal),
                }).ToList(),
            Chapters = definition.Chapters.ToDictionary(StringComparer.Ordinal),
        };
    }

    private static string FormatObjective(LegacyQuestObjective objective)
    {
        return objective.Current == 0 && objective.Total == 0
            ? objective.State.ToString()
            : string.Join(
                ',',
                objective.State,
                objective.Current.ToString(CultureInfo.InvariantCulture),
                objective.Total.ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatBodyLocation(LegacyQuestBodyLocation location)
    {
        return string.Join(
            ',',
            location.Latitude.ToString("R", CultureInfo.InvariantCulture),
            location.Longitude.ToString("R", CultureInfo.InvariantCulture),
            location.Radius.ToString("R", CultureInfo.InvariantCulture));
    }

    private static RavenQuestMessage MapMessage(
        LegacyQuestMessage message,
        LegacyQuestDefinition? definition)
    {
        var declared = definition?.Messages.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, message.Id, StringComparison.Ordinal));
        return new RavenQuestMessage
        {
            Id = message.Id,
            Received = message.Received ?? default,
            From = message.From == declared?.From ? null : message.From,
            Subject = message.Subject == declared?.Subject ? null : message.Subject,
            Body = message.Body == declared?.Body ? null : message.Body,
            Chapter = message.Chapter,
            Actions = message.Actions.Count == 0
                ? null
                : message.Actions.ToArray(),
            Read = message.Read,
            Replied = message.Replied,
        };
    }

    private static Dictionary<string, JsonElement> CloneJsonMap(
        IReadOnlyDictionary<string, JsonElement> source)
    {
        return source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal);
    }
}
