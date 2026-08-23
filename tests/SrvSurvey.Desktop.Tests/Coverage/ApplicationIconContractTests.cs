using System.Xml.Linq;
using SkiaSharp;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class ApplicationIconContractTests
{
    private const string HighResolutionIconFileName =
        "logo-remastered-linux-windows-split.png";
    private const int MaximumResamplingChannelDelta = 24;

    private static readonly int[] RequiredIconSizes =
        [16, 20, 24, 32, 48, 64, 128, 256];

    [Fact]
    public void DesktopProjectEmbedsTheAvaloniaIconInTheExecutable()
    {
        var root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "SrvSurvey.Desktop.csproj"));
        var applicationIcon = project
            .Descendants("ApplicationIcon")
            .Single();

        Assert.Equal("Assets\\logo.ico", applicationIcon.Value.Trim());
    }

    [Fact]
    public void IconContainsWindowsTrayAndApplicationSizes()
    {
        var root = FindRepositoryRoot();
        var iconPath = Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Assets",
            "logo.ico");
        using var stream = File.OpenRead(iconPath);
        using var reader = new BinaryReader(stream);

        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        var entries = Enumerable.Range(0, count)
            .Select(_ => ReadEntry(reader))
            .ToArray();

        Assert.Equal(256, entries[0].Width);
        foreach (var requiredSize in RequiredIconSizes)
        {
            Assert.Contains(entries, entry =>
                entry.Width == requiredSize && entry.Height == requiredSize);
        }

        foreach (var entry in entries)
        {
            Assert.True(entry.BytesInResource > 0);
            Assert.InRange(
                (long)entry.ImageOffset + entry.BytesInResource,
                1,
                stream.Length);
        }
    }

    [Fact]
    public void WindowsIconUsesTheCurrentRemasteredArtwork()
    {
        var assets = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "SrvSurvey.Desktop",
            "Assets");
        using var source = SKBitmap.Decode(Path.Combine(
            assets,
            HighResolutionIconFileName));
        using var icon = SKBitmap.Decode(Path.Combine(assets, "logo.ico"));

        Assert.NotNull(source);
        Assert.NotNull(icon);
        Assert.Equal(1024, source.Width);
        Assert.Equal(1024, source.Height);
        Assert.Equal(256, icon.Width);
        Assert.Equal(256, icon.Height);

        foreach (var (x, y) in new[]
                 {
                     (128, 28),
                     (48, 128),
                     (208, 128),
                     (128, 220),
                 })
        {
            var expected = source.GetPixel(x * 4, y * 4);
            var actual = icon.GetPixel(x, y);
            Assert.InRange(
                Math.Abs(expected.Red - actual.Red),
                0,
                MaximumResamplingChannelDelta);
            Assert.InRange(
                Math.Abs(expected.Green - actual.Green),
                0,
                MaximumResamplingChannelDelta);
            Assert.InRange(
                Math.Abs(expected.Blue - actual.Blue),
                0,
                MaximumResamplingChannelDelta);
            Assert.InRange(
                Math.Abs(expected.Alpha - actual.Alpha),
                0,
                MaximumResamplingChannelDelta);
        }
    }

    [Fact]
    public void EveryWindowInheritsTheApplicationIcon()
    {
        var root = FindRepositoryRoot();
        var application = XDocument.Load(Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "App.axaml"));
        var windowStyle = application.Descendants()
            .Single(element =>
                element.Name.LocalName == "Style"
                && element.Attribute("Selector")?.Value == "Window");
        var iconSetter = windowStyle.Elements()
            .Single(element =>
                element.Name.LocalName == "Setter"
                && element.Attribute("Property")?.Value == "Icon");

        Assert.Equal("/Assets/logo.ico", iconSetter.Attribute("Value")?.Value);
    }

    [Fact]
    public void HighResolutionIconSourceDrivesLinuxPackaging()
    {
        var root = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            root,
            "src",
            "SrvSurvey.Desktop",
            "Assets",
            HighResolutionIconFileName);
        using var stream = File.OpenRead(sourcePath);
        using var reader = new BinaryReader(stream);

        Assert.Equal(
            [137, 80, 78, 71, 13, 10, 26, 10],
            reader.ReadBytes(8));
        _ = ReadBigEndianUInt32(reader);
        Assert.Equal("IHDR", new string(reader.ReadChars(4)));
        Assert.Equal(1024u, ReadBigEndianUInt32(reader));
        Assert.Equal(1024u, ReadBigEndianUInt32(reader));

        var workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "build-srvsurvey-xp.yml"));
        Assert.Contains(
            $"Assets/{HighResolutionIconFileName}",
            workflow,
            StringComparison.Ordinal);
    }

    private static IconEntry ReadEntry(BinaryReader reader)
    {
        var width = DecodeDimension(reader.ReadByte());
        var height = DecodeDimension(reader.ReadByte());
        _ = reader.ReadByte();
        _ = reader.ReadByte();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        var bytesInResource = reader.ReadUInt32();
        var imageOffset = reader.ReadUInt32();
        return new IconEntry(width, height, bytesInResource, imageOffset);
    }

    private static int DecodeDimension(byte value) => value == 0 ? 256 : value;

    private static uint ReadBigEndianUInt32(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(sizeof(uint));
        Assert.Equal(sizeof(uint), bytes.Length);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToUInt32(bytes);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SrvSurvey.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }

    private sealed record IconEntry(
        int Width,
        int Height,
        uint BytesInResource,
        uint ImageOffset);
}
