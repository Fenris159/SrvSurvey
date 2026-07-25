using System.Text.Json;
using SrvSurvey.Core.Colonization;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Settlements;

public sealed class HumanSiteLiveState(
    HumanSiteTemplateCatalog templates,
    TimeProvider? timeProvider = null)
{
    private const string OnFootSettlement = "OnFootSettlement";
    private const string EngineerGovernment = "$government_Engineer;";
    private readonly HumanSiteTemplateCatalog templates = templates
        ?? throw new ArgumentNullException(nameof(templates));
    private readonly TimeProvider timeProvider = timeProvider
        ?? TimeProvider.System;

    public HumanSiteLiveSnapshot? CurrentSite { get; private set; }

    public int Version { get; private set; }

    public bool ApplyGeometry(HumanSiteGeometrySolution geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (CurrentSite is null
            || geometry.Template.Economy != CurrentSite.Economy
            || geometry.Template.SubType != geometry.SubType
            || !double.IsFinite(geometry.Heading))
        {
            return false;
        }

        var heading = SurfaceNavigation.NormalizeDegrees(geometry.Heading);
        if (CurrentSite.SubType == geometry.SubType
            && CurrentSite.Heading == heading)
        {
            return false;
        }

        CurrentSite = CurrentSite with
        {
            SubType = geometry.SubType,
            Template = geometry.Template,
            Heading = heading,
        };
        Version++;
        return true;
    }

    public bool ApplyKnowledge(HumanSiteKnowledge knowledge)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        if (CurrentSite is null
            || CurrentSite.MarketId != knowledge.MarketId
            || CurrentSite.SystemAddress != knowledge.SystemAddress
            || CurrentSite.Economy != knowledge.Economy)
        {
            return false;
        }

        var template = knowledge.SubType > 0
            ? templates.Find(knowledge.Economy, knowledge.SubType)
            : null;
        var heading = knowledge.Heading is { } savedHeading
            ? SurfaceNavigation.NormalizeDegrees(savedHeading)
            : CurrentSite.Heading;
        var pads = knowledge.AvailablePads.Total > 0
            ? knowledge.AvailablePads
            : CurrentSite.AvailablePads;
        var subType = template?.SubType ?? CurrentSite.SubType;
        template ??= CurrentSite.Template;
        if (CurrentSite.SubType == subType
            && CurrentSite.Template == template
            && CurrentSite.Heading == heading
            && CurrentSite.AvailablePads == pads)
        {
            return false;
        }

        CurrentSite = CurrentSite with
        {
            SubType = subType,
            Template = template,
            Heading = heading,
            AvailablePads = pads,
        };
        Version++;
        return true;
    }

    public bool Apply(JournalEventEnvelope journalEvent)
    {
        ArgumentNullException.ThrowIfNull(journalEvent);
        var changed = journalEvent.EventName switch
        {
            "ApproachSettlement" => ApplyApproach(journalEvent),
            "DockingRequested" => ApplyDockingRequested(journalEvent.Payload),
            "DockingGranted" => ApplyDockingGranted(journalEvent.Payload),
            "DockingDenied" => ApplyDockingDenied(journalEvent.Payload),
            "DockingCancelled" => ApplyDockingCancelled(journalEvent.Payload),
            "Docked" => ApplyDocked(journalEvent.Payload),
            "Undocked" => ApplyUndocked(journalEvent.Payload),
            "Touchdown" => ApplyTouchdown(journalEvent.Payload),
            "StartJump" or "SupercruiseEntry" or "FSDJump"
                or "CarrierJump" or "Shutdown" => Clear(),
            _ => false,
        };
        if (changed)
        {
            if (CurrentSite is not null)
            {
                CurrentSite = CurrentSite with
                {
                    LastUpdated = journalEvent.Timestamp
                        ?? timeProvider.GetUtcNow(),
                };
            }

            Version++;
        }

        return changed;
    }

    private bool ApplyApproach(JournalEventEnvelope journalEvent)
    {
        var root = journalEvent.Payload;
        if (!TryReadCompatibleSite(root, out var site))
        {
            return Clear();
        }

        var timestamp = journalEvent.Timestamp ?? timeProvider.GetUtcNow();
        var current = CurrentSite;
        var firstApproached = current is not null
            && IsSameSite(current, site.MarketId, site.SystemAddress)
                ? current.FirstApproached
                : timestamp;
        CurrentSite = site with
        {
            FirstApproached = firstApproached,
            LastUpdated = timestamp,
            AvailablePads = current is not null
                && IsSameSite(current, site.MarketId, site.SystemAddress)
                    ? current.AvailablePads
                    : HumanSiteLandingPads.Empty,
            SubType = current is not null
                && IsSameSite(current, site.MarketId, site.SystemAddress)
                    ? current.SubType
                    : 0,
            Template = current is not null
                && IsSameSite(current, site.MarketId, site.SystemAddress)
                    ? current.Template
                    : null,
            Heading = current is not null
                && IsSameSite(current, site.MarketId, site.SystemAddress)
                    ? current.Heading
                    : null,
            Docking = HumanSiteDockingStatus.None,
            GrantedPad = 0,
            DockingDeniedReason = null,
            HasLanded = current is not null
                && IsSameSite(current, site.MarketId, site.SystemAddress)
                && current.HasLanded,
        };
        return true;
    }

    private bool ApplyDockingRequested(JsonElement root)
    {
        if (!IsCurrentStation(root) || !IsOnFootSettlement(root))
        {
            return false;
        }

        var pads = ReadLandingPads(root);
        var subType = InferSubType(CurrentSite!.Economy, pads);
        CurrentSite = CurrentSite with
        {
            StationType = OnFootSettlement,
            AvailablePads = pads,
            SubType = subType,
            Template = subType > 0
                ? templates.Find(CurrentSite.Economy, subType)
                : null,
            Docking = HumanSiteDockingStatus.Requested,
            GrantedPad = 0,
            DockingDeniedReason = null,
        };
        return true;
    }

    private bool ApplyDockingGranted(JsonElement root)
    {
        if (!IsCurrentStation(root) || !IsOnFootSettlement(root))
        {
            return false;
        }

        CurrentSite = CurrentSite! with
        {
            StationType = OnFootSettlement,
            Docking = HumanSiteDockingStatus.Granted,
            GrantedPad = Math.Max(0, GetInt32(root, "LandingPad") ?? 0),
            DockingDeniedReason = null,
        };
        return true;
    }

    private bool ApplyDockingDenied(JsonElement root)
    {
        if (!IsCurrentStation(root))
        {
            return false;
        }

        CurrentSite = CurrentSite! with
        {
            Docking = HumanSiteDockingStatus.Denied,
            GrantedPad = 0,
            DockingDeniedReason = GetString(root, "Reason"),
        };
        return true;
    }

    private bool ApplyDockingCancelled(JsonElement root)
    {
        if (!IsCurrentStation(root))
        {
            return false;
        }

        CurrentSite = CurrentSite! with
        {
            Docking = HumanSiteDockingStatus.None,
            GrantedPad = 0,
            DockingDeniedReason = null,
        };
        return true;
    }

    private bool ApplyDocked(JsonElement root)
    {
        if (!IsCurrentStation(root) || !IsOnFootSettlement(root))
        {
            return false;
        }

        var pads = ReadLandingPads(root);
        if (pads == HumanSiteLandingPads.Empty)
        {
            pads = CurrentSite!.AvailablePads;
        }

        var subType = CurrentSite!.SubType > 0
            ? CurrentSite.SubType
            : InferSubType(CurrentSite.Economy, pads);
        CurrentSite = CurrentSite with
        {
            StationType = OnFootSettlement,
            AvailablePads = pads,
            SubType = subType,
            Template = subType > 0
                ? templates.Find(CurrentSite.Economy, subType)
                : null,
            Docking = HumanSiteDockingStatus.Docked,
            DockingDeniedReason = null,
            HasLanded = true,
        };
        return true;
    }

    private bool ApplyUndocked(JsonElement root)
    {
        if (!IsCurrentStation(root))
        {
            return false;
        }

        CurrentSite = CurrentSite! with
        {
            Docking = HumanSiteDockingStatus.None,
            GrantedPad = 0,
            DockingDeniedReason = null,
        };
        return true;
    }

    private bool ApplyTouchdown(JsonElement root)
    {
        if (CurrentSite is null)
        {
            return false;
        }

        var nearestDestination = GetString(root, "NearestDestination");
        if (!string.IsNullOrWhiteSpace(nearestDestination)
            && !string.Equals(
                nearestDestination,
                CurrentSite.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        CurrentSite = CurrentSite with { HasLanded = true };
        return true;
    }

    private bool TryReadCompatibleSite(
        JsonElement root,
        out HumanSiteLiveSnapshot site)
    {
        site = null!;
        var name = GetString(root, "Name");
        var services = GetStringArray(root, "StationServices");
        var marketId = GetInt64(root, "MarketID") ?? 0;
        var systemAddress = GetInt64(root, "SystemAddress") ?? 0;
        var bodyId = GetInt32(root, "BodyID") ?? -1;
        var latitude = GetDouble(root, "Latitude");
        var longitude = GetDouble(root, "Longitude");
        var economy = HumanSiteEconomyParser.ParseJournalValue(
            GetString(root, "StationEconomy"));
        if (string.IsNullOrWhiteSpace(name)
            || name.StartsWith("$Ancient", StringComparison.Ordinal)
            || marketId <= 0
            || systemAddress <= 0
            || bodyId < 0
            || latitude is not >= -90 or > 90
            || longitude is not >= -180 or > 180
            || economy == HumanSiteEconomy.Unknown
            || services.Count == 0
            || services.Contains("socialspace", StringComparer.OrdinalIgnoreCase)
            || IsConstructionSite(name, services)
            || string.Equals(
                GetString(root, "StationGovernment"),
                EngineerGovernment,
                StringComparison.Ordinal))
        {
            return false;
        }

        site = new HumanSiteLiveSnapshot(
            name,
            GetString(root, "Name_Localised") ?? name,
            marketId,
            systemAddress,
            bodyId,
            GetString(root, "BodyName") ?? string.Empty,
            new HumanSiteSurfaceLocation(latitude.Value, longitude.Value),
            economy,
            GetString(root, "StationEconomy") ?? string.Empty,
            GetString(root, "StationEconomy_Localised") ?? string.Empty,
            GetNestedString(root, "StationFaction", "Name") ?? string.Empty,
            GetNestedString(root, "StationFaction", "FactionState"),
            GetString(root, "StationGovernment") ?? string.Empty,
            GetString(root, "StationGovernment_Localised") ?? string.Empty,
            services,
            null,
            HumanSiteLandingPads.Empty,
            0,
            null,
            null,
            HumanSiteDockingStatus.None,
            0,
            null,
            false,
            default,
            default);
        return true;
    }

    private int InferSubType(
        HumanSiteEconomy economy,
        HumanSiteLandingPads pads)
    {
        var matches = templates.ForEconomy(economy)
            .Where(template => HumanSiteLandingPads.From(template) == pads)
            .Select(template => template.SubType)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : 0;
    }

    private bool IsCurrentStation(JsonElement root)
    {
        if (CurrentSite is null)
        {
            return false;
        }

        var marketId = GetInt64(root, "MarketID");
        return marketId == CurrentSite.MarketId;
    }

    private static bool IsOnFootSettlement(JsonElement root)
    {
        return string.Equals(
            GetString(root, "StationType"),
            OnFootSettlement,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameSite(
        HumanSiteLiveSnapshot site,
        long marketId,
        long systemAddress)
    {
        return site.MarketId == marketId
            && site.SystemAddress == systemAddress;
    }

    private bool Clear()
    {
        if (CurrentSite is null)
        {
            return false;
        }

        CurrentSite = null;
        return true;
    }

    private static bool IsConstructionSite(
        string name,
        IReadOnlyList<string> services)
    {
        return ColonizationDockingSnapshot.IsConstructionSiteName(name)
            && services.Contains(
                "colonisationcontribution",
                StringComparer.OrdinalIgnoreCase);
    }

    private static HumanSiteLandingPads ReadLandingPads(JsonElement root)
    {
        if (!root.TryGetProperty("LandingPads", out var pads)
            || pads.ValueKind != JsonValueKind.Object)
        {
            return HumanSiteLandingPads.Empty;
        }

        return new HumanSiteLandingPads(
            Math.Max(0, GetInt32(pads, "Small") ?? 0),
            Math.Max(0, GetInt32(pads, "Medium") ?? 0),
            Math.Max(0, GetInt32(pads, "Large") ?? 0));
    }

    private static IReadOnlyList<string> GetStringArray(
        JsonElement root,
        string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!)
                    .ToArray()
                : [];
    }

    private static string? GetNestedString(
        JsonElement root,
        string objectName,
        string propertyName)
    {
        return root.TryGetProperty(objectName, out var nested)
            && nested.ValueKind == JsonValueKind.Object
                ? GetString(nested, propertyName)
                : null;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? GetInt32(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var result)
                ? result
                : null;
    }

    private static long? GetInt64(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            && value.TryGetInt64(out var result)
                ? result
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
}

public static class HumanSiteEconomyParser
{
    public static HumanSiteEconomy ParseJournalValue(string? value)
    {
        return value switch
        {
            "$economy_Agri;" => HumanSiteEconomy.Agriculture,
            "$economy_Colony;" => HumanSiteEconomy.Colony,
            "$economy_Damaged;" => HumanSiteEconomy.Damaged,
            "$economy_Extraction;" => HumanSiteEconomy.Extraction,
            "$economy_HighTech;" => HumanSiteEconomy.HighTech,
            "$economy_Industrial;" => HumanSiteEconomy.Industrial,
            "$economy_Military;" => HumanSiteEconomy.Military,
            "$economy_Prison;" => HumanSiteEconomy.Prison,
            "$economy_Carrier;" => HumanSiteEconomy.PrivateEnterprise,
            "$economy_Refinery;" => HumanSiteEconomy.Refinery,
            "$economy_Repair;" => HumanSiteEconomy.Repair,
            "$economy_Rescue;" => HumanSiteEconomy.Rescue,
            "$economy_Service;" => HumanSiteEconomy.Service,
            "$economy_Terraforming;" => HumanSiteEconomy.Terraforming,
            "$economy_Tourism;" => HumanSiteEconomy.Tourist,
            _ => HumanSiteEconomy.Unknown,
        };
    }
}

public sealed record HumanSiteLiveSnapshot(
    string Name,
    string LocalizedName,
    long MarketId,
    long SystemAddress,
    int BodyId,
    string BodyName,
    HumanSiteSurfaceLocation Location,
    HumanSiteEconomy Economy,
    string EconomyToken,
    string EconomyLocalized,
    string FactionName,
    string? FactionState,
    string Government,
    string GovernmentLocalized,
    IReadOnlyList<string> Services,
    string? StationType,
    HumanSiteLandingPads AvailablePads,
    int SubType,
    HumanSiteTemplate? Template,
    double? Heading,
    HumanSiteDockingStatus Docking,
    int GrantedPad,
    string? DockingDeniedReason,
    bool HasLanded,
    DateTimeOffset FirstApproached,
    DateTimeOffset LastUpdated);

public readonly record struct HumanSiteSurfaceLocation(
    double Latitude,
    double Longitude);

public readonly record struct HumanSiteLandingPads(
    int Small,
    int Medium,
    int Large)
{
    public static HumanSiteLandingPads Empty { get; } = new(0, 0, 0);

    public static HumanSiteLandingPads From(HumanSiteTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new HumanSiteLandingPads(
            template.LandingPads.Count(
                pad => pad.Size == HumanSiteLandingPadSize.Small),
            template.LandingPads.Count(
                pad => pad.Size == HumanSiteLandingPadSize.Medium),
            template.LandingPads.Count(
                pad => pad.Size == HumanSiteLandingPadSize.Large));
    }

    public int Total => Small + Medium + Large;
}

public enum HumanSiteDockingStatus
{
    None,
    Requested,
    Granted,
    Denied,
    Docked,
}

public sealed record HumanSiteKnowledge(
    string Name,
    long MarketId,
    long SystemAddress,
    int BodyId,
    HumanSiteEconomy Economy,
    string EconomyToken,
    HumanSiteSurfaceLocation Location,
    int SubType,
    double? Heading,
    HumanSiteLandingPads AvailablePads,
    HumanSiteGeometrySource GeometrySource);

public enum HumanSiteGeometrySource
{
    Unknown,
    PadConfig,
    AutoDock,
    ManualDock,
    ManualFoot,
    TaxiDock,
}
