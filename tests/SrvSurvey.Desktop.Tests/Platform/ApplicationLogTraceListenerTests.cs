using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Desktop.Platform;

namespace SrvSurvey.Desktop.Tests.Platform;

public sealed class ApplicationLogTraceListenerTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"SrvSurvey-trace-{Guid.NewGuid():N}");

    [Fact]
    public void ListenerCombinesFragmentsAndSeparatesCompleteLines()
    {
        var log = new ApplicationLogService(temporaryDirectory);
        var listener = new ApplicationLogTraceListener(log);

        listener.Write("Avalonia ");
        listener.WriteLine("started");
        listener.Write("first\nsecond");
        listener.Flush();

        Assert.Collection(
            log.Entries,
            line => Assert.EndsWith(": Avalonia started", line),
            line => Assert.EndsWith(": first", line),
            line => Assert.EndsWith(": second", line));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
