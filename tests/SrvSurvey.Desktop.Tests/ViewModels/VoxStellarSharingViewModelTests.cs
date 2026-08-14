using SrvSurvey.Core.Network;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class VoxStellarSharingViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SrvSurvey-VoxStellarViewModel-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void JournalSharingIsOptInAndPersistsImmediately()
    {
        var viewModel = CreateViewModel(isAvailable: true);
        var changes = new List<bool>();
        viewModel.UploadEnabledChanged += changes.Add;

        Assert.False(viewModel.JournalUploadEnabled);
        viewModel.JournalUploadEnabled = true;

        Assert.True(CreateViewModel(isAvailable: true).JournalUploadEnabled);
        Assert.Equal([true], changes);
    }

    [Fact]
    public void MissingIntegrationKeyIsVisibleBeforeOptIn()
    {
        var viewModel = CreateViewModel(isAvailable: false);

        Assert.False(viewModel.IsUploadAvailable);
        Assert.False(viewModel.CanChangeUploadPreference);
        Assert.Contains("signing key", viewModel.StatusMessage);

        viewModel.JournalUploadEnabled = true;

        Assert.False(viewModel.JournalUploadEnabled);
    }

    [Fact]
    public void QueuedEventsAndWarningsAreReportedWithoutPayloadData()
    {
        var viewModel = CreateViewModel(isAvailable: true);

        viewModel.ReportPublicationResult(new VoxStellarPublicationResult(
            ["Scan", "FSDJump"],
            []));
        Assert.Equal(
            "Queued 2 exploration events for VoxStellar.",
            viewModel.StatusMessage);

        viewModel.ReportPublicationResult(new VoxStellarPublicationResult(
            [],
            ["simulated warning"]));
        Assert.Equal("simulated warning", viewModel.StatusMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private VoxStellarSharingViewModel CreateViewModel(bool isAvailable) => new(
        new VoxStellarSettingsStore(Path.Combine(
            temporaryDirectory,
            "ui-settings.json")),
        isAvailable);
}
