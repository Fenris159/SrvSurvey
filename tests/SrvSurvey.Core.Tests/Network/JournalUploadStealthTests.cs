using System.Globalization;
using System.Net;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

public sealed partial class JournalUploadScenarioTests
{
    [Theory]
    [InlineData("EDDN")]
    [InlineData("Inara")]
    [InlineData("EDSM")]
    [InlineData("EDAstro")]
    public async Task StealthHoldsAcrossRestartAndOnlyReleasesTheDockedExpedition(string provider)
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.stealth.log");
        await File.WriteAllTextAsync(
            journal,
            """
            {"timestamp":"2026-10-12T12:00:00Z","event":"Fileheader","Odyssey":true,"gameversion":"4.1.0.100"}
            {"timestamp":"2026-10-12T12:00:00Z","event":"LoadGame","Commander":"Explorer","FID":"F123","Odyssey":true}
            """ + "\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        string captured = provider == "EDAstro" ? "ScanOrganic" : "FSDJump";
        await File.AppendAllTextAsync(
            journal,
            $$"""{"timestamp":"2026-10-12T12:00:01Z","event":"{{captured}}","StarSystem":"Secret first discovery","SystemAddress":42,"StarPos":[0,0,0],"JumpDist":12.5} """
                + "\n"
        );
        JournalMonitorUpdate live = await monitor.PollAsync();
        string policyPath = Path.Combine(directory, "stealth.json");
        uploadPrivacy = new(policyPath, true);
        uploadPrivacy.Observe(bootstrap.JournalEvents, false);
        var blocked = new ProviderHandler(provider, HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using (var http = new HttpClient(blocked))
        {
            await Run(provider, http, bootstrap, live);
        }
        Assert.Empty(blocked.Posts);

        clock.Now += TimeSpan.FromHours(1);
        uploadPrivacy = new(policyPath, true);
        JournalMonitorUpdate reopened = await new JournalDirectoryMonitor(directory, "F123").PollAsync();
        uploadPrivacy.Observe(reopened.JournalEvents, false);
        var stillBlocked = new ProviderHandler(provider, HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using (var http = new HttpClient(stillBlocked))
        {
            await Run(provider, http, reopened, null);
        }
        Assert.Empty(stillBlocked.Posts);
        uploadPrivacy.Observe([StealthEvent("Docked", 2, "\"StationName\":\"Safe port\"")], true);
        var released = new ProviderHandler(provider, HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using (var http = new HttpClient(released))
        {
            await Run(provider, http, reopened, null);
        }
        Assert.Contains(released.Posts, body => body.Contains("Secret first discovery", StringComparison.Ordinal));

        clock.Now += TimeSpan.FromHours(1);
        await File.AppendAllTextAsync(
            journal,
            $$"""{"timestamp":"2026-10-12T12:00:03Z","event":"{{captured}}","StarSystem":"Secret next expedition","SystemAddress":43,"StarPos":[1,0,0],"JumpDist":1} """
                + "\n"
        );
        var next = new ProviderHandler(provider, HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using (var http = new HttpClient(next))
        {
            await Run(provider, http, bootstrap, await monitor.PollAsync());
        }
        Assert.DoesNotContain(next.Posts, body => body.Contains("Secret next expedition", StringComparison.Ordinal));
        uploadPrivacy.SetEnabled(false);
        clock.Now += TimeSpan.FromHours(1);
        var resumed = new ProviderHandler(provider, HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using (var http = new HttpClient(resumed))
        {
            await Run(provider, http, reopened, null);
        }
        Assert.Contains(resumed.Posts, body => body.Contains("Secret next expedition", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("EDDN")]
    [InlineData("Inara")]
    [InlineData("EDSM")]
    [InlineData("EDAstro")]
    public async Task EnablingStealthCancelsAnActiveRequestAndKeepsItsDurableReports(string provider)
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.cancel-stealth.log");
        await File.WriteAllTextAsync(
            journal,
            """{"timestamp":"2026-10-12T12:00:00Z","event":"LoadGame","Commander":"Explorer","FID":"F123","Odyssey":true}"""
                + "\n"
        );
        var monitor = new JournalDirectoryMonitor(directory, "F123");
        JournalMonitorUpdate bootstrap = await monitor.PollAsync();
        string name = provider == "EDAstro" ? "ScanOrganic" : "FSDJump";
        await File.AppendAllTextAsync(
            journal,
            $$"""{"timestamp":"2026-10-12T12:00:01Z","event":"{{name}}","StarSystem":"Unsent discovery","SystemAddress":42,"StarPos":[0,0,0],"JumpDist":5}"""
                + "\n"
        );
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uploadPrivacy = new(Path.Combine(directory, "stealth.json"), false);
        var handler = new ProviderHandler(provider, HttpStatusCode.OK)
        {
            EdAstroEvents = "[\"ScanOrganic\"]",
            BeforePost = async token =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };
        using (var http = new HttpClient(handler))
        {
            Task pending = Run(provider, http, bootstrap, await monitor.PollAsync());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            uploadPrivacy.SetEnabled(true);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(handler.Posts);
        }
        clock.Now += TimeSpan.FromHours(1);
        uploadPrivacy.Observe(bootstrap.JournalEvents, false);
        uploadPrivacy.Observe([StealthEvent("Docked", 2, "\"StationName\":\"Port\"")], true);
        var released = new ProviderHandler(provider, HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using var recovery = new HttpClient(released);
        await Run(provider, recovery, bootstrap, null);
        Assert.Contains(released.Posts, body => body.Contains("Unsent discovery", StringComparison.Ordinal));
    }

    private static JournalEventEnvelope StealthEvent(string name, int second, string fields)
    {
        Assert.True(
            JournalEventEnvelope.TryParse(
                $$"""{"timestamp":"2026-10-12T12:00:{{second.ToString("00", CultureInfo.InvariantCulture)}}Z","event":"{{name}}",{{fields}}} """,
                out JournalEventEnvelope? entry,
                out _
            )
        );
        return entry!;
    }
}
