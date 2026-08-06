using SrvSurvey.Core.Search;

namespace SrvSurvey.Core.Navigation
{
    public sealed record GalacticRegion(int Id, string Name);

    public static class GalacticRegionMap
    {
        public static IReadOnlyList<GalacticRegion> Regions =>
            EliteDangerousRegionMap.RegionMap.Regions;

        public static GalacticRegion? Find(GalacticCoordinate position)
        {
            return EliteDangerousRegionMap.RegionMap.FindRegion(
                position.X,
                position.Y,
                position.Z);
        }
    }
}

namespace EliteDangerousRegionMap
{
    // The region grid is the repository's existing copy of klightspeed's
    // EliteDangerousRegionMap data. This partial keeps only the deterministic
    // coordinate lookup needed by the cross-platform core.
    public static partial class RegionMap
    {
        private const double XOrigin = -49985;
        private const double ZOrigin = -24105;
        private static readonly Lazy<IReadOnlyList<
            SrvSurvey.Core.Navigation.GalacticRegion>> regions =
                new(CreateRegions);

        public static IReadOnlyList<SrvSurvey.Core.Navigation.GalacticRegion>
            Regions => regions.Value;

        private static SrvSurvey.Core.Navigation.GalacticRegion[]
            CreateRegions()
        {
            return RegionNames
                .Select((name, id) => new { name, id })
                .Where(item => item.id > 0)
                .Select(item => new SrvSurvey.Core.Navigation.GalacticRegion(
                    item.id,
                    item.name))
                .ToArray();
        }

        public static SrvSurvey.Core.Navigation.GalacticRegion? FindRegion(
            double x,
            double y,
            double z)
        {
            _ = y;
            var pixelX = (int)((x - XOrigin) * 83 / 4096);
            var pixelZ = (int)((z - ZOrigin) * 83 / 4096);
            if (pixelX < 0
                || pixelZ < 0
                || pixelZ >= RegionMapLines.Length)
            {
                return null;
            }

            var regionId = 0;
            var runStart = 0;
            foreach (var (runLength, value) in RegionMapLines[pixelZ])
            {
                if (pixelX < runStart + runLength)
                {
                    regionId = value;
                    break;
                }

                runStart += runLength;
            }

            return regionId == 0
                ? null
                : new SrvSurvey.Core.Navigation.GalacticRegion(
                    regionId,
                    RegionNames[regionId]);
        }
    }
}
