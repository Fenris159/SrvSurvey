namespace SrvSurvey.Core.Tests.Network;

/// <summary>Hosts isolated writers for crash recovery and cross-process persistence scenarios.</summary>
internal static class JournalUploadCrashWorker
{
    /// <summary>Runs explicit persistence-worker commands; ordinary tests execute through VSTest.</summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--journey-profile-cas-worker")
        {
            return await Journeys.JourneyUpgradeRecoveryTests.RunProfileSelectionWorkerAsync(args[1], args[2]);
        }
        if (args.Length != 3 || args[0] != "--journal-upload-crash-worker")
        {
            return 2;
        }
        await JournalUploadScenarioTests.RunCrashWorkerAsync(args[1], args[2]);
        return 0;
    }
}
