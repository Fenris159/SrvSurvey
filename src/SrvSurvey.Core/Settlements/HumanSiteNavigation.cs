using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Settlements;

public sealed class HumanSiteNavigation(HumanSiteTemplateCatalog templates)
{
    private const double MaximumPadDistance = 14;
    private readonly HumanSiteTemplateCatalog templates = templates
        ?? throw new ArgumentNullException(nameof(templates));

    public HumanSiteGeometrySolution? InferGeometry(
        HumanSiteLiveSnapshot site,
        SurfaceCoordinate observedLocation,
        double observerHeading,
        double bodyRadius,
        string? vehicle = null,
        int targetPad = 0)
    {
        ArgumentNullException.ThrowIfNull(site);
        ValidateRadius(bodyRadius);
        var location = AdjustForVehicle(
            observedLocation,
            observerHeading,
            bodyRadius,
            vehicle);
        var candidates = templates.ForEconomy(site.Economy)
            .Where(template => site.AvailablePads.Total == 0
                || HumanSiteLandingPads.From(template) == site.AvailablePads);
        foreach (var template in candidates)
        {
            if (targetPad > template.LandingPads.Count)
            {
                continue;
            }

            for (var index = 0; index < template.LandingPads.Count; index++)
            {
                var padNumber = index + 1;
                if (targetPad > 0 && targetPad != padNumber)
                {
                    continue;
                }

                var pad = template.LandingPads[index];
                var siteHeading = SurfaceNavigation.NormalizeDegrees(
                    observerHeading - pad.Rotation);
                var offset = GetSiteOffset(
                    new SurfaceCoordinate(
                        site.Location.Latitude,
                        site.Location.Longitude),
                    location,
                    bodyRadius,
                    siteHeading);
                var distance = GetDistance(offset, pad.Offset);
                if (distance < MaximumPadDistance)
                {
                    return new HumanSiteGeometrySolution(
                        template.SubType,
                        template,
                        siteHeading,
                        padNumber,
                        distance);
                }
            }
        }

        return null;
    }

    public static HumanSiteMapPoint GetSiteOffset(
        SurfaceCoordinate site,
        SurfaceCoordinate current,
        double bodyRadius,
        double siteHeading)
    {
        ValidateRadius(bodyRadius);
        var distance = SurfaceNavigation.GetDistance(site, current, bodyRadius);
        var bearing = SurfaceNavigation.GetBearing(site, current);
        var relativeBearing = DegreesToRadians(
            SurfaceNavigation.NormalizeDegrees(bearing - siteHeading));
        return new HumanSiteMapPoint(
            Math.Sin(relativeBearing) * distance,
            Math.Cos(relativeBearing) * distance);
    }

    public static SurfaceCoordinate GetSurfaceLocation(
        SurfaceCoordinate site,
        HumanSiteMapPoint offset,
        double bodyRadius,
        double siteHeading)
    {
        ValidateRadius(bodyRadius);
        if (!offset.IsFinite)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                "The site offset must be finite.");
        }

        var distance = Math.Sqrt(
            (offset.X * offset.X) + (offset.Y * offset.Y));
        if (distance == 0)
        {
            return site;
        }

        var localBearing = RadiansToDegrees(Math.Atan2(offset.X, offset.Y));
        return Move(
            site,
            distance,
            SurfaceNavigation.NormalizeDegrees(siteHeading + localBearing),
            bodyRadius);
    }

    public static SurfaceCoordinate AdjustForVehicle(
        SurfaceCoordinate observedLocation,
        double heading,
        double bodyRadius,
        string? vehicle)
    {
        ValidateRadius(bodyRadius);
        var offset = HumanSiteVehicleOffsets.Find(vehicle);
        if (offset == default)
        {
            return observedLocation;
        }

        var rotated = Rotate(offset, heading);
        return GetSurfaceLocation(
            observedLocation,
            rotated,
            bodyRadius,
            siteHeading: 0);
    }

    private static SurfaceCoordinate Move(
        SurfaceCoordinate origin,
        double distance,
        double bearing,
        double radius)
    {
        var angularDistance = distance / radius;
        var bearingRadians = DegreesToRadians(bearing);
        var latitude = DegreesToRadians(origin.Latitude);
        var longitude = DegreesToRadians(origin.Longitude);
        var destinationLatitude = Math.Asin(
            (Math.Sin(latitude) * Math.Cos(angularDistance))
            + (Math.Cos(latitude)
                * Math.Sin(angularDistance)
                * Math.Cos(bearingRadians)));
        var destinationLongitude = longitude + Math.Atan2(
            Math.Sin(bearingRadians)
                * Math.Sin(angularDistance)
                * Math.Cos(latitude),
            Math.Cos(angularDistance)
                - (Math.Sin(latitude) * Math.Sin(destinationLatitude)));
        var normalizedLongitude = ((RadiansToDegrees(destinationLongitude) + 540)
            % 360) - 180;
        return new SurfaceCoordinate(
            RadiansToDegrees(destinationLatitude),
            normalizedLongitude);
    }

    private static HumanSiteMapPoint Rotate(
        HumanSiteMapPoint point,
        double rotation)
    {
        var distance = Math.Sqrt(
            (point.X * point.X) + (point.Y * point.Y));
        if (distance == 0)
        {
            return point;
        }

        var angle = RadiansToDegrees(Math.Atan2(point.X, point.Y));
        var radians = DegreesToRadians(angle + rotation);
        return new HumanSiteMapPoint(
            Math.Sin(radians) * distance,
            Math.Cos(radians) * distance);
    }

    private static double GetDistance(
        HumanSiteMapPoint left,
        HumanSiteMapPoint right)
    {
        var x = left.X - right.X;
        var y = left.Y - right.Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    private static void ValidateRadius(double bodyRadius)
    {
        if (!double.IsFinite(bodyRadius) || bodyRadius <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bodyRadius),
                "The body radius must be positive.");
        }
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * 180 / Math.PI;
    }
}

