using System.Net;
using Newtonsoft.Json.Linq;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

public sealed partial class JournalUploadScenarioTests
{
    /// <summary>Freed dictionary slots cannot move a held report ahead of released work after a real durable restart.</summary>
    [Theory]
    [InlineData("EDSM")]
    [InlineData("Inara")]
    [InlineData("EDAstro")]
    public async Task RecoveredReleasedReportsAreDeliveredBeforeLaterHeldReports(string provider)
    {
        Directory.CreateDirectory(directory);
        string journal = Path.Combine(directory, "Journal.recovery.log");
        await File.WriteAllTextAsync(
            journal,
            """
            {"timestamp":"2026-10-12T12:00:00Z","event":"Fileheader","Odyssey":true,"gameversion":"4.1.0.100"}
            {"timestamp":"2026-10-12T12:00:00Z","event":"LoadGame","Commander":"Explorer","FID":"F123","Odyssey":true}
            """ + "\n"
        );
        JournalMonitorUpdate bootstrap = await new JournalDirectoryMonitor(directory, "F123").PollAsync();
        string privacyPath = Path.Combine(directory, "stealth.json");
        uploadPrivacy = new(privacyPath, true);
        uploadPrivacy.Observe(bootstrap.JournalEvents, false);
        uploadPrivacy.Observe([StealthEvent("Docked", 3, "\"StationName\":\"Safe port\"")], true);
        SeedPartiallyCompletedRecovery(provider, journal);
        uploadPrivacy = new(privacyPath, true);
        uploadPrivacy.Observe(bootstrap.JournalEvents, false);

        var recovery = new ProviderHandler(provider, HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using (var http = new HttpClient(recovery))
        {
            await Run(provider, http, bootstrap, null);
        }
        Assert.Contains(recovery.Posts, body => body.Contains("Released before docking", StringComparison.Ordinal));
        Assert.DoesNotContain(recovery.Posts, body => body.Contains("Held after docking", StringComparison.Ordinal));
        Assert.DoesNotContain(recovery.Posts, body => body.Contains("Already acknowledged", StringComparison.Ordinal));

        clock.Now += TimeSpan.FromHours(1);
        uploadPrivacy.Observe([StealthEvent("Docked", 5, "\"StationName\":\"Next port\"")], true);
        var nextDocking = new ProviderHandler(provider, HttpStatusCode.OK) { EdAstroEvents = "[\"ScanOrganic\"]" };
        using var resumedHttp = new HttpClient(nextDocking);
        await Run(provider, resumedHttp, bootstrap, null);
        Assert.Contains(nextDocking.Posts, body => body.Contains("Held after docking", StringComparison.Ordinal));
        Assert.DoesNotContain(
            nextDocking.Posts,
            body => body.Contains("Released before docking", StringComparison.Ordinal)
        );
    }

    /// <summary>Replays an acknowledgement and a later capture to reproduce dictionary-slot reuse in the append log.</summary>
    private void SeedPartiallyCompletedRecovery(string provider, string journal)
    {
        string identityProperty = provider == "EDAstro" ? "Identity" : "UploadIdentity";
        string owner = provider switch
        {
            "Inara" => "F123:EXPLORER",
            "EDSM" => "F123:EXPLORER EDSM",
            _ => "EXPLORER",
        };
        using var outbox = new JournalUploadOutbox<JObject>(
            Path.Combine(directory, "uploads"),
            provider.ToLowerInvariant(),
            item => item.Value<string>(identityProperty)!,
            _ => true,
            _ => { }
        );
        Assert.True(outbox.TryOpen(owner, out _));
        outbox.Enqueue([
            RecoveryReport(provider, journal, "A", 1, "Already acknowledged"),
            RecoveryReport(provider, journal, "B", 2, "Released before docking"),
        ]);
        outbox.Complete(["A"]);
        // The new report reuses the completed report's dictionary slot. Replaying
        // this durable history therefore recovers C before the older retained B.
        outbox.Enqueue([RecoveryReport(provider, journal, "C", 4, "Held after docking")]);
    }

    /// <summary>Creates each provider's private persisted report shape without bypassing durable recovery.</summary>
    private static JObject RecoveryReport(string provider, string journal, string identity, int second, string system)
    {
        string name = provider == "EDAstro" ? "ScanOrganic" : "FSDJump";
        JournalEventEnvelope entry = StealthEvent(
            name,
            second,
            $$"""
            "StarSystem":"{{system}}","SystemAddress":42,"StarPos":[0,0,0],"JumpDist":1
            """
        );
        if (provider == "Inara")
        {
            return new JObject
            {
                ["UploadIdentity"] = identity,
                ["Name"] = "addCommanderTravelFSDJump",
                ["Timestamp"] = entry.Payload.GetProperty("timestamp").GetString(),
                ["Data"] = new JObject { ["starsystemName"] = system },
            };
        }
        if (provider == "EDSM")
        {
            return new JObject
            {
                ["UploadIdentity"] = identity,
                ["EventName"] = name,
                ["RawJson"] = entry.RawJson,
                ["GameVersion"] = "4.1.0.100",
                ["GameBuild"] = "r300000",
            };
        }
        return new JObject
        {
            ["Identity"] = identity,
            ["Session"] = "Explorer\n" + journal,
            ["Odyssey"] = true,
            ["Json"] = entry.RawJson,
            ["QueuedAt"] = entry.Timestamp,
        };
    }
}
