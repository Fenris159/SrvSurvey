using System.Net;
using System.Text.Json.Nodes;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Network;

public sealed partial class JournalUploadScenarioTests
{
    [Fact]
    public async Task EdAstroAggregateAcceptanceDoesNotClaimIndividualSurveySuccess()
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.2026-10-12T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            """{"event":"LoadGame","timestamp":"2026-10-12T12:00:00Z","Commander":"Explorer","FID":"F123","Odyssey":true}"""
                + "\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        const string scan =
            """{"event":"ScanOrganic","timestamp":"2026-10-12T12:00:02Z","ScanType":"Sample","Genus":"$Genus_Name;","Species":"$Species_Name;","SystemAddress":10477373803,"Body":4}""";
        await File.AppendAllTextAsync(
            journal,
            """{"event":"Docked","timestamp":"2026-10-12T12:00:01Z","StarSystem":"Sol","StationName":"Galileo","MarketID":128666762}"""
                + "\n"
                + scan
                + "\n"
        );
        var first = new ProviderHandler("EDAstro", HttpStatusCode.OK)
        {
            EdAstroEvents = "[\"Docked\",\"ScanOrganic\"]",
        };
        using (var client = new HttpClient(first))
        {
            await Run("EDAstro", client, bootstrap, await monitor.PollAsync());
        }
        Assert.Equal(3, JsonNode.Parse(Assert.Single(first.Posts))!.AsArray().Count);

        clock.Now += TimeSpan.FromHours(1);
        var restarted = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate recovered = await restarted.PollAsync();
        await File.AppendAllTextAsync(journal, scan + "\n");
        var next = new ProviderHandler("EDAstro", HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using (var client = new HttpClient(next))
        {
            await Run("EDAstro", client, recovered, await restarted.PollAsync());
        }
        JsonArray submitted = JsonNode.Parse(Assert.Single(next.Posts))!.AsArray();
        Assert.Equal(2, submitted.Count);
        Assert.Equal("ScanOrganic", submitted[1]!["event"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(500)]
    [InlineData(501)]
    public async Task EdsmProcessingFailuresDoNotBecomeAcceptedContentReceipts(int eventStatus)
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.2026-10-12T120000.01.log");
        await File.WriteAllTextAsync(
            journal,
            """{"event":"LoadGame","timestamp":"2026-10-12T12:00:00Z","Commander":"Explorer","FID":"F123","Odyssey":true}"""
                + "\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        const string observation =
            """{"event":"FSDJump","timestamp":"2026-10-12T12:00:01Z","StarSystem":"Sol","SystemAddress":10477373803,"StarPos":[0,0,0]}""";
        await File.AppendAllTextAsync(journal, observation + "\n");
        var first = new ProviderHandler("EDSM", HttpStatusCode.OK) { EdsmEventStatus = eventStatus };
        using (var client = new HttpClient(first))
        {
            await Run("EDSM", client, bootstrap, await monitor.PollAsync());
        }
        Assert.Equal(1, ObservationCount("EDSM", Assert.Single(first.Posts)));

        clock.Now += TimeSpan.FromHours(1);
        var restarted = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate recovered = await restarted.PollAsync();
        await File.AppendAllTextAsync(journal, observation + "\n");
        var next = new ProviderHandler("EDSM", HttpStatusCode.OK);
        using (var client = new HttpClient(next))
        {
            await Run("EDSM", client, recovered, await restarted.PollAsync());
        }
        Assert.Equal(1, ObservationCount("EDSM", Assert.Single(next.Posts)));
    }
}
