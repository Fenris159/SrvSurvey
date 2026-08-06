using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace SrvSurvey.Core.Network
{
    internal sealed record EddnLocationContext(
        string systemName,
        long systemAddress,
        double[] starPosition);

    internal sealed record EddnMessageContext(
        EddnLocationContext? location,
        bool? horizons,
        bool? odyssey,
        string? statusBodyName = null,
        string? trackedBodyName = null,
        int? trackedBodyId = null,
        string? trackedBodyType = null);

    internal sealed record EddnPreparedMessage(
        string eventName,
        string schemaRef,
        JObject message);

    /// <summary>
    /// Builds schema-specific EDDN messages from journal events and the
    /// companion JSON files written by Elite Dangerous.
    /// </summary>
    internal static class EddnMessageSanitizer
    {
        private const string schemaRoot = "https://eddn.edcd.io/schemas/";
        private const string horizonsSku = "ELITE_HORIZONS_V_PLANETARY_LANDINGS";
        private const string EventKey = "event";
        private const string TimestampKey = "timestamp";
        private const string DockedEvent = "Docked";
        private const string FsdJumpEvent = "FSDJump";
        private const string CarrierJumpEvent = "CarrierJump";
        private const string ScanEvent = "Scan";
        private const string LocationEvent = "Location";
        private const string SaasSignalsFoundEvent = "SAASignalsFound";
        private const string MarketEvent = "Market";
        private const string OutfittingEvent = "Outfitting";
        private const string ShipyardEvent = "Shipyard";
        private const string FcmaterialsEvent = "FCMaterials";
        private const string NavrouteEvent = "NavRoute";
        private const string CodexEntryEvent = "CodexEntry";
        private const string ApproachSettlementEvent = "ApproachSettlement";
        private const string DockingDeniedEvent = "DockingDenied";
        private const string DockingGrantedEvent = "DockingGranted";
        private const string FssAllBodiesFoundEvent = "FSSAllBodiesFound";
        private const string FssBodySignalsEvent = "FSSBodySignals";
        private const string FssDiscoveryScanEvent = "FSSDiscoveryScan";
        private const string NavBeaconScanEvent = "NavBeaconScan";
        private const string ScanBaryCentreEvent = "ScanBaryCentre";
        private const string SystemProperty = "System";
        private const string StarSystemProperty = "StarSystem";
        private const string SystemNameProperty = "SystemName";
        private const string SystemAddressProperty = "SystemAddress";
        private const string StarPosProperty = "StarPos";
        private const string NameProperty = "Name";
        private const string RegionProperty = "Region";
        private const string CategoryProperty = "Category";
        private const string SubCategoryProperty = "SubCategory";
        private const string NearestDestinationProperty = "NearestDestination";
        private const string VoucherAmountProperty = "VoucherAmount";
        private const string TraitsProperty = "Traits";
        private const string BodyIdProperty = "BodyID";
        private const string BodyNameProperty = "BodyName";
        private const string StationNameProperty = "StationName";
        private const string StationTypeProperty = "StationType";
        private const string ReasonProperty = "Reason";
        private const string CountProperty = "Count";
        private const string LatitudeProperty = "Latitude";
        private const string LongitudeProperty = "Longitude";
        private const string MarketIdProperty = "MarketID";
        private const string BodyProperty = "Body";
        private const string BodyTypeProperty = "BodyType";
        private const string IdProperty = "id";
        private const string NameSourceProperty = "Name";
        private const string MeanPriceSourceProperty = "MeanPrice";
        private const string BuyPriceSourceProperty = "BuyPrice";
        private const string StockSourceProperty = "Stock";
        private const string StockBracketSourceProperty = "StockBracket";
        private const string SellPriceSourceProperty = "SellPrice";
        private const string DemandSourceProperty = "Demand";
        private const string DemandBracketSourceProperty = "DemandBracket";
        private const string MarketIdResultKey = "marketId";
        private const string StationNameResultKey = "stationName";
        private const string SystemNameResultKey = "systemName";
        private const string SignalNameKey = "SignalName";
        private const string SignalTypeKey = "SignalType";
        private const string IsStationKey = "IsStation";
        private const string UsstypeKey = "USSType";
        private const string SpawningStateKey = "SpawningState";
        private const string SpawningFactionKey = "SpawningFaction";
        private const string SpawningPowerKey = "SpawningPower";
        private const string OpposingPowerKey = "OpposingPower";
        private const string ThreatLevelKey = "ThreatLevel";
        private const string ItemsKey = "Items";
        private const string EntryIdProperty = "EntryID";
        private const string WantedProperty = "Wanted";
        private const string ActiveFineProperty = "ActiveFine";
        private const string CockpitBreachProperty = "CockpitBreach";
        private const string BoostUsedProperty = "BoostUsed";
        private const string FuelLevelProperty = "FuelLevel";
        private const string FuelUsedProperty = "FuelUsed";
        private const string JumpDistProperty = "JumpDist";
        private const string RouteProperty = "Route";
        private const string CommoditiesProperty = "commodities";
        private const string CarrierNameProperty = "CarrierName";
        private const string CarrierIdProperty = "CarrierID";
        private const string SignalsProperty = "Signals";
        private const string BodyCountProperty = "BodyCount";
        private const string NonBodyCountProperty = "NonBodyCount";
        private const string NumBodiesProperty = "NumBodies";
        private const string ModulesProperty = "modules";
        private const string ShipsProperty = "ships";
        private const string StationTypeResultKey = "stationType";
        private const string StockProperty = "stock";
        private const string DemandProperty = "demand";
        private const string CarrierDockingAccessSourceProperty = "CarrierDockingAccess";
        private const string PriceProperty = "Price";
        private const string CarrierDockingAccessProperty = "carrierDockingAccess";
        private const string StarClassProperty = "StarClass";
        private const string LandingPadProperty = "LandingPad";
        private const string TheEventDidNotMatchTrackedSystemMessage =
            "the event did not match the tracked system";
        private static readonly TimeSpan regexTimeout = TimeSpan.FromSeconds(1);
        private static readonly string[] RequiredCommodityFields =
        [
            "name", "meanPrice", "buyPrice", "stock", "stockBracket",
            "sellPrice", "demand", "demandBracket",
        ];
        private static readonly string[] CommodityStatusFlags =
            ["Producer", "Consumer", "Rare"];

        private static readonly string[] NavRouteFields =
            [StarSystemProperty, SystemAddressProperty, StarPosProperty, StarClassProperty];
        private static readonly string[] CodexRequiredProperties =
            [SystemProperty, NameProperty, RegionProperty, CategoryProperty, SubCategoryProperty];

        private static readonly HashSet<string> genericEvents = new(StringComparer.Ordinal)
        {
            DockedEvent,
            FsdJumpEvent,
            CarrierJumpEvent,
            ScanEvent,
            LocationEvent,
            SaasSignalsFoundEvent,
        };

        private static readonly HashSet<string> companionEvents = new(StringComparer.Ordinal)
        {
            MarketEvent,
            OutfittingEvent,
            ShipyardEvent,
            FcmaterialsEvent,
            NavrouteEvent,
        };

        private static readonly Regex canonicalCommodityName = new(
            @"^\$(.+)_name;$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            regexTimeout);

        private static readonly Regex moduleName = new(
            @"^Hpt_|^Int_|Armour_",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            regexTimeout);

        internal static bool isCompanionEvent(string? eventName)
        {
            return eventName != null && companionEvents.Contains(eventName);
        }

        internal static EddnLocationContext? getLocation(JObject raw)
        {
            ArgumentNullException.ThrowIfNull(raw);

            var eventName = raw.Value<string>(EventKey);
            if (eventName is not (LocationEvent or FsdJumpEvent or CarrierJumpEvent))
                return null;

            var systemName = raw.Value<string>(StarSystemProperty);
            var systemAddress = raw.Value<long?>(SystemAddressProperty);
            var position = raw[StarPosProperty] as JArray;
            if (string.IsNullOrWhiteSpace(systemName)
                || systemAddress is not > 0
                || position?.Count != 3
                || position.Any(value => value.Type is not (JTokenType.Float or JTokenType.Integer)))
            {
                return null;
            }

            return new EddnLocationContext(
                systemName,
                systemAddress.Value,
                position.Values<double>().ToArray());
        }

        internal static bool tryBuildJournal(
            JObject raw,
            EddnMessageContext context,
            out EddnPreparedMessage? prepared,
            out string reason)
        {
            ArgumentNullException.ThrowIfNull(raw);
            ArgumentNullException.ThrowIfNull(context);

            prepared = null;
            var eventName = raw.Value<string>(EventKey);
            if (string.IsNullOrWhiteSpace(eventName))
            {
                reason = "the journal event name was missing";
                return false;
            }

            if (genericEvents.Contains(eventName))
                return tryBuildGenericJournal(raw, context, out prepared, out reason);

            JObject? message;
            string schema;
            switch (eventName)
            {
                case CodexEntryEvent:
                    schema = "codexentry/1";
                    if (!hasMatchingLocation(raw, context, SystemProperty))
                        return fail(
                            TheEventDidNotMatchTrackedSystemMessage,
                            out prepared,
                            out reason);
                    message = select(raw,
                        TimestampKey, EventKey, SystemProperty, SystemAddressProperty, EntryIdProperty, NameProperty,
                        RegionProperty, CategoryProperty, LatitudeProperty, LongitudeProperty, SubCategoryProperty,
                        NearestDestinationProperty, VoucherAmountProperty, TraitsProperty, BodyIdProperty, BodyNameProperty);
                    message[StarPosProperty] = position(context.location!);
                    addFlags(message, context);

                    var bodyNamesAgree = !string.IsNullOrWhiteSpace(context.statusBodyName)
                        && string.Equals(
                            context.statusBodyName,
                            context.trackedBodyName,
                            StringComparison.Ordinal);
                    if (!message.ContainsKey(BodyNameProperty)
                        && bodyNamesAgree
                        && (!message.ContainsKey(BodyIdProperty)
                            || message.Value<int?>(BodyIdProperty) == context.trackedBodyId))
                    {
                        message[BodyNameProperty] = context.statusBodyName;
                    }
                    if (!message.ContainsKey(BodyIdProperty)
                        && context.trackedBodyId.HasValue
                        && bodyNamesAgree
                        && (!message.ContainsKey(BodyNameProperty)
                            || string.Equals(
                                message.Value<string>(BodyNameProperty),
                                context.statusBodyName,
                                StringComparison.Ordinal)))
                    {
                        message[BodyIdProperty] = context.trackedBodyId.Value;
                    }
                    break;

                case ApproachSettlementEvent:
                    schema = "approachsettlement/1";
                    if (!hasMatchingLocation(raw, context))
                        return fail(
                            TheEventDidNotMatchTrackedSystemMessage,
                            out prepared,
                            out reason);
                    message = select(raw,
                        TimestampKey, EventKey, SystemAddressProperty, NameProperty, MarketIdProperty, BodyIdProperty,
                        BodyNameProperty, LatitudeProperty, LongitudeProperty, "StationGovernment",
                        "StationAllegiance", "StationEconomies", "StationFaction",
                        "StationServices", "StationEconomy");
                    message[StarSystemProperty] = context.location!.systemName;
                    message[StarPosProperty] = position(context.location);
                    addFlags(message, context);
                    break;

                case DockingDeniedEvent:
                    schema = "dockingdenied/1";
                    message = select(raw,
                        TimestampKey, EventKey, MarketIdProperty, StationNameProperty, StationTypeProperty, ReasonProperty);
                    addFlags(message, context);
                    break;

                case DockingGrantedEvent:
                    schema = "dockinggranted/1";
                    message = select(raw,
                        TimestampKey, EventKey, MarketIdProperty, StationNameProperty, StationTypeProperty, LandingPadProperty);
                    addFlags(message, context);
                    break;

                case FssAllBodiesFoundEvent:
                    schema = "fssallbodiesfound/1";
                    if (!hasMatchingLocation(raw, context, SystemNameProperty))
                        return fail(
                            TheEventDidNotMatchTrackedSystemMessage,
                            out prepared,
                            out reason);
                    message = select(raw,
                        TimestampKey, EventKey, SystemNameProperty, SystemAddressProperty, CountProperty);
                    message[StarPosProperty] = position(context.location!);
                    addFlags(message, context);
                    break;

                case FssBodySignalsEvent:
                    schema = "fssbodysignals/1";
                    if (!hasMatchingLocation(raw, context))
                        return fail(
                            TheEventDidNotMatchTrackedSystemMessage,
                            out prepared,
                            out reason);
                    message = select(raw,
                        TimestampKey, EventKey, SystemAddressProperty, BodyIdProperty, BodyNameProperty, SignalsProperty);
                    message[StarSystemProperty] = context.location!.systemName;
                    message[StarPosProperty] = position(context.location);
                    addFlags(message, context);
                    break;

                case FssDiscoveryScanEvent:
                    schema = "fssdiscoveryscan/1";
                    if (!hasMatchingLocation(raw, context, SystemNameProperty))
                        return fail(
                            TheEventDidNotMatchTrackedSystemMessage,
                            out prepared,
                            out reason);
                    message = select(raw,
                        TimestampKey, EventKey, SystemNameProperty, SystemAddressProperty, BodyCountProperty, NonBodyCountProperty);
                    message[StarPosProperty] = position(context.location!);
                    addFlags(message, context);
                    break;

                case NavBeaconScanEvent:
                    schema = "navbeaconscan/1";
                    if (!hasMatchingLocation(raw, context))
                        return fail(
                            TheEventDidNotMatchTrackedSystemMessage,
                            out prepared,
                            out reason);
                    message = select(raw,
                        TimestampKey, EventKey, SystemAddressProperty, NumBodiesProperty);
                    message[StarSystemProperty] = context.location!.systemName;
                    message[StarPosProperty] = position(context.location);
                    addFlags(message, context);
                    break;

                case ScanBaryCentreEvent:
                    schema = "scanbarycentre/1";
                    if (!hasMatchingLocation(raw, context, StarSystemProperty))
                        return fail(
                            TheEventDidNotMatchTrackedSystemMessage,
                            out prepared,
                            out reason);
                    message = select(raw,
                        TimestampKey, EventKey, StarSystemProperty, SystemAddressProperty, BodyIdProperty,
                        "SemiMajorAxis", "Eccentricity", "OrbitalInclination", "Periapsis",
                        "OrbitalPeriod", "AscendingNode", "MeanAnomaly");
                    message[StarPosProperty] = position(context.location!);
                    addFlags(message, context);
                    break;

                default:
                    reason = "the event has no EDDN schema supported by SrvSurvey";
                    return false;
            }

            removeLocalised(message);
            removeNulls(message);
            if (eventName == CodexEntryEvent
                && !hasValidCodexStrings(message, out reason))
            {
                return false;
            }
            if (!hasRequiredFields(message, eventName, out reason))
                return false;

            prepared = new EddnPreparedMessage(eventName, schemaRoot + schema, message);
            reason = string.Empty;
            return true;
        }

        internal static bool tryBuildCompanion(
            JObject companion,
            EddnMessageContext context,
            out EddnPreparedMessage? prepared,
            out string reason)
        {
            ArgumentNullException.ThrowIfNull(companion);
            ArgumentNullException.ThrowIfNull(context);

            prepared = null;
            var eventName = companion.Value<string>(EventKey);
            JObject message;
            string schema;
            switch (eventName)
            {
                case MarketEvent:
                    schema = "commodity/3";
                    message = buildCommodity(companion, context);
                    break;

                case OutfittingEvent:
                    schema = "outfitting/2";
                    message = buildOutfitting(companion, context);
                    break;

                case ShipyardEvent:
                    schema = "shipyard/2";
                    message = buildShipyard(companion, context);
                    break;

                case FcmaterialsEvent:
                    schema = "fcmaterials_journal/1";
                    message = buildFleetCarrierMaterials(companion, context);
                    break;

                case NavrouteEvent:
                    schema = "navroute/1";
                    message = buildNavRoute(companion, context);
                    break;

                default:
                    reason = "the companion file event is not supported by EDDN";
                    return false;
            }

            removeNulls(message);
            if (!hasRequiredFields(message, eventName, out reason))
                return false;

            prepared = new EddnPreparedMessage(eventName, schemaRoot + schema, message);
            reason = string.Empty;
            return true;
        }

        internal static bool tryBuildSignalBatch(
            IReadOnlyList<JObject> pendingSignals,
            EddnLocationContext? location,
            bool? horizons,
            bool? odyssey,
            out EddnPreparedMessage? prepared,
            out string reason)
        {
            ArgumentNullException.ThrowIfNull(pendingSignals);
            prepared = null;
            if (pendingSignals.Count == 0)
            {
                reason = "the signal batch was empty";
                return false;
            }
            if (location == null)
            {
                reason = "the system location for the signal batch was unknown";
                return false;
            }

            var signals = new JArray();
            foreach (var raw in pendingSignals)
            {
                if (raw.Value<long?>(SystemAddressProperty) != location.systemAddress
                    || raw.Value<string>(UsstypeKey) == "$USS_Type_MissionTarget;")
                {
                    continue;
                }

                var signal = select(raw,
                    TimestampKey, SignalNameKey, SignalTypeKey, IsStationKey, UsstypeKey,
                    SpawningStateKey, SpawningFactionKey, SpawningPowerKey, OpposingPowerKey,
                    ThreatLevelKey);
                removeLocalised(signal);
                if (hasValue(signal, TimestampKey) && hasValue(signal, SignalNameKey))
                    signals.Add(signal);
            }

            if (signals.Count == 0)
            {
                reason = "no public signals remained after filtering";
                return false;
            }

            var message = new JObject
            {
                [EventKey] = "FSSSignalDiscovered",
                [TimestampKey] = signals[0][TimestampKey]!.DeepClone(),
                [SystemAddressProperty] = location.systemAddress,
                [StarSystemProperty] = location.systemName,
                [StarPosProperty] = position(location),
                ["signals"] = signals,
            };
            addFlags(message, new EddnMessageContext(location, horizons, odyssey));
            removeNulls(message);

            prepared = new EddnPreparedMessage(
                "FSSSignalDiscovered",
                schemaRoot + "fsssignaldiscovered/1",
                message);
            reason = string.Empty;
            return true;
        }

        private static bool tryBuildGenericJournal(
            JObject raw,
            EddnMessageContext context,
            out EddnPreparedMessage? prepared,
            out string reason)
        {
            prepared = null;
            var eventName = raw.Value<string>(EventKey)!;
            if (!hasMatchingLocation(raw, context,
                    eventName is FsdJumpEvent or CarrierJumpEvent or LocationEvent or DockedEvent or ScanEvent
                        ? StarSystemProperty
                        : null))
            {
                reason = TheEventDidNotMatchTrackedSystemMessage;
                return false;
            }

            var message = new JObject(raw);
            removeLocalised(message);
            switch (eventName)
            {
                case DockedEvent:
                    remove(message, WantedProperty, ActiveFineProperty, CockpitBreachProperty);
                    if (!message.ContainsKey(BodyProperty)
                        && context.trackedBodyType == "Planet"
                        && !string.IsNullOrWhiteSpace(context.trackedBodyName))
                    {
                        message[BodyProperty] = context.trackedBodyName;
                        message[BodyTypeProperty] = "Planet";
                    }
                    break;

                case FsdJumpEvent:
                case CarrierJumpEvent:
                    remove(message,
                        WantedProperty,
                        BoostUsedProperty,
                        FuelLevelProperty,
                        FuelUsedProperty,
                        JumpDistProperty);
                    removeFactionPersonalData(message);
                    break;

                case LocationEvent:
                    remove(message, WantedProperty, LatitudeProperty, LongitudeProperty);
                    removeFactionPersonalData(message);
                    break;
            }

            message[StarSystemProperty] ??= context.location!.systemName;
            message[StarPosProperty] ??= position(context.location!);
            addFlags(message, context);
            removeNulls(message);
            if (!hasRequiredFields(message, eventName, out reason))
                return false;

            prepared = new EddnPreparedMessage(
                eventName,
                schemaRoot + "journal/1",
                message);
            reason = string.Empty;
            return true;
        }

        private static JObject buildCommodity(JObject source, EddnMessageContext context)
        {
            var commodities = new List<JObject>();
            foreach (var item in source[ItemsKey] as JArray ?? [])
            {
                if (item is not JObject commodity
                    || commodity.Value<string>(CategoryProperty)?.Contains(
                        "NonMarketable",
                        StringComparison.OrdinalIgnoreCase) == true
                    || !string.IsNullOrWhiteSpace(commodity.Value<string>("Legality")))
                {
                    continue;
                }

                var name = canonicalCommodity(commodity.Value<string>(NameSourceProperty));
                if (string.IsNullOrWhiteSpace(name)) continue;
                var output = new JObject
                {
                    ["name"] = name,
                    ["meanPrice"] = commodity.Value<int?>(MeanPriceSourceProperty),
                    ["buyPrice"] = commodity.Value<int?>(BuyPriceSourceProperty),
                    [StockProperty] = commodity.Value<int?>(StockSourceProperty),
                    ["stockBracket"] = commodity[StockBracketSourceProperty]?.DeepClone(),
                    ["sellPrice"] = commodity.Value<int?>(SellPriceSourceProperty),
                    [DemandProperty] = commodity.Value<int?>(DemandSourceProperty),
                    ["demandBracket"] = commodity[DemandBracketSourceProperty]?.DeepClone(),
                };
                if (RequiredCommodityFields.Any(field =>
                    !hasValue(output, field)))
                {
                    continue;
                }
                var statusFlags = new JArray();
                foreach (var flag in CommodityStatusFlags
                    .Where(flag => commodity.Value<bool?>(flag) == true))
                {
                    statusFlags.Add(flag);
                }
                if (statusFlags.Count > 0) output["statusFlags"] = statusFlags;
                commodities.Add(output);
            }

            var sorted = new JArray(commodities.OrderBy(
                item => item.Value<string>("name"),
                StringComparer.Ordinal));
            var message = new JObject
            {
                [SystemNameResultKey] = source.Value<string>(StarSystemProperty),
                [StationNameResultKey] = source.Value<string>(StationNameProperty),
                [StationTypeResultKey] = source.Value<string>(StationTypeProperty),
                [MarketIdResultKey] = source.Value<long?>(MarketIdProperty),
                [TimestampKey] = source[TimestampKey]?.DeepClone(),
                [CommoditiesProperty] = sorted,
            };
            var access = source.Value<string>(CarrierDockingAccessSourceProperty);
            if (!string.IsNullOrWhiteSpace(access))
                message[CarrierDockingAccessProperty] = access;
            addFlags(message, context);
            removeNulls(message);
            return message;
        }

        private static JObject buildOutfitting(JObject source, EddnMessageContext context)
        {
            var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in source[ItemsKey] as JArray ?? [])
            {
                if (item is not JObject module) continue;
                var name = module.Value<string>(NameProperty);
                var sku = module.Value<string>("sku") ?? module.Value<string>("SKU");
                if (string.IsNullOrWhiteSpace(name)
                    || !moduleName.IsMatch(name)
                    || name.Equals("Int_PlanetApproachSuite", StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(sku)
                        && !sku.Equals(horizonsSku, StringComparison.Ordinal)))
                {
                    continue;
                }
                modules.Add(normalizeModuleName(name));
            }

            var message = new JObject
            {
                [SystemNameResultKey] = source.Value<string>(StarSystemProperty),
                [StationNameResultKey] = source.Value<string>(StationNameProperty),
                [MarketIdResultKey] = source.Value<long?>(MarketIdProperty),
                [TimestampKey] = source[TimestampKey]?.DeepClone(),
                [ModulesProperty] = new JArray(modules.OrderBy(value => value, StringComparer.Ordinal)),
            };
            addFlags(message, context with
            {
                horizons = source.Value<bool?>("Horizons") ?? context.horizons,
            });
            removeNulls(message);
            return message;
        }

        private static JObject buildShipyard(JObject source, EddnMessageContext context)
        {
            var ships = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in source["PriceList"] as JArray ?? [])
            {
                if (item is not JObject ship) continue;
                var type = ship.Value<string>("ShipType");
                if (!string.IsNullOrWhiteSpace(type)) ships.Add(type);
            }

            var message = new JObject
            {
                [SystemNameResultKey] = source.Value<string>(StarSystemProperty),
                [StationNameResultKey] = source.Value<string>(StationNameProperty),
                [MarketIdResultKey] = source.Value<long?>(MarketIdProperty),
                [TimestampKey] = source[TimestampKey]?.DeepClone(),
                [ShipsProperty] = new JArray(ships.OrderBy(value => value, StringComparer.Ordinal)),
            };
            if (source.Value<bool?>("AllowCobraMkIV") is bool allowCobra)
                message["allowCobraMkIV"] = allowCobra;
            addFlags(message, context with
            {
                horizons = source.Value<bool?>("Horizons") ?? context.horizons,
            });
            removeNulls(message);
            return message;
        }

        private static JObject buildFleetCarrierMaterials(
            JObject source,
            EddnMessageContext context)
        {
            var items = new JArray();
            foreach (var item in source[ItemsKey] as JArray ?? [])
            {
                if (item is not JObject material) continue;
                var output = select(
                    material,
                    IdProperty,
                    NameProperty,
                    PriceProperty,
                    StockSourceProperty,
                    DemandSourceProperty);
                if (hasValue(output, IdProperty)
                    && hasValue(output, NameProperty)
                    && hasValue(output, PriceProperty)
                    && hasValue(output, StockSourceProperty)
                    && hasValue(output, DemandSourceProperty))
                {
                    items.Add(output);
                }
            }

            var message = select(
                source,
                TimestampKey, EventKey, MarketIdProperty, CarrierNameProperty, CarrierIdProperty);
            message[ItemsKey] = items;
            addFlags(message, context);
            return message;
        }

        private static JObject buildNavRoute(JObject source, EddnMessageContext context)
        {
            var route = new JArray();
            foreach (var item in source[RouteProperty] as JArray ?? [])
            {
                if (item is not JObject waypoint) continue;
                var output = select(
                    waypoint,
                    StarSystemProperty, SystemAddressProperty, StarPosProperty, StarClassProperty);
                if (NavRouteFields.All(field => hasValue(output, field)))
                {
                    route.Add(output);
                }
            }

            var message = select(source, TimestampKey, EventKey);
            message[RouteProperty] = route;
            addFlags(message, context);
            return message;
        }

        private static bool hasMatchingLocation(
            JObject raw,
            EddnMessageContext context,
            string? systemNameField = null)
        {
            var location = context.location;
            if (location == null
                || raw.Value<long?>(SystemAddressProperty) != location.systemAddress)
            {
                return false;
            }

            if (systemNameField == null) return true;
            var eventName = raw.Value<string>(systemNameField);
            return !string.IsNullOrWhiteSpace(eventName)
                && eventName.Equals(location.systemName, StringComparison.Ordinal);
        }

        private static bool hasRequiredFields(
            JObject message,
            string eventName,
            out string reason)
        {
            string[] required = eventName switch
            {
                CodexEntryEvent => [TimestampKey, EventKey, SystemProperty, StarPosProperty, SystemAddressProperty, EntryIdProperty],
                ApproachSettlementEvent => [TimestampKey, EventKey, StarSystemProperty, StarPosProperty, SystemAddressProperty, NameProperty, BodyIdProperty, BodyNameProperty, LatitudeProperty, LongitudeProperty],
                DockingDeniedEvent => [TimestampKey, EventKey, MarketIdProperty, StationNameProperty, ReasonProperty],
                DockingGrantedEvent => [TimestampKey, EventKey, MarketIdProperty, StationNameProperty],
                FssAllBodiesFoundEvent => [TimestampKey, EventKey, SystemNameProperty, StarPosProperty, SystemAddressProperty, CountProperty],
                FssBodySignalsEvent => [TimestampKey, EventKey, StarSystemProperty, StarPosProperty, SystemAddressProperty, BodyIdProperty, SignalsProperty],
                FssDiscoveryScanEvent => [TimestampKey, EventKey, SystemNameProperty, StarPosProperty, SystemAddressProperty, BodyCountProperty, NonBodyCountProperty],
                NavBeaconScanEvent => [TimestampKey, EventKey, StarSystemProperty, StarPosProperty, SystemAddressProperty, NumBodiesProperty],
                ScanBaryCentreEvent => [TimestampKey, EventKey, StarSystemProperty, StarPosProperty, SystemAddressProperty, BodyIdProperty],
                MarketEvent => [SystemNameResultKey, StationNameResultKey, MarketIdResultKey, TimestampKey, CommoditiesProperty],
                OutfittingEvent => [SystemNameResultKey, StationNameResultKey, MarketIdResultKey, TimestampKey, ModulesProperty],
                ShipyardEvent => [SystemNameResultKey, StationNameResultKey, MarketIdResultKey, TimestampKey, ShipsProperty],
                FcmaterialsEvent => [TimestampKey, EventKey, MarketIdProperty, CarrierNameProperty, CarrierIdProperty, ItemsKey],
                NavrouteEvent => [TimestampKey, EventKey, RouteProperty],
                _ when genericEvents.Contains(eventName) => [TimestampKey, EventKey, StarSystemProperty, StarPosProperty, SystemAddressProperty],
                _ => [],
            };

            var missing = required.Where(name => !hasValue(message, name)).ToArray();
            if (missing.Length > 0)
            {
                reason = "required field(s) were missing: " + string.Join(", ", missing);
                return false;
            }

            if (eventName is OutfittingEvent or ShipyardEvent
                && message[required[^1]] is JArray array
                && array.Count == 0)
            {
                reason = $"{required[^1]} was empty";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool hasValidCodexStrings(JObject message, out string reason)
        {
            foreach (var field in CodexRequiredProperties)
            {
                if (message[field]?.Type == JTokenType.String
                    && string.IsNullOrWhiteSpace(message.Value<string>(field)))
                {
                    reason = $"{field} was empty";
                    return false;
                }
            }

            if (message["Traits"] is JArray traits
                && traits.Any(value => value.Type != JTokenType.String
                    || string.IsNullOrWhiteSpace(value.Value<string>())))
            {
                reason = "Traits contained an empty or non-string value";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool hasValue(JObject obj, string name)
        {
            var value = obj[name];
            return value != null
                && value.Type != JTokenType.Null
                && (value.Type != JTokenType.String
                    || !string.IsNullOrWhiteSpace(value.Value<string>()));
        }

        private static JObject select(JObject source, params string[] names)
        {
            var result = new JObject();
            foreach (var name in names)
                if (source[name] != null) result[name] = source[name]!.DeepClone();
            removeLocalised(result);
            return result;
        }

        private static void addFlags(JObject message, EddnMessageContext context)
        {
            if (context.horizons.HasValue) message["horizons"] = context.horizons.Value;
            if (context.odyssey.HasValue) message["odyssey"] = context.odyssey.Value;
        }

        private static JArray position(EddnLocationContext location)
        {
            return new JArray(location.starPosition);
        }

        private static string? canonicalCommodity(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var match = canonicalCommodityName.Match(value);
            return match.Success ? match.Groups[1].Value : value;
        }

        private static string normalizeModuleName(string value)
        {
            return moduleName.Replace(value, match =>
            {
                var lower = match.Value.ToLowerInvariant();
                return char.ToUpperInvariant(lower[0]) + lower[1..];
            });
        }

        private static void removeFactionPersonalData(JObject message)
        {
            if (message["Factions"] is not JArray factions) return;
            foreach (var faction in factions.OfType<JObject>())
                remove(faction,
                    "HappiestSystem", "HomeSystem", "MyReputation", "SquadronFaction");
        }

        private static void remove(JObject message, params string[] names)
        {
            foreach (var name in names) message.Remove(name);
        }

        private static void removeNulls(JToken token)
        {
            if (token is JObject message)
            {
                foreach (var property in message.Properties().ToArray())
                {
                    if (property.Value.Type == JTokenType.Null)
                        property.Remove();
                    else
                        removeNulls(property.Value);
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array.ToArray())
                {
                    if (item.Type == JTokenType.Null)
                        item.Remove();
                    else
                        removeNulls(item);
                }
            }
        }

        private static void removeLocalised(JToken? token)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties().ToArray())
                {
                    if (property.Name.EndsWith("_Localised", StringComparison.Ordinal))
                        property.Remove();
                    else
                        removeLocalised(property.Value);
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array) removeLocalised(item);
            }
        }

        private static bool fail(
            string failure,
            out EddnPreparedMessage? prepared,
            out string reason)
        {
            prepared = null;
            reason = failure;
            return false;
        }
    }
}



