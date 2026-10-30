using System.Globalization;
using SrvSurvey.Core.Journeys;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.ViewModels;

public sealed partial class JourneyWorkspaceViewModel
{
    public async Task SaveHtmlReportAsync(JourneyHtmlExport report, string destination)
    {
        if (IsBusy)
        {
            return;
        }
        int version = contextVersion;
        try
        {
            IsBusy = true;
            StatusMessage = "Copying report images and preparing previews...";
            string folder = await JourneyReportBundleExporter.SaveAsync(report, destination);
            if (version == contextVersion)
            {
                StatusMessage =
                    $"Journey report exported to {folder}. Open index.html to view it, and share the entire folder to include its images.";
            }
        }
        catch (Exception exception) when (IsExpectedException(exception))
        {
            if (version == contextVersion)
            {
                StatusMessage = "The journey report could not be saved: " + exception.Message;
            }
        }
        finally
        {
            if (version == contextVersion)
            {
                IsBusy = false;
            }
        }
    }

    private Dictionary<long, string?> CaptureReportImageFolders(JourneyDocument document) =>
        document
            .VisitedSystems.Select(visit => visit.StarSystem)
            .DistinctBy(system => system.SystemAddress)
            .ToDictionary(system => system.SystemAddress, system => screenshotDirectoryProvider(system.Name));

    private static JourneyReportImages CaptureReportImages(IReadOnlyDictionary<long, string?> folders)
    {
        var assets = new List<JourneyReportAsset>();
        var bySystem = new Dictionary<long, IReadOnlyList<JourneyReportImage>>();
        foreach ((long address, string? folder) in folders)
        {
            string[] files = GetScreenshotFiles(folder);
            var images = new List<JourneyReportImage>();
            for (int index = 0; index < files.Length; index++)
            {
                string prefix =
                    $"images/{address.ToString(CultureInfo.InvariantCulture)}/{(index + 1).ToString("D4", CultureInfo.InvariantCulture)}";
                var image = new JourneyReportImage(
                    Path.GetFileName(files[index]),
                    prefix + ".png",
                    prefix + "-preview.png"
                );
                images.Add(image);
                assets.Add(new(files[index], image));
            }
            bySystem[address] = images.ToArray();
        }
        return new(assets.ToArray(), bySystem);
    }

    private sealed record JourneyReportImages(
        IReadOnlyList<JourneyReportAsset> Assets,
        IReadOnlyDictionary<long, IReadOnlyList<JourneyReportImage>> BySystem
    );
}
