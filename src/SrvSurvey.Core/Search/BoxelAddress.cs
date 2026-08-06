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
        var storedAddress = 0L;
        var hasStoredAddress = storedParts.Length == 2
            && long.TryParse(
                storedParts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out storedAddress)
            && storedAddress > 0;
        var match = BoxelNamePattern().Match(storedParts[0]);
        if (!match.Success)
        {
            return hasStoredAddress
                && TryFromSystemAddress(
                    storedAddress,
                    storedParts[0],
                    out boxel);
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

        boxel = new BoxelAddress(
            match.Groups[1].Value.Trim(),
            match.Groups[2].Value.ToUpperInvariant(),
            massCode,
            n1,
            n2,
            hasStoredAddress ? storedAddress : 0);
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

        var normalizedPublicName = publicName?
            .Split('|', 2, StringSplitOptions.TrimEntries)[0];
        if (TryParse(normalizedPublicName, out var parsed)
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
            new BoxelAddressIdentity(
                (int)remaining,
                systemAddress,
                normalizedPublicName?.Trim()));
        return true;
    }

    /// <summary>
    /// Returns an authoritative address supplied by the journal or known-system
    /// catalog, or calculates the procedural system address when one is absent.
    /// </summary>
    public bool TryGetSystemAddress(out long systemAddress)
    {
        if (SystemAddress > 0)
        {
            systemAddress = SystemAddress;
            return true;
        }

        return TryEncodeSystemAddress(out systemAddress);
    }

    /// <summary>
    /// Calculates the system address represented by this procedural boxel name.
    /// Hand-authored sector names intentionally remain unsupported until their
    /// region geometry is completed; callers should use a journal or catalog
    /// address for those systems.
    /// </summary>
    public bool TryEncodeSystemAddress(out long systemAddress)
    {
        systemAddress = 0;
        if (!CanEncodeSystemAddress())
        {
            return false;
        }

        var sectorCoordinates = BoxelSectorNameResolver.GetSectorCoordinates(
            Sector,
            MassCode);
        if (!IsValidSectorCoordinate(sectorCoordinates))
        {
            return false;
        }

        var sector = sectorCoordinates!.Value;
        var relative = GetRelativeCoordinates();
        var massCodeValue = MassCode - MinimumMassCode;
        var relativeBitCount = MaximumMassCode - MassCode;
        var relativeLimit = 1 << relativeBitCount;
        if (!IsValidRelativeCoordinate(relative, relativeLimit))
        {
            return false;
        }

        var systemNumberBits = 11 + (massCodeValue * 3);
        var systemNumberLimit = 1UL << systemNumberBits;
        if ((ulong)N2 >= systemNumberLimit)
        {
            return false;
        }

        long address = 0;
        address = BoxelSectorNameResolver.pack_and_shift(address, 0, 9);
        address = BoxelSectorNameResolver.pack_and_shift(
            address,
            N2,
            systemNumberBits);
        address = BoxelSectorNameResolver.pack_and_shift(
            address,
            sector.x,
            7);
        address = BoxelSectorNameResolver.pack_and_shift(
            address,
            relative.X,
            relativeBitCount);
        address = BoxelSectorNameResolver.pack_and_shift(
            address,
            sector.y,
            6);
        address = BoxelSectorNameResolver.pack_and_shift(
            address,
            relative.Y,
            relativeBitCount);
        address = BoxelSectorNameResolver.pack_and_shift(
            address,
            sector.z,
            7);
        address = BoxelSectorNameResolver.pack_and_shift(
            address,
            relative.Z,
            relativeBitCount);
        address = BoxelSectorNameResolver.pack_and_shift(
            address,
            massCodeValue,
            3);
        if (address <= 0)
        {
            return false;
        }

        if (!TryFromSystemAddress(address, null, out var decoded)
            || decoded is null
            || !string.Equals(
                GeneratedName,
                decoded.GeneratedName,
                StringComparison.Ordinal))
        {
            return false;
        }

        systemAddress = address;
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
        ArgumentOutOfRangeException.ThrowIfNegative(massCodeDifference);

        var total = 0;
        for (var level = massCodeDifference; level >= 0; level--)
        {
            total = checked(total + (int)Math.Pow(8, level));
        }

        return total;
    }

    public BoxelAddress WithSystemNumber(int systemNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(systemNumber);

        var candidate = this with
        {
            N2 = systemNumber,
            SystemAddress = 0,
            PublicName = null,
        };
        return candidate.TryEncodeSystemAddress(out var systemAddress)
            ? candidate with { SystemAddress = systemAddress }
            : candidate;
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
        BoxelAddressIdentity identity = default)
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
            identity.SystemNumber,
            identity.SystemAddress,
            identity.PublicName);
    }

    private readonly record struct BoxelAddressIdentity(
        int SystemNumber = 0,
        long SystemAddress = 0,
        string? PublicName = null);

    private bool CanEncodeSystemAddress()
    {
        return IsValidMassCode(MassCode)
            && N1 >= 0
            && N2 >= 0
            && !string.IsNullOrEmpty(Letters)
            && Letters.Length == 4
            && Letters[2] == '-'
            && Letters[0] is >= 'A' and <= 'Z'
            && Letters[1] is >= 'A' and <= 'Z'
            && Letters[3] is >= 'A' and <= 'Z'
            && BoxelSectorNameResolver.IsValidSectorName(Sector);
    }

    private static bool IsValidSectorCoordinate(SectorCoordinate? sector)
    {
        return sector is not null
            && sector.Value.x is >= 0 and < 128
            && sector.Value.y is >= 0 and < 64
            && sector.Value.z is >= 0 and < 128;
    }

    private static bool IsValidRelativeCoordinate(
        BoxelCoordinate relative,
        int relativeLimit)
    {
        return relative.X is >= 0
            && relative.X < relativeLimit
            && relative.Y is >= 0
            && relative.Y < relativeLimit
            && relative.Z is >= 0
            && relative.Z < relativeLimit;
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
