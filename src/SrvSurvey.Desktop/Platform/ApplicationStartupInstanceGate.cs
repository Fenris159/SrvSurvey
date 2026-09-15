namespace SrvSurvey.Desktop.Platform;

internal delegate Task<bool> ConfirmApplicationInstanceReplacement(
    ApplicationInstanceScan scan,
    Func<Task> closeOtherInstances,
    CancellationToken cancellationToken
);

internal enum ApplicationStartupInstanceDecision
{
    Exit,
    Continue,
    ReplacedExisting,
}

internal sealed class ApplicationStartupInstanceGate(IApplicationInstanceManager instanceManager)
{
    private readonly IApplicationInstanceManager instanceManager =
        instanceManager ?? throw new ArgumentNullException(nameof(instanceManager));

    public async Task<ApplicationStartupInstanceDecision> EvaluateAsync(
        bool concurrentInstanceAuthorized,
        ConfirmApplicationInstanceReplacement confirmReplacement,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(confirmReplacement);
        if (concurrentInstanceAuthorized)
        {
            return ApplicationStartupInstanceDecision.Continue;
        }

        ApplicationInstanceScan scan = await instanceManager
            .ScanOtherInstancesAsync(cancellationToken)
            .ConfigureAwait(false);
        if (scan.TotalCount == 0)
        {
            return ApplicationStartupInstanceDecision.Continue;
        }

        bool replacedExisting = await confirmReplacement(
                scan,
                () => instanceManager.CloseOtherInstancesAsync(cancellationToken),
                cancellationToken
            )
            .ConfigureAwait(false);
        return replacedExisting
            ? ApplicationStartupInstanceDecision.ReplacedExisting
            : ApplicationStartupInstanceDecision.Exit;
    }
}
