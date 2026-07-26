namespace SrvSurvey.Core.Journal;

public sealed class StatusBlinkDetector(
    StatusFlags trigger,
    TimeSpan maximumInterval)
{
    private bool? previousState;
    private StatusFlags previousTrigger;
    private DateTimeOffset? previousChange;

    public StatusFlags Trigger { get; } = trigger == StatusFlags.None
        ? StatusFlags.HudInAnalysisMode
        : trigger;

    public TimeSpan MaximumInterval { get; } = maximumInterval > TimeSpan.Zero
        ? maximumInterval
        : TimeSpan.FromSeconds(3);

    public StatusBlinkResult Update(
        EliteStatus status,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(status);
        var activeTrigger = status.OnFootExterior
            ? StatusFlags.ShieldsUp
            : Trigger;
        var currentState = status.Flags.HasFlag(activeTrigger);
        if (previousState is null || previousTrigger != activeTrigger)
        {
            previousState = currentState;
            previousTrigger = activeTrigger;
            previousChange = null;
            return new StatusBlinkResult(false, false, activeTrigger);
        }

        if (previousState == currentState)
        {
            var primed = previousChange is { } last
                && observedAt - last < MaximumInterval;
            if (!primed)
            {
                previousChange = null;
            }

            return new StatusBlinkResult(false, primed, activeTrigger);
        }

        previousState = currentState;
        var detected = previousChange is { } previous
            && observedAt >= previous
            && observedAt - previous < MaximumInterval;
        previousChange = detected ? null : observedAt;
        return new StatusBlinkResult(
            detected,
            !detected,
            activeTrigger);
    }

    public void Reset()
    {
        previousState = null;
        previousTrigger = StatusFlags.None;
        previousChange = null;
    }
}

public readonly record struct StatusBlinkResult(
    bool Detected,
    bool IsPrimed,
    StatusFlags ActiveTrigger);
