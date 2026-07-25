namespace SrvSurvey.Core.Guardian;

public sealed class GuardianSiteTemplateAuthoringSession
{
    public GuardianSiteTemplateAuthoringSession(GuardianSiteTemplate source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Template = Clone(source);
    }

    public GuardianSiteTemplate Template { get; private set; }

    public void UpdateMetadata(
        string name,
        string backgroundImage,
        GuardianMapPoint imageOffset,
        double scaleFactor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A Guardian template name is required.", nameof(name));
        }

        ValidateFinite(imageOffset.X, nameof(imageOffset));
        ValidateFinite(imageOffset.Y, nameof(imageOffset));
        if (!double.IsFinite(scaleFactor) || scaleFactor <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scaleFactor),
                "The Guardian template scale factor must be positive.");
        }

        Template = Template with
        {
            Name = name.Trim(),
            BackgroundImage = backgroundImage?.Trim() ?? string.Empty,
            ImageOffset = imageOffset,
            ScaleFactor = scaleFactor,
        };
    }

    public void AddPoint(GuardianPointOfInterest point)
    {
        ValidatePoint(point);
        if (AllPoints().Any(candidate => string.Equals(
                candidate.Name,
                point.Name,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Guardian template point '{point.Name}' already exists.");
        }

        if (point.Type == GuardianPoiType.DestructiblePanel)
        {
            Template = Template with
            {
                DestructiblePanels = Template.DestructiblePanels
                    .Append(point)
                    .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            };
            return;
        }

        Template = Template with
        {
            PointsOfInterest = Template.PointsOfInterest
                .Append(point)
                .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    public void UpdatePoint(
        string originalName,
        GuardianPointOfInterest replacement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalName);
        ValidatePoint(replacement);
        var existing = AllPoints().FirstOrDefault(point => string.Equals(
            point.Name,
            originalName,
            StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Guardian template point '{originalName}' was not found.");
        if (!string.Equals(
                originalName,
                replacement.Name,
                StringComparison.OrdinalIgnoreCase)
            && AllPoints().Any(point => string.Equals(
                point.Name,
                replacement.Name,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Guardian template point '{replacement.Name}' already exists.");
        }

        RemovePoint(existing.Name);
        AddPoint(replacement);
    }

    public void RemovePoint(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var points = Template.PointsOfInterest
            .Where(point => !string.Equals(
                point.Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var panels = Template.DestructiblePanels
            .Where(point => !string.Equals(
                point.Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (points.Length == Template.PointsOfInterest.Count
            && panels.Length == Template.DestructiblePanels.Count)
        {
            throw new InvalidOperationException(
                $"Guardian template point '{name}' was not found.");
        }

        Template = Template with
        {
            PointsOfInterest = points,
            DestructiblePanels = panels,
        };
    }

    public void SetObeliskGroupLabel(
        string name,
        GuardianMapPoint location)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An obelisk group name is required.", nameof(name));
        }

        ValidateFinite(location.X, nameof(location));
        ValidateFinite(location.Y, nameof(location));
        var labels = new Dictionary<string, GuardianMapPoint>(
            Template.ObeliskGroupNameLocations,
            StringComparer.OrdinalIgnoreCase)
        {
            [name.Trim()] = location,
        };
        Template = Template with { ObeliskGroupNameLocations = labels };
    }

    public void RemoveObeliskGroupLabel(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var labels = new Dictionary<string, GuardianMapPoint>(
            Template.ObeliskGroupNameLocations,
            StringComparer.OrdinalIgnoreCase);
        if (!labels.Remove(name))
        {
            throw new InvalidOperationException(
                $"Guardian obelisk group label '{name}' was not found.");
        }

        Template = Template with { ObeliskGroupNameLocations = labels };
    }

    private IEnumerable<GuardianPointOfInterest> AllPoints()
    {
        return Template.PointsOfInterest.Concat(Template.DestructiblePanels);
    }

    private static GuardianSiteTemplate Clone(GuardianSiteTemplate source)
    {
        return source with
        {
            PointsOfInterest = source.PointsOfInterest.ToArray(),
            DestructiblePanels = source.DestructiblePanels.ToArray(),
            ObeliskGroupNameLocations =
                new Dictionary<string, GuardianMapPoint>(
                    source.ObeliskGroupNameLocations,
                    StringComparer.OrdinalIgnoreCase),
        };
    }

    private static void ValidatePoint(GuardianPointOfInterest point)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (string.IsNullOrWhiteSpace(point.Name))
        {
            throw new ArgumentException("A Guardian point name is required.", nameof(point));
        }

        ValidateFinite(point.Angle, nameof(point));
        ValidateFinite(point.Distance, nameof(point));
        ValidateFinite(point.Rotation, nameof(point));
        if (point.Angle is < 0 or >= 360)
        {
            throw new ArgumentOutOfRangeException(
                nameof(point),
                "Guardian point angles must be from 0 up to but not including 360 degrees.");
        }

        if (point.Distance < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(point),
                "Guardian point distances cannot be negative.");
        }

        if (point.Rotation is < -1 or >= 360)
        {
            throw new ArgumentOutOfRangeException(
                nameof(point),
                "Guardian point rotations must be -1 or from 0 up to but not including 360 degrees.");
        }
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Guardian template geometry must use finite values.");
        }
    }
}
