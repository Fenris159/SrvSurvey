using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SrvSurvey.Desktop.Controls;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Controls;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class GuardianAlignmentControlTests
{
    [AvaloniaFact]
    public void HeadingGuidesAreTheExactLegacyAssets()
    {
        var expected = new Dictionary<string, string>
        {
            ["alpha"] = "fc91e44bd5a9a822bbbba7767fdb3718789324c86eb4dcbfbeea7ce22efa4232",
            ["beta"] = "f5bbd12d2a4f9579ed517a06c51cc12a273cb9ea9620856027bf152cb5f44cc6",
            ["crossroads"] = "a2411d88ba7e7391c87451ed484671d0336e85b9d8044626da271c3f804200d5",
            ["data-port"] = "9139bb2370d5439e35ce0ee75817ebcff99c685949cd22d8936bda800048c025",
            ["fistbump"] = "117456ac963030e0fe1dd49f9022bb1b0bff1ed6ceaf0f7f2c582219b4263450",
            ["gamma"] = "d111f423d72ea3c2d82a36f30d0e74966b02812b40845b58c8f41be7dda9a8d3",
            ["lacrosse"] = "d3aba02afbf249d279bfb16649593232a18c48a4e76019c7dc2b3ba4042db86d",
        };

        foreach (var (name, hash) in expected)
        {
            var uri = new Uri(
                $"avares://SrvSurvey.Desktop/Assets/GuardianGuidance/{name}-heading-guide.png");
            Assert.True(AssetLoader.Exists(uri), name);
            using var stream = AssetLoader.Open(uri);
            Assert.Equal(
                hash,
                Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        }
    }

    [AvaloniaFact]
    public void EveryLegacyAlignmentModeRendersAndStructuresKeepDistinctGeometry()
    {
        var structureHashes = new HashSet<string>();
        foreach (var mode in Enum.GetValues<GuardianAlignmentMode>())
        {
            var control = new GuardianAlignmentControl
            {
                Mode = mode,
                GuideBrush = Brushes.Gold,
                ShadowBrush = Brushes.Black,
            };
            var window = new Window
            {
                Width = 600,
                Height = 600,
                Content = control,
            };

            try
            {
                window.Show();
                var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(new PixelSize(600, 600), frame.PixelSize);
                if (mode >= GuardianAlignmentMode.Bear)
                {
                    using var stream = new MemoryStream();
                    frame.Save(stream, PngBitmapEncoderOptions.Default);
                    structureHashes.Add(Convert.ToHexString(
                        SHA256.HashData(stream.ToArray())));
                }
            }
            finally
            {
                window.Close();
            }
        }

        Assert.True(structureHashes.Count >= 6);
    }
}
