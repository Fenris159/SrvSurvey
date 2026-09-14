using System.Xml.Linq;
using SkiaSharp;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class ApplicationIconContractTests
{
    private const string HighResolutionIconFileName = "logo-remastered-linux-windows-split.png";
    private const int MaximumResamplingChannelDelta = 24;

    private static readonly int[] RequiredIconSizes = [16, 20, 24, 32, 48, 64, 128, 256];

    [Fact]
    public void DesktopProjectEmbedsTheAvaloniaIconInTheExecutable()
    {
        string root = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "src", "SrvSurvey.Desktop", "SrvSurvey.Desktop.csproj"));
        XElement applicationIcon = project.Descendants("ApplicationIcon").Single();

        Assert.Equal("Assets\\logo.ico", applicationIcon.Value.Trim());
    }

    [Fact]
    public void IconContainsWindowsTrayAndApplicationSizes()
    {
        string root = FindRepositoryRoot();
        string iconPath = Path.Combine(root, "src", "SrvSurvey.Desktop", "Assets", "logo.ico");
        using FileStream stream = File.OpenRead(iconPath);
        using var reader = new BinaryReader(stream);

        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        ushort count = reader.ReadUInt16();
        IconEntry[] entries = Enumerable.Range(0, count).Select(_ => ReadEntry(reader)).ToArray();

        Assert.Equal(256, entries[0].Width);
        foreach (int requiredSize in RequiredIconSizes)
        {
            Assert.Contains(entries, entry => entry.Width == requiredSize && entry.Height == requiredSize);
        }

        foreach (IconEntry? entry in entries)
        {
            Assert.True(entry.BytesInResource > 0);
            Assert.InRange((long)entry.ImageOffset + entry.BytesInResource, 1, stream.Length);
        }
    }

    [Fact]
    public void WindowsIconUsesTheCurrentRemasteredArtwork()
    {
        string assets = Path.Combine(FindRepositoryRoot(), "src", "SrvSurvey.Desktop", "Assets");
        using var source = SKBitmap.Decode(Path.Combine(assets, HighResolutionIconFileName));
        using var icon = SKBitmap.Decode(Path.Combine(assets, "logo.ico"));

        Assert.NotNull(source);
        Assert.NotNull(icon);
        Assert.Equal(1024, source.Width);
        Assert.Equal(1024, source.Height);
        Assert.Equal(256, icon.Width);
        Assert.Equal(256, icon.Height);

        foreach ((int x, int y) in new[] { (128, 28), (48, 128), (208, 128), (128, 220) })
        {
            SKColor expected = source.GetPixel(x * 4, y * 4);
            SKColor actual = icon.GetPixel(x, y);
            Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, MaximumResamplingChannelDelta);
            Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, MaximumResamplingChannelDelta);
            Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, MaximumResamplingChannelDelta);
            Assert.InRange(Math.Abs(expected.Alpha - actual.Alpha), 0, MaximumResamplingChannelDelta);
        }
    }

    [Fact]
    public void EveryWindowInheritsTheApplicationIcon()
    {
        string root = FindRepositoryRoot();
        var application = XDocument.Load(Path.Combine(root, "src", "SrvSurvey.Desktop", "App.axaml"));
        XElement windowStyle = application
            .Descendants()
            .Single(element => element.Name.LocalName == "Style" && element.Attribute("Selector")?.Value == "Window");
        XElement iconSetter = windowStyle
            .Elements()
            .Single(element => element.Name.LocalName == "Setter" && element.Attribute("Property")?.Value == "Icon");

        Assert.Equal("/Assets/logo.ico", iconSetter.Attribute("Value")?.Value);
    }

    [Fact]
    public void HighResolutionIconSourceDrivesLinuxPackaging()
    {
        string root = FindRepositoryRoot();
        string sourcePath = Path.Combine(root, "src", "SrvSurvey.Desktop", "Assets", HighResolutionIconFileName);
        using FileStream stream = File.OpenRead(sourcePath);
        using var reader = new BinaryReader(stream);

        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], reader.ReadBytes(8));
        _ = ReadBigEndianUInt32(reader);
        Assert.Equal("IHDR", new string(reader.ReadChars(4)));
        Assert.Equal(1024u, ReadBigEndianUInt32(reader));
        Assert.Equal(1024u, ReadBigEndianUInt32(reader));

        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "build-srvsurvey-xp.yml"));
        Assert.Contains($"Assets/{HighResolutionIconFileName}", workflow, StringComparison.Ordinal);
    }

    private static IconEntry ReadEntry(BinaryReader reader)
    {
        int width = DecodeDimension(reader.ReadByte());
        int height = DecodeDimension(reader.ReadByte());
        _ = reader.ReadByte();
        _ = reader.ReadByte();
        _ = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        uint bytesInResource = reader.ReadUInt32();
        uint imageOffset = reader.ReadUInt32();
        return new IconEntry(width, height, bytesInResource, imageOffset);
    }

    private static int DecodeDimension(byte value) => value == 0 ? 256 : value;

    private static uint ReadBigEndianUInt32(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(sizeof(uint));
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

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed record IconEntry(int Width, int Height, uint BytesInResource, uint ImageOffset);
}
