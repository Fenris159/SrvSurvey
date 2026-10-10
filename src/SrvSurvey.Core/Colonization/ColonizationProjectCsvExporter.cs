using System.Globalization;
using System.Text;

namespace SrvSurvey.Core.Colonization;

/// <summary>Exports a rectangular CSV containing build details, cargo, effects, and delivery history without credentials or editing controls.</summary>
public static class ColonizationProjectCsvExporter
{
    /// <summary>Replaces a selected file with an Excel-compatible UTF-8 CSV while leaving stream ownership with the caller.</summary>
    public static async Task WriteUtf8Async(Stream stream, string csv)
    {
        stream.SetLength(0);
        stream.Position = 0;
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            leaveOpen: true
        );
        await writer.WriteAsync(csv);
    }

    /// <summary>Produces invariant numbers and quoted, formula-safe text; each record kind can be filtered in a spreadsheet.</summary>
    public static string Write(ColonizationProjectPreview preview, int currentShipCapacity = 0)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var csv = new StringBuilder();
        Append(
            csv,
            [
                "Record",
                "Field",
                "Value",
                "Category",
                "Commodity",
                "Need",
                "FC Diff",
                "Time UTC",
                "Commander",
                "Cargo",
                .. preview.Carriers.Select(carrier => Text(carrier.Label)),
            ]
        );
        foreach ((string field, string value) in Details(preview))
        {
            Append(
                csv,
                ["Project", Text(field), Text(value), .. Enumerable.Repeat(string.Empty, 7 + preview.Carriers.Count)]
            );
        }
        if (currentShipCapacity > 0)
        {
            Append(
                csv,
                [
                    "Project",
                    "Current ship capacity",
                    Number(currentShipCapacity),
                    .. Enumerable.Repeat(string.Empty, 7 + preview.Carriers.Count),
                ]
            );
            Append(
                csv,
                [
                    "Project",
                    "Current ship trips",
                    Number(ColonizationProjectPreview.Trips(preview.Remaining, currentShipCapacity)),
                    .. Enumerable.Repeat(string.Empty, 7 + preview.Carriers.Count),
                ]
            );
        }
        foreach (ColonizationPreviewCommodity row in preview.Rows)
        {
            Append(
                csv,
                [
                    "Commodity",
                    "",
                    "",
                    Text(row.Category),
                    Text(row.Name),
                    Number(row.Need),
                    Number(row.CarrierDifference),
                    "",
                    "",
                    "",
                    .. row.CarrierQuantities.Select(quantity => Number(quantity)),
                ]
            );
        }
        foreach (ColonizationPreviewCarrier carrier in preview.Carriers)
        {
            Append(
                csv,
                [
                    "Carrier",
                    Text(carrier.Label),
                    Number(carrier.TotalCargo),
                    .. Enumerable.Repeat(string.Empty, 7 + preview.Carriers.Count),
                ]
            );
        }
        if (preview.Effects is { } effects)
        {
            foreach ((string field, string value) in EffectDetails(effects))
            {
                Append(
                    csv,
                    ["Effect", Text(field), Text(value), .. Enumerable.Repeat(string.Empty, 7 + preview.Carriers.Count)]
                );
            }
        }
        if (preview.Statistics is { } statistics)
        {
            foreach ((string commander, long cargo) in statistics.Cmdrs)
            {
                Append(
                    csv,
                    [
                        "Commander deliveries",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        "",
                        Text(commander),
                        Number(cargo),
                        .. Enumerable.Repeat(string.Empty, preview.Carriers.Count),
                    ]
                );
            }
            foreach (ColonizationDeliveryBucket bucket in statistics.Stats.OrderBy(bucket => bucket.Time))
            {
                foreach ((string commander, long cargo) in bucket.Cmdrs)
                {
                    Append(
                        csv,
                        [
                            "Hourly deliveries",
                            "",
                            "",
                            "",
                            "",
                            "",
                            "",
                            bucket.Time.ToString("O", CultureInfo.InvariantCulture),
                            Text(commander),
                            Number(cargo),
                            .. Enumerable.Repeat(string.Empty, preview.Carriers.Count),
                        ]
                    );
                }
            }
        }
        return csv.ToString();
    }

    /// <summary>Includes the build identity and refresh time so exports remain distinct and valid on supported desktops.</summary>
    public static string SuggestedFileName(ColonizationProjectPreview preview)
    {
        return $"Raven-build-{string.Concat(preview.Project.BuildId.Select(character => char.IsAsciiLetterOrDigit(character) || character == '-' ? character : '_'))}-{preview.FetchedAt:yyyyMMdd-HHmmss}.csv";
    }

    /// <summary>Lists the public project and summary fields shared by the preview and export.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Details(ColonizationProjectPreview preview)
    {
        ColonizationProject project = preview.Project;
        return new Dictionary<string, string>
        {
            ["Build ID"] = project.BuildId,
            ["Project name"] = project.BuildName,
            ["Build type"] = project.BuildType,
            ["System"] = project.SystemName,
            ["System address"] = Number(project.SystemAddress),
            ["Body"] = project.BodyName ?? string.Empty,
            ["Architect"] = project.ArchitectName ?? string.Empty,
            ["Faction"] = project.FactionName ?? string.Empty,
            ["Commanders"] = string.Join(
                "; ",
                project.Commanders.Select(pair =>
                    $"{pair.Key}{(pair.Value.Count == 0 ? string.Empty : $" ({string.Join(", ", pair.Value)})")}"
                )
            ),
            ["Linked fleet carriers"] = string.Join("; ", preview.Carriers.Select(carrier => carrier.Label)),
            ["Notes"] = project.Notes ?? string.Empty,
            ["Discord"] = project.DiscordLink ?? string.Empty,
            ["Maximum required"] = Number(project.MaximumRequired),
            ["Delivered"] = Number(preview.Delivered),
            ["Progress (%)"] = preview.Progress?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty,
            ["Remaining cargo"] = Number(preview.Remaining),
            ["Ready on fleet carriers"] = Number(preview.ReadyOnCarriers),
            ["Fleet carrier deficit"] = Number(preview.CarrierDeficit),
            ["Large ship capacity"] = Number(ColonizationProjectPreview.LargeShipCapacity),
            ["Medium ship capacity"] = Number(ColonizationProjectPreview.MediumShipCapacity),
            ["Remaining large ship trips"] = Number(
                ColonizationProjectPreview.Trips(preview.Remaining, ColonizationProjectPreview.LargeShipCapacity)
            ),
            ["Remaining medium ship trips"] = Number(
                ColonizationProjectPreview.Trips(preview.Remaining, ColonizationProjectPreview.MediumShipCapacity)
            ),
            ["Deficit large ship trips"] = Number(
                ColonizationProjectPreview.Trips(preview.CarrierDeficit, ColonizationProjectPreview.LargeShipCapacity)
            ),
            ["Deficit medium ship trips"] = Number(
                ColonizationProjectPreview.Trips(preview.CarrierDeficit, ColonizationProjectPreview.MediumShipCapacity)
            ),
            ["Tracked deliveries"] = Number(preview.Statistics?.TotalDeliveries),
            ["Tracked cargo delivered"] = Number(preview.Statistics?.TotalCargo),
            ["First delivery UTC"] =
                preview.Statistics?.Start?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ["Last delivery UTC"] =
                preview.Statistics?.End?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            ["Fetched UTC"] = preview.FetchedAt.ToString("O", CultureInfo.InvariantCulture),
            ["Carrier stock status"] = preview.CarrierDeficit is null ? "Unavailable" : "Available",
            ["Delivery history status"] = preview.Statistics is null ? "Unavailable" : "Available",
        };
    }

    /// <summary>Lists Raven's static system effects, prerequisites, unlocks, and tier points.</summary>
    public static IEnumerable<KeyValuePair<string, string>> EffectDetails(ColonizationBuildEffects effects)
    {
        return new Dictionary<string, string>
        {
            ["Type"] = effects.Name,
            ["System score"] = Number(effects.Score),
            ["Landing pads"] = effects.Pads,
            ["Economic influence"] = effects.Economy,
            ["Average haul"] = effects.AverageHaul.ToString("0", CultureInfo.InvariantCulture),
            ["Needs"] = $"{effects.NeedsCount} Tier {effects.NeedsTier} points",
            ["Provides"] = $"{effects.GivesCount} Tier {effects.GivesTier} points",
            ["Requires"] = effects.Prerequisite,
            ["Unlocks"] = string.Join("; ", effects.Unlocks),
        }.Concat(
            effects.Effects.Select(pair =>
                KeyValuePair.Create(pair.Key, pair.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture))
            )
        );
    }

    /// <summary>Formats numeric cells without locale separators or formula-like text prefixes.</summary>
    private static string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Prevents spreadsheet formulas from being executed when a public name or note begins with an operator.</summary>
    private static string Text(string value) =>
        value.TrimStart() is ['=' or '+' or '-' or '@', ..] ? $"'{value}" : value;

    /// <summary>Quotes every field and uses CRLF records, preserving commas, quotes, Unicode, and multiline notes.</summary>
    private static void Append(StringBuilder csv, IEnumerable<string> cells)
    {
        csv.AppendJoin(',', cells.Select(cell => $"\"{cell.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));
        csv.Append("\r\n");
    }
}
