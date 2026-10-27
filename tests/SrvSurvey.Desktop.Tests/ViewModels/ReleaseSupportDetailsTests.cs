using System.Runtime.InteropServices;
using SrvSurvey.Core.Updates;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class ReleaseSupportDetailsTests
{
    [Fact]
    public async Task CopyContainsTheInstalledRcAndSystemDetailsWithoutChangingUpdateStatus()
    {
        ReleaseUpdateViewModel model = Create();
        string status = model.StatusMessage;
        string? copied = null;
        var notifications = new List<string?>();
        model.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        Assert.False(model.CopySupportDetailsCommand.CanExecute(null));
        Assert.False(model.HasSupportDetailsStatusMessage);
        model.SetClipboardWriter(text =>
        {
            copied = text;
            return Task.CompletedTask;
        });

        Assert.True(model.CopySupportDetailsCommand.CanExecute(null));
        model.CopySupportDetailsCommand.Execute(null);

        Assert.Equal(model.SupportDetails, copied);
        Assert.Contains("SrvSurvey-XP: 2.1.3.0-rc.73", copied);
        Assert.Contains("Release channel: development", copied);
        Assert.Contains($"OS: {RuntimeInformation.OSDescription}", copied);
        Assert.Contains($"OS architecture: {RuntimeInformation.OSArchitecture}", copied);
        Assert.Contains($"App architecture: {RuntimeInformation.ProcessArchitecture}", copied);
        Assert.Contains($"Runtime: {RuntimeInformation.FrameworkDescription}", copied);
        Assert.Equal(6, copied!.Split(Environment.NewLine).Length);
        Assert.Equal(status, model.StatusMessage);
        Assert.Contains("copied to the clipboard", model.SupportDetailsStatusMessage);
        Assert.True(model.HasSupportDetailsStatusMessage);
        Assert.Contains(nameof(model.HasSupportDetailsStatusMessage), notifications);

        model.SetClipboardWriter(null);
        Assert.False(model.CopySupportDetailsCommand.CanExecute(null));
        await model.CopySupportDetailsAsync();
        Assert.Contains("unavailable", model.SupportDetailsStatusMessage);
    }

    [Fact]
    public async Task CopiedDetailsFollowTheSelectedChannel()
    {
        ReleaseUpdateViewModel model = Create();
        model.UseDevelopmentReleases = false;
        string? copied = null;
        model.SetClipboardWriter(text =>
        {
            copied = text;
            return Task.CompletedTask;
        });

        await model.CopySupportDetailsAsync();

        Assert.Contains("Release channel: stable", copied);
        Assert.Contains("SrvSurvey-XP: 2.1.3.0-rc.73", copied);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("io")]
    [InlineData("unsupported")]
    [InlineData("external")]
    [InlineData("access")]
    public async Task ClipboardFailuresAreReportedWithoutDisruptingUpdates(string failure)
    {
        ReleaseUpdateViewModel model = Create();
        string status = model.StatusMessage;
        Exception exception = failure switch
        {
            "io" => new IOException("clipboard busy"),
            "unsupported" => new NotSupportedException("clipboard busy"),
            "external" => new System.ComponentModel.Win32Exception("clipboard busy"),
            "access" => new UnauthorizedAccessException("clipboard busy"),
            _ => new InvalidOperationException("clipboard busy"),
        };
        model.SetClipboardWriter(_ => Task.FromException(exception));

        await model.CopySupportDetailsAsync();

        Assert.Contains("could not be copied: clipboard busy", model.SupportDetailsStatusMessage);
        Assert.Equal(status, model.StatusMessage);
        Assert.True(model.CheckCommand.CanExecute(null));
    }

    [Fact]
    public async Task InstalledNotesAreAvailableOfflineAndIndependentOfFutureReleaseNotesAndChannels()
    {
        ReleaseUpdateViewModel model = Create();
        string installedNotes = ReleaseUpdateViewModel.InstalledReleaseNotes;
        Assert.StartsWith("# SrvSurvey-XP ", installedNotes);
        Assert.Contains("## What's changed", installedNotes);
        Assert.False(model.IsUpdateAvailable);

        await model.CheckAsync();

        Assert.True(model.IsUpdateAvailable);
        Assert.Contains("Future release change", model.ReleaseNotes);
        Assert.Equal(installedNotes, ReleaseUpdateViewModel.InstalledReleaseNotes);
        Assert.DoesNotContain("Future release change", ReleaseUpdateViewModel.InstalledReleaseNotes);

        model.UseDevelopmentReleases = false;
        Assert.Equal(installedNotes, ReleaseUpdateViewModel.InstalledReleaseNotes);

        var offline = new ReleaseUpdateViewModel(new OfflineService(), ReleaseVersion.Parse("2.1.3.0-rc.73"));
        await offline.CheckAsync();
        Assert.Equal("Unavailable", offline.LatestVersion);
        Assert.False(offline.HasReleaseNotes);
        Assert.Equal(installedNotes, ReleaseUpdateViewModel.InstalledReleaseNotes);
    }

    private static ReleaseUpdateViewModel Create() =>
        new(new FutureReleaseService(), ReleaseVersion.Parse("2.1.3.0-rc.73"));

    private sealed class FutureReleaseService : IReleaseUpdateService
    {
        public Task<ReleaseUpdateResult> CheckAsync(
            ReleaseVersion currentVersion,
            ReleaseChannel channel,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new ReleaseUpdateResult(
                    currentVersion,
                    ReleaseVersion.Parse("2.1.3.0-rc.74"),
                    true,
                    new Uri("https://example.test/releases/next"),
                    null,
                    channel,
                    "# Next release\n\n## What's changed\n\n- Future release change"
                )
            );
    }

    private sealed class OfflineService : IReleaseUpdateService
    {
        public Task<ReleaseUpdateResult> CheckAsync(
            ReleaseVersion currentVersion,
            ReleaseChannel channel,
            CancellationToken cancellationToken = default
        ) => Task.FromException<ReleaseUpdateResult>(new HttpRequestException("offline"));
    }
}
