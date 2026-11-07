using System.Diagnostics;
using System.Globalization;
using System.Text;
using SrvSurvey.Core.Exploration.History;
using SrvSurvey.Core.Journal;

namespace SrvSurvey.Core.Tests.Exploration.History;

public sealed class ExplorationHistoryIoTests
{
    [Fact]
    public async Task LiveAppendsReadTheNewRangeInsteadOfRescanningTheGrowingJournal()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }
        string root = Path.Combine(Path.GetTempPath(), "SrvSurvey-growing-journal-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(typeof(ExplorationHistoryIoTests).Assembly.Location);
        start.ArgumentList.Add("--exploration-io-worker");
        start.ArgumentList.Add(root);
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.ExitCode == 0, await output + await errors);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync();
            Directory.Delete(root, true);
        }
    }

    /// <summary>Measures logical reads in an isolated process, independent of page-cache state and other tests.</summary>
    internal static async Task<int> RunWorkerAsync(string root)
    {
        string journal = Path.Combine(root, "Journal.2026-10-01T120000.01.log");
        using (var writer = new StreamWriter(journal, false, new UTF8Encoding(false)))
        {
            await writer.WriteLineAsync("""{"timestamp":"2026-10-01T12:00:00Z","event":"Fileheader","Odyssey":true}""");
            await writer.WriteLineAsync("""{"event":"Commander","FID":"F100"}""");
            await writer.WriteLineAsync("""{"event":"Location","StarSystem":"Growing survey","SystemAddress":100}""");
            await writer.WriteLineAsync(
                """{"event":"Scan","SystemAddress":100,"BodyID":0,"StarType":"N","StellarMass":1.4}"""
            );
            string padding = "{\"event\":\"Music\",\"MusicTrack\":\"" + new string('x', 384) + "\"}";
            for (int index = 0; index < 25000; index++)
            {
                await writer.WriteLineAsync(padding);
            }
        }
        var catalog = new ExplorationHistoryCatalog(Path.Combine(root, "data"), "F100", true);
        await catalog.SynchronizeAsync([root]);
        if (
            !JournalEventEnvelope.TryParse("""{"event":"Music"}""", out JournalEventEnvelope? observed, out _)
            || observed is null
        )
        {
            return 2;
        }
        JournalEventEnvelope[] batch = [observed with { SourceRecordIdentity = Path.GetFileName(journal) + ":0" }];
        await catalog.ObserveAsync(batch, [root]);
        static long LogicalReadBytes() =>
            long.Parse(
                File.ReadLines("/proc/self/io")
                    .Single(line => line.StartsWith("rchar:", StringComparison.Ordinal))
                    .Split(':')[1]
                    .Trim(),
                CultureInfo.InvariantCulture
            );
        long before = LogicalReadBytes();
        ExplorationHistorySummary? summary = null;
        for (int index = 0; index < 20; index++)
        {
            await File.AppendAllTextAsync(journal, "{\"event\":\"Music\",\"MusicTrack\":\"NewTrack\"}\n");
            summary = await catalog.ObserveAsync(batch, [root]);
        }
        long reads = LogicalReadBytes() - before;
        Console.WriteLine(
            $"Growing journal: {reads} logical bytes for 20 appends; journal is {new FileInfo(journal).Length} bytes."
        );
        // Twenty two-pass full rescans exceed 400 MiB; the suffix path has a generous 32 MiB process-wide budget.
        return reads < 32 * 1024 * 1024 && summary is { IsComplete: true, UnsoldValue: 23106, SaleEarnings: 0 } ? 0 : 1;
    }
}
