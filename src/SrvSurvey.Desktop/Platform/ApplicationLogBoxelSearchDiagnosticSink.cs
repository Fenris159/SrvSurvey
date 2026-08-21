using SrvSurvey.Core.Diagnostics;
using SrvSurvey.Core.Search;

namespace SrvSurvey.Desktop.Platform;

public sealed class ApplicationLogBoxelSearchDiagnosticSink(
    ApplicationLogService applicationLog) : IBoxelSearchDiagnosticSink
{
    private readonly ApplicationLogService applicationLog = applicationLog
        ?? throw new ArgumentNullException(nameof(applicationLog));

    public void Report(BoxelSearchDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var context = string.IsNullOrWhiteSpace(diagnostic.Context)
            ? string.Empty
            : $" ({diagnostic.Context})";
        var detail = diagnostic.Exception is null
            ? string.Empty
            : ": " + diagnostic.Exception;
        applicationLog.Append(
            $"Boxel search {diagnostic.Subsystem}/{diagnostic.Code}{context}{detail}");
    }
}
