using SrvSurvey.Core.Navigation;

namespace SrvSurvey.Core.Settlements;

public sealed class HumanSiteTemplateAuthoringSession
{
    private const byte StartPoint = 0;
    private const byte LinePoint = 1;
    private const byte BezierPoint = 3;
    private const byte CloseSubpath = 0x80;
    private const double CircleControlRatio = 0.5522847498307936;

    private HumanSiteTemplate template;
    private readonly List<HumanSiteBuildingPath> pendingBuildingPaths = [];
    private List<HumanSiteMapPoint>? polygonPoints;

    public HumanSiteTemplateAuthoringSession(HumanSiteTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        this.template = Clone(template);
    }

    public HumanSiteTemplate Template => template;

    public IReadOnlyList<HumanSiteBuildingPath> PendingBuildingPaths =>
        pendingBuildingPaths;

    public IReadOnlyList<HumanSiteMapPoint> PendingPolygonPoints =>
        polygonPoints ?? [];

    public bool IsCapturingPolygon => polygonPoints is not null;

    public bool HasPendingBuilding => pendingBuildingPaths.Count > 0;

    public HumanSiteTemplate CreatePreviewTemplate(
        string pendingBuildingName = "Draft building")
    {
        var previewPaths = pendingBuildingPaths.Select(Clone).ToList();
        if (polygonPoints is { Count: > 0 })
        {
            var pointTypes = Enumerable.Repeat(
                LinePoint,
                polygonPoints.Count).ToArray();
            pointTypes[0] = StartPoint;
            previewPaths.Add(new HumanSiteBuildingPath(
                polygonPoints.ToArray(),
                pointTypes,
                FillMode: 0));
        }

        if (previewPaths.Count == 0)
        {
            return template;
        }

        var name = string.IsNullOrWhiteSpace(pendingBuildingName)
            ? "Draft building"
            : pendingBuildingName.Trim();
        return template with
        {
            Buildings = template.Buildings
                .Append(new HumanSiteBuilding(name, previewPaths))
                .ToArray(),
        };
    }

    public void BeginPolygon(HumanSiteMapPoint firstPoint)
    {
        ValidatePoint(firstPoint);
        if (polygonPoints is not null)
        {
            throw new InvalidOperationException(
                "A settlement polygon is already being captured.");
        }

        polygonPoints = [firstPoint];
    }

    public void AddPolygonPoint(HumanSiteMapPoint point)
    {
        ValidatePoint(point);
        if (polygonPoints is null)
        {
            throw new InvalidOperationException(
                "Start a settlement polygon before adding points.");
        }

        if (polygonPoints[^1] != point)
        {
            polygonPoints.Add(point);
        }
    }

    public HumanSiteBuildingPath EndPolygon(
        HumanSiteMapPoint finalPoint,
        bool closePath = false)
    {
        AddPolygonPoint(finalPoint);
        if (polygonPoints!.Count < 2)
        {
            throw new InvalidOperationException(
                "A settlement polygon requires at least two distinct points.");
        }

        var points = polygonPoints.ToArray();
        var pointTypes = Enumerable.Repeat(LinePoint, points.Length).ToArray();
        pointTypes[0] = StartPoint;
        if (closePath)
        {
            pointTypes[^1] |= CloseSubpath;
        }

        var path = new HumanSiteBuildingPath(points, pointTypes, FillMode: 0);
        pendingBuildingPaths.Add(path);
        polygonPoints = null;
        return path;
    }

    public void CancelPolygon()
    {
        polygonPoints = null;
    }

    public HumanSiteBuildingPath AddCircle(
        HumanSiteMapPoint center,
        double radius)
    {
        ValidatePoint(center);
        if (!double.IsFinite(radius) || radius <= 0 || radius > 10_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                "The settlement circle radius must be between 0 and 10,000 metres.");
        }

        if (polygonPoints is not null)
        {
            throw new InvalidOperationException(
                "Finish or cancel the current polygon before adding a circle.");
        }

