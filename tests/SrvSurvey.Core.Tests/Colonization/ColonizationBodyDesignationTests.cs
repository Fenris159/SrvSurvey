using System.Text.Json;
using SrvSurvey.Core.Colonization;

namespace SrvSurvey.Core.Tests.Colonization;

public sealed class ColonizationBodyJournalTests
{
    [Fact]
    public void JournalBodyIdAndNameAreReadTogether()
    {
        using var document = JsonDocument.Parse("""{"Body":"Peralta 4 a","BodyID":12,"StarSystem":"Peralta"}""");

        Assert.True(ColonizationBodyJournal.ReportsCurrentBody("Docked"));
        Assert.False(ColonizationBodyJournal.ClearsCurrentBody("Docked"));
        Assert.True(ColonizationBodyJournal.ClearsCurrentBody("SupercruiseEntry"));
        Assert.Equal(12, ColonizationBodyJournal.ReadBodyId(document.RootElement));
        Assert.Equal("Peralta 4 a", ColonizationBodyJournal.ReadBodyName(document.RootElement));
    }
}
