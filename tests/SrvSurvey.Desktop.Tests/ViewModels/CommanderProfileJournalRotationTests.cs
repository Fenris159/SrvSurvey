using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Frontier;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.Platform.Frontier;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class CommanderProfileViewModelTests
{
    /// <summary>Verifies real journal rotation cannot apply a previous commander's pending profile event to the newly selected commander.</summary>
    [AvaloniaTheory]
    [InlineData("Reputation")]
    [InlineData("CommunityGoal")]
    [InlineData("CarrierJumpRequest")]
    public async Task JournalRotationDoesNotLeakThePreviousCommandersPendingProfileEvent(string eventName)
    {
        string root = Path.Combine(Path.GetTempPath(), $"SrvSurvey-profile-rotation-{Guid.NewGuid():N}");
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
            CommanderReputation = [new("Federation", 50)],
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
                ReconciliationEvents(1)
                    .Single(entry => entry.EventName == eventName)
                    .RawJson.Replace(
                        "1970-01-01T00:00:01.0000000+00:00",
                        "2026-10-23T02:00:00Z",
                        StringComparison.Ordinal
                    )
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
            File.SetLastWriteTimeUtc(secondJournal, DateTime.UtcNow.AddMinutes(1));
            await main.RefreshAsync();
            await main.ShowProfileAsync();

            Assert.Equal("F456", account.ActiveFrontierId);
            Assert.Equal(0, account.RefreshCount);
            FrontierRankCardViewModel card = profile.Ranks.Single(rank => rank.Category == "Powerplay");
            Assert.Equal("Aisling Duval", card.Name);
            Assert.Equal(80, card.Level);
            Assert.Equal("50%", Assert.Single(profile.CommanderReputation).Score);
            Assert.Equal("250 contributed", Assert.Single(profile.CommunityGoals).PlayerContribution);
            Assert.Equal("None plotted", profile.CarrierOperations.Single(row => row.Label == "Current jump").Value);
            Assert.Equal("500 merits", card.MeritsText);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
