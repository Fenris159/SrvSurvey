using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Localization;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class CommanderProfileViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PowerplayRankAndTimestampedSourceAreFullyTranslatable(bool fromLocal)
    {
        string language = LocalizationCatalog.CurrentLanguage;
        try
        {
            LocalizationCatalog.Initialize("de");
            FrontierAccountSnapshot snapshot = CreateSnapshot(DateTimeOffset.UnixEpoch) with
            {
                Ranks = [],
                Powerplay = new("Aisling Duval", 160, 500, DateTimeOffset.UnixEpoch),
            };
            using var model = new CommanderProfileViewModel(
                new StubAccountService(new FrontierAccountState(true, snapshot, snapshot.FetchedAt))
            );
            await model.SetCommanderContextAsync("F123", "Fenris", refreshIfOpen: true);
            if (fromLocal)
            {
                model.UpdateJournalPowerplay(
                    "F123",
                    [
                        ParseJournalEvent(
                            """{"timestamp":"2026-10-23T03:00:00Z","event":"PowerplayRank","Power":"Aisling Duval","Rank":161}"""
                        ),
                    ]
                );
            }
            FrontierRankCardViewModel card = Assert.Single(model.Ranks);
            string translatedSource = LocalizationCatalog.Translate(card.Source);

            Assert.NotEqual(card.Source, translatedSource);
            Assert.EndsWith(card.Source.Split(" · ", 2)[1], translatedSource);
            Assert.NotEqual(card.RankText, LocalizationCatalog.Translate(card.RankText));
            Assert.NotEqual(card.MeritsText, LocalizationCatalog.Translate(card.MeritsText));
        }
        finally
        {
            LocalizationCatalog.Initialize(language);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task JournalRotationDoesNotApplyThePreviousCommandersPendingPowerplayEvent(bool hasNewRank)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-powerplay-rotation-{Guid.NewGuid():N}");
        string journals = Path.Combine(root, "journals");
        Directory.CreateDirectory(journals);
        var fetchedAt = DateTimeOffset.Parse(
            "2026-10-22T12:00:00Z",
            global::System.Globalization.CultureInfo.InvariantCulture
        );
        FrontierAccountSnapshot first = CreateSnapshot(fetchedAt) with
        {
            FetchedAt = DateTimeOffset.UtcNow,
            Ranks = [],
            Powerplay = new("A. Lavigny-Duval", 160, 1000, fetchedAt),
        };
        FrontierAccountSnapshot second = first with
        {
            CommanderName = "Second",
            CommanderId = 456,
            Powerplay = new("Aisling Duval", 80, 500, fetchedAt),
        };
        var account = new StubAccountService(new FrontierAccountState(true, first, fetchedAt));
        account.SetStateForCommander("F123", new FrontierAccountState(true, first, fetchedAt));
        account.SetStateForCommander("F456", new FrontierAccountState(true, second, fetchedAt));
        using var profile = new CommanderProfileViewModel(account);
        try
        {
            string firstJournal = Path.Combine(journals, "Journal.2026-10-23T010000.01.log");
            await File.WriteAllTextAsync(
                firstJournal,
                """
                {"timestamp":"2026-10-23T01:00:00Z","event":"Fileheader"}
                {"timestamp":"2026-10-23T01:00:01Z","event":"Commander","Name":"Fenris","FID":"F123"}
                {"timestamp":"2026-10-23T01:00:02Z","event":"LoadGame","Commander":"Fenris","FID":"F123"}

                """
            );
            using MainWindowViewModel main = MainWindowViewModelTestBuilder.Create(
                journals,
                builder =>
                    builder
                        .WithAppDataPaths(
                            new AppDataPaths(
                                Path.Combine(root, "config"),
                                Path.Combine(root, "profile"),
                                Path.Combine(root, "cache"),
                                []
                            )
                        )
                        .WithFrontierProfile(profile)
            );
            await main.RefreshAsync();
            await main.ShowProfileAsync();
            Assert.Equal("F123", account.ActiveFrontierId);
            await File.AppendAllTextAsync(
                firstJournal,
                """{"timestamp":"2026-10-23T02:00:00Z","event":"PowerplayLeave"}"""
            );
            await main.RefreshAsync();
            Assert.Equal("A. Lavigny-Duval", profile.Ranks.Single(rank => rank.Category == "Powerplay").Name);

            string secondJournal = Path.Combine(journals, "Journal.2026-10-23T030000.01.log");
            await File.WriteAllTextAsync(
                secondJournal,
                """
                {"timestamp":"2026-10-23T03:00:00Z","event":"Fileheader"}
                {"timestamp":"2026-10-23T03:00:01Z","event":"Commander","Name":"Second","FID":"F456"}
                {"timestamp":"2026-10-23T03:00:02Z","event":"LoadGame","Commander":"Second","FID":"F456"}

                """
            );
            if (hasNewRank)
            {
                await File.AppendAllTextAsync(
                    secondJournal,
                    """{"timestamp":"2026-10-23T03:00:03Z","event":"PowerplayRank","Power":"Aisling Duval","Rank":81}"""
                        + "\n"
                );
            }
            File.SetLastWriteTimeUtc(secondJournal, DateTime.UtcNow.AddMinutes(1));
            await main.RefreshAsync();
            await main.ShowProfileAsync();

            Assert.Equal("F456", account.ActiveFrontierId);
            Assert.Equal(0, account.RefreshCount);
            FrontierRankCardViewModel card = profile.Ranks.Single(rank => rank.Category == "Powerplay");
            Assert.Equal("Aisling Duval", card.Name);
            Assert.Equal(hasNewRank ? 81 : 80, card.Level);
            Assert.Equal("500 merits", card.MeritsText);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
