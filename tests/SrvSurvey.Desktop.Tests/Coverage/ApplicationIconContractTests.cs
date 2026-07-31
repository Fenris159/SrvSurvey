using System.Xml.Linq;

namespace SrvSurvey.Desktop.Tests.Coverage;

public sealed class ApplicationIconContractTests
{
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
