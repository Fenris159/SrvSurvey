namespace SrvSurvey.Core.Routes;

/// <summary>Anonymous planner controls verified against Spansh's first-party forms and submit code.</summary>
public static class SpanshPlannerCatalog
{
    private const string SourceSystem = "Source system";
    private const string DestinationSystem = "Destination system";
    private const string CalculateStartingFuel = "calculate_starting_fuel";

    /// <summary>
    /// Defines a single-system field and whether a value is required.
    /// </summary>
    private static SpanshPlannerField System(string key, string label, bool required = true) =>
        new(key, label, SpanshPlannerFieldType.System, Required: required);

    /// <summary>
    /// Defines a numeric field with its website default and accepted bounds.
    /// </summary>
    private static SpanshPlannerField Number(string key, string label, string value, double min, double? max = null) =>
        new(key, label, SpanshPlannerFieldType.Number, value, true, min, max);

    /// <summary>
    /// Defines a checkbox using the website's numeric boolean encoding.
    /// </summary>
    private static SpanshPlannerField Flag(string key, string label, bool value) =>
        new(key, label, SpanshPlannerFieldType.Boolean, value ? "1" : "0");

    /// <summary>
    /// Defines an ordered list of system names submitted as repeated parameters.
    /// </summary>
    private static SpanshPlannerField Systems(string key, string label, bool required = false) =>
        new(
            key,
            label,
            SpanshPlannerFieldType.Systems,
            Required: required,
            Help: "Choose a system and select + Add. Edit, remove, or reorder destinations below."
        );

    private static SpanshPlannerField Range => Number("range", "Range (LY)", "", 10, 100);

    public static IReadOnlyList<SpanshPlannerDefinition> ShipPlanners { get; } =
    [
        new(
            "Neutron Plotter",
            "route",
            "plotter",
            SpanshRouteKind.Neutron,
            [
                System("from", SourceSystem),
                Systems("via", "Via systems"),
                System("to", DestinationSystem),
                Range,
                Number("efficiency", "Efficiency (%)", "60", 0, 100),
                new(
                    "supercharge_multiplier",
                    "Supercharge",
                    SpanshPlannerFieldType.Choice,
                    "4",
                    Choices: [new("Normal Supercharge", "4"), new("Overcharge Supercharge", "6")]
                ),
            ]
        ),
        new(
            "Road to Riches",
            "riches/route",
            "riches",
            SpanshRouteKind.Riches,
            [
                System("from", SourceSystem),
                System("to", DestinationSystem, false),
                Range,
                Number("radius", "Radius (LY)", "25", 1, 1000),
                Number("max_results", "Maximum systems", "100", 1, 2000),
                Number("max_distance", "Maximum distance to arrival (LS)", "50000", 0),
                Number("min_value", "Minimum scan value (CR)", "100000", 0),
                Flag("use_mapping_value", "Use mapping value", false),
                Flag("avoid_thargoids", "Avoid Thargoids", true),
                Flag("loop", "Loop", true),
            ]
        ),
        new(
            "Tourist Planner",
            "tourist/route",
            "tourist",
            SpanshRouteKind.Tourist,
            [
                System("source", SourceSystem),
                System("final_destination", "Final destination", false),
                Systems("destination", "Destinations", true),
                Range,
                Flag("loop", "Loop", true),
            ]
        ),
        new(
            "Galaxy Plotter",
            "generic/route",
            "exact-plotter",
            SpanshRouteKind.Galaxy,
            [
                System("source", SourceSystem),
                System("destination", DestinationSystem),
                new("ship_build", "Ship Build (EDSY SLEF)", SpanshPlannerFieldType.ShipBuild, Required: true),
                Number("cargo", "Cargo (t)", "0", 0, 1000),
                Number("reserve_size", "Reserve fuel (t)", "0", 0, 32),
                Flag("is_supercharged", "Already Supercharged", false),
                Flag("use_supercharge", "Use Supercharge", true),
                Flag("use_injections", "Use FSD Injections Always", false),
                Flag("use_injections_when_required", "Use FSD Injections When Required", false),
                Flag("exclude_secondary", "Exclude Secondary Stars", false),
                Flag("refuel_every_scoopable", "Refuel Every Scoopable", true),
                new(
                    "algorithm",
                    "Routing Algorithm",
                    SpanshPlannerFieldType.Choice,
                    "optimistic",
                    Choices:
                    [
                        new("Fuel", "fuel"),
                        new("Fuel Jumps", "fuel_jumps"),
                        new("Guided", "guided"),
                        new("Optimistic", "optimistic"),
                        new("Pessimistic", "pessimistic"),
                    ]
                ),
            ]
        ),
        new(
            "Expressway to Exomastery",
            "exobiology/route",
            "exobiology",
            SpanshRouteKind.Exobiology,
            [
                System("from", SourceSystem),
                System("to", DestinationSystem, false),
                Range,
                Number("radius", "Radius (LY)", "25", 1, 1000),
                Number("max_results", "Maximum systems", "100", 1, 2000),
                Number("max_distance", "Maximum distance to arrival (LS)", "50000", 0),
                Number("min_value", "Minimum biological value (CR)", "10000000", 0),
                Flag("avoid_thargoids", "Avoid Thargoids", true),
                Flag("loop", "Loop", true),
            ]
        ),
        new(
            "Colonisation Plotter",
            "colonisation/route",
            "colonisation",
            SpanshRouteKind.Colonisation,
            [System("source_system", SourceSystem, false), System("destination_system", DestinationSystem)]
        ),
    ];

    public static IReadOnlyList<SpanshPlannerDefinition> CarrierPlanners { get; } =
    [
        new(
            "Fleet Carrier Router",
            "fleetcarrier/route",
            "fleet-carrier",
            SpanshRouteKind.FleetCarrier,
            [
                new(
                    "carrier_type",
                    "Carrier type",
                    SpanshPlannerFieldType.Choice,
                    "Fleet Carrier",
                    Choices: [new("Fleet Carrier", "Fleet Carrier"), new("Squadron Carrier", "Squadron Carrier")],
                    Send: false
                ),
                System("source", SourceSystem),
                Systems("destinations", "Destinations", true),
                Number("capacity_used", "Used capacity (t)", "0", 0, 60000),
                Flag(CalculateStartingFuel, "Calculate starting fuel", true),
                new(
                    "refuel_destinations",
                    "Refuel at destinations",
                    SpanshPlannerFieldType.Systems,
                    Help: "List route destinations where the carrier can refill. One per line.",
                    VisibleWhen: CalculateStartingFuel,
                    VisibleValue: "1"
                ),
                Number("fuel_loaded", "Tritium in fuel tank (t)", "1000", 0, 1000) with
                {
                    VisibleWhen = CalculateStartingFuel,
                    VisibleValue = "0",
                },
                Number("tritium_stored", "Tritium in market (t)", "0", 0, 60000) with
                {
                    VisibleWhen = CalculateStartingFuel,
                    VisibleValue = "0",
                },
            ]
        ),
    ];
}
