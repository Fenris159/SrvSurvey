using SrvSurvey.Core.Exploration;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Search;
using SrvSurvey.Desktop.Configuration;
using SrvSurvey.Desktop.Platform;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed class NotificationViewModelTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-notification-vm-tests-{Guid.NewGuid():N}");

    [Fact]
    public void JournalNotificationsUseLiveStateAndNeverReplayBootstrapMessages()
    {
        var time = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero));
        var viewModel = CreateViewModel(time);
        var materials = Parse(
            """
            {"event":"Materials","Raw":[],"Manufactured":[],"Encoded":[{"Name":"ancienttechnologicaldata","Name_Localised":"Pattern Epsilon Obelisk Data","Count":4}]}
            """);
        var collected = Parse(
            """
            {"event":"MaterialCollected","Category":"Encoded","Name":"ancienttechnologicaldata","Name_Localised":"Pattern Epsilon Obelisk Data","Count":3}
            """);
        var cargo = Parse(
            """
            {"event":"CargoDepot","UpdateType":"Deliver","CargoType":"Bertrandite","ItemsDelivered":736,"TotalItemsToDeliver":912}
            """);

        viewModel.ApplyJournalEvents([materials, collected, cargo], false);
        Assert.Empty(viewModel.Messages);

        viewModel.ApplyJournalEvents([collected, cargo], true);

        Assert.Contains(
            viewModel.Messages,
            message => message.Text
                == "Collected: 3x Pattern Epsilon Obelisk Data, new total 10");
        Assert.Contains(
            viewModel.Messages,
            message => message.Text
                == "Deliver Bertrandite: 176 units remaining");
        Assert.True(viewModel.ShouldShow);
        Assert.Equal(100, viewModel.ProgressPercent);

        time.Advance(TimeSpan.FromSeconds(6));
        viewModel.Refresh();

        Assert.Empty(viewModel.Messages);
        Assert.False(viewModel.ShouldShow);
    }

    [Fact]
    public void DuplicateMessagesResetTheirLifetimeWithoutAddingRows()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var viewModel = CreateViewModel(time);

        viewModel.ShowMessage("same");
        time.Advance(TimeSpan.FromSeconds(5));
        viewModel.ShowMessage("same");

        Assert.Equal("same", Assert.Single(viewModel.Messages).Text);
        Assert.Equal(100, viewModel.ProgressPercent);
        time.Advance(TimeSpan.FromSeconds(2));
        viewModel.Refresh();
        Assert.Single(viewModel.Messages);
    }

    [Fact]
    public void BoxelScreenshotUploadAndBannerMessagesMatchLegacyWording()
    {
        var viewModel = CreateViewModel(new MutableTimeProvider(
            DateTimeOffset.UtcNow));
        var before = new BoxelSearchNotificationState(
            true,
            BoxelCompletionMode.FssAllBodies,
            2,
            4,
            false,
            "Synuefe AA-A b1-2");
        var after = before with
        {
            CompletedSystems = 4,
            CurrentSystemsComplete = true,
            NextSystem = "Synuefe AA-A b1-3",
        };

        viewModel.ReportBoxelUpdate(before, after, true, true);
        viewModel.ReportScreenshotResult(
            new ScreenshotProcessingResult(
                [new ScreenshotConversion(
                    "source.bmp",
                    Path.Combine("target", "shot.png"),
                    false,
                    null)],
                []),
            includedBanner: false);
        viewModel.ReportGreenGasGiantUploads(
            new GreenGasGiantPublicationResult(
                [new GreenGasGiantCandidate(
                    "Drew",
                    "GGG #1",
                    new SrvSurvey.Core.Search.GalacticCoordinate(1, 2, 3),
                    "{}")],
                []));
        viewModel.ShowBannerPreference(true);

        Assert.Contains(
            viewModel.Messages,
            message => message.Text == "Current boxel 100% searched.");
        Assert.Contains(
            viewModel.Messages,
            message => message.Text
                == "Next boxel to search: Synuefe AA-A b1-3");
        Assert.Contains(
            viewModel.Messages,
            message => message.Text == "Saved 'shot.png' with no banner");
        Assert.Contains(
            viewModel.Messages,
            message => message.Text == "Congrats, GGG #1 GGG uploaded!");
        Assert.Contains(
            viewModel.Messages,
            message => message.Text
                == "Adding embedded banner to future screenshots");
    }

    private NotificationViewModel CreateViewModel(TimeProvider timeProvider)
    {
        Directory.CreateDirectory(temporaryDirectory);
        return new NotificationViewModel(
            new NotificationSettingsStore(Path.Combine(
                temporaryDirectory,
                "ui-settings.json")),
            timeProvider);
    }

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(
            json,
            out var journalEvent,
            out var error), error);
        return journalEvent!;
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;

        public void Advance(TimeSpan duration)
        {
            value += duration;
        }
    }
}
