using Avalonia.Headless.XUnit;
using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Storage;
using SrvSurvey.Desktop.ViewModels;

namespace SrvSurvey.Desktop.Tests.Runtime;

[Collection(AvaloniaHeadlessTestCollection.Name)]
public sealed class MainWindowViewModelOwnershipTests
{
    [AvaloniaFact]
    public async Task DisposingParentStopsChildLogProjection()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"SrvSurvey-runtime-owner-{Guid.NewGuid():N}");
        try
        {
            var paths = new AppDataPaths(
                Path.Combine(root, "config"),
                Path.Combine(root, "data"),
                Path.Combine(root, "cache"),
                []);
            var applicationLog = new ApplicationLogService(
                paths.DataDirectory);
            await using var viewModel = new MainWindowViewModel(
                configuredJournalDirectory: null,
                new MainWindowViewModelOptions
                {
                    AppDataPaths = paths,
                    ApplicationLogService = applicationLog,
                });
            applicationLog.Append("Before parent disposal");
            Assert.Contains(
                "Before parent disposal",
                viewModel.DiagnosticsLog.LogText,
                StringComparison.Ordinal);

            await viewModel.DisposeAsync();
            var disposedSnapshot = viewModel.DiagnosticsLog.LogText;
            applicationLog.Append("After parent disposal");

            Assert.Equal(disposedSnapshot, viewModel.DiagnosticsLog.LogText);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort test cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort test cleanup.
            }
        }
    }
}
