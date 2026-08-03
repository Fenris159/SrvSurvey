using System.Globalization;
using System.Reflection;

namespace SrvSurvey.Core.Updates;

public readonly record struct ReleaseVersion : IComparable<ReleaseVersion>
{
    private readonly Version coreVersion;
    private readonly string? prerelease;

    public ReleaseVersion(Version version)
        : this(version, null)
    {
    }

    private ReleaseVersion(Version version, string? prerelease)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.Major < 0 || version.Minor < 0 || version.Build < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                "A release version requires at least three numeric components.");
        }

        coreVersion = version;
        this.prerelease = prerelease;
    }

    public int Major => coreVersion.Major;

    public int Minor => coreVersion.Minor;

    public int Build => coreVersion.Build;

    public int Revision => coreVersion.Revision;

    public bool IsPrerelease => prerelease is not null;

    public string? Prerelease => prerelease;

    public Version CoreVersion => coreVersion;

    public static ReleaseVersion FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (TryParse(informational, out var releaseVersion))
        {
            return releaseVersion;
        }

        var fileVersion = assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()
            ?.Version;
        if (TryParse(fileVersion, out releaseVersion))
        {
            return releaseVersion;
        }

        return new ReleaseVersion(
            assembly.GetName().Version ?? new Version(0, 0, 0));
    }

    public static ReleaseVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
        {
            throw new FormatException($"'{value}' is not a valid release version.");
        }

        return version;
    }

    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var metadataIndex = text.IndexOf('+');
        if (metadataIndex >= 0)
        {
            if (metadataIndex == text.Length - 1)
            {
                return false;
            }

            text = text[..metadataIndex];
        }

        string? prerelease = null;
        var prereleaseIndex = text.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            if (prereleaseIndex == 0 || prereleaseIndex == text.Length - 1)
            {
                return false;
            }

            prerelease = text[(prereleaseIndex + 1)..]
                .ToLowerInvariant();
            text = text[..prereleaseIndex];
            if (!IsValidPrerelease(prerelease))
            {
                return false;
            }
        }

        if (!Version.TryParse(text, out var core)
            || core.Major < 0
            || core.Minor < 0
            || core.Build < 0)
        {
            return false;
        }

        version = new ReleaseVersion(core, prerelease);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var coreComparison = coreVersion.CompareTo(other.coreVersion);
        if (coreComparison != 0)
        {
            return coreComparison;
        }

        if (prerelease is null)
        {
            return other.prerelease is null ? 0 : 1;
        }

        if (other.prerelease is null)
        {
            return -1;
        }

        var left = prerelease.Split('.');
        var right = other.prerelease.Split('.');
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            var comparison = CompareIdentifier(left[index], right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    public override string ToString()
    {
        var core = coreVersion.Revision >= 0
            ? coreVersion.ToString(4)
            : coreVersion.ToString(3);
        return prerelease is null ? core : $"{core}-{prerelease}";
    }

    public static implicit operator ReleaseVersion(Version version) => new(version);

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) =>
        left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) =>
        left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) =>
        left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) =>
        left.CompareTo(right) >= 0;

    private static bool IsValidPrerelease(string value)
    {
        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0
                || identifier.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                return false;
            }

            if (identifier.Length > 1
                && identifier[0] == '0'
                && identifier.All(char.IsAsciiDigit))
            {
                return false;
            }
        }

        return true;
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = long.TryParse(
            left,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var leftValue);
        var rightNumeric = long.TryParse(
            right,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var rightValue);
        if (leftNumeric && rightNumeric)
        {
            return leftValue.CompareTo(rightValue);
        }

        if (leftNumeric != rightNumeric)
        {
            return leftNumeric ? -1 : 1;
        }

        return string.Compare(left, right, StringComparison.Ordinal);
    }
}
