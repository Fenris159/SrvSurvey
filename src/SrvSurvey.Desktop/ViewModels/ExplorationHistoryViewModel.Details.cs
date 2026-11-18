using System.Globalization;
using System.Text.Json;
using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class ExplorationHistoryViewModel
{
    private IReadOnlyList<ExplorationHistoryBodyViewModel> bodies = [];
    private string detailTitle = "Select an entry to inspect its records";
    private string detailStatus = string.Empty;
    private string rawJson = string.Empty;
    private long detailGeneration;
    private Func<Task>? cancelDetailsAsync;

    public IReadOnlyList<ExplorationHistoryBodyViewModel> Bodies
    {
        get => bodies;
        private set => Set(ref bodies, value);
    }
    public string DetailTitle
    {
        get => detailTitle;
        private set => Set(ref detailTitle, value);
    }
    public string DetailStatus
    {
        get => detailStatus;
        private set => Set(ref detailStatus, value);
    }
    public string RawJson
    {
        get => rawJson;
        private set => Set(ref rawJson, value);
    }
    public string DetailLocation =>
        SelectedRow is { IsPaid: false } row
            ? $"{row.Position}; system address {row.Data.SystemAddress?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}."
            : string.Empty;
    public string DetailPeriod =>
        SelectedRow switch
        {
            { IsPaid: true } row => $"Paid {FormatTime(row.Data.LastCollected)}",
            { } row =>
                $"Collected {FormatTime(row.Data.FirstCollected)} to {FormatTime(row.Data.LastCollected)}. {row.Status} {row.SettlementDate}".Trim(),
            _ => string.Empty,
        };
    public bool HasSelection => SelectedRow is not null;
    public bool HasPaymentSelection => SelectedRow?.IsPaid == true;
    public IReadOnlyList<ExplorationHistoryFact> PaymentFacts =>
        SelectedRow is { IsPaid: true } row
            ?
            [
                new("Actual payout", row.Estimate),
                new("Travel since previous sale", row.ReceiptTravel),
                new("Identified sold bodies", row.Contents),
                new("Sold systems", row.ReceiptSystems),
            ]
            : [];

    private async Task LoadDetailsAsync(ExplorationHistoryRowViewModel? row)
    {
        if (disposed)
        {
            return;
        }
        long request = ++detailGeneration;
        long version = generation;
        CancellationToken token = contextCancellation.Token;
        if (!await CancelPreviousDetailsAsync(version, request, token))
        {
            return;
        }
        Bodies = [];
        RawJson = string.Empty;
        DetailStatus = string.Empty;
        DetailTitle = row?.Title ?? "Select an entry to inspect its records";
        Changed(nameof(HasSelection));
        Changed(nameof(HasPaymentSelection));
        Changed(nameof(PaymentFacts));
        Changed(nameof(DetailLocation));
        Changed(nameof(DetailPeriod));
        ExplorationHistoryCatalog? current = catalog;
        if (current is null || row is null)
        {
            return;
        }
        using var selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        // This request owns disposal; later selections retain only its cancellation operation.
        Func<Task> cancelSelection = selectionCancellation.CancelAsync;
        cancelDetailsAsync = cancelSelection;
        token = selectionCancellation.Token;
        try
        {
            ExplorationHistoryDetails details = await TrackIo(detailReader(current, row.Data, token));
            if (!Current(version, token) || request != detailGeneration)
            {
                return;
            }
            Bodies = BuildBodies(details.Events);
            RawJson = string.Join(Environment.NewLine, details.Events.Select(entry => entry.RawJson));
            DetailStatus =
                details.Notice
                ?? (
                    row.IsPaid
                        ? "Exploration sale receipt. Body totals cover identified sold systems; travel covers the available journals since the previous sale."
                        : $"{row.Status}: {row.Estimate} estimated from {row.Contents}."
                );
            DetailStatus += DescribeRecordedSale(row, details.Events);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A different commander, selection, or closed workspace makes this retained result obsolete.
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            if (Current(version, token) && request == detailGeneration)
            {
                DetailStatus = "Original exploration records could not be read: " + exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(cancelDetailsAsync, cancelSelection))
            {
                cancelDetailsAsync = null;
            }
        }
    }

    private static string DescribeRecordedSale(
        ExplorationHistoryRowViewModel row,
        IReadOnlyList<JournalEventEnvelope> entries
    )
    {
        JournalEventEnvelope? sale = entries.LastOrDefault(entry =>
            entry.EventName is "SellExplorationData" or "MultiSellExplorationData"
        );
        if (
            !row.IsPaid
            && sale is not null
            && sale.Payload.TryGetProperty("TotalEarnings", out JsonElement earnings)
            && earnings.ValueKind == JsonValueKind.Number
            && earnings.TryGetInt64(out long amount)
        )
        {
            return $" Recorded transaction earnings: {amount:N0} CR. This receipt may include other systems.";
        }
        return string.Empty;
    }

    private async Task<bool> CancelPreviousDetailsAsync(long version, long request, CancellationToken token)
    {
        if (cancelDetailsAsync is { } cancelPrevious)
        {
            await cancelPrevious();
        }
        return Current(version, token) && request == detailGeneration;
    }

    private static ExplorationHistoryBodyViewModel[] BuildBodies(IReadOnlyList<JournalEventEnvelope> entries)
    {
        return entries
            .Where(entry =>
                entry.EventName is "Scan" or "SAAScanComplete" or "FSSBodySignals" or "SAASignalsFound" or "Touchdown"
            )
            .GroupBy(entry =>
                JsonValue(entry.Payload, "BodyID")
                ?? JsonValue(entry.Payload, "BodyName")
                ?? JsonValue(entry.Payload, "Body")
                ?? "Unknown body"
            )
            .Select(group =>
            {
                JournalEventEnvelope[] events = group.ToArray();
                JournalEventEnvelope? scan = events.LastOrDefault(entry => entry.EventName == "Scan");
                JournalEventEnvelope? map = events.LastOrDefault(entry => entry.EventName == "SAAScanComplete");
                string name =
                    events
                        .Select(entry => JsonValue(entry.Payload, "BodyName") ?? JsonValue(entry.Payload, "Body"))
                        .FirstOrDefault(value => value is not null)
                    ?? "Body " + group.Key;
                var facts = new List<ExplorationHistoryFact>();
                if (scan is not null)
                {
                    AddFact(facts, "Class", scan.Payload, "PlanetClass", "StarType");
                    AddFact(facts, "Terraforming", scan.Payload, "TerraformState");
                    AddFact(facts, "Earth masses", scan.Payload, "MassEM");
                    AddFact(facts, "Solar masses", scan.Payload, "StellarMass");
                    AddFact(facts, "Atmosphere", scan.Payload, "Atmosphere");
                    AddFact(facts, "Volcanism", scan.Payload, "Volcanism");
                    AddFact(facts, "Gravity (m/s²)", scan.Payload, "SurfaceGravity");
                    AddFact(facts, "Temperature (K)", scan.Payload, "SurfaceTemperature");
                    AddFact(facts, "Landable", scan.Payload, "Landable");
                    AddFact(facts, "Previously discovered", scan.Payload, "WasDiscovered");
                    AddFact(facts, "Previously mapped", scan.Payload, "WasMapped");
                    facts.Add(new("Scanned", FormatTime(scan.Timestamp)));
                }
                if (map is not null)
                {
                    facts.Add(new("Mapped", FormatTime(map.Timestamp)));
                    AddFact(facts, "Probes used", map.Payload, "ProbesUsed");
                    AddFact(facts, "Efficiency target", map.Payload, "EfficiencyTarget");
                }
                foreach (
                    JsonElement signals in events
                        .Where(entry => entry.EventName is "FSSBodySignals" or "SAASignalsFound")
                        .Select(entry => entry.Payload)
                )
                {
                    AddFact(facts, "Signals", signals, "Signals");
                    AddFact(facts, "Genera", signals, "Genuses");
                }
                string bodyClass = scan is null
                    ? "Mapping record"
                    : JsonValue(scan.Payload, "PlanetClass") ?? JsonValue(scan.Payload, "StarType") ?? "Unknown class";
                return new ExplorationHistoryBodyViewModel(name, bodyClass, map is null ? "Scanned" : "Mapped", facts);
            })
            .OrderBy(body => body.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatTime(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "Unknown date";

    private static void AddFact(
        List<ExplorationHistoryFact> facts,
        string label,
        JsonElement root,
        params string[] properties
    )
    {
        string? value = properties
            .Select(property => JsonValue(root, property))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (value is not null)
        {
            facts.Add(new(label, value));
        }
    }

    private static string? JsonValue(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            JsonValueKind.Null => null,
            _ => value.GetRawText(),
        };
    }
}

public sealed record ExplorationHistoryFact(string Label, string Value);

public sealed record ExplorationHistoryBodyViewModel(
    string Name,
    string BodyClass,
    string ScanState,
    IReadOnlyList<ExplorationHistoryFact> Facts
);
