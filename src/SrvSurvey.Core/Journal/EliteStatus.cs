using System.Text.Json;
using System.Text.Json.Serialization;

namespace SrvSurvey.Core.Journal;

public sealed record EliteStatus
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("event")]
    public string EventName { get; init; } = string.Empty;

    public StatusFlags Flags { get; init; }

    public StatusFlags2 Flags2 { get; init; }

    public IReadOnlyList<int> Pips { get; init; } = [];

    public int FireGroup { get; init; }

    public GuiFocus GuiFocus { get; init; }

    public FuelStatus? Fuel { get; init; }

    public double Cargo { get; init; }

    public string? LegalState { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    public int Heading { get; init; }

    public double Altitude { get; init; }

    public double Temperature { get; init; }

    public string? BodyName { get; init; }

    public long Balance { get; init; }

    public StatusDestination? Destination { get; init; }

    public decimal PlanetRadius { get; init; }

    public string? SelectedWeapon { get; init; }

    [JsonPropertyName("SelectedWeapon_Localised")]
    public string? SelectedWeaponLocalised { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }

    [JsonIgnore]
    public int NormalizedHeading => ((Heading % 360) + 360) % 360;

    [JsonIgnore]
    public bool OnFoot => Flags2.HasFlag(StatusFlags2.OnFoot);

    [JsonIgnore]
    public bool OnFootOnPlanet => Flags2.HasFlag(StatusFlags2.OnFootOnPlanet);

    [JsonIgnore]
    public bool OnFootInside => OnFoot
        && Flags2.HasFlag(StatusFlags2.BreathableAtmosphere);

    [JsonIgnore]
    public bool OnFootExterior => Flags2.HasFlag(StatusFlags2.OnFootExterior);

    [JsonIgnore]
    public bool OnFootSocial => OnFoot
        && Flags2.HasFlag(StatusFlags2.OnFootSocialSpace);

    [JsonIgnore]
    public bool OnFootInStation => OnFoot
        && (Flags2 & (StatusFlags2.OnFootInHangar
            | StatusFlags2.OnFootInStation
            | StatusFlags2.OnFootSocialSpace)) != 0;

    [JsonIgnore]
    public bool InSrv => Flags.HasFlag(StatusFlags.InSrv);

    [JsonIgnore]
    public bool InFighter => Flags.HasFlag(StatusFlags.InFighter);

    [JsonIgnore]
    public bool InMainShip => Flags.HasFlag(StatusFlags.InMainShip);

    [JsonIgnore]
    public bool HasLatitudeLongitude => Flags.HasFlag(StatusFlags.HasLatLong);

    [JsonIgnore]
    public bool UsingSrvTurret => Flags.HasFlag(StatusFlags.SrvUsingTurretView);

    [JsonIgnore]
    public bool InTaxi => Flags2.HasFlag(StatusFlags2.InTaxi);

    [JsonIgnore]
    public bool Docked => Flags.HasFlag(StatusFlags.Docked);

    [JsonIgnore]
    public bool Landed => Flags.HasFlag(StatusFlags.Landed);

    [JsonIgnore]
    public bool ShieldsUp => Flags.HasFlag(StatusFlags.ShieldsUp);

    [JsonIgnore]
    public bool FsdCharging => Flags.HasFlag(StatusFlags.FsdCharging);

    [JsonIgnore]
    public bool FsdChargingJump => Flags2.HasFlag(StatusFlags2.FsdChargingJump);

    [JsonIgnore]
    public bool GlideMode => Flags2.HasFlag(StatusFlags2.GlideMode);

    [JsonIgnore]
    public bool HudInAnalysisMode => Flags.HasFlag(StatusFlags.HudInAnalysisMode);

    [JsonIgnore]
    public bool LandingGearDown => Flags.HasFlag(StatusFlags.LandingGearDown);

    [JsonIgnore]
    public bool CargoScoopDeployed => Flags.HasFlag(StatusFlags.CargoScoopDeployed);

    [JsonIgnore]
    public bool LightsOn => Flags.HasFlag(StatusFlags.LightsOn);

    [JsonIgnore]
    public bool SupercruiseOverdrive => Flags2.HasFlag(StatusFlags2.SupercruiseOverdrive);
}

public sealed record FuelStatus
{
    public double FuelMain { get; init; }

    public double FuelReservoir { get; init; }
}

public sealed record StatusDestination
{
    public long System { get; init; }

    public int Body { get; init; }

    public string? Name { get; init; }

    [JsonPropertyName("Name_Localised")]
    public string? NameLocalised { get; init; }
}

[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "S2344:Enumeration type names should not have Flags suffixes",
    Justification = "The name mirrors Elite's Status.json Flags field and is part of the public model.")]
public enum StatusFlags : uint
{
    None = 0,
    Docked = 0x00000001,
    Landed = 0x00000002,
    LandingGearDown = 0x00000004,
    ShieldsUp = 0x00000008,
    Supercruise = 0x00000010,
    FlightAssistOff = 0x00000020,
    HardpointsDeployed = 0x00000040,
    InWing = 0x00000080,
    LightsOn = 0x00000100,
    CargoScoopDeployed = 0x00000200,
    SilentRunning = 0x00000400,
    ScoopingFuel = 0x00000800,
    SrvHandbrake = 0x00001000,
    SrvUsingTurretView = 0x00002000,
    SrvTurretRetracted = 0x00004000,
    SrvDriveAssist = 0x00008000,
    FsdMassLocked = 0x00010000,
    FsdCharging = 0x00020000,
    FsdCooldown = 0x00040000,
    LowFuel = 0x00080000,
    OverHeating = 0x00100000,
    HasLatLong = 0x00200000,
    IsInDanger = 0x00400000,
    BeingInterdicted = 0x00800000,
    InMainShip = 0x01000000,
    InFighter = 0x02000000,
    InSrv = 0x04000000,
    HudInAnalysisMode = 0x08000000,
    NightVision = 0x10000000,
    AltitudeFromAverageRadius = 0x20000000,
    FsdJump = 0x40000000,
    SrvHighBeam = 0x80000000,
}

[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "S2342:Enumeration types should comply with a naming convention",
    Justification = "The name mirrors Elite's Status.json Flags2 field and is part of the public model.")]
public enum StatusFlags2 : uint
{
    None = 0,
    OnFoot = 0x00000001,
    InTaxi = 0x00000002,
    InMulticrew = 0x00000004,
    OnFootInStation = 0x00000008,
    OnFootOnPlanet = 0x00000010,
    AimDownSight = 0x00000020,
    LowOxygen = 0x00000040,
    LowHealth = 0x00000080,
    Cold = 0x00000100,
    Hot = 0x00000200,
    VeryCold = 0x00000400,
    VeryHot = 0x00000800,
    GlideMode = 0x00001000,
    OnFootInHangar = 0x00002000,
    OnFootSocialSpace = 0x00004000,
    OnFootExterior = 0x00008000,
    BreathableAtmosphere = 0x00010000,
    TelepresenceMulticrew = 0x00020000,
    PhysicalMulticrew = 0x00040000,
    FsdChargingJump = 0x00080000,
    SupercruiseOverdrive = 0x00100000,
    SupercruiseAssist = 0x00200000,
    Unknown = 0x00400000,
}

public enum GuiFocus
{
    NoFocus = 0,
    InternalPanel,
    ExternalPanel,
    CommsPanel,
    RolePanel,
    StationServices,
    GalaxyMap,
    SystemMap,
    Orrery,
    Fss,
    Saa,
    Codex,
}
