using System.Globalization;
using System.Text;

namespace SrvSurvey.Core.Colonization;

/// <summary>Exports compact CSV tables for build details, cargo, effects, and delivery history without credentials or editing controls.</summary>
public static partial class ColonizationProjectCsvExporter
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

    /// <summary>Produces labelled sections with only their relevant columns, invariant numbers, and quoted, formula-safe text.</summary>
    public static string Write(ColonizationProjectPreview preview, int currentShipCapacity = 0)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var csv = new StringBuilder();
        AppendTable(
            csv,
            preview.IsCombined ? "Combined report" : "Project details",
            ["Field", "Value"],
            Details(preview)
                .Concat(CurrentShipDetails(preview, currentShipCapacity))
                .Select(pair => new[] { Text(pair.Key), Text(pair.Value) })
        );
        if (preview.IsCombined)
        {
            AppendTable(
                csv,
                "Build projects",
                ["Project", "Build type", "System", "Build ID"],
                preview.Projects.Select(project =>
                    new[]
                    {
                        Text(project.BuildName),
                        Text(BuildTypeName(project)),
                        Text(project.SystemName),
                        Text(project.BuildId),
                    }
                )
            );
            AppendTable(
                csv,
                "Linked commanders",
                ["Commander", "Commodity assignments"],
                preview.Commanders.Select(pair => new[] { Text(pair.Key), Text(string.Join("; ", pair.Value)) })
            );
            AppendTable(
                csv,
                "System effects",
                ["Field", "Value"],
                CombinedEffectDetails(preview).Select(pair => new[] { Text(pair.Key), Text(pair.Value) })
            );
        }
        AppendTable(
            csv,
            "Cargo requirements (tonnes)",
            ["Category", "Commodity", "Need", "FC Diff", .. preview.Carriers.Select(carrier => Text(carrier.Label))],
            preview.Rows.Select(row =>
                new[] { Text(row.Category), Text(row.Name), Number(row.Need), Number(row.CarrierDifference) }
                    .Concat(row.CarrierQuantities.Select(quantity => Number(quantity)))
                    .ToArray()
            )
        );
        AppendTable(
            csv,
            "Fleet carrier cargo (tonnes)",
            ["Fleet carrier", "Total cargo"],
            preview.Carriers.Select(carrier => new[] { Text(carrier.Label), Number(carrier.TotalCargo) })
        );
        if (preview.Effects is { } effects)
        {
            AppendTable(
                csv,
                "System effects",
                ["Field", "Value"],
                EffectDetails(effects).Select(pair => new[] { Text(pair.Key), Text(pair.Value) })
            );
        }
        if (preview.Statistics is { } statistics)
        {
            AppendTable(
                csv,
                "Commander deliveries (tonnes)",
                ["Commander", "Cargo"],
                statistics.Cmdrs.Select(pair => new[] { Text(pair.Key), Number(pair.Value) })
            );
            AppendTable(
                csv,
                "Hourly deliveries (tonnes)",
                ["Time UTC", "Commander", "Cargo"],
                statistics
                    .Stats.OrderBy(bucket => bucket.Time)
                    .SelectMany(bucket =>
                        bucket.Cmdrs.Select(pair =>
                            new[]
                            {
                                bucket.Time.ToString("O", CultureInfo.InvariantCulture),
                                Text(pair.Key),
                                Number(pair.Value),
                            }
                        )
                    )
            );
        }
        return csv.ToString();
    }

    /// <summary>Includes current-ship trip estimates only when the preview has a usable cargo capacity.</summary>
    private static IEnumerable<KeyValuePair<string, string>> CurrentShipDetails(
        ColonizationProjectPreview preview,
        int currentShipCapacity
    )
    {
        if (currentShipCapacity > 0)
        {
            yield return KeyValuePair.Create("Current ship capacity", Number(currentShipCapacity));
            yield return KeyValuePair.Create(
                "Current ship trips",
                Number(ColonizationProjectPreview.Trips(preview.Remaining, currentShipCapacity))
            );
        }
    }

    /// <summary>Separates populated tables with one blank line, omitting empty tables and unrelated column padding.</summary>
    private static void AppendTable(StringBuilder csv, string title, string[] header, IEnumerable<string[]> rows)
    {
        bool started = false;
        foreach (string[] row in rows)
        {
            if (!started)
            {
                if (csv.Length > 0)
                {
                    csv.Append("\r\n");
                }
                Append(csv, [title]);
                Append(csv, header);
                started = true;
            }
            Append(csv, row);
        }
    }

    /// <summary>Includes the build identity and refresh time so exports remain distinct and valid on supported desktops.</summary>
    public static string SuggestedFileName(ColonizationProjectPreview preview)
    {
        if (preview.IsCombined)
        {
            return $"Raven-combined-build-report-{preview.FetchedAt:yyyyMMdd-HHmmss}.csv";
        }
        return $"Raven-build-{string.Concat(preview.Project.BuildId.Select(character => char.IsAsciiLetterOrDigit(character) || character == '-' ? character : '_'))}-{preview.FetchedAt:yyyyMMdd-HHmmss}.csv";
    }

    /// <summary>Lists the public project and summary fields shared by the preview and export.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Details(ColonizationProjectPreview preview)
    {
        if (preview.IsCombined)
        {
            return new Dictionary<string, string>
            {
                ["Build projects"] = Number(preview.Projects.Count),
                ["Systems"] = Number(
                    preview
                        .Projects.Select(project => project.SystemName)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count()
                ),
                ["Linked fleet carriers"] = string.Join("; ", preview.Carriers.Select(carrier => carrier.Label)),
                ["Linked commanders"] = string.Join("; ", preview.Commanders.Keys),
            }.Concat(SummaryDetails(preview));
        }
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
        }.Concat(SummaryDetails(preview));
    }

    /// <summary>Exports the same cargo, progress, trips, and availability fields for individual and combined reports.</summary>
    private static Dictionary<string, string> SummaryDetails(ColonizationProjectPreview preview) =>
        new()
        {
            ["Maximum required"] = Number(preview.MaximumRequired),
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
