using System.Globalization;
using System.IO.Compression;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;

namespace SrvSurvey.Core.Tests.Mining;

public sealed class MiningCargoJournalRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppliedMiningJournalReconcilesActualCargoRatherThanDistinctRefineryMessages(bool reopen)
    {
        var state = new MiningWorkspaceState(new MiningCommanderData());
        var context = new JournalSessionState();
        int index = 0;
        foreach (JournalEventEnvelope entry in ReadJournal())
        {
            context.Apply(entry);
            state.Apply(entry, false, context.SystemName ?? "", context.BodyName ?? "", context.ShipType ?? "");
            if (reopen && ++index % 100 == 0)
            {
                state = new MiningWorkspaceState(MiningStore.Parse(MiningStore.Export(state.Data)));
            }
        }
        MiningSession[] sessions = state.Data.History.OrderBy(s => s.Started).ToArray();
        Assert.Equal(6, sessions.Length);
        Assert.Equal([288d, 301, 301, 301, 301, 251], sessions.Select(s => s.RefinedTons));
        Assert.Equal(
            "2026-10-28T08:11:34.0000000+00:00",
            sessions[4].Started.ToString("O", CultureInfo.InvariantCulture)
        );
        Assert.Equal([0d, 6, 6, 6, 6, 0], sessions.Select(s => s.CarryoverTons));
        Assert.All(sessions, session => Assert.False(session.HasUnconfirmedYield));
        Assert.All(sessions, session => Assert.False(session.EstimatedMineralBreakdown));
        Assert.All(sessions, session => Assert.All(session.Collections, entry => Assert.True(entry.Count > 0)));
        Assert.Equal(301 * 3600d / sessions[4].ActiveDuration.TotalSeconds, sessions[4].TonsPerHour, 6);
        Assert.Equal(24, state.Data.History.Sum(s => s.CarryoverTons));
        string csv = MiningReport.Csv(sessions);
        Assert.Equal(
            sessions.Select(s => s.CarryoverTons),
            MiningReportImport.ReadCsv(csv).Select(s => s.CarryoverTons)
        );
        Assert.Contains("Refinery carryover after session: 6 t", MiningReport.Text([sessions[4]]));
        Assert.Contains("Refinery carryover after session: 6 t", MiningReport.Html([sessions[4]]));
        // Replaying the same physical records must not add another session or more tonnage.
        foreach (JournalEventEnvelope entry in ReadJournal())
        {
            context.Apply(entry);
            state.Apply(entry, true, context.SystemName ?? "", context.BodyName ?? "", context.ShipType ?? "");
        }
        Assert.Equal(6, state.Data.History.Count);
        Assert.Equal(1743, state.Data.History.Sum(s => s.RefinedTons));
    }

    internal static IEnumerable<JournalEventEnvelope> ReadJournal()
    {
        using FileStream file = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mining", "refinery-cargo-2026-10-28.jsonl.gz")
        );
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        int line = 0;
        while (reader.ReadLine() is { } raw)
        {
            Assert.True(JournalEventEnvelope.TryParse(raw, out JournalEventEnvelope? entry, out string? error), error);
            yield return entry! with
            {
                SourceRecordIdentity = "mining-regression:" + (++line).ToString(CultureInfo.InvariantCulture),
            };
        }
    }
}
