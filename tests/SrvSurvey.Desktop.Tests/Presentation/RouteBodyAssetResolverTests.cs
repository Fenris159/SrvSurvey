using System.Xml.Linq;
using SkiaSharp;
using SrvSurvey.Desktop.Presentation;

namespace SrvSurvey.Desktop.Tests.Presentation;

public sealed class RouteBodyAssetResolverTests
{
    [Theory]
    [InlineData(null, RouteBodyVisualKind.Unknown, "unknown.png")]
    [InlineData("Supermassive Black Hole", RouteBodyVisualKind.BlackHole, "black-hole.png")]
    [InlineData("Neutron Star", RouteBodyVisualKind.NeutronStar, "neutron-star.png")]
    [InlineData("White Dwarf (DA) Star", RouteBodyVisualKind.WhiteDwarf, "white-dwarf.png")]
    [InlineData("G (White-Yellow) Star", RouteBodyVisualKind.Star, "star.png")]
    [InlineData("Class V gas giant", RouteBodyVisualKind.GasGiant, "gas-giant.png")]
    [InlineData("Gas giant with water based life", RouteBodyVisualKind.GasGiant, "gas-giant.png")]
    [InlineData("Water giant", RouteBodyVisualKind.WaterGiant, "water-giant.png")]
    [InlineData("Water world", RouteBodyVisualKind.WaterWorld, "water-world.png")]
    [InlineData("Earth-like world", RouteBodyVisualKind.EarthLikeWorld, "earth-like-world.png")]
    [InlineData("Ammonia world", RouteBodyVisualKind.AmmoniaWorld, "ammonia-world.png")]
    [InlineData("High metal content world", RouteBodyVisualKind.HighMetalContentWorld, "high-metal-content.png")]
    [InlineData("Metal-rich body", RouteBodyVisualKind.MetalRichBody, "metal-rich.png")]
    [InlineData("Rocky body", RouteBodyVisualKind.RockyBody, "rocky-body.png")]
    [InlineData("Rocky ice body", RouteBodyVisualKind.RockyIceBody, "rocky-ice-body.png")]
    [InlineData("Icy body", RouteBodyVisualKind.IcyBody, "icy-body.png")]
    [InlineData("Asteroid Cluster", RouteBodyVisualKind.AsteroidCluster, "asteroid-cluster.png")]
    [InlineData("Barycentre", RouteBodyVisualKind.Barycentre, "barycentre.png")]
    public void SubtypesMapToStableSharedAssets(
        string? subtype,
        RouteBodyVisualKind expectedKind,
        string expectedFileName)
    {
        var visual = RouteBodyAssetResolver.Resolve(subtype);

        Assert.Equal(expectedKind, visual.Kind);
        Assert.EndsWith(
            $"/Assets/Bodies/{expectedFileName}",
            visual.AssetPath,
            StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(visual.AccessibleName));
    }

    [Fact]
    public void EverySharedBodyAssetHasValidVectorAndRuntimeImages()
    {
        var assetRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "Assets",
            "Bodies");
        var pngs = Directory.GetFiles(assetRoot, "*.png");
        var svgs = Directory.GetFiles(assetRoot, "*.svg");

        Assert.Equal(17, pngs.Length);
        Assert.Equal(17, svgs.Length);
        foreach (var png in pngs)
        {
            using var image = SKBitmap.Decode(png);
            Assert.NotNull(image);
            Assert.Equal(152, image.Width);
            Assert.Equal(152, image.Height);
        }

        foreach (var svg in svgs)
        {
            var document = XDocument.Load(svg);
            Assert.Equal("svg", document.Root?.Name.LocalName);
            Assert.Equal("0 0 76 76", document.Root?.Attribute("viewBox")?.Value);
        }
    }

    [Fact]
    public void DesktopProjectBundlesBodyRuntimeAssets()
    {
        var project = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "SrvSurvey.Desktop.csproj"));

        Assert.Contains(
            "Assets\\Bodies\\**\\*.png",
            project,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SrvSurvey.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