public static class HumanSiteVehicleOffsets
{
    private static readonly IReadOnlyDictionary<string, HumanSiteMapPoint>
        Offsets = new Dictionary<string, HumanSiteMapPoint>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["sidewinder"] = new(0.003973524156032526, -1.8918079917214574),
            ["eagle"] = new(0.2022841743348611, -9.475366622689792),
            ["hauler"] = new(0.09697669984436012, -12.599239384408765),
            ["adder"] = new(-1.0448119356622934, -11.715904797681277),
            ["empire_eagle"] = new(0.24583362859945412, -8.536714551074842),
            ["viper"] = new(0.12291616974324898, -7.182614926481333),
            ["cobramkiii"] = new(-0.15764970873549044, -9.031276393889643),
            ["viper_mkiv"] = new(-0.000002767397930854968, -8.065723234733316),
            ["diamondback"] = new(-0.000004161491337663819, -9.890813997121282),
            ["type6"] = new(0, -20.957581116002204),
            ["dolphin"] = new(0.24276978, -19.054316),
            ["diamondbackxl"] = new(0.5462154, -18.501362),
            ["empire_courier"] = new(0, -14.442907807215595),
            ["independant_trader"] = new(0, -21.080499480318932),
            ["asp_scout"] = new(0.23624479066336016, -24.01767627376691),
            ["vulture"] = new(0.24582565904236156, -16.13144680685502),
            ["asp"] = new(0, -25.075346320612607),
            ["federation_dropship"] = new(0.0330741525538769, -34.46660635414665),
            ["type7"] = new(-0.3844698663548397, -36.25912276792104),
            ["typex"] = new(0, -26.1201524173048),
            ["federation_dropship_mkii"] = new(-0.03360819181317313, -34.48447154692093),
            ["empire_trader"] = new(-1.6299010929385205, -42.45929907453549),
            ["typex_2"] = new(-0.1340211463206372, -25.743781130382647),
            ["typex_3"] = new(-0.14999680198641616, -23.32654308111006),
            ["federation_gunship"] = new(-0.03360819181317313, -34.48447154692093),
            ["krait_light"] = new(0.6031567730891499, -29.808101534971505),
            ["krait_mkii"] = new(-0.439005506050203, -28.642501220707378),
            ["orca"] = new(0.8935034127811378, -60.66695165859758),
            ["ferdelance"] = new(-1.2886041335053922, -11.051961482268358),
            ["mamba"] = new(-0.3384479441319697, -17.0160874323599),
            ["python"] = new(0.02428152046769198, -27.8032388647518),
            ["python_nx"] = new(-0.19850712748954485, -27.652575857555383),
            ["type8"] = new(-0.29301020868417604, -19.568625297953936),
            ["type9"] = new(0, -41.97662141416277),
            ["belugaliner"] = new(-0.15900690862724954, -96.06768779190353),
            ["type9_military"] = new(0, -41.97662141416277),
            ["anaconda"] = new(-0.2973854218978083, 11.835423460533919),
            ["federation_corvette"] = new(0, 17.57732609729217),
            ["cutter"] = new(0, -78.97504907349804),
            ["mandalay"] = new(-0.07054133462671332, -19.3093099026056),
            ["cobramkv"] = new(0.06363939123438424, -13.024934562983267),
            ["corsair"] = new(-0.3444149294353526, -28.23179340609525),
            ["panthermkii"] = new(-1.3078012419992395, -54.711916783483),
            ["lakonminer"] = new(3.2158969190817974, -30.937907691306424),
            ["explorer_nx"] = new(-0.7820737162192071, -62.305031957744566),
            ["smallcombat01_nx"] = new(0.20853152946436159, -14.369195925926613),
            ["mediumtransport01"] = new(-0.6075405725357383, -42.08868797230343),
            ["taxi"] = new(-0.999665340505111, -11.913859432190865),
            ["foot"] = default,
        };

    public static HumanSiteMapPoint Find(string? vehicle)
    {
        return string.IsNullOrWhiteSpace(vehicle)
            ? default
            : Offsets.GetValueOrDefault(vehicle);
    }
}

public sealed record HumanSiteGeometrySolution(
    int SubType,
    HumanSiteTemplate Template,
    double Heading,
    int PadNumber,
    double DistanceFromPadCenter);
