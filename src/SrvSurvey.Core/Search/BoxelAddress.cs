using System.Globalization;
using System.Text.RegularExpressions;

namespace SrvSurvey.Core.Search;

public sealed partial record BoxelAddress(
    string Sector,
    string Letters,
    char MassCode,
    int N1,
    int N2,
    long SystemAddress = 0,
    string? PublicName = null)
{
    public const char MinimumMassCode = 'a';
    public const char MaximumMassCode = 'h';

    public string Id => N1 == 0
        ? $"{Letters} {MassCode}"
        : $"{Letters} {MassCode}{N1}";

    public string Prefix => N1 == 0
        ? $"{Sector} {Letters} {MassCode}"
        : $"{Sector} {Letters} {MassCode}{N1}-";

    public string GeneratedName => $"{Prefix}{N2}";

    public string Name => PublicName ?? GeneratedName;

    public int CubeSize => GetCubeSize(MassCode);

    public BoxelAddress Parent
    {
        get
        {
            if (MassCode == MaximumMassCode)
            {
                throw new InvalidOperationException(
                    "Mass-code h boxels do not have a supported parent.");
            }

            var relative = GetRelativeCoordinates();
            return FromCoordinates(
                Sector,
                relative.X / 2,
                relative.Y / 2,
                relative.Z / 2,
                (char)(MassCode + 1));
        }
    }

    public IReadOnlyList<BoxelAddress> Children
    {
        get
        {
            if (MassCode == MinimumMassCode)
            {
                return [];
            }

            var relative = GetRelativeCoordinates();
            var x = relative.X * 2;
            var y = relative.Y * 2;
            var z = relative.Z * 2;
            var childMassCode = (char)(MassCode - 1);
            return
            [
                FromCoordinates(Sector, x, y, z, childMassCode),
                FromCoordinates(Sector, x + 1, y, z, childMassCode),
                FromCoordinates(Sector, x, y + 1, z, childMassCode),
                FromCoordinates(Sector, x + 1, y + 1, z, childMassCode),
                FromCoordinates(Sector, x, y, z + 1, childMassCode),
                FromCoordinates(Sector, x + 1, y, z + 1, childMassCode),
                FromCoordinates(Sector, x, y + 1, z + 1, childMassCode),
                FromCoordinates(Sector, x + 1, y + 1, z + 1, childMassCode),
            ];
        }
    }

    public static bool TryParse(string? value, out BoxelAddress? boxel)
    {
        boxel = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var storedParts = value.Split('|', 2, StringSplitOptions.TrimEntries);
        var match = BoxelNamePattern().Match(storedParts[0]);
        if (!match.Success)
        {
            return false;
        }

        var massCode = char.ToLowerInvariant(match.Groups[3].Value[0]);
        if (!IsValidMassCode(massCode))
        {
            return false;
        }

        if (!int.TryParse(
                match.Groups[4].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var firstNumber))
        {
            return false;
        }

        int n1;
        int n2;
        if (match.Groups[5].Success)
        {
            n1 = firstNumber;
            if (!int.TryParse(
                    match.Groups[5].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out n2))
            {
                return false;
            }
        }
        else
        {
            n1 = 0;
            n2 = firstNumber;
        }

        var systemAddress = storedParts.Length == 2
            && long.TryParse(
                storedParts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedAddress)
                ? parsedAddress
                : 0;
        boxel = new BoxelAddress(
            match.Groups[1].Value.Trim(),
            match.Groups[2].Value.ToUpperInvariant(),
            massCode,
            n1,
            n2,
            systemAddress);
        return true;
    }

    public static BoxelAddress Parse(string value)
    {
        return TryParse(value, out var boxel)
            ? boxel!
            : throw new FormatException($"'{value}' is not a generated boxel name.");
    }

    public static bool TryFromSystemAddress(
        long systemAddress,
        string? publicName,
        out BoxelAddress? boxel)
    {
        boxel = null;
        if (systemAddress <= 0)
        {
            return false;
        }

        if (TryParse(publicName, out var parsed)
            && parsed is not null
            && BoxelSectorNameResolver.IsValidSectorName(parsed.Sector))
        {
            boxel = parsed with { SystemAddress = systemAddress };
            return true;
        }

        var remaining = (ulong)systemAddress;
        var massCodeValue = TakeBits(ref remaining, 3);
        var massCode = (char)(MinimumMassCode + massCodeValue);
        if (!IsValidMassCode(massCode))
        {
            return false;
        }

        var relativeBitCount = MaximumMassCode - massCode;
        var relativeZ = TakeBits(ref remaining, relativeBitCount);
        var sectorZ = TakeBits(ref remaining, 7);
        var relativeY = TakeBits(ref remaining, relativeBitCount);
        var sectorY = TakeBits(ref remaining, 6);
        var relativeX = TakeBits(ref remaining, relativeBitCount);
        var sectorX = TakeBits(ref remaining, 7);
        if (remaining > int.MaxValue)
        {
            return false;
        }

        var sector = BoxelSectorNameResolver.GetSectorName(
            sectorX,
            sectorY,
            sectorZ);
        if (string.IsNullOrWhiteSpace(sector))
        {
            return false;
        }

        boxel = FromCoordinates(
            sector,
            relativeX,
            relativeY,
            relativeZ,
            massCode,
            (int)remaining,
            systemAddress,
            publicName?.Trim());
        return true;
    }

    public static bool IsValidMassCode(char massCode)
    {
        return massCode is >= MinimumMassCode and <= MaximumMassCode;
    }

    public static int GetCubeSize(char massCode)
    {
        if (!IsValidMassCode(massCode))
        {
            throw new ArgumentOutOfRangeException(nameof(massCode));
        }

        return (int)Math.Pow(2, massCode - MinimumMassCode) * 10;
    }

    public static int GetTotalChildCount(int massCodeDifference)
    {
        if (massCodeDifference < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(massCodeDifference));
        }

        var total = 0;
        for (var level = massCodeDifference; level >= 0; level--)
        {
            total = checked(total + (int)Math.Pow(8, level));
        }

        return total;
    }

    public BoxelAddress WithSystemNumber(int systemNumber)
    {
        if (systemNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(systemNumber));
        }

        return this with
        {
            N2 = systemNumber,
            SystemAddress = 0,
            PublicName = null,
        };
    }

    public bool Contains(BoxelAddress? child)
    {
        if (child is null)
        {
            return false;
        }

        if (string.Equals(Prefix, child.Prefix, StringComparison.Ordinal))
        {
            return true;
        }

        if (child.MassCode >= MassCode)
        {
            return false;
        }

        var candidate = child;
        while (candidate.MassCode < MassCode)
        {
            candidate = candidate.Parent;
            if (string.Equals(Prefix, candidate.Prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public string ToStoredString()
    {
        return SystemAddress > 0 ? $"{Name}|{SystemAddress}" : Name;
    }

    public override string ToString()
    {
        return Name;
    }

    private BoxelCoordinate GetRelativeCoordinates()
    {
        var first = Letters[0] - 'A';
        var second = Letters[1] - 'A';
        var third = Letters[3] - 'A';
        var value = first + (second * 26) + (third * 676) + (N1 * 17576);
        return new BoxelCoordinate(
            value % 128,
            value % 16384 / 128,
            value / 16384);
    }

    private static BoxelAddress FromCoordinates(
        string sector,
        int x,
        int y,
        int z,
        char massCode,
        int systemNumber = 0,
        long systemAddress = 0,
        string? publicName = null)
    {
        var value = x + (y * 128) + (z * 16384);
        var first = value % 26;
        value = (value - first) / 26;
        var second = value % 26;
        value = (value - second) / 26;
        var third = value % 26;
        value = (value - third) / 26;
        return new BoxelAddress(
            sector,
            $"{(char)(first + 'A')}{(char)(second + 'A')}-{(char)(third + 'A')}",
            massCode,
            value,
            systemNumber,
            systemAddress,
            publicName);
    }

    private static int TakeBits(ref ulong value, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        var mask = (1UL << count) - 1;
        var result = (int)(value & mask);
        value >>= count;
        return result;
    }

    [GeneratedRegex(
        @"^(.+) (\w\w-\w) (\w)(\d+)-?(\d+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex BoxelNamePattern();

    private readonly record struct BoxelCoordinate(int X, int Y, int Z);
}
