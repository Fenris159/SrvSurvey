using System.Globalization;
using System.IO.Compression;
using SrvSurvey.Core.Journal;
using SrvSurvey.Core.Mining;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.ViewModels;

public sealed partial class MiningWorkspaceViewModelTests
{
    [Fact]
    public void SuppliedJournalShowsCargoConfirmedSessionAndSeparatePersistentCarryover()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var context = new JournalSessionState();
            var ship = new EliteStatus { Flags = StatusFlags.InMainShip };
            var clock = new Clock();
            using var workspace = new MiningWorkspaceViewModel(
                directory,
                new Resolver(),
                new BookmarksViewModel(directory),
                clock: clock
            );
            var notifications = new List<string?>();
            workspace.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
            JournalEventEnvelope[] entries = ReadMiningCargoJournal().ToArray();
            var start = DateTimeOffset.Parse("2026-10-28T08:11:34Z", CultureInfo.InvariantCulture);
            var end = DateTimeOffset.Parse("2026-10-28T08:32:44Z", CultureInfo.InvariantCulture);
            void Apply(JournalEventEnvelope[] batch, bool bootstrap)
            {
                foreach (JournalEventEnvelope entry in batch)
                {
                    context.Apply(entry);
                    clock.Now = entry.Timestamp!.Value;
                }
                workspace.Apply(new(null, batch, ship, null, null, null, [], bootstrap), context, null, ship);
            }

            Apply(entries.Where(e => e.Timestamp < start).ToArray(), true);
            Assert.Null(workspace.Current);
            Assert.False(workspace.HasRefineryCarryover);
            Assert.Contains("exclude post-session", workspace.YieldConfirmation);
            var departure = DateTimeOffset.Parse("2026-10-28T08:30:12Z", CultureInfo.InvariantCulture);
            using var overview = new MiningSessionOverlayViewModel(workspace);
            Apply(entries.Where(e => e.Timestamp >= start && e.Timestamp <= departure).ToArray(), false);
            Assert.Equal(301, workspace.Current!.RefinedTons);
            Assert.StartsWith("301 t", overview.Yield);
            Assert.Contains("confirmed from cargo readings", workspace.YieldConfirmation);
            Apply(entries.Where(e => e.Timestamp > departure && e.Timestamp <= end).ToArray(), false);
            MiningSession session = Assert.Single(workspace.History);
            Assert.Null(workspace.Current);
            Assert.Equal(301, session.RefinedTons);
            Assert.Equal(6, session.CarryoverTons);
            Assert.False(session.HasUnconfirmedYield);
            Assert.True(workspace.HasRefineryCarryover);
            Assert.Contains("6 t refinery carryover", workspace.RefineryCarryoverSummary);
            Assert.Contains("excluded from t/hour", workspace.RefineryCarryoverSummary);
            Assert.Contains(nameof(workspace.RefineryCarryoverSummary), notifications);
            Assert.Contains(nameof(workspace.HasRefineryCarryover), notifications);
            Assert.Contains(nameof(workspace.YieldConfirmation), notifications);
            MiningSession saved = Assert.Single(new MiningStore(directory).Load(context.FrontierId!).History);
            Assert.Equal(301, saved.RefinedTons);
            Assert.Equal(6, saved.CarryoverTons);

            clock.Now = end.AddMinutes(1);
            workspace.StartCommand.Execute(null);
            Assert.False(workspace.HasRefineryCarryover);
            Assert.Contains("awaiting cargo confirmation", workspace.YieldConfirmation);
            JournalEventEnvelope refined = FiregroupsWorkspaceViewModelTests.Event(
                $$"""{"event":"MiningRefined","timestamp":"{{clock.Now:O}}","Type":"platinum"}"""
            );
            JournalEventEnvelope cargo = FiregroupsWorkspaceViewModelTests.Event(
                $$"""{"event":"Cargo","timestamp":"{{clock.Now:O}}","Count":1}"""
            );
            Apply([refined, cargo], false);
            Assert.Equal(1, workspace.Current!.RefinedTons);
            Assert.Contains("confirmed from cargo readings", workspace.YieldConfirmation);
            clock.Now = clock.Now.AddSeconds(1);
            Apply(
                [
                    FiregroupsWorkspaceViewModelTests.Event(
                        $$"""{"event":"Synthesis","timestamp":"{{clock.Now:O}}","Name":"Limpet Basic"}"""
                    ),
                    FiregroupsWorkspaceViewModelTests.Event(
                        $$"""{"event":"MiningRefined","timestamp":"{{clock.Now:O}}","Type":"platinum"}"""
                    ),
                    FiregroupsWorkspaceViewModelTests.Event(
                        $$"""{"event":"Cargo","timestamp":"{{clock.Now:O}}","Count":6}"""
                    ),
                ],
                false
            );
            Assert.Equal(2, workspace.Current.RefinedTons);
            Assert.Contains("estimates from incomplete cargo readings", workspace.YieldConfirmation);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static IEnumerable<JournalEventEnvelope> ReadMiningCargoJournal()
    {
        using FileStream file = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mining", "refinery-cargo-2026-10-28.jsonl.gz")
        );
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        int line = 0;
        while (reader.ReadLine() is { } raw)
        {
            yield return FiregroupsWorkspaceViewModelTests.Event(raw) with
            {
                SourceRecordIdentity = "mining-cargo:" + (++line).ToString(CultureInfo.InvariantCulture),
            };
        }
    }
}