        var control = radius * CircleControlRatio;
        var points = new HumanSiteMapPoint[]
        {
            new(center.X + radius, center.Y),
            new(center.X + radius, center.Y + control),
            new(center.X + control, center.Y + radius),
            new(center.X, center.Y + radius),
            new(center.X - control, center.Y + radius),
            new(center.X - radius, center.Y + control),
            new(center.X - radius, center.Y),
            new(center.X - radius, center.Y - control),
            new(center.X - control, center.Y - radius),
            new(center.X, center.Y - radius),
            new(center.X + control, center.Y - radius),
            new(center.X + radius, center.Y - control),
            new(center.X + radius, center.Y),
        };
        var pointTypes = new byte[]
        {
            StartPoint,
            BezierPoint,
            BezierPoint,
            BezierPoint,
            BezierPoint,
            BezierPoint,
            BezierPoint,
            BezierPoint,
            BezierPoint,
            BezierPoint,
            BezierPoint,
            BezierPoint,
            BezierPoint | CloseSubpath,
        };
        var path = new HumanSiteBuildingPath(points, pointTypes, FillMode: 0);
        pendingBuildingPaths.Add(path);
        return path;
    }

    public bool RemoveLastPendingPath()
    {
        if (pendingBuildingPaths.Count == 0)
        {
            return false;
        }

        pendingBuildingPaths.RemoveAt(pendingBuildingPaths.Count - 1);
        return true;
    }

    public HumanSiteBuilding CommitBuilding(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A settlement building requires a name.",
                nameof(name));
        }

        if (polygonPoints is not null)
        {
            throw new InvalidOperationException(
                "Finish or cancel the current polygon before committing a building.");
        }

        if (pendingBuildingPaths.Count == 0)
        {
            throw new InvalidOperationException(
                "Add at least one building path before committing a building.");
        }

        var building = new HumanSiteBuilding(
            name.Trim(),
            pendingBuildingPaths.Select(Clone).ToArray());
        template = template with
        {
            Buildings = template.Buildings.Append(building).ToArray(),
        };
        pendingBuildingPaths.Clear();
        return building;
    }

    public void DiscardPendingBuilding()
    {
        polygonPoints = null;
        pendingBuildingPaths.Clear();
    }

    public HumanSiteNamedPointOfInterest AddNamedPoint(
        string name,
        HumanSiteMapPoint offset,
        int securityLevel,
        int floor)
    {
        ValidatePointMetadata(offset, securityLevel, floor);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "A named settlement point requires a name.",
                nameof(name));
        }

        var point = new HumanSiteNamedPointOfInterest(
            offset,
            Rotation: 0,
            securityLevel,
            floor,
            name.Trim());
        template = template with
        {
            NamedPoints = template.NamedPoints.Append(point).ToArray(),
        };
        return point;
    }

    public HumanSitePointOfInterest AddDataTerminal(
        HumanSiteMapPoint offset,
        int securityLevel,
        int floor)
    {
        ValidatePointMetadata(offset, securityLevel, floor);
        var point = new HumanSitePointOfInterest(
            offset,
            Rotation: 0,
            securityLevel,
            floor);
        template = template with
        {
            DataTerminals = template.DataTerminals.Append(point).ToArray(),
        };
        return point;
    }

    public HumanSitePointOfInterest AddSecureDoor(
        HumanSiteMapPoint offset,
        double rotation,
        int securityLevel,
        int floor)
    {
        ValidatePointMetadata(offset, securityLevel, floor);
        if (!double.IsFinite(rotation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rotation),
                "The secure-door rotation must be finite.");
        }

        var point = new HumanSitePointOfInterest(
            offset,
            SurfaceNavigation.NormalizeDegrees(rotation),
            securityLevel,
            floor);
        template = template with
        {
            SecureDoors = template.SecureDoors.Append(point).ToArray(),
        };
        return point;
    }

    public bool RemoveLastNamedPoint()
    {
        if (template.NamedPoints.Count == 0)
        {
            return false;
        }

        template = template with
        {
            NamedPoints = template.NamedPoints.SkipLast(1).ToArray(),
        };
        return true;
    }

    public bool RemoveLastDataTerminal()
    {
        if (template.DataTerminals.Count == 0)
        {
            return false;
        }

        template = template with
        {
            DataTerminals = template.DataTerminals.SkipLast(1).ToArray(),
        };
        return true;
    }

    public bool RemoveLastSecureDoor()
    {
        if (template.SecureDoors.Count == 0)
        {
            return false;
        }

        template = template with
        {
            SecureDoors = template.SecureDoors.SkipLast(1).ToArray(),
        };
        return true;
    }

    public bool RemoveLastBuilding()
    {
        if (template.Buildings.Count == 0)
        {
            return false;
        }

        template = template with
        {
            Buildings = template.Buildings.SkipLast(1).ToArray(),
        };
        return true;
    }

    private static void ValidatePointMetadata(
        HumanSiteMapPoint point,
        int securityLevel,
        int floor)
    {
        ValidatePoint(point);
        if (securityLevel is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(securityLevel),
                "The settlement security level must be between 0 and 3.");
        }

        if (floor is < 0 or > 99)
        {
            throw new ArgumentOutOfRangeException(
                nameof(floor),
                "The settlement floor must be between 0 and 99.");
        }
    }

    private static void ValidatePoint(HumanSiteMapPoint point)
    {
        if (!point.IsPlausibleMapOffset())
        {
            throw new ArgumentOutOfRangeException(
                nameof(point),
                "The settlement map point must be finite and within 10 km of the origin.");
        }
    }

    private static HumanSiteTemplate Clone(HumanSiteTemplate source)
    {
        return source with
        {
            LandingPads = source.LandingPads.ToArray(),
            SecureDoors = source.SecureDoors.ToArray(),
            NamedPoints = source.NamedPoints.ToArray(),
            DataTerminals = source.DataTerminals.ToArray(),
            ConflictZonePoints = source.ConflictZonePoints.ToArray(),
            Buildings = source.Buildings
                .Select(building => building with
                {
                    Paths = building.Paths.Select(Clone).ToArray(),
                })
                .ToArray(),
        };
    }

    private static HumanSiteBuildingPath Clone(HumanSiteBuildingPath path)
    {
        return path with
        {
            Points = path.Points.ToArray(),
            PointTypes = path.PointTypes.ToArray(),
        };
    }
}
