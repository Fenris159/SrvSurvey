using SrvSurvey.Core.Search;
using SrvSurvey.Core.Storage;

namespace SrvSurvey.Core.Tests.Storage;

public sealed class LegacySystemDataFileStoreConcurrencyTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-system-lock-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task ProfileTransactionSerializesWritesAcrossStoreInstances()
    {
        var transactionStore = new LegacySystemDataFileStore(temporaryDirectory);
        var noteStore = new SystemNoteStore(temporaryDirectory);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var transaction = transactionStore.ExecuteProfileWriteAsync(
            "F123",
            async _ =>
            {
                entered.SetResult();
                await release.Task;
                return true;
            });
        await entered.Task;

        var save = noteStore.SaveAsync(
            new SystemNoteContext(
                "F123",
                "Drew",
                "Test",
                42,
                new GalacticCoordinate(1, 2, 3)),
            "serialized");

        Assert.False(save.IsCompleted);
        release.SetResult();
        await transaction;
        var path = await save;
        Assert.Contains("serialized", await File.ReadAllTextAsync(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
