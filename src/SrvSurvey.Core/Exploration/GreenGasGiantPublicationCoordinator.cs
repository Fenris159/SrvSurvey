using System.Text.Json;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Exploration;

public sealed class GreenGasGiantPublicationCoordinator
{
    private readonly GreenGasGiantCriteriaCatalog criteria;
    private readonly IGreenGasGiantClient client;
    private string? commanderName;
    private GalacticCoordinate? starPosition;

    public GreenGasGiantPublicationCoordinator(
        GreenGasGiantCriteriaCatalog criteria,
        IGreenGasGiantClient client)
    {
        this.criteria = criteria
            ?? throw new ArgumentNullException(nameof(criteria));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<GreenGasGiantPublicationResult> ApplyAsync(
        IReadOnlyList<JournalEventEnvelope> journalEvents,
        bool enabled,
        bool allowPublishing,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journalEvents);
        var published = new List<GreenGasGiantCandidate>();
        var warnings = new List<string>();
        foreach (var journalEvent in journalEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateContext(journalEvent);
            if (!ShouldAttemptPublish(journalEvent, enabled, allowPublishing))
            {
                continue;
            }

            await TryPublishScanAsync(
                    journalEvent,
                    published,
                    warnings,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new GreenGasGiantPublicationResult(published, warnings);
    }

    private static bool ShouldAttemptPublish(
        JournalEventEnvelope journalEvent,
        bool enabled,
        bool allowPublishing)
    {
        return journalEvent.EventName == "Scan"
            && enabled
            && allowPublishing;
    }

    private async Task TryPublishScanAsync(
        JournalEventEnvelope journalEvent,
        List<GreenGasGiantCandidate> published,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var candidate = TryCreateCandidate(journalEvent, warnings);
        if (candidate is null)
        {
            return;
        }

        await PublishCandidateAsync(
                candidate,
                published,
                warnings,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private GreenGasGiantCandidate? TryCreateCandidate(
        JournalEventEnvelope journalEvent,
        List<string> warnings)
    {
        var root = journalEvent.Payload;
        var planetClass = GetString(root, "PlanetClass");
        var temperature = GetDouble(root, "SurfaceTemperature");
        var tag = temperature is double value
            ? criteria.Match(planetClass, value)
            : null;
        if (tag is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(commanderName)
            || starPosition is null)
        {
            warnings.Add(
                $"A {tag} Green Gas Giant candidate was not uploaded because commander or system coordinates were unavailable.");
            return null;
        }

        return new GreenGasGiantCandidate(
            commanderName,
            tag,
            starPosition.Value,
            journalEvent.RawJson);
    }

    private async Task PublishCandidateAsync(
        GreenGasGiantCandidate candidate,
        List<GreenGasGiantCandidate> published,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.PublishAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
            published.Add(candidate);
        }
        catch (OperationCanceledException exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            warnings.Add(CreateUploadWarning(exception));
        }
        catch (Exception exception) when (
            exception is HttpRequestException
                or JsonException
                or IOException
                or InvalidOperationException)
        {
            warnings.Add(CreateUploadWarning(exception));
        }
    }

    private static string CreateUploadWarning(Exception exception)
    {
        return "A Green Gas Giant candidate could not be uploaded: "
            + exception.Message;
    }

    private void UpdateContext(JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        if (journalEvent.EventName == "Commander")
        {
            commanderName = GetString(root, "Name") ?? commanderName;
        }
        else if (journalEvent.EventName == "LoadGame")
        {
            commanderName = GetString(root, "Commander") ?? commanderName;
        }

        if (journalEvent.EventName is "Location" or "FSDJump" or "CarrierJump")
        {
            starPosition = GetCoordinate(root, "StarPos") ?? starPosition;
        }
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.TryGetDouble(out var result)
            && double.IsFinite(result)
                ? result
                : null;
    }

    private static GalacticCoordinate? GetCoordinate(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var components = value.EnumerateArray()
            .Select(component => component.TryGetDouble(out var number)
                && double.IsFinite(number)
                    ? number
                    : double.NaN)
            .ToArray();
        return components.Length == 3 && components.All(double.IsFinite)
            ? new GalacticCoordinate(
                components[0],
                components[1],
                components[2])
            : null;
    }
}

public sealed record GreenGasGiantPublicationResult(
    IReadOnlyList<GreenGasGiantCandidate> Published,
    IReadOnlyList<string> Warnings);
