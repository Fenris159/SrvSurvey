using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class MultiCommanderJournalUploadTests
{
    /// <summary>Real projection senders hold discoveries, release earlier evidence, and keep docking-second records private.</summary>
    [Theory]
    [InlineData("EDDN")]
    [InlineData("Inara")]
    [InlineData("EDSM")]
    [InlineData("EDAstro")]
    public async Task StealthCheckboxHoldsRealProjectionUploadsUntilALiveDocking(string provider)
    {
        string root = Path.Combine(Path.GetTempPath(), "stealth-journal-" + Guid.NewGuid().ToString("N"));
        var paths = new AppDataPaths(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache"),
            []
        );
        try
        {
            string folder = await CreateJournalAsync(root, "Steam", "F1", "First");
            string peerFolder = await CreateJournalAsync(root, "Epic", "F2", "Second");
            await SaveProfilesAsync(paths);
            using var sender = new Sender(provider, root);
            sender.Handler.EdAstroEvents = "[\"Docked\",\"ScanOrganic\"]";
            await using MainWindowViewModel main = Create(folder, "F1", paths, sender);
            using var peerSender = new Sender(provider, root);
            await using MainWindowViewModel peer = Create(peerFolder, "F2", paths, peerSender);
            main.NetworkPrivacy.StealthModeEnabled = true;
            await main.RefreshAsync();
            await peer.RefreshAsync();
            await main.RefreshAsync();
            string journal = Path.Combine(folder, "Journal.F1.log");
            string discovery = provider == "EDAstro" ? "ScanOrganic" : "FSDJump";
            await File.AppendAllTextAsync(
                journal,
                $$"""{"timestamp":"2026-10-14T12:00:04Z","event":"{{discovery}}","StarSystem":"Private expedition","SystemAddress":42,"StarPos":[0,0,0],"JumpDist":5} """
                    + "\n"
            );
            await main.RefreshAsync();
            await sender.FlushAsync(false);
            Assert.Empty(sender.Handler.Posts);
            await File.AppendAllTextAsync(
                journal,
                """{"timestamp":"2026-10-14T12:00:30Z","event":"Docked","StarSystem":"Safe port system","StationName":"Safe port","MarketID":123} """
                    + "\n"
            );
            await main.RefreshAsync();
            await sender.FlushAsync(false);
            Assert.Contains(
                sender.Handler.Posts,
                body => body.Contains("Private expedition", StringComparison.Ordinal)
            );
            sender.Handler.Posts.Clear();
            await File.AppendAllTextAsync(
                journal,
                $$"""{"timestamp":"2026-10-14T12:00:30Z","event":"{{discovery}}","StarSystem":"Private next expedition","SystemAddress":43,"StarPos":[1,0,0],"JumpDist":1} """
                    + "\n"
            );
            await main.RefreshAsync();
            await sender.FlushAsync(false);
            Assert.DoesNotContain(
                sender.Handler.Posts,
                body => body.Contains("Private next expedition", StringComparison.Ordinal)
            );
            await File.AppendAllTextAsync(
                journal,
                """{"timestamp":"2026-10-14T12:00:31Z","event":"Docked","StarSystem":"Safe port system","StationName":"Safe port","MarketID":123} """
                    + "\n"
            );
            await main.RefreshAsync();
            await sender.FlushAsync(false);
            Assert.Contains(
                sender.Handler.Posts,
                body => body.Contains("Private next expedition", StringComparison.Ordinal)
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
