namespace SrvSurvey.Core.Tests.Network;

/// <summary>Hosts the isolated writer killed by journal upload crash-recovery scenarios.</summary>
internal static class JournalUploadCrashWorker
{
    /// <summary>Runs only the explicit crash-worker command; ordinary tests execute through VSTest.</summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3 || args[0] != "--journal-upload-crash-worker")
        {
            return 2;
        }
        await JournalUploadScenarioTests.RunCrashWorkerAsync(args[1], args[2]);
        return 0;
    }
}
