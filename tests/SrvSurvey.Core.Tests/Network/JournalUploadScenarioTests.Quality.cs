using System.Net;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Network;

public sealed partial class JournalUploadScenarioTests
{
    [Theory]
    [InlineData("EDSM")]
    [InlineData("EDAstro")]
    [InlineData("VoxStellar")]
    public async Task IdenticalObservationsAcrossNewRecordsAndRestartAreUploadedOnce(string provider)
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.2026-10-12T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"event\":\"LoadGame\",\"timestamp\":\"2026-10-12T12:00:00Z\",\"Commander\":\"Explorer\",\"FID\":\"F123\",\"Odyssey\":true}\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        string observation =
            provider == "EDAstro"
                ? "{\"event\":\"ScanOrganic\",\"timestamp\":\"2026-10-12T12:00:01Z\",\"ScanType\":\"Sample\",\"Genus\":\"$Genus_Name;\",\"Species\":\"$Species_Name;\",\"SystemAddress\":10477373803,\"Body\":4}\n"
                : "{\"event\":\"FSDJump\",\"timestamp\":\"2026-10-12T12:00:01Z\",\"StarSystem\":\"Sol\",\"SystemAddress\":10477373803,\"StarPos\":[0,0,0]}\n";
        await File.AppendAllTextAsync(journal, observation + observation);
        JournalMonitorUpdate live = await monitor.PollAsync();
        var first = new ProviderHandler(provider, HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using (var client = new HttpClient(first))
        {
            await Run(provider, client, bootstrap, live);
        }
        Assert.Equal(1, ObservationCount(provider, Assert.Single(first.Posts)));

        clock.Now += TimeSpan.FromHours(1);
        var restartedMonitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate restarted = await restartedMonitor.PollAsync();
        await File.AppendAllTextAsync(journal, observation);
        JournalMonitorUpdate duplicate = await restartedMonitor.PollAsync();
        var second = new ProviderHandler(provider, HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using (var client = new HttpClient(second))
        {
            await Run(provider, client, restarted, duplicate);
        }
        Assert.Empty(second.Posts);

        clock.Now += TimeSpan.FromHours(1);
        await File.AppendAllTextAsync(journal, observation.Replace("12:00:01", "12:00:02", StringComparison.Ordinal));
        JournalMonitorUpdate changed = await restartedMonitor.PollAsync();
        var third = new ProviderHandler(provider, HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using (var client = new HttpClient(third))
        {
            await Run(provider, client, restarted, changed);
        }
        Assert.Equal(1, ObservationCount(provider, Assert.Single(third.Posts)));
    }

    [Fact]
    public async Task EdsmKeepsDistinctIdenticalPersonalTransactions()
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.2026-10-12T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            "{\"event\":\"LoadGame\",\"timestamp\":\"2026-10-12T12:00:00Z\",\"Commander\":\"Explorer\",\"FID\":\"F123\",\"Odyssey\":true}\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        const string trade =
            "{\"event\":\"MarketSell\",\"timestamp\":\"2026-10-12T12:00:01Z\",\"Type\":\"gold\",\"Count\":2,\"SellPrice\":1000}\n";
        await File.AppendAllTextAsync(journal, trade + trade);
        var handler = new ProviderHandler("EDSM", HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        await Run("EDSM", client, bootstrap, await monitor.PollAsync());
        Assert.Equal(2, ObservationCount("EDSM", Assert.Single(handler.Posts)));
    }

    private static int ObservationCount(string provider, string body) =>
        provider switch
        {
            "EDSM" => JsonNode.Parse(body[(body.IndexOf("message=", StringComparison.Ordinal) + 8)..])!.AsArray().Count,
            "EDAstro" => JsonNode.Parse(body)!.AsArray().Count - 1,
            "VoxStellar" => JsonNode.Parse(body)!["data"]!["event"]!.GetValue<string>() == "FSDJump" ? 1 : 0,
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
}
