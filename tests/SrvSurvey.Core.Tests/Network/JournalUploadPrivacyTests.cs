using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Network;

namespace SrvSurvey.Core.Tests.Network;

public sealed class JournalUploadPrivacyTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "stealth-" + Guid.NewGuid().ToString("N"));
    private string StatePath => Path.Combine(directory, "state.json");

    [Fact]
    public void RestartAndBootstrapCannotReleaseNewerEvidenceOrAnotherCommander()
    {
        var policy = new JournalUploadPrivacy(StatePath, true);
        int changes = 0;
        policy.Changed += _ => changes++;
        policy.SetEnabled(true);
        policy.Observe([Header, Commander("First", "F1"), Dock], false);
        Assert.False(policy.CanSend("First", "2026-10-28T00:00:00Z"));
        policy.Observe([Dock], true);
        Assert.True(policy.CanSend("first", "2026-10-28T00:00:00Z"));
        Assert.False(policy.CanSend("First", "2026-10-28T00:00:02Z"));
        Assert.False(policy.CanSend("Other", "2026-10-28T00:00:00Z"));
        Assert.False(policy.CanSend(null, "2026-10-28T00:00:00Z"));
        Assert.False(policy.CanSend("First", "invalid"));
        Assert.False(policy.CanSendJournal("First", "invalid"));
        Assert.True(policy.CanSendJournal("First", Commander("First", "F1").RawJson));
        Assert.Equal(1, changes);
        policy.Observe([Dock], true);
        Assert.Equal(1, changes);
        var restored = new JournalUploadPrivacy(StatePath, true);
        restored.Observe(
            [Header, Commander("First", "F1"), Parse("""{"event":"Docked","timestamp":"2026-10-28T01:00:00Z"}""")],
            false
        );
        Assert.True(restored.CanSend("First", "2026-10-28T00:00:00Z"));
        Assert.False(restored.CanSend("First", "2026-10-28T00:00:02Z"));
        restored.SetEnabled(false);
        Assert.True(restored.CanSend(null, null));
        Assert.True(restored.CanSendJournal(null, "invalid"));
        Assert.False(restored.Enabled);
    }

    [Theory]
    [InlineData("{\"event\":\"Commander\",\"Name\":\"Other\",\"FID\":\"F2\"}")]
    [InlineData("{\"event\":\"LoadGame\",\"Commander\":\"Other\",\"FID\":\"F2\"}")]
    public void DockingIsAttributedToItsEventTimeCommander(string boundary)
    {
        var policy = new JournalUploadPrivacy(StatePath, true);
        policy.Observe([Header, Commander("First", "F1"), Parse(boundary), Dock], true);
        Assert.False(policy.CanSend("First", "2026-10-28T00:00:00Z"));
        Assert.True(policy.CanSend("Other", "2026-10-28T00:00:00Z"));
    }

    [Theory]
    [InlineData("{\"event\":\"Fileheader\",\"Odyssey\":false}")]
    [InlineData("{\"event\":\"Commander\",\"Name\":\"Other\"}")]
    [InlineData("{\"event\":\"Commander\",\"FID\":\"F2\"}")]
    [InlineData("{\"event\":\"Fileheader\",\"Odyssey\":true}")]
    public void UnknownAndLegacyIdentitiesCannotRelease(string boundary)
    {
        var policy = new JournalUploadPrivacy(StatePath, true);
        policy.Observe([Header, Commander("First", "F1"), Parse(boundary), Dock], true);
        Assert.False(policy.CanSend("First", "2026-10-28T00:00:00Z"));
        Assert.False(policy.CanSend("Other", "2026-10-28T00:00:00Z"));
    }

    [Fact]
    public void InvalidSavedStateFailsClosedAndUnwritableDockingStaysHeld()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(StatePath, "invalid json");
        var policy = new JournalUploadPrivacy(StatePath, true);
        Assert.False(policy.CanSend("First", "2026-10-28T00:00:00Z"));
        File.Delete(StatePath);
        Directory.CreateDirectory(StatePath);
        policy.Observe([Header, Commander("First", "F1"), Dock], true);
        Assert.NotNull(policy.LastError);
        Assert.False(policy.CanSend("First", "2026-10-28T00:00:00Z"));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void SameCommanderNameOrFidBoundariesRetainTheConfirmedIdentity()
    {
        var policy = new JournalUploadPrivacy(StatePath, true);
        policy.Observe(
            [
                Header,
                Commander("First", "F1"),
                Parse("""{"event":"LoadGame","Commander":"First"}"""),
                Parse("""{"event":"Commander","FID":"F1"}"""),
                Dock,
            ],
            true
        );
        Assert.True(policy.CanSend("First", "2026-10-28T00:00:00Z"));
    }

    private static JournalEventEnvelope Header => Parse("""{"event":"Fileheader","Odyssey":true}""");
    private static JournalEventEnvelope Dock => Parse("""{"event":"Docked","timestamp":"2026-10-28T00:00:01Z"}""");

    private static JournalEventEnvelope Commander(string name, string fid) =>
        Parse($$"""{"event":"Commander","Name":"{{name}}","FID":"{{fid}}","timestamp":"2026-10-28T00:00:00Z"}""");

    private static JournalEventEnvelope Parse(string json)
    {
        Assert.True(JournalEventEnvelope.TryParse(json, out JournalEventEnvelope? entry, out _));
        return entry!;
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
