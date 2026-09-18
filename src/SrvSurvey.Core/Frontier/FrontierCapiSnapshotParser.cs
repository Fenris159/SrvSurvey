using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SrvSurvey.Core.Frontier;

public static partial class FrontierCapiSnapshotParser
{
    private const string RankElite = "Elite";
    private const string RankEliteI = "Elite I";
    private const string RankEliteII = "Elite II";
    private const string RankEliteIII = "Elite III";
    private const string RankEliteIV = "Elite IV";
    private const string RankEliteV = "Elite V";
    private const string JsonLastSystem = "lastSystem";
    private const string JsonLastStarport = "lastStarport";
    private const string JsonLocName = "locName";
    private const string JsonValue = "value";
    private const string JsonTotal = "total";
    private const string JsonStock = "stock";
    private const string JsonStarSystem = "starsystem";
    private const string JsonSystemName = "systemName";
    private const string JsonPlayerContribution = "playerContribution";
    private const string JsonContribution = "contribution";

    private static readonly Dictionary<string, string[]> RankNames = new Dictionary<string, string[]>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["combat"] =
        [
            "Harmless",
            "Mostly Harmless",
            "Novice",
            "Competent",
            "Expert",
            "Master",
            "Dangerous",
            "Deadly",
            RankElite,
            RankEliteI,
            RankEliteII,
            RankEliteIII,
            RankEliteIV,
            RankEliteV,
        ],
        ["trade"] =
        [
            "Penniless",
            "Mostly Penniless",
            "Peddler",
            "Dealer",
            "Merchant",
            "Broker",
            "Entrepreneur",
            "Tycoon",
            RankElite,
            RankEliteI,
            RankEliteII,
            RankEliteIII,
            RankEliteIV,
            RankEliteV,
        ],
        ["explore"] =
        [
            "Aimless",
            "Mostly Aimless",
            "Scout",
            "Surveyor",
            "Trailblazer",
            "Pathfinder",
            "Ranger",
            "Pioneer",
            RankElite,
            RankEliteI,
            RankEliteII,
            RankEliteIII,
            RankEliteIV,
            RankEliteV,
        ],
        ["soldier"] =
        [
            "Defenceless",
            "Mostly Defenceless",
            "Rookie",
            "Soldier",
            "Gunslinger",
            "Warrior",
            "Gladiator",
            "Deadeye",
            RankElite,
            RankEliteI,
            RankEliteII,
            RankEliteIII,
            RankEliteIV,
            RankEliteV,
        ],
        ["exobiologist"] =
        [
            "Directionless",
            "Mostly Directionless",
            "Compiler",
            "Collector",
            "Cataloguer",
            "Taxonomist",
            "Ecologist",
            "Geneticist",
            RankElite,
            RankEliteI,
            RankEliteII,
            RankEliteIII,
            RankEliteIV,
            RankEliteV,
        ],
        ["cqc"] =
        [
            "Helpless",
            "Mostly Helpless",
            "Amateur",
            "Semi Professional",
            "Professional",
            "Champion",
            "Hero",
            "Gladiator",
            RankElite,
            RankEliteI,
            RankEliteII,
            RankEliteIII,
            RankEliteIV,
            RankEliteV,
        ],
        ["federation"] =
        [
            "None",
            "Recruit",
            "Cadet",
            "Midshipman",
            "Petty Officer",
            "Chief Petty Officer",
            "Warrant Officer",
            "Ensign",
            "Lieutenant",
            "Lieutenant Commander",
            "Post Commander",
            "Post Captain",
            "Rear Admiral",
            "Vice Admiral",
            "Admiral",
        ],
        ["empire"] =
        [
            "None",
            "Outsider",
            "Serf",
            "Master",
            "Squire",
            "Knight",
            "Lord",
            "Baron",
            "Viscount",
            "Count",
            "Earl",
            "Marquis",
            "Duke",
            "Prince",
            "King",
        ],
        ["power"] = ["None", "Rating 1", "Rating 2", "Rating 3", "Rating 4", "Rating 5"],
    };

    private static readonly IReadOnlyDictionary<string, string> RankCategories = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["combat"] = "Combat",
        ["trade"] = "Trade",
        ["explore"] = "Exploration",
        ["soldier"] = "Mercenary",
        ["exobiologist"] = "Exobiology",
        ["cqc"] = "CQC",
        ["federation"] = "Federation",
        ["empire"] = "Empire",
        ["power"] = "Powerplay",
    };

    private static readonly Dictionary<string, string> ShipNames = new Dictionary<string, string>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["sidewinder"] = "Sidewinder",
        ["cobramkiii"] = "Cobra Mk III",
        ["cobramkiv"] = "Cobra Mk IV",
        ["cobramkv"] = "Cobra Mk V",
        ["vipermkiii"] = "Viper Mk III",
        ["vipermkiv"] = "Viper Mk IV",
        ["diamondback"] = "Diamondback Scout",
        ["diamondbackxl"] = "Diamondback Explorer",
        ["federaldropship"] = "Federal Dropship",
        ["federalassaultship"] = "Federal Assault Ship",
        ["federalgunship"] = "Federal Gunship",
        ["imperialcourier"] = "Imperial Courier",
        ["imperialclipper"] = "Imperial Clipper",
        ["imperialcutter"] = "Imperial Cutter",
        ["imperialeagle"] = "Imperial Eagle",
        ["ferdelance"] = "Fer-de-Lance",
        ["belugaliner"] = "Beluga Liner",
        ["type6"] = "Type-6 Transporter",
        ["type7"] = "Type-7 Transporter",
        ["type8"] = "Type-8 Transporter",
        ["type9"] = "Type-9 Heavy",
        ["type9_military"] = "Type-10 Defender",
        ["krait_mkii"] = "Krait Mk II",
        ["krait_light"] = "Krait Phantom",
        ["alliance_chieftain"] = "Alliance Chieftain",
        ["alliance_challenger"] = "Alliance Challenger",
        ["alliance_crusader"] = "Alliance Crusader",
        ["python_nx"] = "Python Mk II",
    };

    public static FrontierAccountSnapshot Parse(string profileJson, string? carrierJson, DateTimeOffset fetchedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileJson);

        using var profile = JsonDocument.Parse(profileJson);
        JsonElement root = RequireObject(profile.RootElement, "Frontier profile");
        JsonElement commander =
            GetObject(root, "commander")
            ?? throw new InvalidDataException("Frontier profile did not contain commander information.");

        string commanderName = GetString(commander, "name");
        if (string.IsNullOrWhiteSpace(commanderName))
        {
            throw new InvalidDataException("Frontier profile did not contain a commander name.");
        }

        JsonElement? currentShipElement = GetObject(root, "ship");
        long? currentShipId = GetInt64(commander, "currentShipId");
        var parsedShips = EnumerateObjects(root, "ships")
            .Select(
                (element, index) =>
                    ParseShip(
                        element,
                        currentShipElement,
                        currentShipId,
                        index,
                        GetString(GetObject(root, JsonLastSystem), "name"),
                        GetString(GetObject(root, JsonLastStarport), "name")
                    )
            )
            .Where(ship => ship is not null)
            .Cast<FrontierShipSnapshot>()
            .ToList();

        FrontierShipSnapshot? parsedCurrentShip = currentShipElement is { } shipElement
            ? ParseShip(
                shipElement,
                currentShipElement,
                currentShipId,
                index: -1,
                GetString(GetObject(root, JsonLastSystem), "name"),
                GetString(GetObject(root, JsonLastStarport), "name"),
                forceCurrent: true
            )
            : parsedShips.FirstOrDefault(ship => ship.IsCurrent);

        if (parsedCurrentShip is not null && !parsedShips.Any(ship => SameShip(ship, parsedCurrentShip)))
        {
            parsedShips.Insert(0, parsedCurrentShip);
        }

        parsedShips = parsedShips
            .OrderByDescending(ship => ship.IsCurrent)
            .ThenBy(ship => ship.Type, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(ship => ship.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FrontierCarrierEndpointSnapshot? carrierEndpoint = string.IsNullOrWhiteSpace(carrierJson)
            ? null
            : ParseCarrierEndpoint(carrierJson, fetchedAt);
        FrontierReputationSnapshot[] profileReputation = ParseReputation(
            GetProperty(root, "reputation") ?? GetProperty(commander, "reputation")
        );
        FrontierReputationSnapshot[] commanderReputation = MergeReputation(
            profileReputation,
            carrierEndpoint?.CommanderReputation ?? []
        );
        FrontierLocationSnapshot? lastSystem = ParseLocation(GetObject(root, JsonLastSystem), false);
        FrontierLocationSnapshot? lastStation = ParseLocation(GetObject(root, JsonLastStarport), true);

        return new FrontierAccountSnapshot(
            commanderName.Trim(),
            GetInt64(commander, "credits") ?? 0,
            GetInt64(commander, "debt") ?? 0,
            GetBoolean(commander, "docked") ?? false,
            GetBoolean(commander, "alive") ?? true,
            GetString(GetObject(root, JsonLastSystem), "name"),
            GetString(GetObject(root, JsonLastStarport), "name"),
            parsedCurrentShip,
            ParseRanks(GetObject(commander, "rank")),
            parsedShips,
            ParseCapabilities(GetObject(commander, "capabilities")),
            carrierEndpoint?.Carrier,
            fetchedAt,
            GetInt64(commander, "id"),
            lastSystem,
            lastStation,
            ProfileData: Flatten(root, "profile"),
            CarrierFetchedAt: carrierJson is null ? null : fetchedAt,
            CommanderReputation: commanderReputation,
            CommanderReputationFetchedAt: commanderReputation.Length > 0 ? fetchedAt : null,
            CarrierEndpointData: carrierEndpoint?.DataPoints
        );
    }

    public static FrontierMarketSnapshot ParseMarket(string marketJson, DateTimeOffset fetchedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketJson);
        using var document = JsonDocument.Parse(marketJson);
        JsonElement root = RequireObject(document.RootElement, "Frontier market");
        return ParseMarket(root, fetchedAt, "market");
    }

    public static FrontierShipyardSnapshot ParseShipyard(string shipyardJson, DateTimeOffset fetchedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipyardJson);
        using var document = JsonDocument.Parse(shipyardJson);
        JsonElement root = RequireObject(document.RootElement, "Frontier shipyard");
        return ParseShipyard(root, fetchedAt, "shipyard");
    }

    public static IReadOnlyList<FrontierCommunityGoalSnapshot> ParseCommunityGoals(string communityGoalsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(communityGoalsJson);
        using var document = JsonDocument.Parse(communityGoalsJson);
        List<JsonElement> candidates = FindCommunityGoalObjects(document.RootElement);
        return candidates
            .Select(ParseCommunityGoal)
            .Where(goal => !string.IsNullOrWhiteSpace(goal.Title))
            .OrderBy(goal => goal.IsComplete)
            .ThenBy(goal => goal.ExpiresAt ?? DateTimeOffset.MaxValue)
            .ThenBy(goal => goal.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<FrontierDataPointSnapshot> ParseDataPoints(string json, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        return Flatten(document.RootElement, source);
    }

    private static FrontierShipSnapshot? ParseShip(
        JsonElement ship,
        JsonElement? currentShip,
        long? currentShipId,
        int index,
        string fallbackSystem,
        string fallbackStation,
        bool forceCurrent = false
    )
    {
        if (ship.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        long? id = GetInt64(ship, "id") ?? GetInt64(ship, "shipID");
        long? currentId = currentShip is { } current ? GetInt64(current, "id") ?? GetInt64(current, "shipID") : null;
        bool isCurrent =
            forceCurrent
            || (id is not null && currentId == id)
            || (currentShipId is not null && (currentShipId == id || currentShipId == index));
        string type = FirstNonEmpty(GetString(ship, JsonLocName), GetString(ship, "name"), "Unknown ship");
        string customName = GetString(ship, "shipName");
        string identifier = GetString(ship, "shipID");
        string system = GetString(GetObject(ship, JsonStarSystem), "name");
        string station = GetString(GetObject(ship, "station"), "name");
        if (isCurrent)
        {
            system = FirstNonEmpty(system, fallbackSystem);
            station = FirstNonEmpty(station, fallbackStation);
        }

        JsonElement? health = GetObject(ship, "health");
        JsonElement? value = GetObject(ship, JsonValue);
        JsonElement? starsystem = GetObject(ship, JsonStarSystem);
        JsonElement? stationObject = GetObject(ship, "station");
        return new FrontierShipSnapshot(
            id,
            HumanizeShipType(type),
            FirstNonEmpty(customName, HumanizeShipType(type)),
            identifier,
            system,
            station,
            GetInt64(value, JsonTotal) ?? 0,
            isCurrent,
            NormalizeHealth(GetDouble(health, "hull")),
            NormalizeHealth(GetDouble(health, "shield")),
            GetInt64(value, "hull") ?? 0,
            GetInt64(value, "modules") ?? 0,
            GetInt64(value, "cargo") ?? 0,
            GetInt64(value, "unloaned") ?? 0,
            GetBoolean(ship, "free") ?? false,
            GetBoolean(ship, "alive") ?? true,
            GetBoolean(health, "shieldup") ?? false,
            NormalizeHealth(GetDouble(health, "integrity")),
            NormalizeHealth(GetDouble(health, "paintwork")),
            GetBoolean(ship, "cockpitBreached") ?? false,
            GetDouble(ship, "oxygenRemaining"),
            GetInt64(starsystem, "id"),
            GetInt64(starsystem, "systemaddress"),
            GetInt64(stationObject, "id"),
            ParseShipModules(GetObject(ship, "modules")),
            ParseLaunchBays(GetObject(ship, "launchBays")),
            Flatten(ship, "ship")
        );
    }

    private static FrontierLocationSnapshot? ParseLocation(JsonElement? value, bool includeServices)
    {
        if (value is not { ValueKind: JsonValueKind.Object } location)
        {
            return null;
        }

        string name = GetString(location, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string[] services = includeServices
            ? ParseNamedValues(GetProperty(location, "services"))
                .Where(item => IsAvailable(item.Value))
                .Select(item => item.Name)
                .ToArray()
            : [];
        return new FrontierLocationSnapshot(
            GetInt64(location, "id"),
            GetInt64(location, "systemaddress"),
            name,
            HumanizeIdentifier(FirstNonEmpty(GetString(location, "faction"), GetString(location, "allegiance"))),
            ReadNamedValue(GetProperty(location, "minorfaction")),
            services
        );
    }

    private static FrontierShipModuleSnapshot[] ParseShipModules(JsonElement? modules)
    {
        if (modules is not { ValueKind: JsonValueKind.Object } moduleObject)
        {
            return [];
        }

        var result = new List<FrontierShipModuleSnapshot>();
        foreach (JsonProperty property in moduleObject.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            JsonElement wrapper = property.Value;
            JsonElement module = GetObject(wrapper, "module") ?? wrapper;
            JsonElement? engineer = GetObject(wrapper, "engineer");
            string[] effects = EnumerateScalarValues(GetProperty(wrapper, "specialModifications"));
            result.Add(
                new FrontierShipModuleSnapshot(
                    HumanizeIdentifier(property.Name),
                    GetInt64(module, "id"),
                    FirstNonEmpty(
                        GetString(module, JsonLocName),
                        HumanizeIdentifier(GetString(module, "name")),
                        "Unknown module"
                    ),
                    GetString(module, "locDescription"),
                    GetInt64(module, JsonValue) ?? 0,
                    GetBoolean(module, "free") ?? false,
                    NormalizeHealth(GetDouble(module, "health")),
                    GetBoolean(module, "on") ?? false,
                    GetInt32(module, "priority"),
                    GetString(engineer, "engineerName"),
                    FirstNonEmpty(
                        GetString(engineer, "recipeLocName"),
                        HumanizeIdentifier(GetString(engineer, "recipeName"))
                    ),
                    GetInt32(engineer, "recipeLevel"),
                    effects,
                    GetString(module, "name")
                )
            );
        }

        return result.OrderBy(item => item.Slot, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static FrontierLaunchBaySnapshot[] ParseLaunchBays(JsonElement? launchBays)
    {
        if (launchBays is not { ValueKind: JsonValueKind.Object } bayObject)
        {
            return [];
        }

        return bayObject
            .EnumerateObject()
            .Where(item => item.Value.ValueKind == JsonValueKind.Object)
            .Select(item => new FrontierLaunchBaySnapshot(
                HumanizeIdentifier(item.Name),
                FirstNonEmpty(GetString(item.Value, JsonLocName), HumanizeIdentifier(GetString(item.Value, "name"))),
                FirstNonEmpty(
                    GetString(item.Value, "loadoutName"),
                    HumanizeIdentifier(GetString(item.Value, "loadout"))
                ),
                GetInt32(item.Value, "rebuilds") ?? 0
            ))
            .OrderBy(item => item.Slot, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static List<FrontierRankSnapshot> ParseRanks(JsonElement? ranks)
    {
        if (ranks is not { ValueKind: JsonValueKind.Object } rankObject)
        {
            return [];
        }

        var result = new List<FrontierRankSnapshot>();
        foreach (string key in RankCategories.Keys)
        {
            int? level = GetInt32(rankObject, key);
            if (level is null)
            {
                continue;
            }

            string[] names = RankNames[key];
            string name =
                level >= 0 && level < names.Length
                    ? names[level.Value]
                    : $"Rank {level.Value.ToString(CultureInfo.InvariantCulture)}";
            result.Add(new FrontierRankSnapshot(key, RankCategories[key], level.Value, name));
        }

        return result;
    }

    private static string[] ParseCapabilities(JsonElement? capabilities)
    {
        if (capabilities is not { ValueKind: JsonValueKind.Object } values)
        {
            return [];
        }

        return values
            .EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.True)
            .Select(property => HumanizeIdentifier(property.Name))
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static FrontierCarrierSnapshot ParseCarrier(string carrierJson, DateTimeOffset fetchedAt)
    {
        FrontierCarrierEndpointSnapshot endpoint = ParseCarrierEndpoint(carrierJson, fetchedAt);
        return endpoint.Carrier
            ?? throw new InvalidDataException("Frontier fleet-carrier response did not contain a carrier callsign.");
    }

    public static FrontierCarrierEndpointSnapshot ParseCarrierEndpoint(string carrierJson, DateTimeOffset fetchedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierJson);
        using var document = JsonDocument.Parse(carrierJson);
        JsonElement root = RequireObject(document.RootElement, "Frontier fleet carrier");
        return ParseCarrierEndpoint(root, fetchedAt, dataPathPrefix: "fleetcarrier");
    }

    /// <summary>
    /// Parses Frontier CAPI <c>/squadron</c> payloads. EDDI reads the nested
    /// <c>squadronCarrier</c> object with the same shape as <c>/fleetcarrier</c>.
    /// </summary>
    public static FrontierCarrierEndpointSnapshot ParseSquadronEndpoint(string squadronJson, DateTimeOffset fetchedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(squadronJson);
        using var document = JsonDocument.Parse(squadronJson);
        JsonElement root = RequireObject(document.RootElement, "Frontier squadron");
        List<FrontierDataPointSnapshot> squadronData = Flatten(root, "squadron");
        if (GetObject(root, "squadronCarrier") is not { } carrierRoot)
        {
            return new FrontierCarrierEndpointSnapshot(
                null,
                ParseReputation(GetProperty(root, "reputation")),
                squadronData
            );
        }

        FrontierCarrierEndpointSnapshot carrier = ParseCarrierEndpoint(
            carrierRoot,
            fetchedAt,
            dataPathPrefix: "squadron.squadronCarrier"
        );
        return new FrontierCarrierEndpointSnapshot(
            carrier.Carrier,
            carrier.CommanderReputation.Count > 0
                ? carrier.CommanderReputation
                : ParseReputation(GetProperty(root, "reputation")),
            squadronData.Concat(carrier.DataPoints).ToArray()
        );
    }

    private static FrontierCarrierEndpointSnapshot ParseCarrierEndpoint(
        JsonElement root,
        DateTimeOffset fetchedAt,
        string dataPathPrefix
    )
    {
        FrontierReputationSnapshot[] reputation = ParseReputation(GetProperty(root, "reputation"));
        List<FrontierDataPointSnapshot> dataPoints = Flatten(root, dataPathPrefix);
        JsonElement? name = GetObject(root, "name");
        if (string.IsNullOrWhiteSpace(GetString(name, "callsign")))
        {
            return new FrontierCarrierEndpointSnapshot(null, reputation, dataPoints);
        }

        JsonElement? capacity = GetObject(root, "capacity");
        JsonElement? finance = GetObject(root, "finance");
        JsonElement? marketFinances = GetObject(root, "marketFinances");
        JsonElement? blackMarketFinances = GetObject(root, "blackmarketFinances");
        JsonElement? bartenderFinances = GetObject(finance, "bartender");
        JsonElement? itinerary = GetObject(root, "itinerary");

        FrontierCapacitySnapshot[] capacityRows = capacity is { } capacityValue
            ? capacityValue
                .EnumerateObject()
                .Where(property =>
                    property.Value.TryGetInt32(out _)
                    && !property.Name.Contains("microresource", StringComparison.OrdinalIgnoreCase)
                    && !property.Name.Equals("freeSpace", StringComparison.OrdinalIgnoreCase)
                )
                .Select(property => new FrontierCapacitySnapshot(
                    HumanizeIdentifier(property.Name),
                    property.Value.GetInt32()
                ))
                .Where(item => item.Used > 0)
                .OrderByDescending(item => item.Used)
                .ToArray()
            : [];
        int capacityFree = GetInt32(capacity, "freeSpace") ?? 0;
        int capacityUsed = capacityRows.Sum(row => row.Used);

        FrontierInventorySnapshot[] cargo = EnumerateObjects(root, "cargo")
            .Select(item => new FrontierInventorySnapshot(
                "Cargo",
                FirstNonEmpty(
                    GetString(item, JsonLocName),
                    HumanizeIdentifier(GetString(item, "commodity")),
                    "Unknown commodity"
                ),
                GetInt32(item, "qty") ?? 1,
                GetInt64(item, JsonValue) ?? 0
            ))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new FrontierInventorySnapshot(
                "Cargo",
                group.First().Name,
                group.Sum(item => item.Quantity),
                group.Sum(item => item.Value)
            ))
            .OrderByDescending(item => item.Quantity)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        FrontierInventorySnapshot[] locker = ParseLocker(GetObject(root, "carrierLocker"));
        JsonElement? orders = GetObject(root, "orders");
        JsonElement? commodities = GetObject(orders, "commodities");
        JsonElement? microresources = GetObject(orders, "onfootmicroresources");
        FrontierMarketOrderSnapshot[] sellOrders = ParseOrders(commodities, "sales", "Commodity", false)
            .Concat(ParseOrders(microresources, "sales", "Microresource", false))
            .OrderBy(order => order.Category)
            .ThenBy(order => order.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        FrontierMarketOrderSnapshot[] buyOrders = ParseOrders(commodities, "purchases", "Commodity", true)
            .Concat(ParseOrders(microresources, "purchases", "Microresource", true))
            .OrderBy(order => order.Category)
            .ThenBy(order => order.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        string[] services = GetObject(root, "servicesCrew") is { } serviceObject
            ? serviceObject
                .EnumerateObject()
                .Where(property => property.Value.ValueKind == JsonValueKind.Object)
                .Select(property => HumanizeIdentifier(property.Name))
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                .ToArray()
            : [];

        var carrier = new FrontierCarrierSnapshot(
            GetString(name, "callsign"),
            FirstNonEmpty(
                DecodeHex(GetString(name, "filteredVanityName")),
                DecodeHex(GetString(name, "vanityName")),
                GetString(name, "callsign")
            ),
            ReadNamedValue(GetProperty(root, "currentStarSystem")),
            HumanizeIdentifier(GetString(root, "state")),
            HumanizeIdentifier(GetString(root, "dockingAccess")),
            GetInt64(finance, "bankBalance") ?? GetInt64(root, "balance") ?? 0,
            GetInt64(finance, "bankReservedBalance") ?? 0,
            GetInt64(finance, "maintenance") ?? 0,
            GetInt64(marketFinances, "cargoTotalValue") ?? 0,
            GetInt64(marketFinances, "allTimeProfit") ?? 0,
            GetInt64(marketFinances, "balanceAllocForPurchaseOrders") ?? 0,
            GetInt32(root, "fuel") ?? 0,
            capacityUsed,
            capacityFree,
            capacityRows,
            cargo,
            locker,
            sellOrders,
            buyOrders,
            services,
            HumanizeIdentifier(GetString(root, "theme")),
            GetBoolean(root, "notoriousAccess") ?? false,
            GetInt64(finance, "taxation") ?? 0,
            GetInt64(finance, "debtThreshold") ?? 0,
            GetInt64(finance, "maintenanceToDate") ?? 0,
            GetInt64(finance, "coreCost") ?? 0,
            GetInt64(finance, "servicesCost") ?? 0,
            GetInt64(finance, "servicesCostToDate") ?? 0,
            GetInt64(finance, "jumpsCost") ?? 0,
            GetInt32(finance, "numJumps") ?? 0,
            GetDouble(itinerary, "totalDistanceJumpedLY") ?? 0,
            ParseCarrierCurrentJump(GetProperty(itinerary, "currentJump")),
            ParseCarrierFinances(blackMarketFinances),
            ParseCarrierFinances(bartenderFinances),
            ParseNamedValues(GetProperty(finance, "service_taxation")),
            ParseCarrierCrew(GetObject(root, "servicesCrew")),
            ParseCarrierItinerary(GetProperty(itinerary, "completed")),
            reputation,
            GetObject(root, "market") is { } carrierMarket
                ? ParseMarket(carrierMarket, fetchedAt, dataPathPrefix + ".market")
                : null,
            ParseShipyard(root, fetchedAt, dataPathPrefix + ".shipyard"),
            dataPoints
        );
        return new FrontierCarrierEndpointSnapshot(carrier, reputation, dataPoints);
    }

    private static FrontierCarrierFinanceSnapshot? ParseCarrierFinances(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.Object } finances)
        {
            return null;
        }

        return new FrontierCarrierFinanceSnapshot(
            GetInt64(finances, "cargoTotalValue") ?? GetInt64(finances, "microresourcesTotalValue") ?? 0,
            GetInt64(finances, "allTimeProfit") ?? 0,
            GetInt32(finances, "numCommodsForSale") ?? GetInt32(finances, "microresourcesForSale") ?? 0,
            GetInt32(finances, "numCommodsPurchaseOrders") ?? GetInt32(finances, "microresourcesPurchaseOrders") ?? 0,
            GetInt64(finances, "balanceAllocForPurchaseOrders") ?? 0
        );
    }

    private static string ParseCarrierCurrentJump(JsonElement? value)
    {
        string currentJump = value is { ValueKind: JsonValueKind.Object } jump
            ? FirstNonEmpty(GetString(jump, JsonStarSystem), GetString(jump, JsonSystemName), ReadNamedValue(jump))
            : ReadNamedValue(value);
        return string.Equals(currentJump, "None", StringComparison.OrdinalIgnoreCase) ? string.Empty : currentJump;
    }

    private static FrontierCarrierCrewSnapshot[] ParseCarrierCrew(JsonElement? services)
    {
        if (services is not { ValueKind: JsonValueKind.Object } serviceObject)
        {
            return [];
        }

        var result = new List<FrontierCarrierCrewSnapshot>();
        foreach (JsonProperty property in serviceObject.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            JsonElement? crew = GetObject(property.Value, "crewMember");
            result.Add(
                new FrontierCarrierCrewSnapshot(
                    HumanizeIdentifier(property.Name),
                    GetString(crew, "name"),
                    HumanizeIdentifier(GetString(crew, "gender")),
                    IsAvailable(GetString(crew, "enabled")),
                    HumanizeIdentifier(GetString(crew, "faction")),
                    GetInt64(crew, "salary") ?? 0,
                    HumanizeIdentifier(GetString(property.Value, "status")),
                    GetDateTimeOffsetAny(crew, "lastEdit")
                )
            );
        }

        return result.OrderBy(item => item.Service, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static FrontierCarrierJumpSnapshot[] ParseCarrierItinerary(JsonElement? completed)
    {
        return completed is null
            ? []
            : EnumerateObjects(completed.Value)
                .Select(item => new FrontierCarrierJumpSnapshot(
                    ReadNamedValue(GetProperty(item, JsonStarSystem)),
                    HumanizeIdentifier(GetString(item, "state")),
                    GetDateTimeOffsetAny(item, "arrivalTime"),
                    GetDateTimeOffsetAny(item, "departureTime"),
                    GetInt64(item, "visitDurationSeconds") ?? 0
                ))
                .OrderByDescending(item => item.ArrivedAt)
                .ToArray();
    }

    private static FrontierReputationSnapshot[] ParseReputation(JsonElement? reputation)
    {
        if (reputation is null)
        {
            return [];
        }

        List<FrontierReputationSnapshot> values =
            reputation.Value.ValueKind == JsonValueKind.Object
                ? ParseReputationObject(reputation.Value)
                : ParseReputationArray(reputation.Value);

        return NormalizeReputation(values);
    }

    private static List<FrontierReputationSnapshot> ParseReputationArray(JsonElement reputation)
    {
        return EnumerateObjects(reputation)
            .Select(item => new FrontierReputationSnapshot(
                HumanizeIdentifier(GetString(item, "majorFaction")),
                GetDouble(item, "score") ?? 0
            ))
            .ToList();
    }

    private static FrontierReputationSnapshot[] NormalizeReputation(IEnumerable<FrontierReputationSnapshot> values)
    {
        return values
            .Where(item => !string.IsNullOrWhiteSpace(item.Faction))
            .GroupBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.Faction, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static List<FrontierReputationSnapshot> ParseReputationObject(JsonElement reputation)
    {
        if (TryParseSingleReputation(reputation, out FrontierReputationSnapshot? single))
        {
            return [single];
        }

        var values = new List<FrontierReputationSnapshot>();
        foreach (JsonProperty property in reputation.EnumerateObject())
        {
            if (TryReadReputationScore(property.Value, out double score))
            {
                values.Add(new FrontierReputationSnapshot(HumanizeIdentifier(property.Name), score));
            }
        }

        return values;
    }

    private static bool TryParseSingleReputation(JsonElement reputation, out FrontierReputationSnapshot snapshot)
    {
        snapshot = null!;
        string faction = GetString(reputation, "majorFaction");
        double? score = GetDouble(reputation, "score");
        if (string.IsNullOrWhiteSpace(faction) || score is null)
        {
            return false;
        }

        snapshot = new FrontierReputationSnapshot(HumanizeIdentifier(faction), score.Value);
        return true;
    }

    private static bool TryReadReputationScore(JsonElement value, out double score)
    {
        double? parsed = value.ValueKind == JsonValueKind.Object ? GetDouble(value, "score") : ReadDouble(value);
        if (parsed is null)
        {
            score = 0;
            return false;
        }

        score = parsed.Value;
        return true;
    }

    private static FrontierReputationSnapshot[] MergeReputation(
        IReadOnlyList<FrontierReputationSnapshot> first,
        IReadOnlyList<FrontierReputationSnapshot> second
    )
    {
        return first
            .Concat(second)
            .GroupBy(item => item.Faction, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.Faction, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static FrontierMarketSnapshot ParseMarket(JsonElement root, DateTimeOffset fetchedAt, string source)
    {
        FrontierCommoditySnapshot[] commodities = EnumerateObjects(root, "commodities")
            .Select(item => new FrontierCommoditySnapshot(
                GetInt64(item, "id"),
                HumanizeIdentifier(FirstNonEmpty(GetString(item, "categoryName"), GetString(item, "categoryname"))),
                FirstNonEmpty(
                    GetString(item, JsonLocName),
                    HumanizeIdentifier(GetString(item, "name")),
                    "Unknown commodity"
                ),
                HumanizeIdentifier(GetString(item, "legality")),
                GetInt64(item, "buyPrice") ?? 0,
                GetInt64(item, "sellPrice") ?? 0,
                GetInt64(item, "meanPrice") ?? 0,
                GetInt32(item, "demandBracket") ?? 0,
                GetInt32(item, "stockBracket") ?? 0,
                GetInt64(item, JsonStock) ?? 0,
                GetInt64(item, "demand") ?? 0,
                EnumerateScalarValues(GetProperty(item, "statusFlags"))
            ))
            .OrderBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return new FrontierMarketSnapshot(
            GetInt64(root, "id"),
            GetString(root, "name"),
            HumanizeIdentifier(GetString(root, "outpostType")),
            ParseStringList(GetProperty(root, "imported")),
            ParseStringList(GetProperty(root, "exported")),
            ParseStringList(GetProperty(root, "prohibited")),
            ParseNamedValues(GetProperty(root, "services")),
            ParseEconomies(GetProperty(root, "economies")),
            commodities,
            fetchedAt,
            Flatten(root, source)
        );
    }

    private static FrontierShipyardSnapshot ParseShipyard(JsonElement root, DateTimeOffset fetchedAt, string source)
    {
        FrontierOutfittingModuleSnapshot[] modules = EnumerateObjects(root, "modules")
            .Select(item => new FrontierOutfittingModuleSnapshot(
                GetInt64(item, "id"),
                HumanizeIdentifier(GetString(item, "category")),
                FirstNonEmpty(
                    GetString(item, JsonLocName),
                    HumanizeIdentifier(GetString(item, "name")),
                    "Unknown module"
                ),
                GetInt64(item, "cost") ?? 0,
                GetString(item, "sku"),
                GetInt32(item, JsonStock) ?? 0
            ))
            .OrderBy(item => item.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        JsonElement? shipsContainer = GetObject(root, "ships");
        JsonElement? shipList =
            GetProperty(shipsContainer, "shipyard_list")
            ?? GetProperty(root, "shipyard_list")
            ?? GetProperty(root, "ships");
        FrontierShipForSaleSnapshot[] ships = shipList is { } shipValue
            ? EnumerateObjects(shipValue)
                .Select(item => new FrontierShipForSaleSnapshot(
                    GetInt64(item, "id"),
                    HumanizeShipType(FirstNonEmpty(GetString(item, JsonLocName), GetString(item, "name"))),
                    GetInt64(item, "basevalue") ?? 0,
                    GetString(item, "sku"),
                    GetInt32(item, JsonStock) ?? 0
                ))
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray()
            : [];

        return new FrontierShipyardSnapshot(
            GetInt64(root, "id"),
            GetString(root, "name"),
            HumanizeIdentifier(GetString(root, "outpostType")),
            ParseStringList(GetProperty(root, "imported")),
            ParseStringList(GetProperty(root, "exported")),
            ParseStringList(GetProperty(root, "prohibited")),
            ParseNamedValues(GetProperty(root, "services")),
            ParseEconomies(GetProperty(root, "economies")),
            modules,
            ships,
            fetchedAt,
            Flatten(root, source)
        );
    }

    private static List<JsonElement> FindCommunityGoalObjects(JsonElement root)
    {
        var result = new List<JsonElement>();
        Visit(root);
        return result;

        void Visit(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    Visit(item);
                }

                return;
            }

            if (value.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (LooksLikeCommunityGoal(value))
            {
                result.Add(value.Clone());
                return;
            }

            foreach (JsonProperty property in value.EnumerateObject())
            {
                Visit(property.Value);
            }
        }
    }

    private static bool LooksLikeCommunityGoal(JsonElement value)
    {
        string title = FirstNonEmpty(
            GetString(value, "title"),
            GetString(value, "name"),
            GetString(value, "communitygoalName")
        );
        return !string.IsNullOrWhiteSpace(title)
            && (
                GetProperty(value, "expiry") is not null
                || GetProperty(value, "expiresAt") is not null
                || GetProperty(value, "description") is not null
                || GetProperty(value, "currentTotal") is not null
                || GetProperty(value, JsonPlayerContribution) is not null
                || GetProperty(value, JsonSystemName) is not null
            );
    }

    private static FrontierCommunityGoalSnapshot ParseCommunityGoal(JsonElement goal)
    {
        JsonElement? commander =
            GetObject(goal, "commander") ?? GetObject(goal, JsonContribution) ?? GetObject(goal, "player");
        JsonElement? progress = GetObject(goal, "progress");
        string title = GetStringAny(goal, "title", "name", "communitygoalName");
        string briefing = CleanCommunityGoalText(
            FirstNonEmpty(
                GetStringAny(goal, "bulletin"),
                GetStringAny(goal, "description", "descriptionText", "goalDescriptionText", "locDescription"),
                GetStringAny(goal, "news")
            ),
            title
        );
        return new FrontierCommunityGoalSnapshot(
            GetInt64Any(goal, "id", "cgid", "communitygoalGameID"),
            title,
            briefing,
            GetStringAny(goal, "objective", "objectiveText", "goalObjectiveText"),
            GetStringAny(goal, "reward", "rewardText", "goalRewardText"),
            GetStringAny(goal, JsonSystemName, "starsystemName", "starsystem_name", "system"),
            GetStringAny(goal, "marketName", "market_name", "stationName", "market"),
            GetDateTimeOffsetAny(goal, "expiry", "expiresAt", "goalExpiry"),
            GetBooleanAny(goal, "isComplete", "completed") ?? false,
            GetInt64Any(goal, "currentTotal", "contributionsTotal", JsonTotal, "qty")
                ?? GetInt64Any(progress, "current", JsonTotal)
                ?? 0,
            GetInt64Any(goal, "targetTotal", "target", "goalTarget", "target_qty")
                ?? GetInt64Any(progress, "target", "maximum"),
            GetInt64Any(goal, JsonPlayerContribution, JsonContribution)
                ?? GetInt64Any(commander, JsonPlayerContribution, JsonContribution)
                ?? 0,
            GetInt32Any(goal, "numContributors", "contributorsNum", "contributors") ?? 0,
            GetStringAny(goal, "tierReached", "tier"),
            GetInt32Any(goal, "playerPercentileBand", "percentileBand")
                ?? GetInt32Any(commander, "playerPercentileBand", "percentileBand"),
            GetInt64Any(goal, "bonus", "percentileBandReward")
                ?? GetInt64Any(commander, "bonus", "percentileBandReward")
                ?? 0,
            GetInt32Any(goal, "topRankSize"),
            GetBooleanAny(goal, "playerInTopRank", "isTopRank")
                ?? GetBooleanAny(commander, "playerInTopRank", "isTopRank")
                ?? false,
            Flatten(goal, "goal"),
            GetStringAny(goal, "activityType", "activity_type", "type"),
            HasAnyProperty(goal, JsonPlayerContribution, JsonContribution)
                || HasAnyProperty(commander, JsonPlayerContribution, JsonContribution),
            HasAnyProperty(goal, "numContributors", "contributorsNum", "contributors")
        );
    }

    private static string CleanCommunityGoalText(string value, string title)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("{{top5}}", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        if (!string.IsNullOrWhiteSpace(title) && normalized.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[title.Length..].TrimStart('\n', ' ');
        }

        while (normalized.Contains("\n\n\n", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        return normalized.Trim();
    }

    private static FrontierInventorySnapshot[] ParseLocker(JsonElement? locker)
    {
        if (locker is not { ValueKind: JsonValueKind.Object } lockerObject)
        {
            return [];
        }

        var result = new List<FrontierInventorySnapshot>();
        foreach (JsonProperty category in lockerObject.EnumerateObject())
        {
            foreach (JsonElement item in EnumerateObjects(category.Value))
            {
                result.Add(
                    new FrontierInventorySnapshot(
                        HumanizeIdentifier(category.Name),
                        FirstNonEmpty(
                            GetString(item, JsonLocName),
                            HumanizeIdentifier(GetString(item, "name")),
                            "Unknown item"
                        ),
                        GetInt32(item, "quantity") ?? 0,
                        GetInt64(item, JsonValue) ?? 0
                    )
                );
            }
        }

        return result
            .OrderBy(item => item.Category)
            .ThenByDescending(item => item.Quantity)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<FrontierMarketOrderSnapshot> ParseOrders(
        JsonElement? owner,
        string propertyName,
        string category,
        bool isPurchase
    )
    {
        if (owner is not { } orderOwner)
        {
            yield break;
        }

        foreach (JsonElement item in EnumerateObjects(orderOwner, propertyName))
        {
            int quantity = isPurchase ? GetInt32(item, JsonTotal) ?? 0 : GetInt32(item, JsonStock) ?? 0;
            yield return new FrontierMarketOrderSnapshot(
                category,
                FirstNonEmpty(
                    GetString(item, JsonLocName),
                    HumanizeIdentifier(GetString(item, "name")),
                    "Unknown item"
                ),
                quantity,
                isPurchase ? GetInt32(item, "outstanding") : null,
                GetInt64(item, "price") ?? 0,
                GetBoolean(item, "blackmarket") ?? false
            );
        }
    }

    private static FrontierNamedValueSnapshot[] ParseNamedValues(JsonElement? value)
    {
        if (value is null)
        {
            return [];
        }

        if (value.Value.ValueKind == JsonValueKind.Object)
        {
            return value
                .Value.EnumerateObject()
                .Select(property => new FrontierNamedValueSnapshot(
                    HumanizeIdentifier(property.Name),
                    HumanizeIdentifier(
                        FirstNonEmpty(
                            ReadNamedValue(property.Value),
                            property.Value.ValueKind is JsonValueKind.True ? "Available" : string.Empty,
                            property.Value.ValueKind is JsonValueKind.False ? "Unavailable" : string.Empty
                        )
                    )
                ))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        return EnumerateScalarValues(value)
            .Select(item => new FrontierNamedValueSnapshot(HumanizeIdentifier(item), "Available"))
            .ToArray();
    }

    private static string[] ParseStringList(JsonElement? value)
    {
        if (value is null)
        {
            return [];
        }

        IEnumerable<string> values = value.Value.ValueKind switch
        {
            JsonValueKind.Object => value
                .Value.EnumerateObject()
                .Select(property => FirstNonEmpty(ReadNamedValue(property.Value), HumanizeIdentifier(property.Name))),
            JsonValueKind.Array => value
                .Value.EnumerateArray()
                .Select(item =>
                    FirstNonEmpty(
                        ReadNamedValue(item),
                        item.ValueKind == JsonValueKind.Object ? GetStringAny(item, JsonLocName, "name") : string.Empty
                    )
                ),
            _ => [ReadNamedValue(value)],
        };
        return values
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(HumanizeIdentifier)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static FrontierEconomySnapshot[] ParseEconomies(JsonElement? value)
    {
        if (value is null)
        {
            return [];
        }

        if (value.Value.ValueKind == JsonValueKind.Object)
        {
            return value
                .Value.EnumerateObject()
                .Where(item => item.Value.ValueKind == JsonValueKind.Object)
                .Select(item => new FrontierEconomySnapshot(
                    long.TryParse(item.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id)
                        ? id
                        : null,
                    HumanizeIdentifier(GetString(item.Value, "name")),
                    GetDouble(item.Value, "proportion") ?? 0
                ))
                .OrderByDescending(item => item.Proportion)
                .ToArray();
        }

        return EnumerateObjects(value.Value)
            .Select(item => new FrontierEconomySnapshot(
                GetInt64(item, "id"),
                HumanizeIdentifier(GetString(item, "name")),
                GetDouble(item, "proportion") ?? 0
            ))
            .OrderByDescending(item => item.Proportion)
            .ToArray();
    }

    private static string[] EnumerateScalarValues(JsonElement? value)
    {
        if (value is null)
        {
            return [];
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.Array => value
                .Value.EnumerateArray()
                .Select(ReadScalarValue)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray(),
            JsonValueKind.Object => value
                .Value.EnumerateObject()
                .Select(item => FirstNonEmpty(ReadScalarValue(item.Value), HumanizeIdentifier(item.Name)))
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray(),
            _ => [ReadScalarValue(value.Value)],
        };
    }

    private static string ReadScalarValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            JsonValueKind.Null => "None",
            _ => string.Empty,
        };
    }

    private static List<FrontierDataPointSnapshot> Flatten(JsonElement root, string source)
    {
        var result = new List<FrontierDataPointSnapshot>();
        Visit(root, source);
        return result;

        void Visit(JsonElement value, string path)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in value.EnumerateObject())
                    {
                        Visit(property.Value, path + "." + property.Name);
                    }
                    break;
                case JsonValueKind.Array:
                    int index = 0;
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        Visit(item, $"{path}[{index++}]");
                    }
                    if (index == 0)
                    {
                        result.Add(new FrontierDataPointSnapshot(path, "Empty"));
                    }
                    break;
                default:
                    result.Add(new FrontierDataPointSnapshot(path, ReadScalarValue(value)));
                    break;
            }
        }
    }

    private static string GetStringAny(JsonElement? owner, params string[] names)
    {
        if (owner is null)
        {
            return string.Empty;
        }

        return FirstNonEmpty(names.Select(name => GetString(owner.Value, name)).ToArray());
    }

    private static long? GetInt64Any(JsonElement? owner, params string[] names)
    {
        if (owner is null)
        {
            return null;
        }

        foreach (string name in names)
        {
            if (GetInt64(owner.Value, name) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private static int? GetInt32Any(JsonElement? owner, params string[] names)
    {
        long? value = GetInt64Any(owner, names);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }

    private static bool? GetBooleanAny(JsonElement? owner, params string[] names)
    {
        if (owner is null)
        {
            return null;
        }

        foreach (string name in names)
        {
            if (GetBoolean(owner.Value, name) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private static bool HasAnyProperty(JsonElement? owner, params string[] names)
    {
        return owner is { } value && names.Any(name => GetProperty(value, name) is not null);
    }

    private static DateTimeOffset? GetDateTimeOffsetAny(JsonElement? owner, params string[] names)
    {
        string value = GetStringAny(owner, names);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed
        )
            ? parsed.ToUniversalTime()
            : null;
    }

    private static bool IsAvailable(string value)
    {
        return value.Equals("ok", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("available", StringComparison.OrdinalIgnoreCase)
            || value.Equals("enabled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameShip(FrontierShipSnapshot left, FrontierShipSnapshot right)
    {
        return left.Id is not null && right.Id is not null
            ? left.Id == right.Id
            : string.Equals(left.Type, right.Type, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Identifier, right.Identifier, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement RequireObject(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{description} response was not a JSON object.");
        }

        return value;
    }

    private static JsonElement[] EnumerateObjects(JsonElement owner, string propertyName)
    {
        JsonElement? value = GetProperty(owner, propertyName);
        return value is null ? [] : EnumerateObjects(value.Value);
    }

    private static JsonElement[] EnumerateObjects(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Array => value
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .ToArray(),
            JsonValueKind.Object => value
                .EnumerateObject()
                .Select(property => property.Value)
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .ToArray(),
            _ => [],
        };
    }

    private static JsonElement? GetProperty(JsonElement owner, string name)
    {
        if (owner.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (owner.TryGetProperty(name, out JsonElement exact))
        {
            return exact;
        }

        foreach (JsonProperty property in owner.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static JsonElement? GetProperty(JsonElement? owner, string name)
    {
        return owner is { } value ? GetProperty(value, name) : null;
    }

    private static JsonElement? GetObject(JsonElement owner, string name)
    {
        JsonElement? value = GetProperty(owner, name);
        return value is { ValueKind: JsonValueKind.Object } ? value : null;
    }

    private static JsonElement? GetObject(JsonElement? owner, string name)
    {
        return owner is { } value ? GetObject(value, name) : null;
    }

    private static string GetString(JsonElement owner, string name)
    {
        JsonElement? value = GetProperty(owner, name);
        return value is null ? string.Empty : ReadNamedValue(value);
    }

    private static string GetString(JsonElement? owner, string name)
    {
        return owner is { } value ? GetString(value, name) : string.Empty;
    }

    private static string ReadNamedValue(JsonElement? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.Object => FirstNonEmpty(
                GetString(value.Value, "name"),
                GetString(value.Value, "Name"),
                GetString(value.Value, JsonSystemName)
            ),
            _ => string.Empty,
        };
    }

    private static long? GetInt64(JsonElement owner, string name)
    {
        return GetInt64(GetProperty(owner, name));
    }

    private static long? GetInt64(JsonElement? owner, string name)
    {
        return owner is { } value ? GetInt64(value, name) : null;
    }

    private static long? GetInt64(JsonElement? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value.Value.ValueKind == JsonValueKind.Number && value.Value.TryGetInt64(out long number))
        {
            return number;
        }

        return
            value.Value.ValueKind == JsonValueKind.String
            && long.TryParse(value.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static int? GetInt32(JsonElement owner, string name)
    {
        long? value = GetInt64(owner, name);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }

    private static int? GetInt32(JsonElement? owner, string name)
    {
        return owner is { } value ? GetInt32(value, name) : null;
    }

    private static double? GetDouble(JsonElement? owner, string name)
    {
        JsonElement? value = owner is { } element ? GetProperty(element, name) : null;
        return value is { } elementValue ? ReadDouble(elementValue) : null;
    }

    private static double? ReadDouble(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
        {
            return number;
        }

        return
            value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static bool? GetBoolean(JsonElement owner, string name)
    {
        JsonElement? value = GetProperty(owner, name);
        return value?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static bool? GetBoolean(JsonElement? owner, string name)
    {
        return owner is { } value ? GetBoolean(value, name) : null;
    }

    private static double? NormalizeHealth(double? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            <= 1 => value * 100,
            > 100 => value / 10_000,
            _ => value,
        };
    }

    private static string DecodeHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length % 2 != 0 || !value.All(Uri.IsHexDigit))
        {
            return value;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromHexString(value)).TrimEnd('\0').Trim();
        }
        catch (FormatException)
        {
            return value;
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string HumanizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim().Trim('$', ';').Replace('_', ' ').Replace('-', ' ');
        normalized = WordBoundaryRegex().Replace(normalized, "$1 $2");
        normalized = WhitespaceRegex().Replace(normalized, " ").Trim();
        return CultureInfo
            .CurrentCulture.TextInfo.ToTitleCase(normalized.ToLowerInvariant())
            .Replace(" Iii", " III", StringComparison.Ordinal)
            .Replace(" Ii", " II", StringComparison.Ordinal)
            .Replace(" Iv", " IV", StringComparison.Ordinal)
            .Replace(" Vi", " VI", StringComparison.Ordinal);
    }

    private static string HumanizeShipType(string value)
    {
        string normalized = value.Trim().Trim('$', ';');
        return ShipNames.TryGetValue(normalized, out string? known) ? known : HumanizeIdentifier(normalized);
    }

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex WordBoundaryRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
